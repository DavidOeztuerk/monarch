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

    /// <summary>Eine zweite Worktree auf einem Gate-Branch, auf die die Datei zeigt (nicht das cwd-Projekt).</summary>
    private string OtherGateWorktree(string head)
    {
        var other = Path.Combine(_root, "other-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(other, ".git", "objects"));
        File.WriteAllText(Path.Combine(other, ".git", "HEAD"), head);
        return other;
    }

    [Fact]
    public void Quest_in_progress_in_the_cwd_project_does_not_allow_an_edit_into_another_gate_worktree()
    {
        // Warden PR 150, major: Die Regel las nur den Branch des cwd-Projekts. Ein Ziel in einer anderen Gate-Worktree bleibt verweigert.
        var other = OtherGateWorktree("ref: refs/heads/monarch/G-0001-x\n");
        var q = new ActiveQuest(new QuestId("Q-0181"), Rank.A, new QuestScope(["**"], []), InProgress: true);
        var d = Run(ToolCallPolicyTests.Edit(Path.Combine(other, "src", "x.cs")), q);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.QuestNotStarted, d.Rule);
    }

    [Fact]
    public void Edit_from_a_develop_checkout_into_a_gate_worktree_is_denied()
    {
        WriteHead("develop");
        var other = OtherGateWorktree("ref: refs/heads/monarch/G-0001-x\n");
        var d = Run(ToolCallPolicyTests.Edit(Path.Combine(other, "src", "x.cs")));
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.QuestNotStarted, d.Rule);
    }

    [Fact]
    public void Unreadable_HEAD_on_the_target_path_is_denied_not_silently_allowed()
    {
        // Warden PR 150, minor: ein unlesbares HEAD galt als "kein Branch" und ließ still durch.
        // cwd auf develop, damit nur das Ziel über das Ergebnis entscheidet.
        WriteHead("develop");
        var broken = Path.Combine(_root, "broken-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(broken, ".git", "HEAD"));
        var d = Run(ToolCallPolicyTests.Edit(Path.Combine(broken, "src", "x.cs")));
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.QuestNotStarted, d.Rule);
    }

    [Fact]
    public void Target_on_a_develop_checkout_is_not_affected_by_this_rule()
    {
        WriteHead("develop");
        var other = OtherGateWorktree("ref: refs/heads/develop\n");
        Assert.Equal(PolicyDecisionKind.Allow, Run(ToolCallPolicyTests.Edit(Path.Combine(other, "src", "x.cs"))).Kind);
    }
}
