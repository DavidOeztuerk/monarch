using System.Text;
using System.Text.Json;
using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>
/// The hook hot path: no SQLite, no processes, no network, no DI container. Any failure is logged to
/// <c>.monarch/local/hook-errors.log</c> and never becomes a silent allow for rank A or higher (ADR-0003).
/// </summary>
internal static class HookRunner
{
    private const string ErrorReason = "Monarch-Hook-Fehler: Freigabe nötig";

    /// <summary>Handles one hook event; always returns 0.</summary>
    public static int Run(string evt, CliEnv env)
    {
        string raw;
        try
        {
            raw = env.In.ReadToEnd();
        }
        catch (IOException)
        {
            raw = string.Empty;
        }

        HookInput? input = null;
        Exception? parseError = null;
        try
        {
            input = raw.Length == 0 ? new HookInput() : HookInput.Parse(raw) ?? new HookInput();
        }
        catch (JsonException ex)
        {
            parseError = ex;
        }

        var cwd = !string.IsNullOrEmpty(input?.Cwd) && Path.IsPathRooted(input.Cwd) ? input.Cwd : env.Cwd;
        var paths = ProjectPaths.Locate(cwd, env.Home) ?? ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            return 0;
        }

        try
        {
            if (parseError is not null)
            {
                throw parseError;
            }

            Dispatch(evt, input!, paths, env);
        }
        catch (Exception ex)
        {
            OnError(evt, ex, paths, env);
        }

