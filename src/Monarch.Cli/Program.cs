using System.CommandLine;

var root = new RootCommand("Monarch: local-first orchestration for AI coding agents.");

var hookEvent = new Argument<string>("event") { Description = "The hook event name." };
var hook = Stub("hook", "Handle an agent hook event.");
hook.Arguments.Add(hookEvent);
root.Subcommands.Add(hook);
foreach (var (name, description) in new[]
{
    ("statusline", "Render the status line."),
    ("mcp", "Run the MCP server."),
    ("awaken", "Initialize Monarch in this repository."),
    ("status", "Show current status."),
    ("gate", "Evaluate policy gates."),
    ("quest", "Manage quests."),
    ("recap", "Summarize recent activity."),
    ("rest", "Rest and restore mana."),
})
{
    root.Subcommands.Add(Stub(name, description));
}

return root.Parse(args).Invoke();

static Command Stub(string name, string description)
{
    var command = new Command(name, description);
    command.SetAction(_ =>
    {
        Console.Error.WriteLine($"monarch {name}: not implemented yet");
        return 2;
    });
    return command;
}
