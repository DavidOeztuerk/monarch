using System.Globalization;
using System.Text;
using System.Text.Json;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Ledger;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch recap|rest|mana</c> and <c>ledger verify</c>.</summary>
internal static class ManaCommands
{
    private const string NoNext = "(noch nicht festgelegt)";
    private const string NoDecisions = "keine";

    private static string Pct(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Writes <c>.monarch/recap/RECAP.md</c> and records the mana it cost.</summary>
    public static int Recap(CliEnv env, string? next, IReadOnlyList<string> decisions)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var config = SystemConfigReader.Load(paths.Root);
        var ledger = new ProjectLedger(paths, env.Clock);
        var session = ledger.SessionId();
        var before = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        ManaSupport.Snapshot(ledger, session, before, "recap-before");

        var (prevNext, prevDecisions) = ReadPrevious(paths.RecapFile);
        var text = Render(env, paths, config, before, next ?? prevNext, decisions.Count > 0 ? decisions : prevDecisions);
        Files.WriteAtomic(paths.RecapFile, text);

        var after = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        ManaSupport.Snapshot(ledger, session, after, "recap-after");
        var cost5 = before is null || after is null ? 0 : Math.Max(0, after.Effective5h - before.Effective5h);
        var cost7 = before is null || after is null ? 0 : Math.Max(0, after.Effective7d - before.Effective7d);
        ledger.Append(session, "cli", "RecapWritten", ("cost5h", cost5), ("cost7d", cost7), ("path", ".monarch/recap/RECAP.md"));
        env.Out.WriteLine($"recap written: {paths.RecapFile} (cost 5h {Pct(cost5)}, 7d {Pct(cost7)})");
        return 0;
    }

    private static (string? Next, IReadOnlyList<string> Decisions) ReadPrevious(string file)
    {
        if (!File.Exists(file))
        {
            return (null, []);
        }

        var lines = File.ReadAllLines(file);
        List<string> Section(string heading)
        {
            var start = Array.FindIndex(lines, l => l.TrimEnd() == "## " + heading);
            if (start < 0)
            {
                return [];
            }

            return [.. lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l.Trim()).Where(l => l.Length > 0)];
        }

