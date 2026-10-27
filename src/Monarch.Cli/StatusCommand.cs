using System.Text;
using System.Text.Json;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Ledger;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch status [--json]</c>.</summary>
internal static class StatusCommand
{
    private const double StepEstimate5h = 5;
    private const double StepEstimate7d = 1;

    /// <summary>Runs the command.</summary>
    public static int Run(CliEnv env, bool json)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            env.Err.WriteLine("Not an awakened project (run 'monarch awaken').");
            return 1;
        }

        var config = SystemConfigReader.Load(paths.Root);
        ActiveFile? active = null;
        try
        {
            active = ActiveStore.Read(paths);
        }
        catch (JsonException)
        {
            env.Err.WriteLine("warning: .monarch/local/active.json is corrupt.");
        }

        var quests = new LocalFileBoard(paths.Root).ListQuestsAsync().GetAwaiter().GetResult();
        var counts = quests.GroupBy(q => q.State).OrderBy(g => g.Key).Select(g => (Name: QuestCommands.StateName(g.Key), Count: g.Count())).ToArray();
        var xp = TotalXp(paths.Root);
        var level = 1 + (xp / 100);
        var snapshot = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        ManaDecision? guard = snapshot is null ? null : ManaGuard.Decide(snapshot, StepEstimate5h, StepEstimate7d, QuestCommands.RecapReservePct, config.Limits, env.Clock.UtcNow);
        var ledger = LedgerVerifier.Verify(paths.Root);

        if (json)
        {
            WriteJson(env, active, counts, xp, level, snapshot, config, guard, ledger.Ok);
            return 0;
        }

        var sb = new StringBuilder();
        sb.AppendLine(active is null ? "Active quest: none" : $"Active quest: {active.QuestId} [{active.Rank}] {active.State ?? "active"} (gate {active.GateId}, scope {string.Join(", ", active.Scope.Include)})");
        sb.AppendLine("Quests: " + (counts.Length == 0 ? "none" : string.Join(" · ", counts.Select(c => $"{c.Name} {c.Count}"))));
        sb.AppendLine($"Level {level} · XP {xp} (placeholder: 100 XP per level)");
        if (snapshot is null)
        {
            sb.AppendLine("Mana: no measurements yet (statusline sensor not active)");
        }
        else
        {
            sb.AppendLine(FormattableString.Invariant($"Mana 5h: {snapshot.Effective5h:0}% / limit {config.Limits.FiveHourPct:0}% · 7d: {snapshot.Effective7d:0}% / limit {config.Limits.SevenDayPct:0}%{(snapshot.IsStale ? " (stale)" : "")}"));
            sb.AppendLine(FormattableString.Invariant($"Mana-Guard for a {StepEstimate5h:0}% step: {guard}"));
        }

        sb.AppendLine("Penalty zone: off (not computed in phase 0)");
        sb.AppendLine($"Plan: {config.Plan} · theme: {config.Theme}");
        sb.Append(ledger.Ok ? "Ledger: ok" : $"Ledger: {ledger.Violations.Count} violation(s) - run the verifier");
        env.Out.WriteLine(sb.ToString());
        return 0;
    }

    private static long TotalXp(string root)
    {
        var dir = Path.Combine(root, ".monarch", "ledger");
        long xp = 0;
        if (!Directory.Exists(dir))
        {
            return xp;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (!line.Contains("\"QuestCleared\"", StringComparison.Ordinal))
                {
                    continue;
                }

                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "QuestCleared"
                    && doc.RootElement.TryGetProperty("payload", out var p) && p.TryGetProperty("xp", out var x) && x.TryGetInt64(out var v))
                {
                    xp += v;
                }
            }
        }

        return xp;
    }

    private static void WriteJson(CliEnv env, ActiveFile? active, (string Name, int Count)[] counts, long xp, long level, ManaSnapshot? s, SystemConfig config, ManaDecision? guard, bool ledgerOk)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            if (active is null) w.WriteNull("activeQuest"); else w.WriteString("activeQuest", active.QuestId);
            w.WriteStartObject("quests");
            foreach (var (name, count) in counts) w.WriteNumber(name, count);
            w.WriteEndObject();
            w.WriteNumber("xp", xp);
            w.WriteNumber("level", level);
            if (s is null)
            {
                w.WriteNull("mana");
            }
            else
            {
                w.WriteStartObject("mana");
                w.WriteNumber("fiveHourUsedPct", s.Effective5h);
                w.WriteNumber("fiveHourLimitPct", config.Limits.FiveHourPct);
                w.WriteNumber("sevenDayUsedPct", s.Effective7d);
                w.WriteNumber("sevenDayLimitPct", config.Limits.SevenDayPct);
                w.WriteBoolean("stale", s.IsStale);
                w.WriteEndObject();
            }

            if (guard is null) w.WriteNull("guard"); else w.WriteString("guard", guard.ToString());
            w.WriteBoolean("penaltyZone", false);
            w.WriteBoolean("ledgerOk", ledgerOk);
            w.WriteEndObject();
        }

        env.Out.WriteLine(Encoding.UTF8.GetString(ms.ToArray()));
    }
}
