using System.Text.Json;
using FsCheck.Xunit;

namespace Monarch.Cli.Tests;

public class HookTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "hooks.jsonl");

    [Fact]
    public void Real_captured_hook_inputs_parse()
    {
        var lines = File.ReadAllLines(Fixture).Where(l => l.Length > 0).ToArray();
        Assert.Equal(10, lines.Length);
        var parsed = lines.Select(l => HookInput.Parse(l)!).ToArray();
        Assert.All(parsed, p => Assert.NotNull(p.HookEventName));
        Assert.Contains(parsed, p => p.HookEventName == "PreToolUse" && p.ToolName == "Bash" && p.ToolInput?.Command == "touch allowed-marker.txt");
        Assert.Contains(parsed, p => p.AgentType == "probe" && p.AgentId == "ac6fc4734ea3ce0fc");
        Assert.Contains(parsed, p => p.PermissionMode == "default");
        Assert.Contains(parsed, p => p.HookEventName == "SessionStart");
    }

    [Fact]
    public void Real_captured_inputs_run_through_the_hook_without_error_or_allow()
    {
        using var p = new TempProject(activeRank: "B");
        foreach (var line in File.ReadAllLines(Fixture).Where(l => l.Length > 0))
        {
            var evt = JsonDocument.Parse(line).RootElement.GetProperty("hook_event_name").GetString()!;
            var name = evt switch { "SessionStart" => "session-start", "PreToolUse" => "pre-tool-use", "PostToolUse" => "post-tool-use", "SubagentStop" => "subagent-stop", "Stop" => "stop", _ => "stop" };
            var r = p.Hook(name, line);
            Assert.Equal(0, r.Code);
            Assert.DoesNotContain("\"allow\"", r.Out, StringComparison.Ordinal);
        }

        Assert.False(File.Exists(p.LocalPath("hook-errors.log")));
    }

    [Fact]
    public void Non_awakened_directory_is_silent_and_untouched()
    {
        using var p = new TempProject(awakened: false);
        foreach (var evt in new[] { "session-start", "pre-tool-use", "post-tool-use", "subagent-stop", "stop", "pre-compact", "session-end" })
        {
            var r = p.Hook(evt, TempProject.PreToolUse("Edit", file: ".monarch/ledger/x.jsonl"));
            Assert.Equal(0, r.Code);
            Assert.Equal(string.Empty, r.Out);
        }

        Assert.False(Directory.Exists(Path.Combine(p.Root, ".monarch")));
    }

    [Fact]
    public void Project_root_is_found_by_walking_up()
    {
        using var p = new TempProject(activeRank: "B");
        var sub = Path.Combine(p.Root, "src", "deep");
        Directory.CreateDirectory(sub);
        var r = p.Run(["hook", "pre-tool-use"], TempProject.PreToolUse("Edit", file: Path.Combine(p.Root, ".monarch", "ledger", "x.jsonl")), cwd: sub);
        Assert.Contains("\"deny\"", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Deny_has_the_documented_shape()
    {
        using var p = new TempProject(activeRank: "B");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "git push --force origin main"));
        Assert.Equal(0, r.Code);
        var o = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("PreToolUse", o.GetProperty("hookEventName").GetString());
        Assert.Equal("deny", o.GetProperty("permissionDecision").GetString());
        Assert.False(string.IsNullOrWhiteSpace(o.GetProperty("permissionDecisionReason").GetString()));
        var ev = Assert.Single(p.Events(), e => e.Type == "PolicyDecided");
        Assert.Equal("deny_bash", ev.Payload.GetProperty("rule").GetString());
        Assert.Equal("deny", ev.Payload.GetProperty("kind").GetString());
        Assert.Equal("Q-0003", ev.Payload.GetProperty("quest").GetString());
    }

    [Fact]
    public void Rank_S_asks_and_allowed_calls_print_nothing()
    {
        using var p = new TempProject(activeRank: "S");
        var ask = p.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "src/a.cs"));
        Assert.Equal("ask", JsonDocument.Parse(ask.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
        using var q = new TempProject(activeRank: "B");
        var ok = q.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "src/a.cs"));
        Assert.Equal(0, ok.Code);
        Assert.Equal(string.Empty, ok.Out);
        Assert.DoesNotContain(q.Events(), e => e.Type == "PolicyDecided");
    }

    [Fact]
    public void Edit_outside_scope_is_denied_and_signals_double_dungeon()
    {
        using var p = new TempProject(activeRank: "B", include: "src/**");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "docs/readme.md"));
        Assert.Contains("\"deny\"", r.Out, StringComparison.Ordinal);
        Assert.Contains(p.Events(), e => e.Type == "DoubleDungeonSignaled");
        Assert.Equal(string.Empty, p.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "src/ok.cs")).Out);
    }

    [Theory]
    [InlineData("Edit", ".monarch/ledger/2026-10/s.jsonl", null)]
    [InlineData("Write", ".monarch/ledger/2026-10/s.jsonl", null)]
    [InlineData("Edit", ".monarch/local/ledger-heads.json", null)]
    [InlineData("Bash", null, "echo x >> .monarch/ledger/2026-10/s.jsonl")]
    [InlineData("Bash", null, "sed -i 's/a/b/' .monarch/ledger/2026-10/s.jsonl")]
    [InlineData("Bash", null, "rm .monarch/ledger/2026-10/s.jsonl")]
    [InlineData("Bash", null, "rm -rf .monarch/ledger")]
    [InlineData("Bash", null, "echo '{}' > .monarch/local/ledger-heads.json")]
    [InlineData("Bash", null, "rm .monarch/local/ledger-heads.json")]
    public void Agent_cannot_tamper_with_the_ledger_through_tools(string tool, string? file, string? command)
    {
        foreach (var rank in new[] { null, "E", "B", "A" })
        {
            using var p = new TempProject(activeRank: rank, include: "**");
            var r = p.Hook("pre-tool-use", TempProject.PreToolUse(tool, file, command));
            Assert.Contains("\"deny\"", r.Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Config_error_with_rank_A_asks_and_is_logged()
    {
        using var p = new TempProject(activeRank: "A", systemYaml: "policy:\n  deny_bash: [unterminated\n");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "src/a.cs"));
        Assert.Equal(0, r.Code);
        var o = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("ask", o.GetProperty("permissionDecision").GetString());
        Assert.Equal("Monarch-Hook-Fehler: Freigabe nötig", o.GetProperty("permissionDecisionReason").GetString());
        Assert.True(new FileInfo(p.LocalPath("hook-errors.log")).Length > 0);
    }

    [Fact]
    public void Config_error_with_low_rank_is_silent_but_logged()
    {
        using var p = new TempProject(activeRank: "B", systemYaml: "policy:\n  deny_bash: [unterminated\n");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Edit", file: "src/a.cs"));
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
        Assert.True(File.Exists(p.LocalPath("hook-errors.log")));
    }

    [Fact]
    public void Garbage_stdin_never_fails_visibly()
    {
        using var p = new TempProject();
        var r = p.Hook("pre-tool-use", "{not json");
        Assert.Equal(0, r.Code);
        Assert.Equal(string.Empty, r.Out);
    }

    [Property(MaxTest = 60)]
    public bool Never_prints_allow(int tool, int pathShape, int cmdShape, int mode, int agent, int rank)
    {
        static int I(int n, int m) => (int)(((long)n % m + m) % m);
        string[] tools = ["Edit", "Write", "MultiEdit", "NotebookEdit", "Bash", "Read", "Grep", "Agent", "WebFetch"];
        string[] paths = ["src/a.cs", "docs/x.md", ".monarch/ledger/a.jsonl", ".env", "a/.env.example", ".monarch/recap/RECAP.md", "../outside.txt", "/etc/passwd"];
        string[] cmds = ["ls", "git push --force", "rm -rf x", "cat .env", "echo a > src/b.cs", "dotnet test", "sed -i s/a/b/ src/a.cs"];
        string?[] modes = [null, "default", "plan", "acceptEdits", "auto", "dontAsk", "bypassPermissions", "weird"];
        string?[] agents = [null, "knight", "warden", "scout"];
        string?[] ranks = [null, "E", "C", "B", "A", "S"];
        using var p = new TempProject(activeRank: ranks[I(rank, ranks.Length)]);
        var json = I(pathShape, 3) == 0
            ? TempProject.PreToolUse(tools[I(tool, tools.Length)], command: cmds[I(cmdShape, cmds.Length)], mode: modes[I(mode, modes.Length)], agentType: agents[I(agent, agents.Length)])
            : TempProject.PreToolUse(tools[I(tool, tools.Length)], file: paths[I(pathShape, paths.Length)], mode: modes[I(mode, modes.Length)], agentType: agents[I(agent, agents.Length)]);
        var r = p.Hook("pre-tool-use", json);
        if (r.Code != 0) return false;
        if (r.Out.Length == 0) return true;
        var d = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString();
        return d is "deny" or "ask";
    }

    [Fact]
    public void Post_tool_use_appends_tool_used_only()
    {
        using var p = new TempProject(activeRank: "B");
        var r = p.Hook("post-tool-use", """{"session_id":"sess1","hook_event_name":"PostToolUse","agent_id":"a1","agent_type":"knight","tool_name":"Edit","tool_input":{"file_path":"src/a.cs"},"tool_response":{"x":"yyyy"}}""");
        Assert.Equal(string.Empty, r.Out);
        var ev = Assert.Single(p.Events());
        Assert.Equal("ToolUsed", ev.Type);
        Assert.Equal("Edit", ev.Payload.GetProperty("tool").GetString());
        Assert.Equal("knight", ev.Payload.GetProperty("agent_type").GetString());
        Assert.Equal("src/a.cs", ev.Payload.GetProperty("file").GetString());
        Assert.Equal("Q-0003", ev.Payload.GetProperty("quest").GetString());
    }

    [Fact]
    public void Subagent_stop_stop_and_session_end_append_events()
    {
        using var p = new TempProject(activeRank: "B");
        File.WriteAllText(Path.Combine(p.Home, "mana.json"),
            """{"schema":1,"written_at":"2026-10-27T11:59:00Z","context_window":{"used_percentage":30},"rate_limits":{"five_hour":{"used_percentage":20,"resets_at":1793115600},"seven_day":{"used_percentage":43,"resets_at":1793185200}},"rate_limits_at":"2026-10-27T11:59:00Z","rate_limits_stale":false}""");
        p.Hook("subagent-stop", """{"session_id":"sess1","agent_id":"a1","agent_type":"scout"}""");
        p.Hook("stop", """{"session_id":"sess1"}""");
        p.Hook("session-end", """{"session_id":"sess1"}""");
        var types = p.Events().Select(e => e.Type).ToArray();
        Assert.Equal(["ShadowRunCompleted", "TurnStopped", "SessionEnded", "ManaSnapshotTaken"], types);
    }

    [Fact]
    public void Session_start_writes_current_session_and_prints_a_short_briefing()
    {
        using var p = new TempProject(activeRank: "A");
        Directory.CreateDirectory(Path.Combine(p.Root, ".monarch", "recap"));
        File.WriteAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md"), "# recap");
        File.WriteAllText(Path.Combine(p.Home, "mana.json"),
            """{"schema":1,"written_at":"2026-10-27T11:59:00Z","rate_limits":{"five_hour":{"used_percentage":20,"resets_at":1793115600},"seven_day":{"used_percentage":43,"resets_at":1793185200}},"rate_limits_at":"2026-10-27T11:59:00Z","rate_limits_stale":false}""");
        var r = p.Hook("session-start", """{"session_id":"sess-42","hook_event_name":"SessionStart"}""");
        Assert.Equal("sess-42", File.ReadAllText(p.LocalPath("current-session")).Trim());
        var o = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", o.GetProperty("hookEventName").GetString());
        var text = o.GetProperty("additionalContext").GetString()!;
        Assert.Contains("Q-0003", text, StringComparison.Ordinal);
        Assert.Contains("RECAP.md", text, StringComparison.Ordinal);
        Assert.Contains("20%", text, StringComparison.Ordinal);
        Assert.True(text.Length <= 1400);
    }

    [Fact]
    public void Pre_compact_writes_handoff_and_event()
    {
        using var p = new TempProject(activeRank: "B");
        for (var i = 0; i < 12; i++)
        {
            p.Hook("post-tool-use", """{"session_id":"sess1","tool_name":"Edit","tool_input":{"file_path":"src/a.cs"}}""");
        }

        p.Hook("pre-compact", """{"session_id":"sess1"}""");
        var handoff = File.ReadAllText(p.LocalPath(Path.Combine("handoff", "sess1.md")));
        Assert.Contains("Q-0003", handoff, StringComparison.Ordinal);
        Assert.Contains("src/**", handoff, StringComparison.Ordinal);
        Assert.Equal(10, handoff.Split('\n').Count(l => l.Contains("ToolUsed", StringComparison.Ordinal)));
        Assert.Contains(p.Events(), e => e.Type == "HandoffWritten");
    }

    [Fact]
    public void Hook_events_keep_the_ledger_chain_valid()
    {
        using var p = new TempProject(activeRank: "B");
        p.Hook("post-tool-use", """{"session_id":"sess1","tool_name":"Edit","tool_input":{"file_path":"src/a.cs"}}""");
        p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "git reset --hard HEAD"));
        p.Hook("stop", """{"session_id":"sess1"}""");
        Assert.True(Monarch.Infrastructure.Ledger.LedgerVerifier.Verify(p.Root).Ok);
    }
}
