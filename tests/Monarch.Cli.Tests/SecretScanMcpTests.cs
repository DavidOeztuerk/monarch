using System.Text.Json.Nodes;
using Monarch.Application.Net;

namespace Monarch.Cli.Tests;

public sealed class SecretScanMcpTests
{
    [Fact]
    public void System_status_carries_the_secret_scan_hook_field()
    {
        using var p = new TempProject();
        Assert.NotNull(Git.Run(p.Root, "init", "-q"));
        var o = new StringWriter();
        var call = """{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"system_status","arguments":{}}}""";
        var env = new CliEnv(p.Root, p.Home, new StringReader(call + "\n"), o, new StringWriter(), p.Clock, p.Http, new GitHubTokenProvider(_ => "t", () => null));
        Assert.Equal(0, MonarchApp.Run(["mcp"], env));
        var text = JsonNode.Parse(o.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[0])!["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var body = text.StartsWith("project: ", StringComparison.Ordinal) ? text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..] : text;
        var hook = JsonNode.Parse(body)!["secretScanHook"]!;
        Assert.Equal("missing", hook["state"]!.GetValue<string>());
        Assert.EndsWith("hooks/pre-commit", hook["path"]!.GetValue<string>(), StringComparison.Ordinal);
    }
}
