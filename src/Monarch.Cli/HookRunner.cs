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
        // Every awakened project around the cwd, nearest first: all of them are evaluated in full (ADR-0011 rule 6).
        var chain = ProjectPaths.LocateAll(cwd, env.Home);
        if (chain.Count == 0)
        {
            chain = ProjectPaths.LocateAll(env.Cwd, env.Home);
        }

        var primary = chain.FirstOrDefault();
        List<ProjectPaths> targets = [];
        if (evt == "pre-tool-use" && parseError is null && input is { ToolName: { Length: > 0 } })
        {
            try
            {
                targets = TargetProjects(input, cwd, chain, env);
            }
            catch (Exception ex)
            {
                // Which projects the call reaches is unknown: never a silent allow.
                if (primary is not null)
                {
                    Log(primary, env, evt, ex);
                }

                Emit(env, new HookSpecificOutput { HookEventName = "PreToolUse", PermissionDecision = "ask", PermissionDecisionReason = ErrorReason });
                return 0;
            }
        }

        var paths = primary ?? (targets.Count > 0 ? targets[0] : null);
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

            if (evt == "pre-tool-use")
            {
                PreToolUse(input!, cwd, chain, targets, env);
            }
            else
            {
                Dispatch(evt, input!, paths, env);
            }
        }
        catch (Exception ex)
        {
            OnError(evt, ex, paths, env, input);
        }

        return 0;
    }

    private static void Dispatch(string evt, HookInput input, ProjectPaths paths, CliEnv env)
    {
        var ledger = new ProjectLedger(paths, env.Clock);
        switch (evt)
        {
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

    private static ToolCall CallOf(HookInput input, string cwd) =>
        new(input.ToolName!, FileOf(input), input.ToolInput?.Command, input.AgentType, input.AgentId, input.PermissionMode, cwd, input.ToolInput?.Glob);

    /// <summary>
    /// Awakened projects a tool call can reach besides the cwd project: found from the canonical target path (file tools) and from
    /// <c>cd</c>/<c>pushd</c>/<c>git -C</c>/<c>--git-dir</c>/<c>--work-tree</c> targets and absolute path tokens (Bash).
    /// </summary>
    private static List<ProjectPaths> TargetProjects(HookInput input, string cwd, List<ProjectPaths> chain, CliEnv env)
    {
        var result = new List<ProjectPaths>();
        var seen = chain.Select(c => PathCanon.Resolve("/", c.Root)).ToList();
        var searched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var location in ProjectTargets.Locations(CallOf(input, cwd)))
        {
            if (!searched.Add(location))
            {
                continue;
            }

            // All enclosing projects count: a decoy .monarch at the target must not hide the real project around it.
            foreach (var found in ProjectPaths.LocateAll(location, env.Home))
            {
                var canonical = PathCanon.Resolve("/", found.Root);
                if (seen.Exists(r => string.Equals(r, canonical, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                seen.Add(canonical);
                result.Add(found);
            }
        }

        return result;
    }

    private static void PreToolUse(HookInput input, string cwd, List<ProjectPaths> chain, List<ProjectPaths> targets, CliEnv env)
    {
        var call = CallOf(input, cwd);

        // Every awakened project around the cwd gets every rule (Scope Lock, rank, deny_bash, protection); each further project reached by the
        // call gets protect_paths and protect_secrets. The strictest result wins and each project logs the decision of its own rules.
        var evaluated = new List<(ProjectPaths Paths, ActiveFile? Active, PolicyDecision Decision, bool Full)>();
        foreach (var project in chain)
        {
            evaluated.Add(Evaluate(project, call, env, full: true, input));
        }

        foreach (var t in targets)
        {
            evaluated.Add(Evaluate(t, call, env, full: false, input));
        }

        var decision = ToolCallPolicy.Combine(evaluated.Select(e => e.Decision));
        if (decision.Kind != PolicyDecisionKind.Allow)
        {
            Emit(env, new HookSpecificOutput
            {
                HookEventName = "PreToolUse",
                PermissionDecision = decision.Kind == PolicyDecisionKind.Deny ? "deny" : "ask",
                PermissionDecisionReason = decision.Reason,
            });
        }

        foreach (var (paths, active, d, full) in evaluated)
        {
            var signaled = full && d.Signals.Contains(PolicySignals.DoubleDungeon);
            if (d.Kind == PolicyDecisionKind.Allow && !signaled)
            {
                continue;
            }

            var kind = d.Kind == PolicyDecisionKind.Deny ? "deny" : "ask";
            var ledger = new ProjectLedger(paths, env.Clock);
            Append(paths, env, () =>
            {
                var session = ledger.SessionId(input.SessionId);
                if (d.Kind != PolicyDecisionKind.Allow)
                {
                    ledger.Append(session, Actor(input), "PolicyDecided", ("rule", d.Rule), ("kind", kind), ("tool", input.ToolName),
                        ("agent_type", input.AgentType), ("quest", active?.QuestId), ("reason", d.Reason));
                }

                if (signaled)
                {
                    ledger.Append(session, Actor(input), "DoubleDungeonSignaled", ("tool", input.ToolName), ("file", FileOf(input)), ("quest", active?.QuestId));
                }
            });
        }
    }

    // A broken config falls back to the built-in defaults (protection and bans stay on); a broken active.json is evaluated
    // as "no quest". Both log and add an ask, so the failure is never a silent allow.
    private static (ProjectPaths, ActiveFile?, PolicyDecision, bool) Evaluate(ProjectPaths paths, ToolCall call, CliEnv env, bool full, HookInput input)
    {
        var failed = false;
        SystemConfig config;
        try
        {
            config = SystemConfigReader.Load(paths.Root);
        }
        catch (Exception ex)
        {
            Log(paths, env, "config", ex);
            config = SystemConfig.Default;
            failed = true;
        }

        ActiveFile? active = null;
        ActiveQuest? quest = null;
        var activeFailed = false;
        try
        {
            active = ActiveStore.Read(paths);
            quest = active is null ? null : ActiveStore.ToPolicy(active);
        }
        catch (Exception ex)
        {
            Log(paths, env, "active", ex);
            active = null;
            activeFailed = true;
        }

        var ctx = new PolicyContext(paths.Root, quest, false, config.ToPolicyOptions());
        var decision = full ? ToolCallPolicy.Evaluate(call, ctx) : ToolCallPolicy.EvaluateProtection(call, ctx);
        // A config error only asks from rank A upwards; below that the defaults decide on their own.
        if (activeFailed || (failed && quest is { Rank: >= Rank.A }))
        {
            decision = ToolCallPolicy.Combine([decision, PolicyDecision.Ask("hook_error", ErrorReason)]);
        }

        return (paths, active, decision, full);
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

    private static void OnError(string evt, Exception ex, ProjectPaths paths, CliEnv env, HookInput? input)
    {
        Log(paths, env, evt, ex);
        if (evt != "pre-tool-use")
        {
            return;
        }

        // Any failure on the PreToolUse path asks, whatever the rank (ADR-0003).
        Emit(env, new HookSpecificOutput { HookEventName = "PreToolUse", PermissionDecision = "ask", PermissionDecisionReason = ErrorReason });
        Append(paths, env, () =>
        {
            var ledger = new ProjectLedger(paths, env.Clock);
            ledger.Append(ledger.SessionId(input?.SessionId), input is null ? "hook" : Actor(input), "PolicyDecided", ("rule", "hook_error"), ("kind", "ask"),
                ("tool", input?.ToolName), ("reason", ErrorReason));
        });
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
        // Flush now: a slow ledger append (git, 10 s) must not cost the decision when the hook times out (5 s).
        try
        {
            env.Out.Flush();
        }
        catch (IOException)
        {
        }
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
            var limits = ManaSupport.Resolve(paths.Root, config).Limits;
            sb.AppendLine(FormattableString.Invariant(
                $"- Mana 5h: {s.FiveHourUsedPct:0}% (Limit {ManaSupport.Num(limits.FiveHourPct)}%, Reset {s.FiveHourResetsAt:HH:mm} UTC); 7d: {s.SevenDayUsedPct:0}% (Limit {ManaSupport.Num(limits.SevenDayPct)}%){(s.IsStale ? " [veraltet]" : "")}."));
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

        sb.AppendLine("- Hooks erzwingen Scope-Lock, geschuetzte Pfade, Secrets und deny_bash.");
        if (active is not null)
        {
            sb.AppendLine($"- {RuleTexts.Rule1}");
            sb.AppendLine($"- {RuleTexts.Rule2}");
        }
        else
        {
            sb.AppendLine($"- {RuleTexts.PlanLine}");
        }

        sb.Append($"- {RuleTexts.MergeLine}");
        return sb.ToString();
    }
}
