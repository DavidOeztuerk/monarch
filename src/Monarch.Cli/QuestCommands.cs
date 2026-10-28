using System.Globalization;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch quest ...</c> (accept, start, submit, abandon, fail, list, show).</summary>
internal static class QuestCommands
{
    /// <summary>Default recap reserve in percentage points used by the Mana-Guard.</summary>
    public const double RecapReservePct = 5;

    /// <summary>Applies a lifecycle transition.</summary>
    public static int Transition(CliEnv env, string action, string id)
    {
        try
        {
            return TransitionCore(env, action, id);
        }
        catch (Exception ex) when (ex is DomainException or FormatException or IOException or System.Text.Json.JsonException)
        {
            env.Err.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static int Refuse(CliEnv env, string message)
    {
        env.Err.WriteLine($"refused: {message}");
        return 1;
    }

    private static int TransitionCore(CliEnv env, string action, string id)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            return Refuse(env, "not an awakened project (run 'monarch awaken').");
        }

        var board = new LocalFileBoard(paths.Root);
        var quests = board.ListQuestsAsync().GetAwaiter().GetResult();
        var bq = quests.FirstOrDefault(q => q.Id.Value == id);
        if (bq is null)
        {
            return Refuse(env, $"quest {id} not found in .monarch/quests/.");
        }

        var estimate = board.ReadEstimate(bq.Id) ?? new ManaCost(5, 1, 0m);
        var quest = Reconstitute(bq, estimate);
        var config = SystemConfigReader.Load(paths.Root);
        var active = ActiveStore.Read(paths);
        string? pairLine = null;
        string? estimateLine = null;

        if (action == "accept")
        {
            if (active is not null)
            {
                return Refuse(env, $"quest {active.QuestId} is already active (one active quest per project, ADR-0001).");
            }

            if (!QuestAcceptancePolicy.CanAccept(bq.Kind, false, config.PenaltyZone))
            {
                return Refuse(env, "Penalty Zone is active; only Penalty, Emergency and Daily quests can be accepted.");
            }

            var snapshot = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
            var (pair, factor) = ManaCalibration.Current(env, paths);
            var scaled = new ManaCost(estimate.SessionPct * factor.Factor, estimate.WeeklyPct * factor.Factor, estimate.GoldUsd);
            pairLine = ManaCalibrator.Display(pair, factor);
            if (snapshot is not null)
            {
                var decision = ManaGuard.Decide(snapshot, scaled.SessionPct, scaled.WeeklyPct, RecapReservePct, config.Limits, env.Clock.UtcNow);
                if (decision != ManaDecision.Proceed)
                {
                    return Refuse(env, FormattableString.Invariant(
                        $"Mana does not fit ({decision}): 5h {snapshot.Effective5h:0}% + step {scaled.SessionPct:0.#}% + recap {RecapReservePct:0}% exceeds limit {config.Limits.FiveHourPct:0}% (or 7d {snapshot.Effective7d:0}% + {scaled.WeeklyPct:0.#}% vs {config.Limits.SevenDayPct:0}%). {pairLine}"));
                }
            }

            estimateLine = FormattableString.Invariant($"Schätzung 5h {estimate.SessionPct:0.#}% x {factor.Factor:0.00} = {scaled.SessionPct:0.#}%");
        }

        string eventType;
        switch (action)
        {
            case "accept": quest.Accept(new ShadowId("main")); eventType = "QuestAccepted"; break;
            case "start": quest.Start(); eventType = "QuestStarted"; break;
            case "submit": quest.Submit(); eventType = "QuestSubmitted"; break;
            case "abandon": quest.Abandon(); eventType = "QuestAbandoned"; break;
            case "fail": quest.Fail(); eventType = "QuestFailed"; break;
            default: return Refuse(env, $"unknown action '{action}'.");
        }

        board.UpsertQuestAsync(bq with { State = quest.State }).GetAwaiter().GetResult();
        var stateName = quest.State == QuestState.InProgress ? "in_progress" : quest.State.ToString().ToLowerInvariant();
        if (action is "accept" or "start")
        {
            ActiveStore.Write(paths, new ActiveFile
            {
                QuestId = bq.Id.Value,
                GateId = bq.Gate.Value,
                Rank = bq.Rank.ToString(),
                Kind = bq.Kind.ToString().ToLowerInvariant(),
                Scope = new ActiveScope { Include = [.. bq.Scope.Include], Exclude = [.. bq.Scope.Exclude] },
                AcceptedAt = active?.AcceptedAt is { Length: > 0 } at ? at : ManaStore.Stamp(env.Clock.UtcNow),
                State = stateName,
            });
        }
        else if (active is null || active.QuestId == bq.Id.Value)
        {
            ActiveStore.Clear(paths);
        }

        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", eventType, ("quest", bq.Id.Value), ("gate", bq.Gate.Value), ("rank", bq.Rank.ToString()), ("state", stateName));
        if (action == "accept")
        {
            ManaCalibration.Begin(env, paths, bq.Id.Value, estimate.SessionPct);
        }
        else if (action == "submit")
        {
            ManaCalibration.Finish(env, paths, ledger, bq.Id.Value);
        }

        env.Out.WriteLine($"{bq.Id.Value}: {action} ok -> {stateName}");
        if (pairLine is not null)
        {
            env.Out.WriteLine(estimateLine);
            env.Out.WriteLine(pairLine);
        }

        return 0;
    }

