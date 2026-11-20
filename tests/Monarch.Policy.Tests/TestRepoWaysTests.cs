using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0163: the ways to work in a test repository outside the project, as the policy treats them today (Rank A).</summary>
public sealed class TestRepoWaysTests
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
    public void Clone_into_a_new_absolute_folder_outside_is_allowed() => Allowed("git clone " + Root + " /scratch/t/c1");

    [Fact]
    public void Clone_with_a_target_in_a_protection_zone_is_denied() => Denied("git clone " + Root + " " + Root + "/.monarch/ledger/c1");

    [Fact]
    public void Checkout_force_with_a_concrete_path_is_allowed() => Allowed("git checkout -f -- src/a.cs");

    [Fact]
    public void Checkout_force_with_a_concrete_path_is_allowed_with_git_dash_C() =>
        Allowed("git -C /scratch/t/c1 checkout -f -- src/a.cs");

    [Fact]
    public void Checkout_force_with_a_concrete_path_is_allowed_after_cd_outside() =>
        Allowed("cd /scratch/t/c1 && git checkout -f -- src/a.cs");

    [Fact]
    public void Checkout_force_with_dot_is_denied() => Denied("git checkout -f -- .");

    [Fact]
    public void Checkout_force_with_dot_is_denied_after_cd_outside() => Denied("cd /scratch/t/c1 && git checkout -f -- .");

    [Fact]
    public void Checkout_force_with_a_relative_ledger_path_is_denied() => Denied("git checkout -f -- .monarch/ledger/x.jsonl");

    [Fact]
    public void Rm_of_a_relative_ledger_path_after_cd_outside_is_denied() =>
        Denied("cd /scratch/t/c1 && rm .monarch/ledger/x.jsonl");

    [Fact]
    public void Git_rm_cached_of_a_relative_ledger_path_after_cd_outside_is_denied() =>
        Denied("cd /scratch/t/c1 && git rm --cached .monarch/ledger/x.jsonl");

    [Fact]
    public void Mutating_an_absolute_path_outside_without_monarch_is_allowed() => Allowed("rm -rf /scratch/t/c1/build");
}
