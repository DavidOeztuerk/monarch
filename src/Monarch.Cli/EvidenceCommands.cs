using System.Globalization;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;

namespace Monarch.Cli;

/// <summary>Outcome of one verify or clear step.</summary>
/// <param name="Kind">Done, pending (try again later) or refused (a rule says no).</param>
/// <param name="Message">Human-readable result.</param>
internal sealed record StepResult(StepKind Kind, string Message);

/// <summary>Kinds of <see cref="StepResult"/>.</summary>
internal enum StepKind
{
    /// <summary>The transition happened.</summary>
    Done,

    /// <summary>Forge data is not there yet.</summary>
    Pending,

    /// <summary>The transition is not allowed.</summary>
    Refused,
}

/// <summary><c>quest pr|verify|clear</c> and <c>evidence sync</c>. Evidence comes only from the forge API, never from the caller.</summary>
internal static class EvidenceCommands
{
    /// <summary>Links a pull request to a quest.</summary>
    public static int Pr(CliEnv env, string id, string number)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        var file = paths is null ? null : new LocalFileBoard(paths.Root).FindQuestFile(id);
        if (file is null)
        {
            env.Err.WriteLine($"refused: quest {id} not found.");
            return 1;
        }

        if (!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var pr) || pr <= 0)
        {
            env.Err.WriteLine($"refused: '{number}' is not a pull request number.");
            return 1;
        }

        BoardFiles.Edit(file, r => BoardFiles.SetBoard(r, ("pr", pr.ToString(CultureInfo.InvariantCulture))));
        env.Out.WriteLine($"{id}: linked PR #{pr}");
        return 0;
    }

    /// <summary>Verifies a quest from the checks of its PR head.</summary>
    public static int Verify(CliEnv env, string id) => Single(env, id, VerifyCore);

    /// <summary>Clears a verified quest once its PR is merged.</summary>
    public static int Clear(CliEnv env, string id) => Single(env, id, ClearCore);

    /// <summary>Verifies and clears every linked quest (or one) as far as the forge data allows.</summary>
    public static int Sync(CliEnv env, string? questId)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var board = new LocalFileBoard(paths.Root);
        var quests = board.ListQuestsAsync().GetAwaiter().GetResult().Where(q => questId is null || q.Id.Value == questId).ToList();
        if (questId is not null && quests.Count == 0)
        {
            env.Err.WriteLine($"refused: quest {questId} not found.");
            return 1;
        }

        var failed = false;
        foreach (var q in quests.Where(q => q.State is QuestState.InReview or QuestState.Verified))
        {
            var file = board.FindQuestFile(q.Id.Value)!;
            if (BoardFiles.Link(file).Pr is null)
            {
                if (questId is not null)
                {
                    env.Out.WriteLine($"{q.Id.Value}: no PR linked");
                }

                continue;
            }

            var current = q;
            if (current.State == QuestState.InReview)
            {
                var v = Guarded(() => VerifyCore(env, paths, current, file));
                env.Out.WriteLine($"{q.Id.Value}: {v.Kind.ToString().ToLowerInvariant()} - {v.Message}");
                failed |= v.Kind == StepKind.Refused;
                if (v.Kind != StepKind.Done)
                {
                    continue;
                }

                current = current with { State = QuestState.Verified };
            }

            var c = Guarded(() => ClearCore(env, paths, current, file));
            env.Out.WriteLine($"{q.Id.Value}: {c.Kind.ToString().ToLowerInvariant()} - {c.Message}");
            failed |= c.Kind == StepKind.Refused;
        }

        return failed ? 1 : 0;
    }

    private static StepResult Guarded(Func<StepResult> step)
    {
        try
        {
            return step();
        }
        catch (Exception ex) when (ex is DomainException or HttpRequestException or InvalidOperationException or IOException or FormatException or System.Text.Json.JsonException)
        {
            return new StepResult(StepKind.Refused, ex.Message);
        }
    }

    private static int Single(CliEnv env, string id, Func<CliEnv, ProjectPaths, BoardQuest, string, StepResult> step)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var board = new LocalFileBoard(paths.Root);
        var bq = board.ListQuestsAsync().GetAwaiter().GetResult().FirstOrDefault(q => q.Id.Value == id);
        if (bq is null)
        {
            env.Err.WriteLine($"refused: quest {id} not found.");
            return 1;
        }

        var result = Guarded(() => step(env, paths, bq, board.FindQuestFile(id)!));
        (result.Kind == StepKind.Done ? env.Out : env.Err).WriteLine(result.Kind == StepKind.Done ? $"{id}: {result.Message}" : $"refused: {result.Message}");
        return result.Kind == StepKind.Done ? 0 : 1;
    }

    private static ManaCost EstimateOf(string root, QuestId id) => new LocalFileBoard(root).ReadEstimate(id) ?? new ManaCost(5, 1, 0m);

    private static StepResult VerifyCore(CliEnv env, ProjectPaths paths, BoardQuest bq, string file)
    {
        if (bq.State != QuestState.InReview)
        {
            return new StepResult(StepKind.Refused, $"quest {bq.Id.Value} is {QuestCommands.StateName(bq.State)}; verify needs in_review.");
        }

        if (BoardFiles.Link(file).Pr is not { } pr)
        {
            return new StepResult(StepKind.Refused, $"no PR linked (monarch quest pr {bq.Id.Value} <number>).");
        }

        if (Forge.Resolve(paths) is not { } forge)
        {
            return new StepResult(StepKind.Refused, "no repository found (add boards.github.owner/repo to system.yaml or set a git origin).");
        }

        var source = Forge.Evidence(env);
        var head = source.GetPullHeadAsync(forge.Owner, forge.Repo, pr).GetAwaiter().GetResult();
        var latest = source.GetChecksAsync(forge.Owner, forge.Repo, head).GetAwaiter().GetResult()
            .GroupBy(c => c.Name).Select(g => g.OrderBy(c => c.Evidence.At).Last()).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        var required = Forge.Required(paths);
        List<CheckEvidence> relevant;
        if (required.Count > 0)
        {
            var missing = required.Where(n => latest.All(c => c.Name != n)).ToList();
            if (missing.Count > 0)
            {
                return new StepResult(StepKind.Pending, $"required checks not completed on {head[..Math.Min(8, head.Length)]}: {string.Join(", ", missing)}.");
            }

            relevant = [.. latest.Where(c => required.Contains(c.Name))];
        }
        else
        {
            relevant = latest;
            if (relevant.Count == 0)
            {
                return new StepResult(StepKind.Pending, $"no completed check runs on {head[..Math.Min(8, head.Length)]} yet.");
            }
        }

        var bad = relevant.Where(c => !c.IsSuccess).ToList();
        if (bad.Count > 0)
        {
            return new StepResult(StepKind.Refused, "checks not green: " + string.Join(", ", bad.Select(c => $"{c.Name}={c.Conclusion}")));
        }

        var digest = ManaSupport.Sha(string.Join("\n", relevant.Select(c => c.Evidence.Digest)));
        var evidence = new Evidence(EvidenceKind.CiRun, $"https://github.com/{forge.Owner}/{forge.Repo}/commit/{head}/checks", digest, relevant.Max(c => c.Evidence.At));
        var quest = QuestCommands.Reconstitute(bq, EstimateOf(paths.Root, bq.Id));
        quest.Verify(evidence);
        new LocalFileBoard(paths.Root).UpsertQuestAsync(bq with { State = QuestState.Verified }).GetAwaiter().GetResult();
        if (ActiveStore.Read(paths) is { } active && active.QuestId == bq.Id.Value)
        {
            active.State = "verified";
            ActiveStore.Write(paths, active);
        }

        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", "QuestVerified", ("quest", bq.Id.Value), ("gate", bq.Gate.Value), ("pr", (long)pr), ("sha", head),
            ("source", evidence.Source), ("digest", digest),
            ("evidence", LedgerScan.StringObjects(relevant.Select(c => (IReadOnlyList<(string, string)>)[("kind", "CiRun"), ("name", c.Name), ("source", c.Evidence.Source), ("digest", c.Evidence.Digest), ("conclusion", c.Conclusion)]))));
        return new StepResult(StepKind.Done, $"verified on PR #{pr} ({relevant.Count} check(s) green).");
    }

    private static StepResult ClearCore(CliEnv env, ProjectPaths paths, BoardQuest bq, string file)
    {
        if (bq.State != QuestState.Verified)
        {
            return new StepResult(StepKind.Refused, $"quest {bq.Id.Value} is {QuestCommands.StateName(bq.State)}; clear needs verified.");
        }

        if (BoardFiles.Link(file) is not { Pr: { } pr } link)
        {
            return new StepResult(StepKind.Refused, $"no PR linked (monarch quest pr {bq.Id.Value} <number>).");
        }

        if (Forge.Resolve(paths) is not { } forge)
        {
            return new StepResult(StepKind.Refused, "no repository found (add boards.github.owner/repo to system.yaml or set a git origin).");
        }

        var rows = LedgerScan.All(paths.Root);
        var verified = rows.LastOrDefault(r => r.Type == "QuestVerified" && r.Str("quest") == bq.Id.Value);
        if (verified?.Str("source") is not { } vSource || verified.Str("digest") is not { } vDigest)
        {
            return new StepResult(StepKind.Refused, "no verification evidence in the ledger; run 'monarch quest verify' first.");
        }

        var merge = Forge.Evidence(env).GetMergeAsync(forge.Owner, forge.Repo, pr).GetAwaiter().GetResult();
        if (merge is null)
        {
            return new StepResult(StepKind.Pending, $"PR #{pr} is not merged yet.");
        }

        var quest = QuestCommands.Reconstitute(bq, EstimateOf(paths.Root, bq.Id), new Evidence(EvidenceKind.CiRun, vSource, vDigest, verified.At));
        quest.Clear(merge);
        var submissions = rows.Count(r => r.Type == "QuestSubmitted" && r.Str("quest") == bq.Id.Value);
        var xp = XpCalculator.Compute(bq.Rank, bq.Kind, submissions);
        new LocalFileBoard(paths.Root).UpsertQuestAsync(bq with { State = QuestState.Cleared }).GetAwaiter().GetResult();
        if (ActiveStore.Read(paths) is { } active && active.QuestId == bq.Id.Value)
        {
            ActiveStore.Clear(paths);
        }

        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", "QuestCleared", ("quest", bq.Id.Value), ("gate", bq.Gate.Value), ("rank", bq.Rank.ToString()), ("kind", bq.Kind.ToString()),
            ("xp", (long)xp.Xp), ("baseXp", (long)xp.Base), ("kindFactor", xp.Kind), ("quality", xp.Quality), ("submissions", (long)submissions), ("pr", (long)pr),
            ("evidence", LedgerScan.StringObjects([[("kind", "MergeCommit"), ("source", merge.Source), ("digest", merge.Digest)]])));
        var note = string.Empty;
        if (link.Issue is not null)
        {
            try
            {
                Forge.Board(env, forge).SyncQuestAsync(bq with { State = QuestState.Cleared }).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                note = $" (warning: could not close issue #{link.Issue}: {ex.Message})";
            }
        }

        return new StepResult(StepKind.Done, $"cleared, XP {xp.Xp} (base {xp.Base} x kind {xp.Kind:0.##} x quality {xp.Quality:0.##}){note}");
    }
}
