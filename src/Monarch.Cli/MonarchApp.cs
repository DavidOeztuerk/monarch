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
        quest.Subcommands.Add(Stub(env, "verify", "Verify a quest (part 2)."));
        quest.Subcommands.Add(Stub(env, "clear", "Clear a quest (part 2)."));
        root.Subcommands.Add(quest);

        foreach (var (name, description) in new[]
        {
            ("mcp", "Run the MCP server."), ("gate", "Evaluate policy gates."), ("recap", "Summarize recent activity."),
            ("rest", "Rest and restore mana."), ("mana", "Mana details."),
        })
        {
            root.Subcommands.Add(Stub(env, name, description));
        }

        return root;
    }

    private static Command Stub(CliEnv env, string name, string description)
    {
        var command = new Command(name, description);
        command.SetAction(_ =>
        {
            env.Err.WriteLine($"monarch {name}: not implemented yet");
            return 2;
        });
        return command;
    }
}
