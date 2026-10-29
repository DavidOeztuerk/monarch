using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Monarch.Application.Net;
using Monarch.Mcp;
using Monarch.Policy;

namespace Monarch.Cli.Tests;

/// <summary>A hermetic directory world: base, MONARCH_HOME and the user's home live below one temp directory (physical path).</summary>
internal sealed class McpWorld : IDisposable
{
    public McpWorld()
    {
        Base = PathCanon.Resolve("/", Path.Combine(Path.GetTempPath(), "mcp-w-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Base);
        Home = Path.Combine(Base, "mhome");
        UserHome = Path.Combine(Base, "uhome");
        Work = Path.Combine(Base, "work");
        Elsewhere = Path.Combine(Base, "elsewhere");
        foreach (var d in new[] { Home, UserHome, Work, Elsewhere })
        {
            Directory.CreateDirectory(d);
        }
    }

    public string Base { get; }

    /// <summary>MONARCH_HOME; replaceable before the first call.</summary>
    public string Home { get; set; }

    public string UserHome { get; }

    public string Work { get; }

    public string Elsewhere { get; }

    public HttpMessageHandler? Http { get; set; }

    public FakeClock Clock { get; } = new(TempProject.Now);

    public string Awaken(string dir, string? yaml = null, bool systemYaml = true)
    {
        foreach (var s in new[] { "local", "ledger", "quests", "gates", "knowledge" })
        {
            Directory.CreateDirectory(Path.Combine(dir, ".monarch", s));
        }

        if (systemYaml)
        {
            File.WriteAllText(Path.Combine(dir, ".monarch", "system.yaml"), yaml ?? Seed.ForgeYaml);
        }

        return dir;
    }

    public string Proj(string name, string? yaml = null) => Awaken(Path.Combine(Work, name), yaml);

    public static string Quest(string project, string id, string state = "proposed")
    {
        var path = Path.Combine(project, ".monarch", "quests", $"{id}-t.md");
        File.WriteAllText(path, $"---\nid: {id}\ngate: G-0001\nkind: main\nrank: C\nstate: {state}\ndod:\n  - first item\nscope:\n  include: [\"src/**\"]\n  exclude: []\nestimate: {{ session_pct: 5, weekly_pct: 1, gold_usd: 0 }}\n---\n# Title {id}\n\n## Definition of Done\n- [ ] first item\n");
        return path;
    }

    public static void Gate(string project)
    {
        Directory.CreateDirectory(Path.Combine(project, ".monarch", "gates"));
        File.WriteAllText(Path.Combine(project, ".monarch", "gates", "G-0001-g.md"), "---\nid: G-0001\ntype: standard\ntitle: Gate\nrank: C\nstate: proposed\nquests: [Q-0001]\nboss: \"boss\"\n---\n# Gate\n");
    }

    /// <summary>Path to file-content hash for every file below a directory; links are not followed.</summary>
    public static SortedDictionary<string, string> Snapshot(string dir)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(dir))
        {
            return map;
        }

        foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            map[Path.GetRelativePath(dir, f)] = new FileInfo(f).LinkTarget is not null ? "link" : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)));
        }

        return map;
    }

    public static string Init(bool roots) =>
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":""" + (roots ? """{"roots":{"listChanged":true}}""" : "{}") + ""","clientInfo":{"name":"t","version":"1"}}}""";

    public const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    public const string ListChanged = """{"jsonrpc":"2.0","method":"notifications/roots/list_changed"}""";

    public static string RootsReply(string id, params string[] uris) =>
        """{"jsonrpc":"2.0","id":""" + JsonSerializer.Serialize(id) + ""","result":{"roots":[""" + string.Join(",", uris.Select(u => """{"uri":""" + JsonSerializer.Serialize(u) + "}")) + "]}}";

    public static string FileUri(string path) => "file://" + path;

    public static string Call(int id, string tool, string args) =>
        """{"jsonrpc":"2.0","id":""" + id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ""","method":"tools/call","params":{"name":""" + JsonSerializer.Serialize(tool) + ""","arguments":""" + args + "}}";

    public static string With(string project, string rest = "") => $$"""{"project":{{JsonSerializer.Serialize(project)}}{{(rest.Length > 0 ? "," + rest : "")}}}""";

    public CliEnv Env(string cwd, TextReader input, TextWriter output, TextWriter err, IReadOnlyDictionary<string, string?>? vars = null) =>
        new(cwd, Home, input, output, err, Clock, Http, new GitHubTokenProvider(_ => "t", () => null), false, vars ?? TempProject.NoVars, UserHome);

    /// <summary>Runs the server on a script of input lines; returns all server messages and stderr.</summary>
    public (List<JsonNode> Out, string Err) Rpc(string cwd, IEnumerable<string> lines, IReadOnlyDictionary<string, string?>? vars = null, int timeoutMs = 300)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var v = new Dictionary<string, string?>(vars ?? TempProject.NoVars) { ["MONARCH_MCP_ROOTS_TIMEOUT_MS"] = timeoutMs.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        Assert.Equal(0, MonarchApp.Run(["mcp"], Env(cwd, new StringReader(string.Join("\n", lines) + "\n"), o, e, v)));
        return (Parse(o.ToString()), e.ToString());
    }

    public static List<JsonNode> Parse(string text) => [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!)];

    /// <summary>One call with the usual handshake. <paramref name="roots"/> null: the client declares no roots capability.</summary>
    public (bool IsError, string Text, string Err) One(string cwd, string[]? roots, string tool, string args, IReadOnlyDictionary<string, string?>? vars = null)
    {
        var lines = new List<string> { Init(roots is not null), Initialized };
        if (roots is not null)
        {
            lines.Add(RootsReply("roots-1", [.. roots.Select(FileUri)]));
        }

        lines.Add(Call(10, tool, args));
        var (msgs, err) = Rpc(cwd, lines, vars);
        var r = ById(msgs, 10);
        return (r["result"]!["isError"]?.GetValue<bool>() ?? false, r["result"]!["content"]![0]!["text"]!.GetValue<string>(), err);
    }

    public static JsonNode ById(List<JsonNode> msgs, int id) => msgs.Single(m => m["id"]?.GetValueKind() == JsonValueKind.Number && m["id"]!.GetValue<int>() == id);

    public static string Body(string text) => text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..];

    public static string Header(string text) => text.Split('\n')[0];

    public List<(string Type, JsonElement Payload)> Events(string project)
    {
        var list = new List<(string, JsonElement)>();
        var dir = Path.Combine(project, ".monarch", "ledger");
        if (!Directory.Exists(dir))
        {
            return list;
        }

        foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadAllLines(f))
            {
                var doc = JsonDocument.Parse(line).RootElement;
                list.Add((doc.GetProperty("type").GetString()!, doc.GetProperty("payload").Clone()));
            }
        }

        return list;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Base, true);
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>A string writer that can be read while another thread writes.</summary>
internal sealed class LockedWriter : StringWriter
{
    private readonly object _gate = new();

