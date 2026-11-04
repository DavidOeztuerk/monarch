using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Evidence;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli;

/// <summary>
/// <c>monarch gate verdict|merge|check</c> (ADR-0007): the only merge route. The hook keeps every other route closed and keeps the commands away from shadows.
/// Monarch makes the one merge call itself, after checking base, rank, CI, verdict and warden run.
/// </summary>
internal static partial class GateMergeCommands
{
    private static readonly string[] WardenTypes = ["monarch:warden", "warden"];
    private static readonly string[] ClosedGateStates = ["cleared", "closed", "done", "failed", "abandoned"];

    private sealed record Target(ProjectPaths Paths, BoardGate Gate, ForgeRef Forge, int Pr, Rank Rank);

    private sealed record Plan(Target Target, PullInfo Pull, SystemConfig Config, string IntegrationHead, List<string>? Overridden);

    private static int Refuse(CliEnv env, string message)
    {
        env.Err.WriteLine($"refused: {message}");
        return 1;
    }

    /// <summary>Records the verdict of an independent warden review for the current head of the gate's pull request.</summary>
    public static int Verdict(CliEnv env, string gateId, string verdict, string source)
    {
        if (verdict is not ("approve" or "changes"))
        {
            return Refuse(env, $"verdict must be 'approve' or 'changes', not '{verdict}'.");
        }

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return Refuse(env, "--source must be the URL of the review (http or https).");
        }

