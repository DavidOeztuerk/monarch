using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>#149 (Owner 19.11.2026): auf einem Gate-Branch (monarch/G-*) verweigern Edit-Werkzeuge, solange keine Quest in_progress ist.</summary>
public sealed class QuestStartedTests : IDisposable
{
    private const string GateBranch = "monarch/G-0126-abschluss-v01";
    private readonly string _root;

    public QuestStartedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "qs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, ".git", "objects"));
        WriteHead(GateBranch);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void WriteHead(string branch) => File.WriteAllText(Path.Combine(_root, ".git", "HEAD"), $"ref: refs/heads/{branch}\n");

    private PolicyDecision Run(ToolCall c, ActiveQuest? q = null) => ToolCallPolicy.Evaluate(c, new PolicyContext(_root, q));

    [Theory]
    [InlineData("Edit")]
    [InlineData("Write")]
    [InlineData("MultiEdit")]
    [InlineData("NotebookEdit")]
    public void Mutation_on_a_gate_branch_without_any_quest_is_denied_and_names_the_way_out(string tool)
    {
        var d = Run(ToolCallPolicyTests.Edit("src/A.cs", tool));
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.QuestNotStarted, d.Rule);
        Assert.Contains("quest accept", d.Reason, StringComparison.Ordinal);
        Assert.Contains("quest start", d.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Accepted_but_not_started_quest_is_denied_and_names_its_id()
    {
        var q = new ActiveQuest(new QuestId("Q-0181"), Rank.A, new QuestScope(["src/**"], []));
        var d = Run(ToolCallPolicyTests.Edit("src/A.cs"), q);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.QuestNotStarted, d.Rule);
        Assert.Contains("quest start Q-0181", d.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void In_progress_quest_lets_the_scope_lock_decide()
    {
        var q = new ActiveQuest(new QuestId("Q-0181"), Rank.A, new QuestScope(["src/**"], []), InProgress: true);
        Assert.Equal(PolicyDecisionKind.Allow, Run(ToolCallPolicyTests.Edit("src/A.cs"), q).Kind);
    }

    [Theory]
    [InlineData(".monarch/recap/q-1.md")]
    [InlineData(".monarch/knowledge/n.md")]
    public void Recap_and_knowledge_stay_writable_without_a_quest(string path)
    {
        Assert.Equal(PolicyDecisionKind.Allow, Run(ToolCallPolicyTests.Edit(path, "Write")).Kind);
    }

    [Fact]
    public void Reads_and_other_branches_are_not_affected()
    {
        Assert.Equal(PolicyDecisionKind.Allow, Run(new ToolCall("Read", FilePath: "src/A.cs")).Kind);
        WriteHead("develop");
        Assert.Equal(PolicyDecisionKind.Allow, Run(ToolCallPolicyTests.Edit("src/A.cs")).Kind);
    }
}
