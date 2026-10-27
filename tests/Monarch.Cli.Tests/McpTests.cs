using System.Text.Json;
using System.Text.Json.Nodes;
using Monarch.Application.Net;
using Monarch.Domain;

namespace Monarch.Cli.Tests;

public class McpServerTests
{
    private static List<JsonNode> Rpc(TempProject p, params string[] lines)
    {
        var o = new StringWriter();
        var env = new CliEnv(p.Root, p.Home, new StringReader(string.Join("\n", lines) + "\n"), o, new StringWriter(), p.Clock, p.Http, new GitHubTokenProvider(_ => "t", () => null));
        Assert.Equal(0, MonarchApp.Run(["mcp"], env));
        return [.. o.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!)];
    }

    private static string Init => """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    private static string Call(int id, string tool, string args) =>
        $$$"""{"jsonrpc":"2.0","id":{{{id}}},"method":"tools/call","params":{"name":"{{{tool}}}","arguments":{{{args}}}}}""";

    private static (bool IsError, string Text) Result(JsonNode r) =>
        (r["result"]!["isError"]?.GetValue<bool>() ?? false, r["result"]!["content"]![0]!["text"]!.GetValue<string>());

    private const string Plan = """{"gate":{"id":"G-0007","title":"Gate seven","rank":"C","kind":"standard"},"quests":[{"id":"Q-0071","title":"Plain","kind":"main","rank":"D","scope":{"include":["src/web/**"],"exclude":[]},"estimate":{"session_pct":5,"weekly_pct":1},"dod":["a","b"],"boss":"boss one"},{"id":"Q-0072","title":"Auth thing","kind":"main","rank":"D","scope":{"include":["src/Foo/Auth/**"],"exclude":[]},"estimate":{"session_pct":4,"weekly_pct":1},"dod":["c"]}]}""";

    [Fact]
    public void Handshake_lists_exactly_six_tools_and_handles_protocol_edge_cases()
    {
        using var p = new TempProject();
        var r = Rpc(p, Init, """{"jsonrpc":"2.0","method":"notifications/initialized"}""", """{"jsonrpc":"2.0","id":2,"method":"ping"}""",
            """{"jsonrpc":"2.0","id":3,"method":"tools/list"}""", """{"jsonrpc":"2.0","id":4,"method":"nope"}""", "not json",
            """{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"verify","arguments":{}}}""");
        Assert.Equal(6, r.Count);
        Assert.Equal("2025-06-18", r[0]["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.NotNull(r[0]["result"]!["capabilities"]!["tools"]);
        Assert.NotNull(r[1]["result"]);
        var names = r[2]["result"]!["tools"]!.AsArray().Select(t => t!["name"]!.GetValue<string>()).Order().ToArray();
        Assert.Equal(["evidence_sync", "gate_plan", "knowledge_write", "mana_forecast", "quest_transition", "system_status"], names);
        Assert.All(r[2]["result"]!["tools"]!.AsArray(), t => Assert.Equal("object", t!["inputSchema"]!["type"]!.GetValue<string>()));
        Assert.Equal(-32601, r[3]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32700, r[4]["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32602, r[5]["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public void System_status_and_mana_forecast_are_read_tools()
    {
        using var p = new TempProject();
        Seed.Mana(p, 40, 20);
        var r = Rpc(p, Call(1, "system_status", "{}"), Call(2, "mana_forecast", """{"estimates":[{"session_pct":5,"weekly_pct":1},{"session_pct":60,"weekly_pct":1}]}"""),
            Call(3, "mana_forecast", """{"estimates":[{"session_pct":5,"weekly_pct":1}]}"""), Call(4, "mana_forecast", "{}"));
        Assert.Contains("ledgerOk", Result(r[0]).Text, StringComparison.Ordinal);
        var big = JsonNode.Parse(Result(r[1]).Text)!;
        Assert.Equal("RecapAndStop", big["decision"]!.GetValue<string>());
        Assert.Equal("plan default", big["limitSource"]!.GetValue<string>());
        var small = JsonNode.Parse(Result(r[2]).Text)!;
        Assert.Equal("Proceed", small["decision"]!.GetValue<string>());
        Assert.Equal(90 - 40 - 5 - 5, small["headroom"]!["fiveHourPct"]!.GetValue<double>(), 3);
        Assert.True(Result(r[3]).IsError);
    }

    [Fact]
    public void Gate_plan_stores_files_events_raises_rank_by_risk_tag_and_protects_started_quests()
    {
        using var p = new TempProject();
        var r = Rpc(p, Call(1, "gate_plan", Plan));
        var (err, text) = Result(r[0]);
        Assert.False(err, text);
        var res = JsonNode.Parse(text)!;
        var raised = res["quests"]!.AsArray().Single(q => q!["id"]!.GetValue<string>() == "Q-0072")!;
        Assert.Equal("A", raised["rank"]!.GetValue<string>());
        Assert.Contains("auth", raised["reason"]!.GetValue<string>(), StringComparison.OrdinalIgnoreCase);
        var list = p.Run(["quest", "list"]).Out;
        Assert.Contains("Q-0071  [D]", list, StringComparison.Ordinal);
        Assert.Contains("Q-0072  [A]", list, StringComparison.Ordinal);
        Assert.Equal(1, p.Events().Count(e => e.Type == "GateOpened"));
        Assert.Equal(2, p.Events().Count(e => e.Type == "QuestProposed"));
        Assert.Contains("g-0007", string.Join(",", Directory.GetFiles(Path.Combine(p.Root, ".monarch", "gates"))).ToLowerInvariant(), StringComparison.Ordinal);
        Assert.Contains("boss one", File.ReadAllText(Directory.GetFiles(Path.Combine(p.Root, ".monarch", "quests"), "Q-0071*").Single()), StringComparison.Ordinal);

        Assert.Equal(0, p.Run(["quest", "accept", "Q-0071"]).Code);
        var before = File.ReadAllText(Directory.GetFiles(Path.Combine(p.Root, ".monarch", "quests"), "Q-0071*").Single());
        var again = Rpc(p, Call(1, "gate_plan", Plan.Replace("Plain", "Changed", StringComparison.Ordinal)));
        Assert.True(Result(again[0]).IsError);
        Assert.Contains("Q-0071", Result(again[0]).Text, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(Directory.GetFiles(Path.Combine(p.Root, ".monarch", "quests"), "Q-0071*").Single()));
        Assert.True(Result(Rpc(p, Call(1, "gate_plan", """{"gate":{"id":"bad","title":"x","rank":"C","kind":"standard"},"quests":[]}"""))[0]).IsError);
    }

    [Fact]
    public void Quest_transition_allows_only_accept_start_submit()
    {
        using var p = new TempProject();
        Seed.Gate(p);
        Seed.Quest(p, "Q-0001");
        var r = Rpc(p, Call(1, "quest_transition", """{"id":"Q-0001","action":"accept"}"""), Call(2, "quest_transition", """{"id":"Q-0001","action":"start"}"""),
            Call(3, "quest_transition", """{"id":"Q-0001","action":"submit"}"""), Call(4, "quest_transition", """{"id":"Q-0001","action":"verify"}"""),
            Call(5, "quest_transition", """{"id":"Q-0001","action":"clear"}"""), Call(6, "quest_transition", """{"id":"Q-0001","action":"abandon"}"""));
        Assert.All(r.Take(3), x => Assert.False(Result(x).IsError, Result(x).Text));
        Assert.Equal("in_review", Seed.State(Directory.GetFiles(Path.Combine(p.Root, ".monarch", "quests")).Single()));
        foreach (var x in r.Skip(3))
        {
            var (err, text) = Result(x);
            Assert.True(err);
            Assert.Contains("verify", text, StringComparison.Ordinal);
            Assert.Contains("clear", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Evidence_sync_pulls_forge_data_only()
    {
        using var p = new TempProject(systemYaml: Seed.ForgeYaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, true);
        f.SetChecks(FakeForge.Run(1, "build", "success"));
        p.Http = f;
        Seed.Gate(p);
        var quest = Seed.Quest(p, "Q-0001", "in_review", extra: "board: { provider: github, pr: 7 }\n");
        var r = Rpc(p, Call(1, "evidence_sync", """{"quest":"Q-0001"}"""));
        Assert.False(Result(r[0]).IsError, Result(r[0]).Text);
        Assert.Equal("cleared", Seed.State(quest));
    }

    [Fact]
    public void Knowledge_write_is_confined_below_knowledge_dir()
    {
        using var p = new TempProject();
        var kdir = Path.Combine(p.Root, ".monarch", "knowledge");
        Directory.CreateDirectory(kdir);
        var outside = Path.Combine(p.Root, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(kdir, "link"), outside);
        var r = Rpc(p, Call(1, "knowledge_write", """{"path":"notes/a.md","content":"# hi"}"""), Call(2, "knowledge_write", """{"path":"../x.md","content":"x"}"""),
            Call(3, "knowledge_write", """{"path":"/etc/x.md","content":"x"}"""), Call(4, "knowledge_write", """{"path":"a/../../x.md","content":"x"}"""),
            Call(5, "knowledge_write", """{"path":"link/x.md","content":"x"}"""), Call(6, "knowledge_write", """{"path":"","content":"x"}"""));
        Assert.False(Result(r[0]).IsError, Result(r[0]).Text);
        Assert.Equal("# hi", File.ReadAllText(Path.Combine(kdir, "notes", "a.md")));
        Assert.Single(p.Events(), e => e.Type == "KnowledgeWritten");
        foreach (var x in r.Skip(1))
        {
            Assert.True(Result(x).IsError);
        }

        Assert.Empty(Directory.GetFiles(outside));
        Assert.False(File.Exists(Path.Combine(p.Root, "x.md")));
    }
}
