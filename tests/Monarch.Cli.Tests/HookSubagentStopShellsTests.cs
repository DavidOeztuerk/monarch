using System.Diagnostics;
using System.Text.Json;
using Monarch.Application.Net;

namespace Monarch.Cli.Tests;

/// <summary>
/// Q-0166 (ADR-0001 addendum): post-tool-use remembers background shells of a subagent, subagent-stop reports the ones still running
/// as a ledger event (program, pid, runtime; never arguments) and ends nothing. Seams: process list, hook parent pid and clock are
/// injected through <see cref="CliEnv"/>; one integration case uses the real <c>ps</c> and a real child process.
/// </summary>
public sealed class HookSubagentStopShellsTests
{
    private const string Hull = "/bin/zsh -c source /x/shell-snapshots/s.sh && eval ";

    private static (int Code, string Out, string Err) HookWith(TempProject p, string evt, string json, Func<string?>? ps = null, int? parent = null, IReadOnlyDictionary<string, string?>? vars = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = MonarchApp.Run(["hook", evt], new CliEnv(p.Root, p.Home, new StringReader(json), o, e, p.Clock, null,
            new GitHubTokenProvider(_ => "t", () => null), false, vars ?? TempProject.NoVars, null, ps, parent));
        return (code, o.ToString(), e.ToString());
    }

    private static string Q(string s) => JsonSerializer.Serialize(s);

    private static string Start(string? agentId, string? taskId, string command, string tool = "Bash")
    {
        var agent = agentId is null ? "" : $$""","agent_id":{{Q(agentId)}},"agent_type":"knight" """;
        var response = taskId is null ? """{"stdout":"x"}""" : $$"""{"backgroundTaskId":{{Q(taskId)}},"stdout":""}""";
        return $$"""{"session_id":"sess1","hook_event_name":"PostToolUse"{{agent}},"tool_name":"{{tool}}","tool_input":{"command":{{Q(command)}}},"tool_response":{{response}}}""";
    }

    private static string Stop(string agentId, string? tasks, string extra = "")
    {
        var bg = tasks is null ? "" : $$""","background_tasks":{{tasks}}""";
        return $$"""{"session_id":"sess1","hook_event_name":"SubagentStop","agent_id":{{Q(agentId)}},"agent_type":"knight"{{bg}}{{extra}}}""";
    }

    private static string Tasks(params (string Id, string Command)[] shells) =>
        "[" + string.Join(",", shells.Select(s => $$"""{"id":{{Q(s.Id)}},"type":"shell","status":"running","description":"d","command":{{Q(s.Command)}}}""")) + "]";

    private static string ShellDir(TempProject p, string agent) => Path.Combine(p.Home, "local", "background-shells", agent);

    private static List<JsonElement> Shells(TempProject p, string type = "BackgroundShellsLeft") =>
        p.Events().Where(e => e.Type == type).Select(e => e.Payload).ToList();