    public override void Write(char value)
    {
        lock (_gate)
        {
            base.Write(value);
        }
    }

    public override void Write(string? value)
    {
        lock (_gate)
        {
            base.Write(value);
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            base.WriteLine(value);
        }
    }

    public override string ToString()
    {
        lock (_gate)
        {
            return base.ToString();
        }
    }
}

/// <summary>A reader whose lines the test feeds one by one; blocks like a pipe until completed.</summary>
internal sealed class PipeReader : TextReader
{
    private readonly BlockingCollection<string> _lines = [];

    public void Feed(string line) => _lines.Add(line);

    public void Complete() => _lines.CompleteAdding();

    public override string? ReadLine() => _lines.TryTake(out var l, Timeout.Infinite) ? l : null;
}

/// <summary>ADR-0013: the project is resolved per call (parameter, roots, cwd) and never leaves the resolved project.</summary>
public class McpProjectTests
{
    private static string Hdr(string root, string source) => $"project: {root} (source: {source})";

    // ---- resolution order -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Parameter_under_a_root_wins_and_the_answer_names_project_and_source()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        w.Proj("p2");
        var r = w.One(w.Elsewhere, [w.Work], "system_status", McpWorld.With(p1));
        Assert.False(r.IsError, r.Text);
        Assert.StartsWith(Hdr(p1, "parameter") + "\n", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_awakened_root_resolves_via_roots_and_so_does_an_awakened_ancestor_of_a_root()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var sub = Directory.CreateDirectory(Path.Combine(p1, "src", "deep")).FullName;
        Assert.StartsWith(Hdr(p1, "roots") + "\n", w.One(w.Elsewhere, [p1], "system_status", "{}").Text, StringComparison.Ordinal);
        Assert.StartsWith(Hdr(p1, "roots") + "\n", w.One(w.Elsewhere, [sub], "system_status", "{}").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_awakened_project_below_a_root_is_never_chosen_implicitly()
    {
        using var w = new McpWorld();
        var decoy = w.Proj("cloned-repo");
        var r = w.One(w.Elsewhere, [w.Work], "system_status", "{}");
        Assert.True(r.IsError);
        Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
        Assert.Contains("`project`", r.Text, StringComparison.Ordinal);
        Assert.Contains(decoy, r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_awakened_projects_below_a_root_give_an_error_with_a_sorted_candidate_list()
    {
        using var w = new McpWorld();
        var b = w.Proj("b");
        var a = w.Proj("a");
        var wt = w.Awaken(Path.Combine(w.Work, ".monarch-worktrees", "wt1"));
        var r = w.One(w.Elsewhere, [w.Work], "system_status", "{}");
        Assert.True(r.IsError);
        var ia = r.Text.IndexOf(a, StringComparison.Ordinal);
        var ib = r.Text.IndexOf(b, StringComparison.Ordinal);
        Assert.True(ia >= 0 && ib > ia, r.Text);
        Assert.Contains(wt, r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_candidate_scan_skips_symlinks_lists_at_most_ten_and_does_not_fail_on_unreadable_directories()
    {
        using var w = new McpWorld();
        for (var i = 0; i < 12; i++)
        {
            w.Proj("p" + i.ToString("00", System.Globalization.CultureInfo.InvariantCulture));
        }

        var other = w.Awaken(Path.Combine(w.Elsewhere, "linked-target"));
        Directory.CreateSymbolicLink(Path.Combine(w.Work, "aaa-link"), other);
        var locked = Directory.CreateDirectory(Path.Combine(w.Work, "locked")).FullName;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.None);
            }

            var r = w.One(w.Elsewhere, [w.Work], "system_status", "{}");
            Assert.True(r.IsError);
            Assert.DoesNotContain("aaa-link", r.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("linked-target", r.Text, StringComparison.Ordinal);
            Assert.Equal(10, r.Text.Split('\n').Count(l => l.StartsWith("  - ", StringComparison.Ordinal)));
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Fact]
    public void An_unawakened_root_falls_through_to_an_awakened_process_cwd()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var r = w.One(p1, [w.Elsewhere], "system_status", "{}");
        Assert.False(r.IsError, r.Text);
        Assert.StartsWith(Hdr(p1, "cwd") + "\n", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_found_tells_the_way()
    {
        using var w = new McpWorld();
        var r = w.One(w.Elsewhere, [w.Elsewhere], "system_status", "{}");
        Assert.True(r.IsError);
        Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
        Assert.Contains("Pass parameter `project`", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_outside_all_roots_is_refused_without_falling_back_to_the_cwd_project()
    {
        using var w = new McpWorld();
        var cwdProject = w.Proj("cwd-proj");
        var outside = w.Awaken(Path.Combine(w.Elsewhere, "outside"));
        var r = w.One(cwdProject, [w.Elsewhere + "/nothing-here"], "system_status", McpWorld.With(outside));
        Assert.True(r.IsError);
        Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
        Assert.Contains("(roots)", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parameter_that_is_an_ancestor_of_a_root_is_refused()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var inner = Directory.CreateDirectory(Path.Combine(p1, "inner")).FullName;
        var r = w.One(w.Elsewhere, [inner], "system_status", McpWorld.With(p1));
        Assert.True(r.IsError);
        Assert.Contains("(roots)", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prefix_trap_proj_versus_proj_evil_is_a_component_comparison()
    {
        using var w = new McpWorld();
        var proj = Directory.CreateDirectory(Path.Combine(w.Work, "proj")).FullName;
        var evil = w.Proj("proj-evil");
        var r = w.One(w.Elsewhere, [proj], "system_status", McpWorld.With(evil));
        Assert.True(r.IsError);
        Assert.Contains("(roots)", r.Text, StringComparison.Ordinal);
    }

    // ---- roots events and protocol ----------------------------------------------------------------------------------------------------

    [Fact]
    public void Roots_are_requested_only_after_initialized_with_deterministic_ids_and_list_changed_replaces_the_list()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var p2 = w.Proj("p2");
        var (msgs, err) = w.Rpc(w.Elsewhere,
        [
            McpWorld.Init(true), McpWorld.Initialized, McpWorld.RootsReply("roots-1", McpWorld.FileUri(p1)), McpWorld.Call(10, "system_status", "{}"),
            McpWorld.ListChanged, McpWorld.RootsReply("roots-2", McpWorld.FileUri(p2)), McpWorld.Call(11, "system_status", "{}"),
        ]);
        Assert.StartsWith(Hdr(p1, "roots"), McpWorld.ById(msgs, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.StartsWith(Hdr(p2, "roots"), McpWorld.ById(msgs, 11)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        var requests = msgs.Where(m => m["method"]?.GetValue<string>() == "roots/list").Select(m => m["id"]!.GetValue<string>()).ToArray();
        Assert.Equal(["roots-1", "roots-2"], requests);
        Assert.Equal(1, msgs.FindIndex(m => m["method"]?.GetValue<string>() == "roots/list"));
        Assert.Contains($"mcp: roots=[{p1}]", err, StringComparison.Ordinal);
        Assert.Contains($"mcp: roots=[{p2}]", err, StringComparison.Ordinal);
    }

    [Fact]
    public void No_roots_request_before_initialized_and_none_without_the_capability()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var (before, _) = w.Rpc(p1, [McpWorld.Init(true), McpWorld.Call(10, "system_status", "{}")]);
        Assert.DoesNotContain(before, m => m["method"]?.GetValue<string>() == "roots/list");
        var (without, _) = w.Rpc(p1, [McpWorld.Init(false), McpWorld.Initialized, McpWorld.Call(10, "system_status", "{}")]);
        Assert.DoesNotContain(without, m => m["method"]?.GetValue<string>() == "roots/list");
        Assert.StartsWith(Hdr(p1, "cwd"), McpWorld.ById(without, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_tools_call_before_the_roots_answer_waits_for_it()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var (msgs, _) = w.Rpc(w.Elsewhere, [McpWorld.Init(true), McpWorld.Initialized, McpWorld.Call(10, "system_status", "{}"), McpWorld.RootsReply("roots-1", McpWorld.FileUri(p1))]);
        Assert.StartsWith(Hdr(p1, "roots"), McpWorld.ById(msgs, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_answer_the_call_times_out_and_the_cwd_is_the_only_root()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var pipe = new PipeReader();
        var o = new LockedWriter();
        var e = new LockedWriter();
        var env = w.Env(p1, pipe, o, e, new Dictionary<string, string?> { ["MONARCH_MCP_ROOTS_TIMEOUT_MS"] = "150" });
        var run = Task.Run(() => MonarchApp.Run(["mcp"], env));
        pipe.Feed(McpWorld.Init(true));
        pipe.Feed(McpWorld.Initialized);
        pipe.Feed(McpWorld.Call(10, "system_status", "{}"));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!o.ToString().Contains("\"id\":10", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(20);
        }

        pipe.Complete();
        Assert.Equal(0, await run);
        var msgs = McpWorld.Parse(o.ToString());
        Assert.StartsWith(Hdr(p1, "cwd"), McpWorld.ById(msgs, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("timeout", e.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void An_answer_is_matched_by_id_a_foreign_id_does_not_count()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var r = w.Rpc(w.Elsewhere, [McpWorld.Init(true), McpWorld.Initialized, McpWorld.RootsReply("roots-9", McpWorld.FileUri(p1)), McpWorld.Call(10, "system_status", "{}")]);
        var text = McpWorld.ById(r.Out, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.StartsWith("project: null\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_list_error_answer_and_non_file_roots_leave_the_cwd_as_the_only_root()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var empty = w.One(p1, [], "system_status", "{}");
        Assert.StartsWith(Hdr(p1, "cwd"), empty.Text, StringComparison.Ordinal);

        var (msgs, err) = w.Rpc(p1, [McpWorld.Init(true), McpWorld.Initialized, """{"jsonrpc":"2.0","id":"roots-1","error":{"code":-32603,"message":"nope"}}""", McpWorld.Call(10, "system_status", "{}")]);
        Assert.StartsWith(Hdr(p1, "cwd"), McpWorld.ById(msgs, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Contains("mcp: roots=[]", err, StringComparison.Ordinal);

        var other = w.Proj("other");
        var mixed = w.Rpc(w.Elsewhere, [McpWorld.Init(true), McpWorld.Initialized, McpWorld.RootsReply("roots-1", "https://example.org/x", "file://evil-host" + other, McpWorld.FileUri(p1)), McpWorld.Call(10, "system_status", "{}")]);
        Assert.StartsWith(Hdr(p1, "roots"), McpWorld.ById(mixed.Out, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("file:///a/b", true, "/a/b")]
    [InlineData("file://localhost/a/b", true, "/a/b")]
    [InlineData("FILE://LOCALHOST/a/b/", true, "/a/b")]
    [InlineData("file:///a/my%20proj", true, "/a/my proj")]
    [InlineData("file:///a/%C3%BCber", true, "/a/über")]
    [InlineData("file://evil/a/b", false, "")]
    [InlineData("file://host.example/a", false, "")]
    [InlineData("https://x/a", false, "")]
    [InlineData("file:relative", false, "")]
    [InlineData("file:///a/%00b", false, "")]
    public void Root_uris_accept_only_file_with_an_empty_or_local_host_and_are_percent_decoded(string uri, bool ok, string path)
    {
        Assert.Equal(ok, McpRoots.TryParseFileUri(uri, out var got));
        Assert.Equal(path, got);
    }

    [Fact]
    public void Percent_encoded_roots_resolve_and_the_roots_log_file_gets_the_measurement_line()
    {
        using var w = new McpWorld();
        var spaced = w.Awaken(Path.Combine(w.Work, "my proj"));
        var log = Path.Combine(w.Base, "roots.log");
        var lines = new[] { McpWorld.Init(true), McpWorld.Initialized, McpWorld.RootsReply("roots-1", "file://localhost" + w.Work + "/my%20proj"), McpWorld.Call(10, "system_status", "{}") };
        var (msgs, _) = w.Rpc(w.Elsewhere, lines, new Dictionary<string, string?> { ["MONARCH_MCP_ROOTS_LOG"] = log });
        Assert.StartsWith(Hdr(spaced, "roots"), McpWorld.ById(msgs, 10)["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal($"mcp: roots=[{spaced}]\n", File.ReadAllText(log));
    }

    // ---- no client roots (T1) ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Without_roots_the_start_cwd_is_the_only_root_so_writing_tools_work_for_a_project_below_it()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var plan = """{"gate":{"id":"G-0007","title":"Seven","rank":"C","kind":"standard"},"quests":[{"id":"Q-0071","title":"Plain","kind":"main","rank":"D","scope":{"include":["src/web/**"],"exclude":[]},"estimate":{"session_pct":5,"weekly_pct":1},"dod":["a"]}]}""";
        var g = w.One(w.Work, null, "gate_plan", McpWorld.With(p1, plan[1..^1]));
        Assert.False(g.IsError, g.Text);
        Assert.StartsWith(Hdr(p1, "parameter"), g.Text, StringComparison.Ordinal);
        Assert.Single(Directory.GetFiles(Path.Combine(p1, ".monarch", "quests"), "Q-0071*"));
        var k = w.One(w.Work, null, "knowledge_write", McpWorld.With(p1, """"" "path":"n/a.md","content":"# x" """""));
        Assert.False(k.IsError, k.Text);
        Assert.Equal("# x", File.ReadAllText(Path.Combine(p1, ".monarch", "knowledge", "n", "a.md")));

        var outside = w.Awaken(Path.Combine(w.Elsewhere, "o"));
        var bad = w.One(w.Work, null, "knowledge_write", McpWorld.With(outside, """"" "path":"n/a.md","content":"x" """""));
        Assert.True(bad.IsError);
        Assert.False(File.Exists(Path.Combine(outside, ".monarch", "knowledge", "n", "a.md")));
    }

    [Fact]
    public void Writing_tools_resolve_implicitly_only_the_root_itself_or_an_ancestor_never_a_subdirectory()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var r = w.One(w.Work, null, "knowledge_write", """{"path":"n/a.md","content":"x"}""");
        Assert.True(r.IsError);
        Assert.False(File.Exists(Path.Combine(p1, ".monarch", "knowledge", "n", "a.md")));
        var up = w.One(Directory.CreateDirectory(Path.Combine(p1, "src")).FullName, null, "knowledge_write", """{"path":"n/a.md","content":"x"}""");
        Assert.False(up.IsError, up.Text);
    }

    // ---- parameter validation ---------------------------------------------------------------------------------------------------------

    public static TheoryData<string> BadParameters() => new()
    {
        "p1", "~/x", "$HOME/x", "", "../p1", "${HOME}",
    };

    [Theory]
    [MemberData(nameof(BadParameters))]
    public void Non_absolute_parameters_are_refused_at_the_first_stage(string value)
    {
        using var w = new McpWorld();
        w.Proj("p1");
        var r = w.One(w.Work, null, "system_status", McpWorld.With(value));
        Assert.True(r.IsError);
        Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
        Assert.Contains("(absolute)", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parameters_that_are_not_a_valid_project_root_are_refused_with_the_stage_in_the_message()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var file = Path.Combine(w.Work, "a-file");
        File.WriteAllText(file, "x");
        var plain = Directory.CreateDirectory(Path.Combine(w.Work, "plain")).FullName;
        var decoy = w.Awaken(Path.Combine(w.Work, "decoy"), systemYaml: false);
        var sub = Directory.CreateDirectory(Path.Combine(p1, "sub")).FullName;
        var cases = new (string Param, string Stage)[]
        {
            (file, "(exists)"), (Path.Combine(w.Work, "missing"), "(exists)"), (plain, "(awakened)"), (decoy, "(awakened)"), (sub, "(awakened)"),
        };
        foreach (var (param, stage) in cases)
        {
            var r = w.One(w.Work, null, "system_status", McpWorld.With(param));
            Assert.True(r.IsError, param);
            Assert.Contains(stage, r.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Links_and_dotdot_are_judged_by_the_physical_path()
    {
        using var w = new McpWorld();
        var p1 = w.Proj("p1");
        var outside = w.Awaken(Path.Combine(w.Elsewhere, "out"));
        Directory.CreateSymbolicLink(Path.Combine(w.Work, "to-out"), outside);
        Directory.CreateSymbolicLink(Path.Combine(w.Work, "to-p1"), p1);
        var viaOut = w.One(w.Work, null, "system_status", McpWorld.With(Path.Combine(w.Work, "to-out")));
        Assert.True(viaOut.IsError);
        Assert.Contains("(roots)", viaOut.Text, StringComparison.Ordinal);
        var viaIn = w.One(w.Work, null, "system_status", McpWorld.With(Path.Combine(w.Work, "to-p1")));
        Assert.False(viaIn.IsError, viaIn.Text);
        Assert.StartsWith(Hdr(p1, "parameter"), viaIn.Text, StringComparison.Ordinal);
        var dotdot = w.One(w.Work, null, "system_status", McpWorld.With(Path.Combine(p1, "..", "..", "elsewhere", "out")));
        Assert.True(dotdot.IsError);
        Assert.Contains("(roots)", dotdot.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_global_monarch_directories_and_their_parents_are_never_projects_physically()
    {
        using var w = new McpWorld();
        File.WriteAllText(Path.Combine(w.Home, "system.yaml"), "x");
        var userMonarch = Directory.CreateDirectory(Path.Combine(w.UserHome, ".monarch")).FullName;
        File.WriteAllText(Path.Combine(userMonarch, "system.yaml"), "x");
        Directory.CreateSymbolicLink(Path.Combine(w.Work, "home-link"), w.UserHome);
        Directory.CreateSymbolicLink(Path.Combine(w.Work, "mhome-link"), w.Home);
        var cases = new[] { w.UserHome, userMonarch, w.Home, Path.Combine(w.Home, "sub"), Path.Combine(w.Work, "home-link"), Path.Combine(w.Work, "mhome-link") };
        foreach (var param in cases)
        {
            Directory.CreateDirectory(Path.Combine(w.Home, "sub"));
            var r = w.One(w.Base, [w.Base], "system_status", McpWorld.With(param));
            Assert.True(r.IsError, param);
            Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
            Assert.Contains("(global)", r.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_parent_of_MONARCH_HOME_is_refused_when_MONARCH_HOME_lives_there_as_dot_monarch()
    {
        using var w = new McpWorld();
        var parent = Directory.CreateDirectory(Path.Combine(w.Base, "acct")).FullName;
        w.Home = Path.Combine(parent, ".monarch");
        Directory.CreateDirectory(w.Home);
        File.WriteAllText(Path.Combine(w.Home, "system.yaml"), "x");
        var r = w.One(w.Base, [w.Base], "system_status", McpWorld.With(parent));
        Assert.True(r.IsError);
        Assert.Contains("(global)", r.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_long_s_variant_is_judged_by_what_the_file_system_makes_of_it()
    {
        using var w = new McpWorld();
        var parent = Directory.CreateDirectory(Path.Combine(w.Base, "ms-acct")).FullName;
        w.Home = Path.Combine(parent, ".monarch");
        Directory.CreateDirectory(w.Home);
        File.WriteAllText(Path.Combine(w.Home, "system.yaml"), "x");
        var variant = Path.Combine(w.Base, "mſ-acct");
        var r = w.One(w.Base, [w.Base], "system_status", McpWorld.With(variant));
        Assert.True(r.IsError);
        Assert.Contains(Directory.Exists(variant) ? "(global)" : "(exists)", r.Text, StringComparison.Ordinal);
    }

    // ---- project boundaries (T2) ------------------------------------------------------------------------------------------------------

    private static readonly string[] AllowedInA =
    [
        ".monarch/gates/", ".monarch/quests/", ".monarch/knowledge/", ".monarch/ledger/", ".monarch/local/ledger-heads.json", ".monarch/local/writer-id",
        ".monarch/local/active.json", ".monarch/local/mana-calibration.json",
    ];

    [Fact]
    public void A_call_with_project_A_leaves_B_byte_identical_and_changes_A_only_in_the_expected_files()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var b = w.Proj("b");
        McpWorld.Gate(a);
        McpWorld.Quest(a, "Q-0001");
        McpWorld.Gate(b);
        McpWorld.Quest(b, "Q-0001");
        w.Http = new FakeForge();
        var plan = """"" "gate":{"id":"G-0009","title":"Nine","rank":"C","kind":"standard"},"quests":[{"id":"Q-0090","title":"Plain","kind":"main","rank":"D","scope":{"include":["src/web/**"],"exclude":[]},"estimate":{"session_pct":5,"weekly_pct":1},"dod":["a"]}] """"";
        var calls = new (string Tool, string Args, bool Writes)[]
        {
            ("system_status", McpWorld.With(a), false),
            ("mana_forecast", McpWorld.With(a, """"" "estimates":[{"session_pct":5,"weekly_pct":1}] """""), false),
            ("quest_transition", McpWorld.With(a, """"" "id":"Q-0001","action":"accept" """""), true),
            ("gate_plan", McpWorld.With(a, plan), true),
            ("knowledge_write", McpWorld.With(a, """"" "path":"x/n.md","content":"hi" """""), true),
            ("evidence_sync", McpWorld.With(a), false),
        };
        foreach (var (tool, args, _) in calls)
        {
            var beforeB = McpWorld.Snapshot(b);
            var beforeA = McpWorld.Snapshot(a);
            var r = w.One(w.Work, [w.Work], tool, args);
            Assert.False(r.IsError, tool + ": " + r.Text);
            Assert.Equal(beforeB, McpWorld.Snapshot(b));
            var afterA = McpWorld.Snapshot(a);
            Assert.Equal(beforeA[Path.Combine(".monarch", "system.yaml")], afterA[Path.Combine(".monarch", "system.yaml")]);
            foreach (var (file, hash) in afterA)
            {
                if (!beforeA.TryGetValue(file, out var old) || old != hash)
                {
                    Assert.True(AllowedInA.Any(p => file.StartsWith(p, StringComparison.Ordinal)), $"{tool} touched {file}");
                }
            }
        }
    }

    [Fact]
    public void A_quest_id_that_exists_only_in_B_fails_in_A_and_B_stays_untouched()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var b = w.Proj("b");
        McpWorld.Gate(b);
        McpWorld.Quest(b, "Q-0042");
        var before = McpWorld.Snapshot(b);
        var r = w.One(w.Work, [w.Work], "quest_transition", McpWorld.With(a, """"" "id":"Q-0042","action":"accept" """""));
        Assert.True(r.IsError);
        Assert.Equal(before, McpWorld.Snapshot(b));
    }

    [Fact]
    public void Knowledge_paths_cannot_reach_B_by_dotdot_or_by_a_link_inside_A()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var b = w.Proj("b");
        Directory.CreateSymbolicLink(Path.Combine(a, ".monarch", "knowledge", "to-b"), Path.Combine(b, ".monarch", "knowledge"));
        var before = McpWorld.Snapshot(b);
        foreach (var path in new[] { "../../../b/.monarch/knowledge/x.md", "to-b/x.md" })
        {
            var r = w.One(w.Work, [w.Work], "knowledge_write", McpWorld.With(a, $$""""" "path":"{{path}}","content":"x" """""));
            Assert.True(r.IsError, path);
        }

        Assert.Equal(before, McpWorld.Snapshot(b));
    }

    [Fact]
    public void A_twin_worktree_with_the_same_quest_id_is_addressed_by_the_parameter_only()
    {
        using var w = new McpWorld();
        var main = w.Proj("main");
        var wt = w.Awaken(Path.Combine(w.Work, ".monarch-worktrees", "wt"));
        foreach (var p in new[] { main, wt })
        {
            McpWorld.Gate(p);
            McpWorld.Quest(p, "Q-0001");
        }

        var mainBefore = McpWorld.Snapshot(main);
        var r = w.One(w.Work, [w.Work], "quest_transition", McpWorld.With(wt, """"" "id":"Q-0001","action":"accept" """""));
        Assert.False(r.IsError, r.Text);
        Assert.StartsWith(Hdr(wt, "parameter"), r.Text, StringComparison.Ordinal);
        Assert.Equal(mainBefore, McpWorld.Snapshot(main));
        Assert.Equal("accepted", Seed.State(Directory.GetFiles(Path.Combine(wt, ".monarch", "quests")).Single()));

        var wtBefore = McpWorld.Snapshot(wt);
        var r2 = w.One(w.Work, [w.Work], "quest_transition", McpWorld.With(main, """"" "id":"Q-0001","action":"accept" """""));
        Assert.False(r2.IsError, r2.Text);
        Assert.Equal(wtBefore, McpWorld.Snapshot(wt));
    }

    [Fact]
    public void A_nested_project_A_x_is_never_picked_implicitly_and_stays_untouched_by_project_A()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var x = w.Awaken(Path.Combine(a, "x"));
        var before = McpWorld.Snapshot(x);
        var implicitCall = w.One(w.Elsewhere, [a], "knowledge_write", """{"path":"n.md","content":"x"}""");
        Assert.False(implicitCall.IsError, implicitCall.Text);
        Assert.StartsWith(Hdr(a, "roots"), implicitCall.Text, StringComparison.Ordinal);
        var viaParam = w.One(w.Elsewhere, [a], "knowledge_write", McpWorld.With(a, """"" "path":"m.md","content":"x" """""));
        Assert.False(viaParam.IsError, viaParam.Text);
        Assert.Equal(before, McpWorld.Snapshot(x));
        var viaRoot = w.One(w.Elsewhere, [a], "system_status", McpWorld.With(x));
        Assert.StartsWith(Hdr(x, "parameter"), viaRoot.Text, StringComparison.Ordinal);
    }

    // ---- mana -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Mana_forecast_works_without_a_project_uncalibrated_with_plan_defaults_and_writes_nothing()
    {
        using var w = new McpWorld();
        var before = McpWorld.Snapshot(w.Base);
        var r = w.One(w.Elsewhere, [w.Elsewhere], "mana_forecast", """{"estimates":[{"session_pct":5,"weekly_pct":1}]}""");
        Assert.False(r.IsError, r.Text);
        Assert.Equal("project: null", McpWorld.Header(r.Text));
        var body = JsonNode.Parse(McpWorld.Body(r.Text))!;
        Assert.Equal(1.0, body["pair"]!["factor"]!.GetValue<double>());
        Assert.Equal("unkalibriert", body["pair"]!["status"]!.GetValue<string>());
        Assert.Equal("plan default", body["limitSource"]!.GetValue<string>());
        Assert.Equal(before, McpWorld.Snapshot(w.Base));
    }

    [Fact]
    public void Mana_forecast_with_a_project_takes_the_factor_from_that_projects_calibration_file()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var b = w.Proj("b");
        File.WriteAllText(Path.Combine(a, ".monarch", "local", "mana-calibration.json"), """{"schema":1,"pairs":{"unknown|xhigh":{"factor":3.2,"n":5}},"pending":{}}""");
        var vars = new Dictionary<string, string?> { ["CLAUDE_EFFORT"] = "xhigh" };
        var viaA = w.One(w.Work, [w.Work], "mana_forecast", McpWorld.With(a, """"" "estimates":[{"session_pct":5,"weekly_pct":1}] """""), vars);
        var viaB = w.One(w.Work, [w.Work], "mana_forecast", McpWorld.With(b, """"" "estimates":[{"session_pct":5,"weekly_pct":1}] """""), vars);
        Assert.Equal(3.2, JsonNode.Parse(McpWorld.Body(viaA.Text))!["pair"]!["factor"]!.GetValue<double>(), 3);
        Assert.Equal(1.0, JsonNode.Parse(McpWorld.Body(viaB.Text))!["pair"]!["factor"]!.GetValue<double>(), 3);
        Assert.StartsWith(Hdr(a, "parameter"), viaA.Text, StringComparison.Ordinal);
    }

    // ---- ledger and session (T3) ------------------------------------------------------------------------------------------------------

    [Fact]
    public void Writing_tools_log_McpProjectResolved_in_the_target_ledger_only_with_source_and_the_callers_session()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var b = w.Proj("b");
        File.WriteAllText(Path.Combine(a, ".monarch", "local", "current-session"), "foreign-session");
        var vars = new Dictionary<string, string?> { ["CLAUDE_CODE_SESSION_ID"] = "sess-caller" };
        Assert.False(w.One(w.Elsewhere, [w.Work], "knowledge_write", McpWorld.With(a, """"" "path":"n.md","content":"x" """""), vars).IsError);
        var ev = w.Events(a).Single(e => e.Type == "McpProjectResolved").Payload;
        Assert.Equal("knowledge_write", ev.GetProperty("tool").GetString());
        Assert.Equal(a, ev.GetProperty("project").GetString());
        Assert.Equal("parameter", ev.GetProperty("source").GetString());
        Assert.Equal(1, ev.GetProperty("roots_count").GetInt32());
        Assert.Equal("sess-caller", ev.GetProperty("session").GetString());
        Assert.Empty(w.Events(b));
        var order = w.Events(a).Select(e => e.Type).ToList();
        Assert.True(order.IndexOf("McpProjectResolved") < order.IndexOf("KnowledgeWritten"));
    }

    [Fact]
    public void Without_a_caller_session_the_session_is_mcp_prefixed_and_never_the_targets_current_session()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        File.WriteAllText(Path.Combine(a, ".monarch", "local", "current-session"), "foreign-session");
        Assert.False(w.One(w.Elsewhere, [a], "knowledge_write", """{"path":"n.md","content":"x"}""").IsError);
        var ev = w.Events(a).Single(e => e.Type == "McpProjectResolved").Payload;
        var session = ev.GetProperty("session").GetString()!;
        Assert.StartsWith("mcp-", session, StringComparison.Ordinal);
        Assert.NotEqual("local/current-session", session);
        Assert.DoesNotContain("foreign", session, StringComparison.Ordinal);
        Assert.Equal("roots", ev.GetProperty("source").GetString());
    }

    [Fact]
    public void The_ledger_stays_valid_after_mcp_writes_and_the_cwd_project_is_recorded()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        var cwdProj = w.Proj("c");
        Assert.False(w.One(cwdProj, [w.Work], "knowledge_write", McpWorld.With(a, """"" "path":"n.md","content":"x" """""), null).IsError);
        var ev = w.Events(a).Single(e => e.Type == "McpProjectResolved").Payload;
        Assert.Equal(cwdProj, ev.GetProperty("cwd_project").GetString());
        var verify = MonarchApp.Run(["ledger", "verify"], w.Env(a, new StringReader(string.Empty), new StringWriter(), new StringWriter()));
        Assert.Equal(0, verify);
    }

    [Fact]
    public void Reading_tools_write_nothing_not_even_when_the_project_comes_from_the_parameter()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        w.Proj("b");
        var before = McpWorld.Snapshot(w.Base);
        Assert.False(w.One(w.Elsewhere, [w.Work], "system_status", McpWorld.With(a)).IsError);
        Assert.False(w.One(w.Elsewhere, [w.Work], "mana_forecast", McpWorld.With(a, """"" "estimates":[{"session_pct":1,"weekly_pct":1}] """"")).IsError);
        Assert.Equal(before, McpWorld.Snapshot(w.Base));
    }

    [Fact]
    public void A_failed_resolution_writes_nowhere()
    {
        using var w = new McpWorld();
        w.Proj("a");
        w.Proj("b");
        var before = McpWorld.Snapshot(w.Base);
        foreach (var tool in new[] { "knowledge_write", "gate_plan", "quest_transition", "evidence_sync" })
        {
            var r = w.One(w.Elsewhere, [w.Work], tool, "{}");
            Assert.True(r.IsError, tool);
            Assert.StartsWith("project: null\n", r.Text, StringComparison.Ordinal);
        }

        Assert.Equal(before, McpWorld.Snapshot(w.Base));
    }

    [Fact]
    public void Every_tool_answer_begins_with_the_project_line()
    {
        using var w = new McpWorld();
        var a = w.Proj("a");
        McpWorld.Gate(a);
        McpWorld.Quest(a, "Q-0001");
        w.Http = new FakeForge();
        var calls = new (string Tool, string Args)[]
        {
            ("system_status", "{}"), ("mana_forecast", """{"estimates":[{"session_pct":1,"weekly_pct":1}]}"""), ("quest_transition", """{"id":"Q-0001","action":"accept"}"""),
            ("evidence_sync", "{}"), ("knowledge_write", """{"path":"n.md","content":"x"}"""), ("gate_plan", """{"gate":{"id":"bad","title":"x","rank":"C"},"quests":[]}"""),
        };
        foreach (var (tool, args) in calls)
        {
            // The first line is exactly the project line; a tool with no output of its own answers with that line alone.
            Assert.Equal(Hdr(a, "cwd"), McpWorld.Header(w.One(a, null, tool, args).Text));
        }
    }
}
