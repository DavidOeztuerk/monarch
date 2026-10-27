using Monarch.Policy;

namespace Monarch.Policy.Tests;

public class LocalStateProtectionTests
{
    [Fact]
    public void Default_protect_paths_include_local_state() =>
        Assert.Contains(".monarch/local/**", new PolicyOptions().ProtectPaths);

    [Theory]
    [InlineData(".monarch/local/ledger-heads.json")]
    [InlineData(".monarch/local/read.db")]
    [InlineData("./.monarch/local/snapshots/x.json")]
    public void Agents_cannot_edit_local_state(string path)
    {
        var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(path), ToolCallPolicyTests.Ctx());
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
    }

    [Fact]
    public void Bash_cannot_overwrite_the_head_anchor()
    {
        var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Bash("echo '{}' > .monarch/local/ledger-heads.json"), ToolCallPolicyTests.Ctx());
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
    }
}
