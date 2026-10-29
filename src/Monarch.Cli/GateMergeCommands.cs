using System.Globalization;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Evidence;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli;

/// <summary>
/// <c>monarch gate verdict|merge</c> (ADR-0007): the only merge route. The hook keeps every other route closed and keeps both commands away from shadows.
/// Monarch makes the one merge call itself, after checking base, rank, CI, verdict and warden run.
/// </summary>
internal static class GateMergeCommands
{
    private static readonly string[] WardenTypes = ["monarch:warden", "warden"];
    private static readonly string[] ClosedGateStates = ["cleared", "closed", "done", "failed", "abandoned"];

    private sealed record Target(ProjectPaths Paths, BoardGate Gate, ForgeRef Forge, int Pr, Rank Rank);

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

    private static bool IsExpected(Exception ex) =>
        ex is DomainException or HttpRequestException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException;

    private static string Short(string sha) => sha[..Math.Min(8, sha.Length)];

    private static int MergeCore(CliEnv env, string gateId)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return Refuse(env, "not an awakened project.");
        }

        // 1. ledger verify
        var ledgerCheck = LedgerVerifier.Verify(paths.Root);
        if (!ledgerCheck.Ok)
        {
            var findings = string.Join("; ", ledgerCheck.Violations.Take(5).Select(v => $"{v.File}:{v.Line} {v.Kind}"));
            return Refuse(env, $"ledger verify reports {ledgerCheck.Violations.Count} finding(s) ({findings}); merge refused until the ledger is clean (monarch ledger verify).");
        }

        // 2. unique pull request of the gate
        if (Resolve(env, gateId, out var target) is { } error)
        {
            return Refuse(env, error);
        }

        var t = target!;
        var pulls = Forge.Pulls(env);
        var pull = pulls.GetPullAsync(t.Forge.Owner, t.Forge.Repo, t.Pr).GetAwaiter().GetResult();
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
        var ci = EvidenceCommands.CheckCi(env, paths, t.Forge, pull.HeadSha).Result;
        if (ci.Kind != StepKind.Done)
        {
            return Refuse(env, $"CI is not green on {Short(pull.HeadSha)}: {ci.Message}");
        }

        // 5b. Q-0132: the integration branch itself must be green (same function, applied to its head)
        var integrationHead = pulls.GetBranchHeadAsync(t.Forge.Owner, t.Forge.Repo, config.IntegrationBranch).GetAwaiter().GetResult();
        var integrationCi = EvidenceCommands.CheckCi(env, paths, t.Forge, integrationHead).Result;
        if (integrationCi.Kind == StepKind.Pending)
        {
            return Refuse(env, $"develop läuft noch (kein abgeschlossener grüner CI-Lauf am Head von {config.IntegrationBranch}, {Short(integrationHead)}): {integrationCi.Message}");
        }

        if (integrationCi.Kind != StepKind.Done)
        {
            return Refuse(env, $"{config.IntegrationBranch} is not green at {Short(integrationHead)}, merge refused until it is fixed: {integrationCi.Message}");
        }

        // 6. verdict and warden run
        var commitTime = pulls.GetCommitTimeAsync(t.Forge.Owner, t.Forge.Repo, pull.HeadSha).GetAwaiter().GetResult();
        if (CheckVerdict(LedgerScan.All(paths.Root), gateId, pull.HeadSha, commitTime) is { } verdictError)
        {
            return Refuse(env, verdictError);
        }

        // 7. merge, bound to the checked head
        var mergeSha = pulls.MergeSquashAsync(t.Forge.Owner, t.Forge.Repo, t.Pr, pull.HeadSha).GetAwaiter().GetResult();

        // 8. read the pull request again, 9. record
        var after = pulls.GetPullAsync(t.Forge.Owner, t.Forge.Repo, t.Pr).GetAwaiter().GetResult();
        var recorded = after.MergeCommitSha ?? mergeSha;
        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", "GateMerged", ("gate", gateId), ("pr", (long)t.Pr), ("sha", pull.HeadSha), ("merge_commit_sha", recorded), ("base", after.BaseRef), ("method", "squash"));
        if (!after.Merged)
        {
            return Refuse(env, $"GitHub accepted the merge, but pull request #{t.Pr} does not read as merged; check it on the forge.");
        }

        if (SystemConfigReader.IsProtectedBranch(config, after.BaseRef) || !string.Equals(after.BaseRef, config.IntegrationBranch, StringComparison.OrdinalIgnoreCase))
        {
            return Refuse(env, $"pull request #{t.Pr} was merged into '{after.BaseRef}', not the integration branch '{config.IntegrationBranch}'. Tell the guild master; GateMerged records the base.");
        }

        var note = string.Equals(recorded, mergeSha, StringComparison.OrdinalIgnoreCase) ? "" : $" (note: merge call returned {Short(mergeSha)}, the pull request reports {Short(recorded)})";
        env.Out.WriteLine($"{gateId}: merged PR #{t.Pr} into {after.BaseRef} (squash {Short(recorded)}){note}. Run 'monarch evidence sync' to verify and clear the quests.");
        return CheckIntegrationRun(env, t.Paths, t.Forge, gateId, after.BaseRef, recorded, ledger);
    }

    private const int DefaultWaitSeconds = 900;
    private const int DefaultPollSeconds = 30;

    private static int Seconds(CliEnv env, string name, int fallback) =>
        int.TryParse(env.GetVar(name), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    /// <summary>
    /// Q-0132 (ADR-0007): after the merge, waits for the run on the merge commit and records <c>IntegrationRunChecked</c>.
    /// The merge stays in every case; red and pending exit non-zero so that somebody looks.
    /// </summary>
    private static int CheckIntegrationRun(CliEnv env, ProjectPaths paths, ForgeRef forge, string gateId, string branch, string sha, ProjectLedger ledger)
    {
        var wait = TimeSpan.FromSeconds(Seconds(env, "MONARCH_MERGE_WAIT_SECONDS", DefaultWaitSeconds));
        var poll = TimeSpan.FromSeconds(Seconds(env, "MONARCH_MERGE_POLL_SECONDS", DefaultPollSeconds));
        var started = System.Diagnostics.Stopwatch.StartNew();
        StepResult ci;
        while (true)
        {
            try
            {
                ci = EvidenceCommands.CheckCi(env, paths, forge, sha).Result;
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                ci = new StepResult(StepKind.Pending, $"could not read the checks: {ex.Message}");
            }

            if (ci.Kind != StepKind.Pending || started.Elapsed + poll > wait)
            {
                break;
            }

            env.Out.Flush();
            Task.Delay(poll).GetAwaiter().GetResult();
        }

        var result = ci.Kind switch { StepKind.Done => "green", StepKind.Pending => "pending", _ => "red" };
        ledger.Append(ledger.SessionId(), "cli", "IntegrationRunChecked", ("gate", gateId), ("sha", sha), ("branch", branch), ("result", result));
        switch (result)
        {
            case "green":
                env.Out.WriteLine($"{branch} is green at {Short(sha)} after the merge.");
                return 0;
            case "red":
                env.Err.WriteLine($"{branch} ist ROT nach dem Merge – zuerst klären ({Short(sha)}): {ci.Message} Der Merge bleibt bestehen; nichts Weiteres auf {branch} mergen, bis das geklärt ist.");
                return 1;
            default:
                env.Err.WriteLine($"{branch}: Lauf auf dem Merge-Commit {Short(sha)} noch nicht abgeschlossen nach {(int)wait.TotalSeconds} s ({ci.Message}) Nachprüfen: die GitHub-Checks des Merge-Commits {sha} ansehen (Actions-Lauf auf {branch}). Der Merge bleibt bestehen.");
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