        return 0;
    }

    private static void Dispatch(string evt, HookInput input, ProjectPaths paths, CliEnv env)
    {
        var ledger = new ProjectLedger(paths, env.Clock);
        switch (evt)
        {
            case "pre-tool-use": PreToolUse(input, paths, env, ledger); break;
            case "post-tool-use": Append(paths, env, () => ledger.Append(ledger.SessionId(input.SessionId), Actor(input), "ToolUsed",
                ("tool", input.ToolName), ("agent_type", input.AgentType), ("agent_id", input.AgentId), ("file", FileOf(input)), ("quest", ActiveId(paths)))); break;
            case "subagent-stop": Append(paths, env, () => ledger.Append(ledger.SessionId(input.SessionId), Actor(input), "ShadowRunCompleted",
                ("agent_id", input.AgentId), ("agent_type", input.AgentType), ("quest", ActiveId(paths)))); break;
            case "stop": Append(paths, env, () => ledger.Append(ledger.SessionId(input.SessionId), Actor(input), "TurnStopped", ("quest", ActiveId(paths)))); break;
            case "session-start": SessionStart(input, paths, env); break;
            case "pre-compact": PreCompact(input, paths, ledger); break;
            case "session-end": SessionEnd(input, paths, env, ledger); break;
            default: break;
        }
    }

    private static string Actor(HookInput i) => i.AgentType ?? "hook";

    private static string? FileOf(HookInput i) => i.ToolInput?.FilePath ?? i.ToolInput?.NotebookPath ?? (i.ToolName is "Grep" or "Glob" ? i.ToolInput?.Path : null);

    private static string? ActiveId(ProjectPaths paths) => ActiveStore.Read(paths)?.QuestId;

    // A ledger failure after a decision was printed must not change the decision; it is only logged.
    private static void Append(ProjectPaths paths, CliEnv env, Action append)
    {
        try
        {
            append();
        }
        catch (Exception ex)
        {
            Log(paths, env, "ledger", ex);
        }
    }

    private static void PreToolUse(HookInput input, ProjectPaths paths, CliEnv env, ProjectLedger ledger)
    {
        if (string.IsNullOrEmpty(input.ToolName))
        {
            return;
        }

        var config = SystemConfigReader.Load(paths.Root);
        var active = ActiveStore.Read(paths);
        var call = new ToolCall(input.ToolName, FileOf(input), input.ToolInput?.Command, input.AgentType, input.AgentId, input.PermissionMode, input.Cwd ?? env.Cwd, input.ToolInput?.Glob);
        var ctx = new PolicyContext(paths.Root, active is null ? null : ActiveStore.ToPolicy(active), false, config.ToPolicyOptions());
        var decision = ToolCallPolicy.Evaluate(call, ctx);

        if (decision.Kind != PolicyDecisionKind.Allow)
        {
            Emit(env, new HookSpecificOutput
            {
                HookEventName = "PreToolUse",
                PermissionDecision = decision.Kind == PolicyDecisionKind.Deny ? "deny" : "ask",
                PermissionDecisionReason = decision.Reason,
            });
        }

        var kind = decision.Kind == PolicyDecisionKind.Deny ? "deny" : "ask";
        var signaled = decision.Signals.Contains(PolicySignals.DoubleDungeon);
        if (decision.Kind == PolicyDecisionKind.Allow && !signaled)
        {
            return;
        }

        Append(paths, env, () =>
        {
            var session = ledger.SessionId(input.SessionId);
            if (decision.Kind != PolicyDecisionKind.Allow)
            {
                ledger.Append(session, Actor(input), "PolicyDecided", ("rule", decision.Rule), ("kind", kind), ("tool", input.ToolName),
                    ("agent_type", input.AgentType), ("quest", active?.QuestId), ("reason", decision.Reason));
            }

            if (signaled)
            {
                ledger.Append(session, Actor(input), "DoubleDungeonSignaled", ("tool", input.ToolName), ("file", FileOf(input)), ("quest", active?.QuestId));
            }
        });
    }

    private static void SessionStart(HookInput input, ProjectPaths paths, CliEnv env)
    {
        if (!string.IsNullOrWhiteSpace(input.SessionId))
        {
            Files.WriteAtomic(paths.CurrentSession, ProjectLedger.Sanitize(input.SessionId.Trim()));
        }

        Emit(env, new HookSpecificOutput { HookEventName = "SessionStart", AdditionalContext = Briefing.Build(paths, env) });
    }

    private static void PreCompact(HookInput input, ProjectPaths paths, ProjectLedger ledger)
    {
        var session = ledger.SessionId(input.SessionId);
        var active = ActiveStore.Read(paths);
        var sb = new StringBuilder();
        sb.Append("# Handoff ").AppendLine(session);
        sb.AppendLine();
        sb.AppendLine(active is null
            ? "Aktive Quest: keine"
            : $"Aktive Quest: {active.QuestId} [{active.Rank}] Gate {active.GateId} ({active.State ?? "aktiv"})");
        if (active is not null)
        {
            sb.Append("Scope include: ").AppendLine(string.Join(", ", active.Scope.Include));
            sb.Append("Scope exclude: ").AppendLine(string.Join(", ", active.Scope.Exclude));
        }

        sb.AppendLine(File.Exists(paths.RecapFile) ? "Recap: .monarch/recap/RECAP.md (lies zuerst)" : "Recap: noch keiner vorhanden");
        sb.AppendLine().AppendLine("Letzte 10 Ereignisse dieser Session:");
        var entries = ledger.ReadSession(session);
        foreach (var e in entries.Skip(Math.Max(0, entries.Count - 10)))
        {
            sb.Append("- ").Append(ManaStore.Stamp(e.At)).Append(' ').Append(e.Type).Append(' ').AppendLine(e.Payload.GetRawText());
        }

        var file = Path.Combine(paths.HandoffDir, session + ".md");
        Files.WriteAtomic(file, sb.ToString());
        ledger.Append(session, "hook", "HandoffWritten", ("path", $".monarch/local/handoff/{session}.md"), ("quest", active?.QuestId));
    }

    private static void SessionEnd(HookInput input, ProjectPaths paths, CliEnv env, ProjectLedger ledger)
    {
        var session = ledger.SessionId(input.SessionId);
        ledger.Append(session, "hook", "SessionEnded", ("quest", ActiveId(paths)));
        var mana = ManaStore.Read(env.Home);
        if (ManaStore.ToSnapshot(mana, env.Clock.UtcNow) is { } s)
        {
            ledger.Append(session, "hook", "ManaSnapshotTaken", ("five_hour_used_pct", s.FiveHourUsedPct), ("five_hour_resets_at", ManaStore.Stamp(s.FiveHourResetsAt)),
                ("seven_day_used_pct", s.SevenDayUsedPct), ("seven_day_resets_at", ManaStore.Stamp(s.SevenDayResetsAt)), ("stale", s.IsStale),
                ("context_used_pct", ManaStore.ContextOf(mana, input.SessionId)));
        }
    }

    private static void OnError(string evt, Exception ex, ProjectPaths paths, CliEnv env)
    {
        Log(paths, env, evt, ex);
        if (evt != "pre-tool-use")
        {
            return;
        }

        try
        {
            if (ActiveStore.Read(paths) is { } a && ActiveStore.RankOf(a) >= Rank.A)
            {
                Emit(env, new HookSpecificOutput { HookEventName = "PreToolUse", PermissionDecision = "ask", PermissionDecisionReason = ErrorReason });
            }
        }
        catch (Exception inner)
        {
            Log(paths, env, evt, inner);
        }
    }

    private static void Log(ProjectPaths paths, CliEnv env, string evt, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(paths.Local);
            File.AppendAllText(paths.HookErrors, $"{ManaStore.Stamp(env.Clock.UtcNow)} {evt} {ex.GetType().Name}: {ex.Message.ReplaceLineEndings(" ")}\n");
        }
        catch (Exception)
        {
            // Nothing left to do; the hook must stay quiet.
        }
    }

    private static void Emit(CliEnv env, HookSpecificOutput body)
    {
        env.Out.WriteLine(JsonSerializer.Serialize(new HookOutput { HookSpecific = body }, CliJson.Default.HookOutput));
    }
}