    /// <summary>Rebuilds the domain aggregate at the board's state by replaying the lifecycle.</summary>
    internal static Quest Reconstitute(BoardQuest bq, ManaCost estimate, Evidence? verification = null)
    {
        var q = new Quest(bq.Id, bq.Gate, bq.Kind, bq.Rank, estimate, bq.DefinitionOfDone, bq.Scope);
        switch (bq.State)
        {
            case QuestState.Proposed: break;
            case QuestState.Accepted: q.Accept(new ShadowId("main")); break;
            case QuestState.InProgress: q.Accept(new ShadowId("main")); q.Start(); break;
            case QuestState.InReview: q.Accept(new ShadowId("main")); q.Start(); q.Submit(); break;
            case QuestState.Verified when verification is not null: q.Accept(new ShadowId("main")); q.Start(); q.Submit(); q.Verify(verification); break;
            default: throw new DomainException($"Quest {bq.Id.Value} is {bq.State}; the CLI cannot transition it with this command.");
        }

        return q;
    }

    /// <summary>Lists quests, optionally filtered by state.</summary>
    public static int List(CliEnv env, string? state)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            return Refuse(env, "not an awakened project.");
        }

        var quests = new LocalFileBoard(paths.Root).ListQuestsAsync().GetAwaiter().GetResult();
        foreach (var q in quests.Where(q => state is null || StateName(q.State) == state.Replace('-', '_').ToLowerInvariant()))
        {
            env.Out.WriteLine($"{q.Id.Value}  [{q.Rank}] {q.Kind.ToString().ToLowerInvariant(),-9} {StateName(q.State),-12} {q.Title}");
        }

        return 0;
    }

    /// <summary>Prints one quest file.</summary>
    public static int Show(CliEnv env, string id)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        var file = paths is null ? null : new LocalFileBoard(paths.Root).FindQuestFile(id);
        if (file is null)
        {
            return Refuse(env, $"quest {id} not found.");
        }

        env.Out.Write(File.ReadAllText(file));
        return 0;
    }

    /// <summary>Wire name of a state.</summary>
    public static string StateName(QuestState s) => s == QuestState.InProgress ? "in_progress" : s == QuestState.InReview ? "in_review" : s.ToString().ToLowerInvariant();

    /// <summary>Formats a number invariantly without trailing zeros.</summary>
    public static string N(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
