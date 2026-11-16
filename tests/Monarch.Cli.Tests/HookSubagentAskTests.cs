using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>
/// Q-0164: ask in a subagent becomes deny with the fixed way out (ADR-0003, addendum of 16.11.2026, approved at 14e638d).
/// Main cases (ask, text unchanged) and the boundary cases that keep ask are green today.
/// Red until Q-0165: every case that expects deny for a subagent (agent_id set), including agent_id without agent_type.
/// Not covered here, open for Q-0165: sources 6 and 8 (hook_error at HookRunner.cs:64 and :330) and "the conversion never throws", both via the pure function.
/// Not covered, no hook trigger without a git repository: sources 3 and 4 (push without refspec, unreadable config or branch).
/// </summary>
public sealed class HookSubagentAskTests
{
    private const string Way = "Im Subagenten gibt es keine Rückfrage. Stoppe diesen Schritt und melde ihn mit Befehl und Grund der Hauptsession.";

    private static TempProject Project(string scenario) => scenario switch
    {
        "rank_s" => new TempProject(activeRank: "S"),
        "integration_push" => new TempProject(activeRank: "B"),
        "checkout_force" => new TempProject(activeRank: "A"),
        "config_error" => new TempProject(activeRank: "A", systemYaml: "policy:\n  deny_bash: [unterminated\n"),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    private static string Input(string scenario, string agentFields)
    {
        var (tool, tin) = scenario switch
        {
            "rank_s" or "config_error" => ("Edit", """{"file_path":"src/a.cs"}"""),
            "integration_push" => ("Bash", """{"command":"git push origin develop"}"""),
            "checkout_force" => ("Bash", """{"command":"git checkout -f"}"""),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
        };
        return $$"""{"session_id":"sess1","hook_event_name":"PreToolUse","permission_mode":"default"{{agentFields}},"tool_name":"{{tool}}","tool_input":{{tin}}}""";
    }

    private const string Sub = ""","agent_id":"a1","agent_type":"knight" """;

    private static (string? Decision, string? Reason, string Raw) Decide(TempProject p, string json)
    {
        var r = p.Hook("pre-tool-use", json);
        Assert.Equal(0, r.Code);
        if (r.Out.Length == 0) return (null, null, r.Out);
        var o = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput");
        return (o.GetProperty("permissionDecision").GetString(), o.GetProperty("permissionDecisionReason").GetString(), r.Out);
    }

    public static TheoryData<string> Scenarios => new() { "rank_s", "integration_push", "checkout_force", "config_error" };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Main_asks_with_the_unchanged_text(string scenario)
    {
        using var p = Project(scenario);
        var (decision, reason, _) = Decide(p, Input(scenario, ""));
        Assert.Equal("ask", decision);
        Assert.False(string.IsNullOrWhiteSpace(reason));
        Assert.DoesNotContain("Subagenten", reason);
        var ev = Assert.Single(p.Events(), e => e.Type == "PolicyDecided");
        Assert.Equal("ask", ev.Payload.GetProperty("kind").GetString());
        Assert.False(ev.Payload.TryGetProperty("converted_from", out _));
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Subagent_gets_deny_with_the_original_text_and_the_way_out(string scenario)
    {
        using var m = Project(scenario);
        var (_, mainReason, _) = Decide(m, Input(scenario, ""));
        var mainRule = Assert.Single(m.Events(), e => e.Type == "PolicyDecided").Payload.GetProperty("rule").GetString();

        using var p = Project(scenario);
        var (decision, reason, _) = Decide(p, Input(scenario, Sub));
        Assert.Equal("deny", decision);
        Assert.Equal(mainReason + " " + Way, reason);
        var ev = Assert.Single(p.Events(), e => e.Type == "PolicyDecided");
        Assert.Equal("deny", ev.Payload.GetProperty("kind").GetString());
        Assert.Equal("ask", ev.Payload.GetProperty("converted_from").GetString());
        Assert.Equal("a1", ev.Payload.GetProperty("agent_id").GetString());
        Assert.Equal(mainRule, ev.Payload.GetProperty("rule").GetString());
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Subagent_with_an_ask_source_is_never_allowed_and_never_silent(string scenario)
    {
        using var p = Project(scenario);
        var (decision, _, raw) = Decide(p, Input(scenario, Sub));
        Assert.NotEqual(string.Empty, raw);
        Assert.Contains(decision, new[] { "ask", "deny" });
    }

    [Theory]
    [InlineData(""","agent_type":"knight" """, "ask")] // agent_type without agent_id: Main with --agent, stays ask (green today)
    [InlineData(""","agent_id":"a1" """, "deny")] // agent_id without agent_type: subagent (red until Q-0165)
    [InlineData(""","agent_id":"","agent_type":"knight" """, "ask")] // empty agent_id: Main (green today)
    [InlineData(""","agent_id":"   ","agent_type":"knight" """, "ask")] // whitespace only: Main (green today)
    [InlineData(""","agent_id":"  " """, "ask")] // whitespace only, no agent_type (green today)
    public void Boundary_cases_of_the_subagent_criterion(string agentFields, string expected)
    {
        using var p = Project("rank_s");
        var (decision, _, _) = Decide(p, Input("rank_s", agentFields));
        Assert.Equal(expected, decision);
    }
}