        try
        {
            if (Resolve(env, gateId, out var target) is { } error)
            {
                return Refuse(env, error);
            }

            var pull = Forge.Pulls(env).GetPullAsync(target!.Forge.Owner, target.Forge.Repo, target.Pr).GetAwaiter().GetResult();
            var ledger = new ProjectLedger(target.Paths, env.Clock);
            ledger.Append(ledger.SessionId(), "cli", "WardenVerdictRecorded", ("gate", gateId), ("verdict", verdict), ("source", source), ("sha", pull.HeadSha), ("pr", (long)target.Pr));
            env.Out.WriteLine($"{gateId}: warden verdict '{verdict}' recorded for PR #{target.Pr} at {Short(pull.HeadSha)}");
            return 0;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Refuse(env, $"verdict failed: {ex.Message}");
        }
    }

    /// <summary>Merges the gate's pull request into the integration branch, once every condition holds.</summary>
    public static int Merge(CliEnv env, string gateId)
    {
        try
        {
            return MergeCore(env, gateId);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Refuse(env, $"merge failed: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>monarch gate check</c> (Q-0137): reads the last <c>GateMerged</c> of the gate from the ledger, checks the run on its merge commit and appends
    /// <c>IntegrationRunChecked</c>. Main only; harmless to repeat.
    /// </summary>
    public static int Check(CliEnv env, string gateId)
    {
        if (!string.IsNullOrWhiteSpace(env.GetVar("MONARCH_AGENT_TYPE")))
        {
            return Refuse(env, "gate check is for the main session, not for a shadow.");
        }

        try
        {
            if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
            {
                return Refuse(env, "not an awakened project.");
            }

            var merged = LedgerScan.All(paths.Root).LastOrDefault(r => r.Type == "GateMerged" && r.Str("gate") == gateId);
            if (merged?.Str("merge_commit_sha") is not { Length: > 0 } sha)
            {
                return Refuse(env, $"no GateMerged recorded for {gateId} in this checkout; nothing to check (the merge did not go through 'monarch gate merge', or it ran in another checkout).");
            }

            if (Forge.Resolve(paths) is not { } forge)
            {
                return Refuse(env, "no repository found (add boards.github.owner/repo to system.yaml or set a git origin).");
            }

            var branch = merged.Str("base");
            if (branch is null)
            {
                try
                {
                    branch = SystemConfigReader.Load(paths.Root).IntegrationBranch;
                }
                catch (FormatException ex)
                {
                    return Refuse(env, $"system.yaml is invalid: {ex.Message}");
                }
            }

            var ledger = new ProjectLedger(paths, env.Clock);
            using var cts = new CancellationTokenSource(PhaseC(env));
            // A fix PR that ended in "Status unbekannt" never checked the first parent: GateMerged carries integration_head, so catch it up here.
            var deviation = merged.Str("integration_head") is { Length: > 0 } head ? CheckFirstParent(env, Forge.Pulls(env), forge, sha, head, cts.Token) : null;
            return CheckIntegrationRun(env, paths, forge, gateId, branch, sha, ledger, cts.Token, deviation);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return Refuse(env, $"check failed: {ex.Message}");
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is DomainException or HttpRequestException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException or OperationCanceledException;

    private static string Short(string sha) => sha[..Math.Min(8, sha.Length)];

    internal const int DefaultWaitSeconds = 60;
    internal const int DefaultPollSeconds = 30;
    internal const int DefaultForgeSeconds = 20;

    private static int Seconds(CliEnv env, string name, int fallback) =>
        int.TryParse(env.GetVar(name), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static TimeSpan Forge1(CliEnv env) => TimeSpan.FromSeconds(Seconds(env, "MONARCH_MERGE_FORGE_SECONDS", DefaultForgeSeconds));

    private static TimeSpan Reserve(CliEnv env) => TimeSpan.FromSeconds(Math.Max(1, Forge1(env).TotalSeconds / 2));

    private static TimeSpan Wait(CliEnv env) => TimeSpan.FromSeconds(Seconds(env, "MONARCH_MERGE_WAIT_SECONDS", DefaultWaitSeconds));

    /// <summary>Phase C deadline: the wait, but never shorter than one forge call, so that the single guaranteed poll cannot be cancelled before it starts.</summary>
    private static TimeSpan PhaseC(CliEnv env) => TimeSpan.FromSeconds(Math.Max(Wait(env).TotalSeconds, Forge1(env).TotalSeconds));

    private static int MergeCore(CliEnv env, string gateId)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return Refuse(env, "not an awakened project.");
        }

        // 1. ledger verify: local git, outside every forge deadline, so it runs before the timer of phase A starts
        var ledgerCheck = LedgerVerifier.Verify(paths.Root);
        if (!ledgerCheck.Ok)
        {
            var findings = string.Join("; ", ledgerCheck.Violations.Take(5).Select(v => $"{v.File}:{v.Line} {v.Kind}"));
            return Refuse(env, $"ledger verify reports {ledgerCheck.Violations.Count} finding(s) ({findings}); merge refused until the ledger is clean (monarch ledger verify).");
        }

        // Phase A (ADR-0007 Q-0137): every forge call before the PUT shares one deadline; running out refuses without a PUT.
        Plan plan;
        using (var phaseA = new CancellationTokenSource(Forge1(env)))
        {
            try
            {
                if (Prepare(env, gateId, paths, phaseA.Token, out var prepared) is { } exit)
                {
                    return exit;
                }

                plan = prepared!;
            }
            catch (OperationCanceledException)
            {
                return Refuse(env, $"Forge-Frist von {(int)Forge1(env).TotalSeconds} s abgelaufen vor dem Merge (kein PUT gesendet); später erneut ausführen.");
            }
        }

        var t = plan.Target;
        var config = plan.Config;
        var pull = plan.Pull;
        var pulls = Forge.Pulls(env);
        var gateStep = $"pull request #{t.Pr}";

        // 7. merge, bound to the checked head; Phase B: PUT and read-back share one deadline, the reserve read one more.
        string? mergeSha = null;
        PullInfo? after = null;
        var putExpired = false;
        using (var phaseB = new CancellationTokenSource(Forge1(env)))
        {
            try
            {
                mergeSha = pulls.MergeSquashAsync(t.Forge.Owner, t.Forge.Repo, t.Pr, pull.HeadSha, phaseB.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is OperationCanceledException || (ex is HttpRequestException h && (h.StatusCode is null || (int)h.StatusCode >= 500)))
            {
                // No answer to the PUT (deadline, transport error, 5xx): the merge may have landed, so read back like after an expired PUT. 4xx stays a plain refusal.
                putExpired = true;
            }

            if (!putExpired)
            {
                after = TryReadPull(pulls, t, phaseB.Token);
            }
        }

        if (after is null)
        {
            using var reserve = new CancellationTokenSource(Reserve(env));
            after = TryReadPull(pulls, t, reserve.Token);
        }

        var ledger = new ProjectLedger(paths, env.Clock);
        if (after is null)
        {
            if (mergeSha is null)
            {
                return Refuse(env, $"Status unbekannt: das PUT für {gateStep} blieb ohne Antwort (Frist {(int)Forge1(env).TotalSeconds} s, Transport- oder 5xx-Fehler) und der PR ist nicht lesbar. Das PUT kann noch ankommen; auf der Forge prüfen, ob der PR gemergt ist, bevor etwas erneut läuft ('monarch gate check {gateId}' nach dem Merge).");
            }

            // The PUT answered, so the merge exists: record it with the sha the PUT returned.
            AppendMerged(ledger, gateId, t.Pr, pull.HeadSha, mergeSha, null, plan);
            env.Err.WriteLine($"Status unbekannt: GitHub hat den Merge von {gateStep} bestätigt ({Short(mergeSha)}), der PR war danach nicht lesbar; GateMerged ist ohne Base geschrieben. Auf der Forge prüfen, danach 'monarch gate check {gateId}'.");
            return 1;
        }

        if (mergeSha is null && !after.Merged)
        {
            return Refuse(env, $"Status unbekannt: das PUT für {gateStep} blieb ohne Antwort (Frist {(int)Forge1(env).TotalSeconds} s, Transport- oder 5xx-Fehler) und der PR liest sich als nicht gemergt. Das PUT kann nach dem letzten Lesen noch ankommen; auf der Forge prüfen, ob der PR gemergt ist, bevor etwas erneut läuft.");
        }

        if (mergeSha is null && string.IsNullOrEmpty(after.MergeCommitSha))
        {
            return Refuse(env, $"Status unbekannt: {gateStep} liest sich als gemergt, aber ohne Merge-Commit; auf der Forge prüfen.");
        }

        // 9. record
        // A read that lags behind the PUT reports the test merge in merge_commit_sha: the answered PUT wins unless the PR reads as merged.
        var recorded = after.Merged ? after.MergeCommitSha ?? mergeSha! : mergeSha!;
        AppendMerged(ledger, gateId, t.Pr, pull.HeadSha, recorded, after.BaseRef, plan);
        if (!after.Merged)
        {
            return Refuse(env, $"GitHub accepted the merge, but pull request #{t.Pr} does not read as merged; check it on the forge.");
        }

        if (SystemConfigReader.IsProtectedBranch(config, after.BaseRef) || !string.Equals(after.BaseRef, config.IntegrationBranch, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(env, $"pull request #{t.Pr} was merged into '{after.BaseRef}', not the integration branch '{config.IntegrationBranch}'. Tell the guild master; GateMerged records the base.");
        }

        var note = mergeSha is null || string.Equals(recorded, mergeSha, StringComparison.OrdinalIgnoreCase) ? "" : $" (note: merge call returned {Short(mergeSha)}, the pull request reports {Short(recorded)})";
        var fix = plan.Overridden is null ? "" : $" Fix-PR: {config.IntegrationBranch} war rot bei {Short(plan.IntegrationHead)}, überstimmte Checks: {string.Join(", ", plan.Overridden)}.";
        env.Out.WriteLine($"{gateId}: merged PR #{t.Pr} into {after.BaseRef} (squash {Short(recorded)}){note}.{fix} Run 'monarch evidence sync' to verify and clear the quests.");

        // Phase C: the run on the merge commit (and, for a fix PR, the first parent of the merge commit).
        using var phaseC = new CancellationTokenSource(PhaseC(env));
        var deviation = plan.Overridden is null ? null : CheckFirstParent(env, pulls, t.Forge, recorded, plan.IntegrationHead, phaseC.Token);
        return CheckIntegrationRun(env, t.Paths, t.Forge, gateId, after.BaseRef, recorded, ledger, phaseC.Token, deviation);
    }

    private static PullInfo? TryReadPull(GitHubPulls pulls, Target t, CancellationToken ct)
    {
        try
        {
            return pulls.GetPullAsync(t.Forge.Owner, t.Forge.Repo, t.Pr, ct).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    private static void AppendMerged(ProjectLedger ledger, string gateId, int pr, string headSha, string mergeCommit, string? baseRef, Plan plan)
    {
        ledger.Append(
            ledger.SessionId(), "cli", "GateMerged", ("gate", gateId), ("pr", (long)pr), ("sha", headSha), ("merge_commit_sha", mergeCommit), ("base", baseRef), ("method", "squash"),
            ("overridden", plan.Overridden is null ? null : StringArray(plan.Overridden)), ("integration_head", plan.Overridden is null ? null : plan.IntegrationHead));
    }

    private static JsonElement StringArray(IEnumerable<string> items)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var item in items)
            {
                w.WriteStringValue(item);
            }

            w.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    /// <summary>A deviation found after the merge of a fix PR: the first parent of the merge commit is not the develop head that was checked.</summary>
    private sealed record ParentDeviation(string Kind, string? FirstParent, string Warning);

    private static ParentDeviation? CheckFirstParent(CliEnv env, GitHubPulls pulls, ForgeRef forge, string mergeCommit, string integrationHead, CancellationToken ct)
    {
        string? parent;
        try
        {
            parent = pulls.GetFirstParentAsync(forge.Owner, forge.Repo, mergeCommit, ct).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            var unreadable = new ParentDeviation("unreadable", null, $"Warnung: der erste Elternteil des Merge-Commits {Short(mergeCommit)} ist nicht lesbar ({ex.Message}); nicht belegt, dass der Merge auf dem geprüften develop-Head {Short(integrationHead)} aufsetzt.");
            env.Err.WriteLine(unreadable.Warning);
            return unreadable;
        }

        if (parent is null)
        {
            var none = new ParentDeviation("unreadable", null, $"Warnung: der Merge-Commit {Short(mergeCommit)} meldet keinen ersten Elternteil; nicht belegt, dass der Merge auf dem geprüften develop-Head {Short(integrationHead)} aufsetzt.");
            env.Err.WriteLine(none.Warning);
            return none;
        }

        if (!string.Equals(parent, integrationHead, StringComparison.OrdinalIgnoreCase))
        {
            var mismatch = new ParentDeviation("mismatch", parent, $"Warnung: der erste Elternteil des Merge-Commits {Short(mergeCommit)} ist {Short(parent)}, geprüft war der develop-Head {Short(integrationHead)}; develop hat sich zwischen Prüfung und Merge bewegt. Der Merge bleibt bestehen; zuerst klären.");
            env.Err.WriteLine(mismatch.Warning);
            return mismatch;
        }

        return null;
    }

    /// <summary>Everything before the PUT (ADR-0007 steps 1 to 6); returns an exit code when the merge is refused.</summary>
    private static int? Prepare(CliEnv env, string gateId, ProjectPaths paths, CancellationToken ct, out Plan? plan)
    {
        plan = null;

        // 2. unique pull request of the gate
        if (Resolve(env, gateId, out var target) is { } error)
        {
            return Refuse(env, error);
        }

        var t = target!;
        var pulls = Forge.Pulls(env);
        var pull = pulls.GetPullAsync(t.Forge.Owner, t.Forge.Repo, t.Pr, ct).GetAwaiter().GetResult();
        if (pull.Merged)
        {
            return Refuse(env, $"pull request #{t.Pr} is already merged.");
        }

        // 3. base = integration branch, never a protected branch (Q-0130: a misconfigured integration_branch must not lead into main)
        SystemConfig config;
        try
        {
            config = SystemConfigReader.Load(paths.Root);
        }
        catch (FormatException ex)
        {
            return Refuse(env, $"system.yaml is invalid: {ex.Message}");
        }

        if (SystemConfigReader.IsProtectedBranch(config, pull.BaseRef))
        {
            return Refuse(env, $"pull request #{t.Pr} targets the protected branch '{pull.BaseRef}', not the integration branch '{config.IntegrationBranch}'. main/master merges only the owner (ADR-0007).");
        }

        if (!string.Equals(pull.BaseRef, config.IntegrationBranch, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(env, $"pull request #{t.Pr} targets '{pull.BaseRef}', not the integration branch '{config.IntegrationBranch}'. main/master merges only the owner.");
        }

        // 4. rank
        if (t.Rank >= Rank.S)
        {
            return Refuse(env, $"rank S (gate or one of its quests): merging is an owner action; ask the guild master.");
        }

        // 5. CI, the same function as 'quest verify'
        var (ci, prChecks) = EvidenceCommands.CheckCi(env, paths, t.Forge, pull.HeadSha, ct);
        if (ci.Kind != StepKind.Done)
        {
            return Refuse(env, $"CI is not green on {Short(pull.HeadSha)}: {ci.Message}");
        }

        // 5b. Q-0132: the integration branch itself must be green (same function, applied to its head); Q-0137: a proven fix PR may merge into a red one
        var integrationHead = pulls.GetBranchHeadAsync(t.Forge.Owner, t.Forge.Repo, config.IntegrationBranch, ct).GetAwaiter().GetResult();
        var (integrationCi, integrationChecks) = EvidenceCommands.CheckCi(env, paths, t.Forge, integrationHead, ct);
        if (integrationCi.Kind == StepKind.Pending)
        {
            return Refuse(env, $"develop läuft noch (kein abgeschlossener grüner CI-Lauf am Head von {config.IntegrationBranch}, {Short(integrationHead)}): {integrationCi.Message}");
        }

        List<string>? overridden = null;
        if (integrationCi.Kind != StepKind.Done)
        {
            var bad = integrationChecks.Where(c => !c.IsSuccess).ToList();
            var cancelled = bad.Where(c => c.IsCancelled).ToList();
            if (cancelled.Count > 0)
            {
                return Refuse(env, $"{config.IntegrationBranch} is not green at {Short(integrationHead)}, merge refused: {Cancelled(cancelled)} Erst den Lauf wiederholen; ohne Ergebnis lässt sich nicht belegen, was der PR behebt.");
            }

            if (FixPrProof(pulls, t, config, pull, integrationHead, bad, prChecks, ct) is { } missing)
            {
                return Refuse(env, $"{config.IntegrationBranch} is not green at {Short(integrationHead)}, merge refused until it is fixed ({integrationCi.Message}); kein belegter Fix-PR: {missing}");
            }

            overridden = [.. bad.Select(c => c.Name)];
        }

        // 6. verdict and warden run
        var commitTime = pulls.GetCommitTimeAsync(t.Forge.Owner, t.Forge.Repo, pull.HeadSha, ct).GetAwaiter().GetResult();
        if (CheckVerdict(LedgerScan.All(paths.Root), gateId, pull.HeadSha, commitTime) is { } verdictError)
        {
            return Refuse(env, verdictError);
        }

        // The sha binding covers the PR head only; GitHub does not bind the base. Read develop once more right before the PUT.
        if (overridden is not null)
        {
            var again = pulls.GetBranchHeadAsync(t.Forge.Owner, t.Forge.Repo, config.IntegrationBranch, ct).GetAwaiter().GetResult();
            if (!string.Equals(again, integrationHead, StringComparison.OrdinalIgnoreCase))
            {
                return Refuse(env, $"{config.IntegrationBranch} moved from {Short(integrationHead)} to {Short(again)} while the fix PR was checked; the proof no longer holds. Run the command again.");
            }
        }

        plan = new Plan(t, pull, config, integrationHead, overridden);
        return null;
    }

    /// <summary>The three proofs of a fix PR (ADR-0007 Q-0137); returns the first missing one, or null when all hold. The CI of the PR head was checked already.</summary>
    private static string? FixPrProof(GitHubPulls pulls, Target t, SystemConfig config, PullInfo pull, string integrationHead, List<CheckEvidence> bad, List<CheckEvidence> prChecks, CancellationToken ct)
    {
        string status;
        try
        {
            status = pulls.CompareAsync(t.Forge.Owner, t.Forge.Repo, integrationHead, pull.HeadSha, ct).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsExpected(ex) && ex is not OperationCanceledException)
        {
            return $"the forge compare of {config.IntegrationBranch} and the PR head failed ({ex.Message}).";
        }

        if (!string.Equals(status, "ahead", StringComparison.Ordinal))
        {
            return $"the head of {config.IntegrationBranch} ({Short(integrationHead)}) is not an ancestor of the PR head (compare says '{status}').";
        }

        foreach (var check in bad)
        {
            var atPr = prChecks.FirstOrDefault(c => c.Name == check.Name);
            if (atPr is null)
            {
                return $"Check '{check.Name}' ist am {config.IntegrationBranch}-Head rot und fehlt am PR-Head ({Short(pull.HeadSha)}).";
            }

            if (!atPr.IsSuccess)
            {
                return $"Check '{check.Name}' ist am PR-Head nicht grün.";
            }
        }

        return null;
    }

    [GeneratedRegex("/actions/runs/([0-9]{1,20})(?:/|$)")]
    private static partial Regex RunIdPattern();

    /// <summary>Names the cancelled checks with their run id (from the check's html_url) and the rerun command; without an id, name and url and no command.</summary>
    private static string Cancelled(List<CheckEvidence> cancelled)
    {
        var parts = new List<string>();
        var ids = new List<string>();
        foreach (var c in cancelled)
        {
            var match = RunIdPattern().Match(c.Evidence.Source);
            if (match.Success)
            {
                parts.Add($"{c.Name}: Lauf {match.Groups[1].Value}");
                if (!ids.Contains(match.Groups[1].Value))
                {
                    ids.Add(match.Groups[1].Value);
                }
            }
            else
            {
                parts.Add($"{c.Name}: {c.Evidence.Source}");
            }
        }

        var rerun = ids.Count == 0 ? "" : " Neu starten: " + string.Join("; ", ids.Select(i => $"gh run rerun {i}")) + ".";
        return $"abgebrochen, kein Beleg ({string.Join("; ", parts)}).{rerun}";
    }

    /// <summary>
    /// Q-0132 (ADR-0007): waits for the run on the merge commit and records <c>IntegrationRunChecked</c>. The merge stays in every case;
    /// red, cancelled and pending exit non-zero so that somebody looks. At least one poll always runs.
    /// </summary>
    private static int CheckIntegrationRun(CliEnv env, ProjectPaths paths, ForgeRef forge, string gateId, string branch, string sha, ProjectLedger ledger, CancellationToken ct, ParentDeviation? deviation)
    {
        var wait = Wait(env);
        var poll = TimeSpan.FromSeconds(Seconds(env, "MONARCH_MERGE_POLL_SECONDS", DefaultPollSeconds));
        var started = System.Diagnostics.Stopwatch.StartNew();
        StepResult ci;
        List<CheckEvidence> relevant;
        TimeSpan lastPoll;
        while (true)
        {
            try
            {
                (ci, relevant) = EvidenceCommands.CheckCi(env, paths, forge, sha, ct);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                (ci, relevant) = (new StepResult(StepKind.Pending, $"could not read the checks: {ex.Message}"), []);
            }

            lastPoll = started.Elapsed;
            if (ci.Kind != StepKind.Pending || ct.IsCancellationRequested || started.Elapsed + poll > wait)
            {
                break;
            }

            env.Out.Flush();
            Task.Delay(poll).GetAwaiter().GetResult();
        }

        var bad = relevant.Where(c => !c.IsSuccess).ToList();
        var result = ci.Kind switch
        {
            StepKind.Done => "green",
            StepKind.Pending => "pending",
            _ => bad.Count > 0 && bad.All(c => c.IsCancelled) ? "cancelled" : "red",
        };
        ledger.Append(ledger.SessionId(), "cli", "IntegrationRunChecked", ("gate", gateId), ("sha", sha), ("branch", branch), ("result", result), ("first_parent", deviation?.FirstParent), ("parent_check", deviation?.Kind));
        var deviationExit = deviation is null ? 0 : 1;
        switch (result)
        {
            case "green":
                env.Out.WriteLine($"{branch} is green at {Short(sha)} after the merge.");
                return deviationExit;
            case "red":
                env.Err.WriteLine($"{branch} ist ROT nach dem Merge – zuerst klären ({Short(sha)}): {ci.Message} Der Merge bleibt bestehen; nichts Weiteres auf {branch} mergen, bis das geklärt ist.");
                return 1;
            case "cancelled":
                env.Err.WriteLine($"{branch}: Lauf auf dem Merge-Commit {Short(sha)} {Cancelled(bad)} Das ist kein Beleg für rot oder grün. Der Merge bleibt bestehen; nichts Weiteres auf {branch} mergen, bis der Lauf wiederholt und grün ist (danach 'monarch gate check {gateId}').");
                return 1;
            default:
                env.Err.WriteLine($"{branch}: Lauf auf dem Merge-Commit {Short(sha)} noch nicht abgeschlossen, letzter Abruf nach {(int)lastPoll.TotalSeconds} s ({ci.Message}) Nachprüfen: 'monarch gate check {gateId}'; oder die GitHub-Checks des Merge-Commits {sha} ansehen (Actions-Lauf auf {branch}). Der Merge bleibt bestehen.");
                return 1;
        }
    }

    /// <summary>
    /// Latest verdict of the gate must be 'approve' for this head. A warden run (SubagentStop, agent_type exactly <c>monarch:warden</c> or <c>warden</c>) must lie
    /// between the previous verdict and this one and after the commit time of the head; so one run covers exactly one verdict (ADR-0007 T1).
    /// </summary>
    private static string? CheckVerdict(List<LedgerRow> rows, string gateId, string head, DateTimeOffset commitTime)
    {
        var verdicts = Enumerable.Range(0, rows.Count).Where(i => rows[i].Type == "WardenVerdictRecorded" && rows[i].Str("gate") == gateId).ToList();
        if (verdicts.Count == 0)
        {
            return $"no warden verdict recorded for {gateId} (monarch gate verdict {gateId} --verdict approve --source <url>).";
        }

        var latest = verdicts[^1];
        var row = rows[latest];
        if (row.Str("sha") != head)
        {
            return $"the latest warden verdict was recorded for head {Short(row.Str("sha") ?? "?")}, the pull request head is {Short(head)} (pushed after the verdict); the warden must review again.";
        }

        if (row.Str("verdict") != "approve")
        {
            return $"the latest warden verdict is '{row.Str("verdict")}' (changes requested); fix, push and let the warden review again.";
        }

        var previous = verdicts.Count > 1 ? verdicts[^2] : -1;
        var runs = Enumerable.Range(previous + 1, latest - previous - 1)
            .Where(i => rows[i].Type == "ShadowRunCompleted" && Array.IndexOf(WardenTypes, rows[i].Str("agent_type")) >= 0).ToList();
        if (runs.Count == 0)
        {
            return "no warden run (SubagentStop of monarch:warden or warden) between the previous verdict and this one; a run covers exactly one verdict.";
        }

        if (runs.All(i => rows[i].At <= commitTime))
        {
            return $"the warden run must come after the commit of the head ({Short(head)} committed {commitTime.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)}); the verdict does not cover this head.";
        }

        return null;
    }

    /// <summary>Finds the gate, its quests, the unique pull request and the rank (maximum over gate and quests).</summary>
    private static string? Resolve(CliEnv env, string gateId, out Target? target)
    {
        target = null;
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return "not an awakened project.";
        }

        var board = new LocalFileBoard(paths.Root);
        var gate = board.ListGatesAsync().GetAwaiter().GetResult().FirstOrDefault(g => g.Id.Value == gateId);
        if (gate is null)
        {
            return $"gate {gateId} not found.";
        }

        if (Array.IndexOf(ClosedGateStates, gate.State.ToLowerInvariant()) >= 0)
        {
            return $"gate {gateId} is {gate.State}, not open.";
        }

        var quests = board.ListQuestsAsync().GetAwaiter().GetResult().Where(q => q.Gate == gate.Id || gate.Quests.Contains(q.Id)).ToList();
        var prs = quests.Select(q => board.FindQuestFile(q.Id.Value) is { } f ? BoardFiles.Link(f).Pr : null).Where(n => n is not null).Select(n => n!.Value).Distinct().Order().ToList();
        if (prs.Count == 0)
        {
            return $"no pull request linked to the quests of gate {gateId} (monarch quest pr <quest> <number>).";
        }

        if (prs.Count > 1)
        {
            return $"the quests of gate {gateId} link more than one pull request ({string.Join(", ", prs.Select(n => "#" + n.ToString(CultureInfo.InvariantCulture)))}); one gate merges one pull request.";
        }

        if (Forge.Resolve(paths) is not { } forge)
        {
            return "no repository found (add boards.github.owner/repo to system.yaml or set a git origin).";
        }

        var rank = quests.Select(q => q.Rank).Append(gate.Rank).Max();
        target = new Target(paths, gate, forge, prs[0], rank);
        return null;
    }
}
