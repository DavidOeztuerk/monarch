using System.CommandLine;
using System.CommandLine.Invocation;

namespace Monarch.Cli;

/// <summary>Entry point shared by <c>Program</c> and the in-process tests.</summary>
internal static class MonarchApp
{
    /// <summary>Runs the CLI. Hook and statusline bypass System.CommandLine to keep the hot path small.</summary>
    public static int Run(string[] args, CliEnv env)
    {
        if (args.Length >= 2 && args[0] == "hook")
        {
            return HookRunner.Run(args[1], env);
        }

        if (args.Length >= 1 && args[0] == "statusline")
        {
            return Statusline.Run(env);
        }

        if (args.Length >= 1 && args[0] == "mcp")
        {
            return McpTools.Run(env);
        }

        var root = BuildRoot(env);
        return root.Parse(args).Invoke(new InvocationConfiguration { Output = env.Out, Error = env.Err });
    }

    private static RootCommand BuildRoot(CliEnv env)
    {
        var root = new RootCommand("Monarch: local-first orchestration for AI coding agents.");

        var hookEvent = new Argument<string>("event") { Description = "session-start | pre-tool-use | post-tool-use | subagent-stop | stop | pre-compact | session-end" };
        var hook = new Command("hook", "Handle an agent hook event (stdin: hook JSON).");
        hook.Arguments.Add(hookEvent);
        hook.SetAction(r => HookRunner.Run(r.GetValue(hookEvent)!, env));
        root.Subcommands.Add(hook);

        var statusline = new Command("statusline", "Mana sensor and status line (stdin: statusline JSON).");
        statusline.SetAction(_ => Statusline.Run(env));
        root.Subcommands.Add(statusline);

        var plan = new Option<string?>("--plan") { Description = "pro | max5 | max20 | api-key" };
        var force = new Option<bool>("--force") { Description = "Rewrite system.yaml even if present." };
        var riskTags = new Option<string?>("--risk-tags") { Description = "YAML file with risk_tags to merge into system.yaml." };
        var awaken = new Command("awaken", "Initialize Monarch in this repository.");
        awaken.Options.Add(plan);
        awaken.Options.Add(force);
        awaken.Options.Add(riskTags);
        awaken.SetAction(r => AwakenCommand.Run(env, r.GetValue(plan), r.GetValue(force), r.GetValue(riskTags)));
        root.Subcommands.Add(awaken);

        var json = new Option<bool>("--json") { Description = "Machine-readable output." };
        var status = new Command("status", "Show current status.");
        status.Options.Add(json);
        status.SetAction(r => StatusCommand.Run(env, r.GetValue(json)));
        root.Subcommands.Add(status);

        var quest = new Command("quest", "Manage quests.");
        foreach (var action in new[] { "accept", "start", "submit", "abandon", "fail" })
        {
            var id = new Argument<string>("id") { Description = "Quest id, e.g. Q-0003." };
            var c = new Command(action, $"{char.ToUpperInvariant(action[0])}{action[1..]} a quest.");
            c.Arguments.Add(id);
            c.SetAction(r => QuestCommands.Transition(env, action, r.GetValue(id)!));
            quest.Subcommands.Add(c);
        }

        var state = new Option<string?>("--state") { Description = "Filter by state, e.g. in_progress." };
        var list = new Command("list", "List quests.");
        list.Options.Add(state);
        list.SetAction(r => QuestCommands.List(env, r.GetValue(state)));
        quest.Subcommands.Add(list);

        var showId = new Argument<string>("id") { Description = "Quest id." };
        var show = new Command("show", "Show a quest.");
        show.Arguments.Add(showId);
        show.SetAction(r => QuestCommands.Show(env, r.GetValue(showId)!));
        quest.Subcommands.Add(show);
        var prId = new Argument<string>("id") { Description = "Quest id." };
        var prNumber = new Argument<string>("number") { Description = "Pull request number." };
        var pr = new Command("pr", "Link a pull request to a quest.");
        pr.Arguments.Add(prId);
        pr.Arguments.Add(prNumber);
        pr.SetAction(r => EvidenceCommands.Pr(env, r.GetValue(prId)!, r.GetValue(prNumber)!));
        quest.Subcommands.Add(pr);
        foreach (var verb in new[] { "verify", "clear" })
        {
            var vid = new Argument<string>("id") { Description = "Quest id." };
            var c = new Command(verb, verb == "verify" ? "Verify an in-review quest from the checks of its PR head (forge evidence only)." : "Clear a verified quest once its PR is merged; books XP.");
            c.Arguments.Add(vid);
            c.SetAction(r => verb == "verify" ? EvidenceCommands.Verify(env, r.GetValue(vid)!) : EvidenceCommands.Clear(env, r.GetValue(vid)!));
            quest.Subcommands.Add(c);
        }

        root.Subcommands.Add(quest);

        var evidence = new Command("evidence", "Forge evidence.");
        var syncQuest = new Option<string?>("--quest") { Description = "Only this quest." };
        var evSync = new Command("sync", "Verify and clear linked quests from GitHub checks and merge state.");
        evSync.Options.Add(syncQuest);
        evSync.SetAction(r => EvidenceCommands.Sync(env, r.GetValue(syncQuest)));
        evidence.Subcommands.Add(evSync);
        root.Subcommands.Add(evidence);

        var gate = new Command("gate", "Gates: import, list, show, sync, verdict, merge.");
        var importDir = new Argument<string>("dir") { Description = "Backlog directory with G-*.md and Q-*.md." };
        var importRepo = new Option<string?>("--repo") { Description = "Only quests whose scope.repo equals this name (case-insensitive) and their gates." };
        var import = new Command("import", "Import backlog files into .monarch/gates and .monarch/quests.");
        import.Arguments.Add(importDir);
        import.Options.Add(importRepo);
        import.SetAction(r => GateCommands.Import(env, r.GetValue(importDir)!, r.GetValue(importRepo)));
        gate.Subcommands.Add(import);
        var gateList = new Command("list", "List gates.");
        gateList.SetAction(_ => GateCommands.List(env));
        gate.Subcommands.Add(gateList);
        var gateId = new Argument<string>("id") { Description = "Gate id." };
        var gateShow = new Command("show", "Show a gate with its quests and merge rules.");
        gateShow.Arguments.Add(gateId);
        gateShow.SetAction(r => GateCommands.Show(env, r.GetValue(gateId)!));
        gate.Subcommands.Add(gateShow);
        var github = new Option<bool>("--github") { Description = "Sync to GitHub milestones and issues." };
        var owner = new Option<string?>("--owner") { Description = "Repository owner." };
        var syncRepo = new Option<string?>("--repo") { Description = "Repository name." };
        var gateSync = new Command("sync", "Upsert milestones and issues and write their numbers back.");
        gateSync.Options.Add(github);
        gateSync.Options.Add(owner);
        gateSync.Options.Add(syncRepo);
        gateSync.SetAction(r => GateCommands.Sync(env, r.GetValue(github), r.GetValue(owner), r.GetValue(syncRepo)));
        gate.Subcommands.Add(gateSync);
        var verdictGate = new Argument<string>("id") { Description = "Gate id." };
        var verdictValue = new Option<string>("--verdict") { Description = "approve or changes.", Required = true };
        var verdictSource = new Option<string>("--source") { Description = "URL of the warden review.", Required = true };
        var gateVerdict = new Command("verdict", "Record the warden verdict for the current head of the gate's pull request (main only).");
        gateVerdict.Arguments.Add(verdictGate);
        gateVerdict.Options.Add(verdictValue);
        gateVerdict.Options.Add(verdictSource);
        gateVerdict.SetAction(r => GateMergeCommands.Verdict(env, r.GetValue(verdictGate)!, r.GetValue(verdictValue)!, r.GetValue(verdictSource)!));
        gate.Subcommands.Add(gateVerdict);
        var mergeGate = new Argument<string>("id") { Description = "Gate id." };
        var gateMerge = new Command("merge", "Squash-merge the gate's pull request into the integration branch after base, rank, CI, verdict and warden run check out (main only).");
        gateMerge.Arguments.Add(mergeGate);
        gateMerge.SetAction(r => GateMergeCommands.Merge(env, r.GetValue(mergeGate)!));
        gate.Subcommands.Add(gateMerge);
        root.Subcommands.Add(gate);

        var next = new Option<string?>("--next") { Description = "The next concrete step." };
        var decision = new Option<string[]>("--decision") { Description = "An open decision; repeatable." };
        var recap = new Command("recap", "Write .monarch/recap/RECAP.md.");
        recap.Options.Add(next);
        recap.Options.Add(decision);
        recap.SetAction(r => ManaCommands.Recap(env, r.GetValue(next), r.GetValue(decision) ?? []));
        root.Subcommands.Add(recap);

        var rest = new Command("rest", "Show reset times, the guard decision and the resume time.");
        rest.SetAction(_ => ManaCommands.Rest(env));
        root.Subcommands.Add(rest);

        var wake = new Command("wake", "Out-of-session wake call (opt-in via mana.wake): schedule, fire, cancel.");
        var wakeSession = new Option<string>("--session") { Description = "Session UUID.", Required = true };
        var wakeAt = new Option<string>("--at") { Description = "Wake time, ISO-8601 (resets_at + 3 min, from 'monarch rest').", Required = true };
        var wakeSchedule = new Command("schedule", "Install the one-shot job (launchd or systemd; owner opt-in).");
        wakeSchedule.Options.Add(wakeSession);
        wakeSchedule.Options.Add(wakeAt);
        wakeSchedule.SetAction(r => WakeCommands.Schedule(env, r.GetValue(wakeSession)!, r.GetValue(wakeAt)!));
        wake.Subcommands.Add(wakeSchedule);
        var fireSession = new Argument<string>("session") { Description = "Session UUID." };
        var fireAt = new Option<string>("--at") { Description = "Wake time as scheduled.", Required = true };
        var wakeFire = new Command("fire", "Job entry point: resume the background session if it did not wake up by itself.");
        wakeFire.Arguments.Add(fireSession);
        wakeFire.Options.Add(fireAt);
        wakeFire.SetAction(r => WakeCommands.Fire(env, r.GetValue(fireSession)!, r.GetValue(fireAt)!));
        wake.Subcommands.Add(wakeFire);
        var cancelSession = new Argument<string>("session") { Description = "Session UUID." };
        var wakeCancel = new Command("cancel", "Remove the job of a session.");
        wakeCancel.Arguments.Add(cancelSession);
        wakeCancel.SetAction(r => WakeCommands.Cancel(env, r.GetValue(cancelSession)!));
        wake.Subcommands.Add(wakeCancel);
        root.Subcommands.Add(wake);

        var estimate = new Option<double?>("--estimate") { Description = "Estimated step cost in percentage points of the 5h window." };
        var mana = new Command("mana", "Mana snapshot, limits and affordability.");
        mana.Options.Add(estimate);
        mana.SetAction(r => ManaCommands.Mana(env, r.GetValue(estimate)));
        root.Subcommands.Add(mana);

        var ledgerCmd = new Command("ledger", "Ledger tools.");
        var ledgerVerify = new Command("verify", "Verify the hash chain of all ledger files.");
        ledgerVerify.SetAction(_ => ManaCommands.LedgerVerify(env));
        ledgerCmd.Subcommands.Add(ledgerVerify);
        var resetHash = new Argument<string>("event-hash") { Description = "Hash of the LedgerAnchorReset event, as printed by 'ledger verify'." };
        var ledgerAck = new Command("ack-reset", "Acknowledge a ledger anchor reset (owner action).");
        ledgerAck.Arguments.Add(resetHash);
        ledgerAck.SetAction(r => ManaCommands.LedgerAckReset(env, r.GetValue(resetHash)!));
        ledgerCmd.Subcommands.Add(ledgerAck);
        root.Subcommands.Add(ledgerCmd);

        var mcp = new Command("mcp", "Run the MCP server on stdio.");
        mcp.SetAction(_ => McpTools.Run(env));
        root.Subcommands.Add(mcp);

        return root;
    }
}