/// <summary>The SessionStart briefing (kept to roughly 300 tokens).</summary>
internal static class Briefing
{
    /// <summary>Builds the briefing text.</summary>
    public static string Build(ProjectPaths paths, CliEnv env)
    {
        var config = SystemConfigReader.Load(paths.Root);
        var sb = new StringBuilder();
        sb.AppendLine("Monarch-Briefing:");
        var active = ActiveStore.Read(paths);
        sb.AppendLine(active is null
            ? "- Quest: keine aktive Quest (quest accept <id>)."
            : $"- Quest: {active.QuestId} [{active.Rank}] Gate {active.GateId}, Status {active.State ?? "aktiv"}; Scope {string.Join(", ", active.Scope.Include)}.");
        var mana = ManaStore.Read(env.Home);
        if (ManaStore.ToSnapshot(mana, env.Clock.UtcNow) is { } s)
        {
            sb.AppendLine(FormattableString.Invariant(
                $"- Mana 5h: {s.FiveHourUsedPct:0}% (Limit {config.Limits.FiveHourPct:0}%, Reset {s.FiveHourResetsAt:HH:mm} UTC); 7d: {s.SevenDayUsedPct:0}% (Limit {config.Limits.SevenDayPct:0}%){(s.IsStale ? " [veraltet]" : "")}."));
        }
        else
        {
            sb.AppendLine("- Mana: keine Messwerte (Statusline-Sensor noch nicht aktiv).");
        }

        sb.AppendLine($"- Penalty Zone: aus (Modus {config.PenaltyZone.ToString().ToLowerInvariant()}).");
        if (File.Exists(paths.RecapFile))
        {
            sb.AppendLine("- Recap vorhanden: lies zuerst .monarch/recap/RECAP.md");
        }

        sb.Append("- Regeln: deny/ask erzwingen Hooks (Scope-Lock, geschuetzte Pfade, Secrets, deny_bash); bei Ablehnung nicht umgehen, sondern melden.");
        return sb.ToString();
    }
}
