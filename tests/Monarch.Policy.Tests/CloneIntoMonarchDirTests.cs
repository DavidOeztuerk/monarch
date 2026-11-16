using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>
/// Q-0164, Issue 119: a git clone whose target lies under .monarch/ but outside the protect_paths list, as the policy treats it today (Rank A).
/// Pinned behaviour only, no verdict on whether it is intended: such a clone is allowed; a target under a protected path
/// (.monarch/ledger/**, .monarch/quests/**) is denied, and so is a target directly under .monarch/shadows/ (pattern .monarch/shadows/*/shadow.yaml).
/// </summary>
public sealed class CloneIntoMonarchDirTests
{
    private const string Root = ToolCallPolicyTests.Root;

    private static PolicyContext RankA() =>
        new(Root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string cwd = "") =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), RankA());

    private static void Allowed(string cmd, string cwd = "")
    {
        var d = Run(cmd, cwd);
        Assert.True(d.Kind == PolicyDecisionKind.Allow, $"expected allow for: {cmd} (was {d.Kind}: {d.Reason})");
    }

    private static void Denied(string cmd, string cwd = "")
    {
        var d = Run(cmd, cwd);
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny for: {cmd} (was {d.Kind}: {d.Reason})");
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
    }

    [Fact]
    public void Clone_into_an_absolute_target_under_monarch_is_allowed_today() =>
        Allowed("git clone " + Root + " " + Root + "/.monarch/c1");

    [Fact]
    public void Clone_into_a_relative_target_under_monarch_is_allowed_today() =>
        Allowed("git clone " + Root + " .monarch/c1", Root);

    [Fact]
    public void Clone_into_a_target_under_monarch_recap_is_allowed_today() =>
        Allowed("git clone " + Root + " " + Root + "/.monarch/recap/c1");

    [Fact]
    public void Clone_into_a_target_directly_under_monarch_shadows_is_denied_today() =>
        Denied("git clone " + Root + " " + Root + "/.monarch/shadows/c1");

    [Fact]
    public void Clone_into_an_absolute_target_under_the_ledger_is_denied() =>
        Denied("git clone " + Root + " " + Root + "/.monarch/ledger/c1");

    [Fact]
    public void Clone_into_a_relative_target_under_the_ledger_is_denied() =>
        Denied("git clone " + Root + " .monarch/ledger/c1", Root);

    [Fact]
    public void Clone_into_a_target_under_quests_is_denied() =>
        Denied("git clone " + Root + " " + Root + "/.monarch/quests/c1");
}
