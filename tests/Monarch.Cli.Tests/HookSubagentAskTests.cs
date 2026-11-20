using System.Text.Json;
using FsCheck.Xunit;
using Monarch.Policy;

namespace Monarch.Cli.Tests;

/// <summary>
/// Q-0164: ask in a subagent becomes deny with the fixed way out (ADR-0003, addendum of 16.11.2026, approved at 14e638d).
/// Main cases (ask, text unchanged) and the boundary cases that keep ask stay unchanged.
/// Subagent cases (agent_id set, also without agent_type) expect deny; they were red in Q-0164 and are green since Q-0165.
/// Sources 6 and 8 (hook_error) and "the conversion never throws" are covered via the pure function HookRunner.ConvertAskForSubagent (Q-0165).
/// Sources 3 and 4 (push without refspec, unreadable config or branch) run over a rebuilt .git in the project root (Q-0167).
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
        "push_config_unreadable" => WithGit(new TempProject(activeRank: "B"), "ref: refs/heads/feature", configIsDirectory: true), // source 3
        "push_detached_head" => WithGit(new TempProject(activeRank: "B"), "0123456789abcdef0123456789abcdef01234567", configIsDirectory: false), // source 4
        _ => throw new ArgumentOutOfRangeException(nameof(scenario)),
    };

    // Rebuilt .git as in ProtectBranchesTests.cs:20-22; an unreadable config is a directory in place of the file.
    private static TempProject WithGit(TempProject p, string head, bool configIsDirectory)
    {
        var git = Path.Combine(p.Root, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "HEAD"), head + "\n");
        if (configIsDirectory)
        {
            Directory.CreateDirectory(Path.Combine(git, "config"));
        }
        else
        {
            File.WriteAllText(Path.Combine(git, "config"), "[core]\n\tbare = false\n");
        }

        return p;
    }

    private static string Input(string scenario, string agentFields)
    {
        var (tool, tin) = scenario switch
        {
            "rank_s" or "config_error" => ("Edit", """{"file_path":"src/a.cs"}"""),
            "integration_push" => ("Bash", """{"command":"git push origin develop"}"""),
            "checkout_force" => ("Bash", """{"command":"git checkout -f"}"""),
            "push_config_unreadable" or "push_detached_head" => ("Bash", """{"command":"git push"}"""),
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

    public static TheoryData<string> Scenarios => new() { "rank_s", "integration_push", "checkout_force", "config_error", "push_config_unreadable", "push_detached_head" };

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void Ledger_verify_stays_ok_after_a_converted_ask(string scenario)
    {
        using var p = Project(scenario);
        var (decision, _, _) = Decide(p, Input(scenario, Sub));
        Assert.Equal("deny", decision);
        Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
    }

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

    // Pure function (Q-0165). Sources 6 and 8 (hook_error) have no hook trigger; they are checked here with the real HookRunner text.

    private static PolicyDecision AskOf(string rule, string reason) => PolicyDecision.Ask(rule, reason);

    [Theory]
    [InlineData("protect_branches", "Push auf develop braucht Freigabe")]
    [InlineData("rank_s_approval", "Rank S")]
    [InlineData("hook_error", HookRunner.ErrorReason)] // sources 6 and 8
    public void Pure_ask_in_a_subagent_becomes_deny_with_the_exact_reason(string rule, string reason)
    {
        var (d, converted) = HookRunner.ConvertAskForSubagent(AskOf(rule, reason), "a1");
        Assert.True(converted);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(rule, d.Rule);
        Assert.Equal(reason + " " + Way, d.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Pure_ask_in_main_stays_unchanged(string? agentId)
    {
        var ask = AskOf("hook_error", HookRunner.ErrorReason);
        var (d, converted) = HookRunner.ConvertAskForSubagent(ask, agentId);
        Assert.False(converted);
        Assert.Same(ask, d);
    }

    [Theory]
    [InlineData("a1")]
    [InlineData(null)]
    public void Pure_deny_and_allow_stay_unchanged(string? agentId)
    {
        var deny = PolicyDecision.Deny("deny_bash", "nein");
        var (d1, c1) = HookRunner.ConvertAskForSubagent(deny, agentId);
        Assert.False(c1);
        Assert.Same(deny, d1);
        var (d2, c2) = HookRunner.ConvertAskForSubagent(PolicyDecision.Allow, agentId);
        Assert.False(c2);
        Assert.Same(PolicyDecision.Allow, d2);
    }

    [Fact]
    public void Pure_function_survives_a_null_reason_and_a_null_decision()
    {
        var (d, converted) = HookRunner.ConvertAskForSubagent(new PolicyDecision(PolicyDecisionKind.Ask, "r", null!), "a1");
        Assert.True(converted);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.EndsWith(Way, d.Reason);
        var (n, nc) = HookRunner.ConvertAskForSubagent(null!, "a1");
        Assert.False(nc);
        Assert.Null(n);
    }

    [Property(MaxTest = 200)]
    public bool Pure_function_never_weakens_and_leaves_main_identical(int kind, string? agentId, string? rule, string? reason)
    {
        var k = (PolicyDecisionKind)(int)(((long)kind % 3 + 3) % 3);
        var input = new PolicyDecision(k, rule ?? "", reason ?? "");
        var (d, converted) = HookRunner.ConvertAskForSubagent(input, agentId);
        var main = string.IsNullOrWhiteSpace(agentId);
        if (main) return ReferenceEquals(d, input) && !converted;
        if (input.Kind != PolicyDecisionKind.Allow && d.Kind == PolicyDecisionKind.Allow) return false;
        if (input.Kind == PolicyDecisionKind.Ask && d.Kind != PolicyDecisionKind.Deny) return false;
        return converted == (input.Kind == PolicyDecisionKind.Ask) && (converted || ReferenceEquals(d, input));
    }
}