        var next = string.Join("\n", Section("Nächster konkreter Schritt"));
        var decisions = Section("Offene Entscheidungen").Select(l => l.StartsWith("- ", StringComparison.Ordinal) ? l[2..] : l).Where(l => l != NoDecisions).ToList();
        return (next.Length == 0 || next == NoNext ? null : next, decisions);
    }

    private static string Render(CliEnv env, ProjectPaths paths, SystemConfig config, ManaSnapshot? snap, string? next, IReadOnlyList<string> decisions)
    {
        var board = new LocalFileBoard(paths.Root);
        var quests = board.ListQuestsAsync().GetAwaiter().GetResult();
        var rows = LedgerScan.All(paths.Root);
        var limits = ManaSupport.Resolve(paths.Root, config);
        var forge = Forge.Resolve(paths);
        ActiveFile? active = null;
        try
        {
            active = ActiveStore.Read(paths);
        }
        catch (JsonException)
        {
        }

        var sb = new StringBuilder();
        sb.AppendLine("# RECAP").AppendLine();
        sb.AppendLine("## Stand");
        sb.AppendLine($"- Zeitpunkt: {ManaSupport.Iso(env.Clock.UtcNow)}");
        sb.AppendLine($"- Plan: {config.Plan}");
        if (snap is null)
        {
            sb.AppendLine("- Mana: keine Messung vorhanden");
        }
        else
        {
            sb.AppendLine($"- Mana 5h: {Pct(snap.Effective5h)} (Limit {Pct(limits.Limits.FiveHourPct)}, Reset {ManaSupport.Iso(snap.FiveHourResetsAt)})");
            sb.AppendLine($"- Mana 7d: {Pct(snap.Effective7d)} (Limit {Pct(limits.Limits.SevenDayPct)}, Reset {ManaSupport.Iso(snap.SevenDayResetsAt)})");
        }

        sb.AppendLine($"- Limits: 5h {Pct(limits.Limits.FiveHourPct)}, 7d {Pct(limits.Limits.SevenDayPct)} ({limits.Source})").AppendLine();

        sb.AppendLine("## Aktiv");
        sb.AppendLine(active is null ? "- keine aktive Quest" : $"- Gate {active.GateId} · Quest {active.QuestId} [{active.Rank}] · Phase {active.State ?? "active"}").AppendLine();

        sb.AppendLine("## Erledigt");
        var done = quests.Where(q => q.State is QuestState.Cleared or QuestState.Verified).ToList();
        if (done.Count == 0)
        {
            sb.AppendLine("- noch nichts");
        }

        foreach (var q in done)
        {
            var pr = board.FindQuestFile(q.Id.Value) is { } f ? BoardFiles.Link(f).Pr : null;
            var prText = pr is null ? "kein PR" : forge is null ? $"PR #{pr}" : $"https://github.com/{forge.Owner}/{forge.Repo}/pull/{pr}";
            var sources = rows.Where(r => (r.Type is "QuestVerified" or "QuestCleared") && r.Str("quest") == q.Id.Value && r.Payload.TryGetProperty("evidence", out _))
                .SelectMany(r => r.Payload.GetProperty("evidence").EnumerateArray().Select(e => e.TryGetProperty("source", out var s) ? s.GetString() : null))
                .OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            sb.AppendLine($"- {q.Id.Value} [{q.Rank}] {QuestCommands.StateName(q.State)}: {q.Title} - {prText}" + (sources.Count == 0 ? string.Empty : " - Evidence: " + string.Join(", ", sources)));
        }

        sb.AppendLine().AppendLine("## Offen");
        var open = false;
        foreach (var q in quests.Where(q => q.State is QuestState.Accepted or QuestState.InProgress))
        {
            if (board.FindQuestFile(q.Id.Value) is not { } file)
            {
                continue;
            }

            foreach (var item in BoardFiles.OpenDod(file))
            {
                sb.AppendLine($"- {q.Id.Value}: {item}");
                open = true;
            }
        }

        if (!open)
        {
            sb.AppendLine("- keine offenen DoD-Punkte");
        }

        sb.AppendLine().AppendLine("## Nächster konkreter Schritt");
        sb.AppendLine(next ?? NoNext).AppendLine();
        sb.AppendLine("## Offene Entscheidungen");
        if (decisions.Count == 0)
        {
            sb.AppendLine($"- {NoDecisions}");
        }

        foreach (var d in decisions)
        {
            sb.AppendLine($"- {d}");
        }

        return sb.ToString();
    }

    /// <summary>Shows the reset times, the Mana-Guard decision for the next default step and the resume time.</summary>
    public static int Rest(CliEnv env)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var config = SystemConfigReader.Load(paths.Root);
        var snap = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        var ledger = new ProjectLedger(paths, env.Clock);
        if (snap is null)
        {
            env.Out.WriteLine("No mana measurements yet (statusline sensor not active); cannot compute a resume time.");
            ledger.Append(ledger.SessionId(), "cli", "RestStarted", ("known", false));
            return 0;
        }

        var limits = ManaSupport.Resolve(paths.Root, config).Limits;
        var now = env.Clock.UtcNow;
        var decision = ManaGuard.Decide(snap, ManaSupport.Step5h, ManaSupport.Step7d, QuestCommands.RecapReservePct, limits, now);
        var blocked5 = snap.Effective5h + ManaSupport.Step5h + QuestCommands.RecapReservePct > limits.FiveHourPct;
        var blocked7 = snap.Effective7d + ManaSupport.Step7d > limits.SevenDayPct;
        var reset = blocked7 && (!blocked5 || snap.SevenDayResetsAt > snap.FiveHourResetsAt) ? snap.SevenDayResetsAt : snap.FiveHourResetsAt;
        var zone = TimeZoneInfo.Local;
        var wake = WakePlan.WakeTime(reset, zone);
        var recap = paths.RecapFile;
        env.Out.WriteLine($"5h window resets at {ManaSupport.Iso(snap.FiveHourResetsAt)} (used {Pct(snap.Effective5h)}, limit {Pct(limits.FiveHourPct)})");
        env.Out.WriteLine($"7d window resets at {ManaSupport.Iso(snap.SevenDayResetsAt)} (used {Pct(snap.Effective7d)}, limit {Pct(limits.SevenDayPct)})");
        env.Out.WriteLine($"Mana-Guard for the next default step ({Pct(ManaSupport.Step5h)}): {decision}");
        env.Out.WriteLine($"Wake at {ManaSupport.Iso(wake)} (reset + {WakePlan.Slack.TotalMinutes:0} min, minute not :00/:30), local {TimeZoneInfo.ConvertTime(wake, zone).ToString("yyyy-MM-dd HH:mm zzz", CultureInfo.InvariantCulture)}");
        env.Out.WriteLine($"Cron: {WakePlan.Cron(wake, zone)}");
        env.Out.WriteLine($"Prompt: {WakePlan.Prompt(recap)}");
        env.Out.WriteLine("CronCreate (one-shot) with this cron and prompt is mandatory (session wake call).");
        if (config.Wake != "off")
        {
            env.Out.WriteLine($"Opt-in {config.Wake}: monarch wake schedule --session <session-uuid> --at {ManaSupport.Iso(wake)}");
        }

        ledger.Append(ledger.SessionId(), "cli", "RestStarted", ("known", true), ("decision", decision.ToString()), ("resumeAt", ManaSupport.Iso(wake)));
        return 0;
    }

    /// <summary>Prints the snapshot, limits and whether an estimate fits.</summary>
    public static int Mana(CliEnv env, double? estimate)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var config = SystemConfigReader.Load(paths.Root);
        var snap = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        var resolved = ManaSupport.Resolve(paths.Root, config);
        var ledger = new ProjectLedger(paths, env.Clock);
        env.Out.WriteLine($"Plan {config.Plan}: start limits 5h {Pct(config.Limits.FiveHourPct)}, 7d {Pct(config.Limits.SevenDayPct)}");
        env.Out.WriteLine($"Limits in use ({resolved.Source}): 5h {Pct(resolved.Limits.FiveHourPct)}, 7d {Pct(resolved.Limits.SevenDayPct)}; recaps measured {resolved.Recaps} (adaptive from {AdaptiveLimit.MinRecaps})");
        if (snap is null)
        {
            env.Out.WriteLine("No mana measurements yet (statusline sensor not active).");
            return 0;
        }

        ManaSupport.Snapshot(ledger, ledger.SessionId(), snap, "mana");
        env.Out.WriteLine($"5h: {Pct(snap.Effective5h)} used, resets {ManaSupport.Iso(snap.FiveHourResetsAt)}{(snap.IsStale ? " (stale)" : string.Empty)}");
        env.Out.WriteLine($"7d: {Pct(snap.Effective7d)} used, resets {ManaSupport.Iso(snap.SevenDayResetsAt)}");
        var (pair, factor) = ManaCalibration.Current(env, paths);
        var raw5 = estimate ?? ManaSupport.Step5h;
        var step5 = raw5 * factor.Factor;
        var step7 = (estimate is null ? ManaSupport.Step7d : estimate.Value * (ManaSupport.Step7d / ManaSupport.Step5h)) * factor.Factor;
        env.Out.WriteLine(ManaCalibrator.Display(pair, factor));
        if (Math.Abs(factor.Factor - 1) > 1e-9)
        {
            env.Out.WriteLine(FormattableString.Invariant($"Estimate {Pct(raw5)} x {factor.Factor:0.00} = {Pct(step5)}"));
        }

        var decision = ManaGuard.Decide(snap, step5, step7, QuestCommands.RecapReservePct, resolved.Limits, env.Clock.UtcNow);
        var can5 = snap.Effective5h + step5 + QuestCommands.RecapReservePct <= resolved.Limits.FiveHourPct;
        env.Out.WriteLine($"Step {Pct(step5)} (7d {Pct(step7)}) + recap {Pct(QuestCommands.RecapReservePct)}: {decision} (5h {(can5 ? "fits" : "does not fit")})");
        return 0;
    }

    /// <summary>Checks the hash chain of every ledger file.</summary>
    public static int LedgerVerify(CliEnv env)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var result = LedgerVerifier.Verify(paths.Root);
        foreach (var file in result.Restorable ?? [])
        {
            env.Out.WriteLine($"{file}: original content is back (git stash pop / branch switch); the next ledger append anchors it again and writes LedgerAnchorRestored.");
        }

        // ADR-0007 T3: a cleared quest whose pull request has no GateMerged was not merged by 'monarch gate merge'. A hint, never a finding.
        List<LedgerRow> rows;
        try
        {
            rows = LedgerScan.All(paths.Root);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            rows = []; // a corrupt or non-object line is a verify finding below, not a reason to crash
        }

        foreach (var cleared in rows.Where(r => r.Type == "QuestCleared" && r.Num("pr") is not null))
        {
            var pr = (int)cleared.Num("pr")!;
            if (!rows.Exists(r => r.Type == "GateMerged" && (int?)r.Num("pr") == pr))
            {
                env.Out.WriteLine($"note: {cleared.Str("quest")} was cleared via PR #{pr} without GateMerged in the ledger (not merged by 'monarch gate merge').");
            }
        }

        if (result.Ok)
        {
            env.Out.WriteLine($"ledger ok ({result.AnchoredLocally} anchored locally, {result.AnchoredByGit} anchored by git)");
            return 0;
        }

        foreach (var v in result.Violations)
        {
            env.Out.WriteLine($"{v.File}:{v.Line} {v.Kind}: {v.Detail}");
        }

        env.Out.WriteLine($"ledger: {result.Violations.Count} violation(s)");
        return 1;
    }

    /// <summary>
    /// G3 (ADR-0011): runs the ledger verification before a clear. On findings it appends <c>ClearRefusedLedgerInvalid</c> and returns the refusal text;
    /// null means the ledger is clean.
    /// </summary>
    public static string? VerifyBeforeClear(CliEnv env, ProjectPaths paths, string questId)
    {
        var result = LedgerVerifier.Verify(paths.Root);
        if (result.Ok)
        {
            return null;
        }

        var findings = string.Join("; ", result.Violations.Take(5).Select(v => $"{v.File}:{v.Line} {v.Kind}"));
        try
        {
            var ledger = new ProjectLedger(paths, env.Clock);
            ledger.Append(ledger.SessionId(), "cli", "ClearRefusedLedgerInvalid", ("quest", questId), ("violations", (long)result.Violations.Count), ("first", findings));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // A ledger too broken to append to still refuses the clear.
        }

        return $"ledger verify reports {result.Violations.Count} finding(s) ({findings}); clear refused until the ledger is clean (monarch ledger verify).";
    }

    /// <summary>Acknowledges a <c>LedgerAnchorReset</c> event; the only way to clear that finding. Not a tool path: runs through the CLI as actor <c>cli</c>.</summary>
    public static int LedgerAckReset(CliEnv env, string eventHash)
    {
        // Owner action (ADR-0011 rule 7): the agent runs without a terminal on stdin, so a pipe or /dev/null is refused before anything is read or written.
        if (!env.StdinIsTty)
        {
            env.Err.WriteLine("refused: 'ledger ack-reset' is an owner action and needs an interactive terminal on stdin (Terminal erforderlich).");
            return 1;
        }

        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            env.Err.WriteLine("refused: not an awakened project.");
            return 1;
        }

        var reset = LedgerVerifier.Verify(paths.Root).Resets?.FirstOrDefault(r => string.Equals(r.Hash, eventHash, StringComparison.OrdinalIgnoreCase));
        if (reset is null)
        {
            env.Err.WriteLine($"refused: no LedgerAnchorReset event with hash {eventHash}.");
            return 1;
        }

        if (reset.Acknowledged)
        {
            env.Out.WriteLine($"already acknowledged: {reset.Hash}");
            return 0;
        }

        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", "LedgerAnchorResetAcknowledged", ("reset", reset.Hash), ("file", reset.File));
        env.Out.WriteLine($"acknowledged ledger anchor reset {reset.Hash} ({reset.Reason})");
        return 0;
    }

    /// <summary>MCP <c>mana_forecast</c>: asks the Mana-Guard about the summed estimates.</summary>
    public static string Forecast(CliEnv env, ProjectPaths paths, double rawSession, double rawWeekly)
    {
        var (pair, factor) = ManaCalibration.Current(env, paths);
        var session = rawSession * factor.Factor;
        var weekly = rawWeekly * factor.Factor;
        var config = SystemConfigReader.Load(paths.Root);
        var resolved = ManaSupport.Resolve(paths.Root, config);
        var snap = ManaStore.ToSnapshot(ManaStore.Read(env.Home), env.Clock.UtcNow);
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("estimateSessionPct", rawSession);
            w.WriteNumber("estimateWeeklyPct", rawWeekly);
            w.WriteNumber("stepSessionPct", session);
            w.WriteNumber("stepWeeklyPct", weekly);
            w.WriteStartObject("pair");
            w.WriteString("model", pair.Model);
            w.WriteString("effort", pair.Effort);
            w.WriteNumber("factor", factor.Factor);
            w.WriteNumber("n", factor.N);
            w.WriteString("status", factor.IsCalibrated ? "kalibriert" : "unkalibriert");
            w.WriteString("display", ManaCalibrator.Display(pair, factor));
            w.WriteEndObject();
            w.WriteNumber("recapReservePct", QuestCommands.RecapReservePct);
            w.WriteStartObject("limits");
            w.WriteNumber("fiveHourPct", resolved.Limits.FiveHourPct);
            w.WriteNumber("sevenDayPct", resolved.Limits.SevenDayPct);
            w.WriteEndObject();
            w.WriteString("limitSource", resolved.Source);
            if (snap is null)
            {
                w.WriteNull("decision");
                w.WriteNull("headroom");
                w.WriteString("note", "no mana measurements yet (statusline sensor not active)");
            }
            else
            {
                w.WriteString("decision", ManaGuard.Decide(snap, session, weekly, QuestCommands.RecapReservePct, resolved.Limits, env.Clock.UtcNow).ToString());
                w.WriteStartObject("headroom");
                w.WriteNumber("fiveHourPct", resolved.Limits.FiveHourPct - snap.Effective5h - QuestCommands.RecapReservePct - session);
                w.WriteNumber("sevenDayPct", resolved.Limits.SevenDayPct - snap.Effective7d - weekly);
                w.WriteEndObject();
                w.WriteStartObject("snapshot");
                w.WriteNumber("fiveHourUsedPct", snap.Effective5h);
                w.WriteNumber("sevenDayUsedPct", snap.Effective7d);
                w.WriteBoolean("stale", snap.IsStale);
                w.WriteEndObject();
            }

            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