    private static string LedgerText(TempProject p) =>
        string.Concat(Directory.EnumerateFiles(Path.Combine(p.Root, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories).Select(File.ReadAllText));

    private static Func<string?> Ps(params string[] lines) => () => string.Join("\n", lines) + "\n";

    // ---- post-tool-use: remembering ----

    [Fact]
    public void Post_tool_use_of_a_subagent_background_bash_writes_one_task_file_and_keeps_tool_used()
    {
        using var p = new TempProject(activeRank: "B");
        var r = HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        var file = Path.Combine(ShellDir(p, "a1"), "t1.json");
        var doc = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        Assert.Equal("sleep", doc.GetProperty("program").GetString());
        Assert.Equal(TempProject.Now, DateTimeOffset.Parse(doc.GetProperty("started_at").GetString()!, System.Globalization.CultureInfo.InvariantCulture));
        Assert.DoesNotContain("301", File.ReadAllText(file), StringComparison.Ordinal);
        Assert.Equal(["ToolUsed"], p.Events().Select(e => e.Type).ToArray());
    }

    [Fact]
    public void Post_tool_use_of_the_main_session_without_agent_id_writes_nothing()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start(null, "m1", "sleep 302"));
        Assert.False(Directory.Exists(Path.Combine(p.Home, "local")));
    }

    [Theory]
    [InlineData("a1", null, "Bash")]
    [InlineData("a1", "", "Bash")]
    [InlineData("a1", "t1", "Edit")]
    [InlineData("  ", "t1", "Bash")]
    [InlineData("../evil", "t1", "Bash")]
    [InlineData("a1", "../t1", "Bash")]
    [InlineData("a/1", "t1", "Bash")]
    [InlineData("a1", "t 1", "Bash")]
    public void Post_tool_use_writes_nothing_without_valid_ids_bash_and_task(string agent, string? task, string tool)
    {
        using var p = new TempProject(activeRank: "B");
        var r = HookWith(p, "post-tool-use", Start(agent, task, "sleep 1", tool));
        Assert.Equal(0, r.Code);
        Assert.False(Directory.Exists(Path.Combine(p.Home, "local")));
        Assert.Single(p.Events());
    }

    [Theory]
    [InlineData("sleep 301", "sleep")]
    [InlineData("/usr/bin/python3 -m http.server", "python3")]
    [InlineData("./run-it.sh --x", "run-it.sh")]
    [InlineData("FOO=wert/mit/schraegstrich cmd", "unbekannt")]
    [InlineData("FOO=bar sleep 1", "unbekannt")]
    [InlineData("\"quoted\" x", "unbekannt")]
    [InlineData("$HOME/bin/tool x", "unbekannt")]
    [InlineData("/usr/bin/ x", "unbekannt")]
    [InlineData("", "unbekannt")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa x", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa x", "unbekannt")]
    [InlineData("/opt/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa x", "unbekannt")]
    public void Program_name_is_checked_on_the_whole_first_word_and_never_shortened(string command, string expected)
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", command));
        var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ShellDir(p, "a1"), "t1.json"))).RootElement;
        Assert.Equal(expected, doc.GetProperty("program").GetString());
        if (command.StartsWith("FOO=wert", StringComparison.Ordinal))
        {
            var text = File.ReadAllText(Path.Combine(ShellDir(p, "a1"), "t1.json"));
            Assert.DoesNotContain("schraegstrich", text, StringComparison.Ordinal);
            Assert.DoesNotContain("wert", text, StringComparison.Ordinal);
            HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", command))), Ps(), 500);
            var raw = LedgerText(p);
            Assert.DoesNotContain("schraegstrich", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("wert", raw, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("5")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("{\"backgroundTaskId\":5}")]
    public void A_broken_tool_response_keeps_tool_used_and_writes_nothing(string response)
    {
        using var p = new TempProject(activeRank: "B");
        var json = $$"""{"session_id":"sess1","agent_id":"a1","agent_type":"knight","tool_name":"Bash","tool_input":{"command":"sleep 1"},"tool_response":{{response}}}""";
        var r = HookWith(p, "post-tool-use", json);
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(["ToolUsed"], p.Events().Select(e => e.Type).ToArray());
        Assert.False(Directory.Exists(Path.Combine(p.Home, "local")));
    }

    // ---- subagent-stop: reporting ----

    [Fact]
    public void Stop_without_task_files_appends_only_shadow_run_completed_and_does_not_ask_ps()
    {
        using var p = new TempProject(activeRank: "B");
        var calls = 0;
        var r = HookWith(p, "subagent-stop", Stop("a1", Tasks(("m1", "sleep 302"))), () => { calls++; return null; });
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(0, calls);
        Assert.Equal(["ShadowRunCompleted"], p.Events().Select(e => e.Type).ToArray());
    }

    [Fact]
    public void Stop_reports_only_the_running_shells_of_the_agent_with_program_pid_and_runtime()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        HookWith(p, "post-tool-use", Start("a1", "t2", "curl --flag=geheimwort https://example.invalid"));
        HookWith(p, "post-tool-use", Start("a1", "t3", "sleep 7"));
        HookWith(p, "post-tool-use", Start("a2", "o1", "sleep 9"));
        p.Clock.UtcNow = TempProject.Now.AddSeconds(90);
        var tasks = Tasks(("t1", "sleep 301"), ("t2", "curl --flag=geheimwort https://example.invalid"), ("m1", "sleep 302"), ("o1", "sleep 9"));
        var r = HookWith(p, "subagent-stop", Stop("a1", tasks), Ps("500 1 claude", "600 500 " + Hull + "'sleep 301'", "601 600 sleep 301"), 500);
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(["ShadowRunCompleted", "BackgroundShellsLeft"], p.Events().Select(e => e.Type).Where(t => t != "ToolUsed").ToArray());
        var ev = Assert.Single(Shells(p));
        Assert.Equal("a1", ev.GetProperty("agent_id").GetString());
        Assert.Equal("knight", ev.GetProperty("agent_type").GetString());
        Assert.Equal("Q-0003", ev.GetProperty("quest").GetString());
        var shells = ev.GetProperty("shells").EnumerateArray().OrderBy(s => s.GetProperty("program").GetString(), StringComparer.Ordinal).ToList();
        Assert.Equal(2, shells.Count);
        Assert.Equal("curl", shells[0].GetProperty("program").GetString());
        Assert.False(shells[0].TryGetProperty("pid", out _));
        Assert.Equal(90, shells[0].GetProperty("runtime_s").GetInt64());
        Assert.Equal("sleep", shells[1].GetProperty("program").GetString());
        Assert.Equal(600, shells[1].GetProperty("pid").GetInt32());
        Assert.All(shells, s => Assert.Empty(s.EnumerateObject().Select(x => x.Name).Except(["program", "pid", "runtime_s"])));
        var raw = LedgerText(p);
        Assert.DoesNotContain("geheimwort", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("example.invalid", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("sleep 301", raw, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(ShellDir(p, "a1"), "t1.json")));
        Assert.False(File.Exists(Path.Combine(ShellDir(p, "a1"), "t3.json")));
        Assert.True(File.Exists(Path.Combine(ShellDir(p, "a2"), "o1.json")));
    }

    [Fact]
    public void A_task_that_no_longer_runs_is_not_reported_and_its_file_is_removed()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        var r = HookWith(p, "subagent-stop", Stop("a1", "[]"), Ps());
        Assert.Equal(0, r.Code);
        Assert.Empty(Shells(p));
        Assert.False(File.Exists(Path.Combine(ShellDir(p, "a1"), "t1.json")));
    }

    [Fact]
    public void A_second_stop_after_resumption_reports_only_new_shells()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 301"))), Ps(), 500);
        HookWith(p, "post-tool-use", Start("a1", "t3", "tail -f x"));
        // t1 still runs in the session list, but was reported and forgotten.
        HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 301"), ("t3", "tail -f x"))), Ps(), 500);
        var events = Shells(p);
        Assert.Equal(2, events.Count);
        Assert.Equal(["sleep"], events[0].GetProperty("shells").EnumerateArray().Select(s => s.GetProperty("program").GetString()).ToArray());
        Assert.Equal(["tail"], events[1].GetProperty("shells").EnumerateArray().Select(s => s.GetProperty("program").GetString()).ToArray());
    }

    [Fact]
    public void A_broken_task_file_is_skipped_and_logged_and_the_others_are_reported()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        File.WriteAllText(Path.Combine(ShellDir(p, "a1"), "t2.json"), "{not json");
        var r = HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 301"), ("t2", "sleep 2"))), Ps(), 500);
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        var ev = Assert.Single(Shells(p));
        Assert.Equal(["sleep"], ev.GetProperty("shells").EnumerateArray().Select(s => s.GetProperty("program").GetString()).ToArray());
        Assert.Contains("background-shells", File.ReadAllText(p.LocalPath("hook-errors.log")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"text\"")]
    [InlineData("{\"a\":1}")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("[1,\"x\",null,{\"id\":5,\"type\":\"shell\",\"status\":\"running\"},{\"type\":\"shell\"}]")]
    public void A_broken_background_tasks_keeps_shadow_run_completed_and_reports_nothing(string tasks)
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        var r = HookWith(p, "subagent-stop", Stop("a1", tasks), Ps(), 500);
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(["ToolUsed", "ShadowRunCompleted"], p.Events().Select(e => e.Type).ToArray());
    }

    [Fact]
    public void A_tool_response_of_the_wrong_type_does_not_make_the_stop_input_unreadable()
    {
        using var p = new TempProject(activeRank: "B");
        var r = HookWith(p, "subagent-stop", Stop("a1", "\"x\"", ",\"tool_response\":\"text\""));
        Assert.Equal(0, r.Code);
        Assert.Equal(["ShadowRunCompleted"], p.Events().Select(e => e.Type).ToArray());
    }

    [Fact]
    public void Process_list_failures_still_report_the_shell_without_pid_and_only_log()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        var r = HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 301"))), () => throw new InvalidOperationException("ps kaputt"), 500);
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        var shell = Assert.Single(Assert.Single(Shells(p)).GetProperty("shells").EnumerateArray());
        Assert.False(shell.TryGetProperty("pid", out _));
        Assert.Contains("ps kaputt", File.ReadAllText(p.LocalPath("hook-errors.log")), StringComparison.Ordinal);
    }

    [Fact]
    public void An_unavailable_process_list_reports_the_shell_without_pid()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 301"));
        HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 301"))), () => null, 500);
        var shell = Assert.Single(Assert.Single(Shells(p)).GetProperty("shells").EnumerateArray());
        Assert.False(shell.TryGetProperty("pid", out _));
        Assert.Equal("sleep", shell.GetProperty("program").GetString());
    }

    // ---- PID match: null, one, two ----

    private static int? PidFor(string command, int? parent, Func<string?> ps)
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "t1", command));
        HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", command))), ps, parent);
        var shell = Assert.Single(Assert.Single(Shells(p)).GetProperty("shells").EnumerateArray());
        return shell.TryGetProperty("pid", out var pid) ? pid.GetInt32() : null;
    }

    [Fact]
    public void No_matching_process_gives_no_pid() =>
        Assert.Null(PidFor("sleep 301", 500, Ps("500 1 claude", "601 600 sleep 301", "700 500 /bin/zsh -c other")));

    [Fact]
    public void One_matching_hull_gives_its_pid_not_the_childs()
    {
        Assert.Equal(600, PidFor("sleep 301", 500, Ps("500 1 claude", "600 500 " + Hull + "'sleep 301'", "601 600 sleep 301")));
    }

    [Fact]
    public void Two_matching_hulls_give_no_pid() =>
        Assert.Null(PidFor("sleep 301", 500, Ps("600 500 " + Hull + "'sleep 301'", "610 500 " + Hull + "'sleep 301'")));

    [Fact]
    public void A_hull_of_another_parent_does_not_count() =>
        Assert.Null(PidFor("sleep 301", 500, Ps("600 999 " + Hull + "'sleep 301'")));

    [Fact]
    public void A_longer_command_with_the_same_start_does_not_match() =>
        Assert.Null(PidFor("sleep 30", 500, Ps("600 500 " + Hull + "'sleep 301'")));

    [Fact]
    public void Single_quotes_in_the_command_are_matched_in_the_shell_encoding() =>
        Assert.Equal(600, PidFor("echo 'hi' there", 500, Ps("600 500 " + Hull + "'echo '\"'\"'hi'\"'\"' there'")));

    [Fact]
    public void Without_an_injected_parent_it_is_read_from_the_line_of_this_process() =>
        Assert.Equal(600, PidFor("sleep 301", null, Ps($"{Environment.ProcessId} 500 dotnet test", "600 500 " + Hull + "'sleep 301'", "601 77 " + Hull + "'sleep 301'")));

    [Fact]
    public void Without_a_line_for_this_process_there_is_no_parent_and_no_pid() =>
        Assert.Null(PidFor("sleep 301", null, Ps("600 500 " + Hull + "'sleep 301'")));

    // ---- cleanup ----

    [Fact]
    public void Task_files_older_than_24_hours_of_any_agent_are_removed_by_the_injected_clock()
    {
        using var p = new TempProject(activeRank: "B");
        HookWith(p, "post-tool-use", Start("a1", "old", "sleep 301"));
        p.Clock.UtcNow = TempProject.Now.AddHours(23);
        HookWith(p, "post-tool-use", Start("a3", "young", "sleep 5"));
        p.Clock.UtcNow = TempProject.Now.AddHours(25);
        var r = HookWith(p, "subagent-stop", Stop("a2", "[]"), Ps());
        Assert.Equal(0, r.Code);
        Assert.False(File.Exists(Path.Combine(ShellDir(p, "a1"), "old.json")));
        Assert.True(File.Exists(Path.Combine(ShellDir(p, "a3"), "young.json")));
    }

    // ---- integration: real ps, real child, nothing is ended ----

    [Fact]
    public void Real_ps_finds_the_hull_of_a_real_child_and_the_hook_leaves_it_running()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var psi = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("eval 'sleep 30'; true");
        using var child = Process.Start(psi)!;
        try
        {
            using var p = new TempProject(activeRank: "B");
            HookWith(p, "post-tool-use", Start("a1", "t1", "sleep 30"));
            var vars = new Dictionary<string, string?> { ["PATH"] = Environment.GetEnvironmentVariable("PATH") };
            var sw = Stopwatch.StartNew();
            var r = HookWith(p, "subagent-stop", Stop("a1", Tasks(("t1", "sleep 30"))), null, Environment.ProcessId, vars);
            sw.Stop();
            Assert.Equal(0, r.Code);
            Assert.Equal(string.Empty, r.Out);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"hook took {sw.Elapsed}");
            var shell = Assert.Single(Assert.Single(Shells(p)).GetProperty("shells").EnumerateArray());
            Assert.Equal("sleep", shell.GetProperty("program").GetString());
            Assert.Equal(child.Id, shell.GetProperty("pid").GetInt32());
            Assert.False(child.HasExited);
        }
        finally
        {
            try
            {
                child.Kill(true);
                child.WaitForExit();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
