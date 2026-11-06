using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0147: Fehlalarme #75 (tr als letzte Pipe-Stufe) und #68.2 (Issue-Text aus .monarch/knowledge), ADR-0017 Fassung 4.</summary>
public sealed class Adr0017Tests : IDisposable
{
    private readonly string _outer;
    private readonly string _wt;

    public Adr0017Tests()
    {
        _outer = Real(Directory.CreateTempSubdirectory("adr0017-").FullName);
        _wt = Path.Combine(_outer, ".claude", "worktrees", "wt");
        Directory.CreateDirectory(Path.Combine(_wt, ".monarch", "knowledge"));
        Directory.CreateDirectory(Path.Combine(_wt, ".monarch", "ledger"));
        File.WriteAllText(Path.Combine(_wt, ".monarch", "knowledge", "x.md"), "x");
        File.WriteAllText(Path.Combine(_wt, ".monarch", "knowledge", ".env"), "x");
        File.WriteAllText(Path.Combine(_wt, ".monarch", "ledger", "x"), "x");
        File.CreateSymbolicLink(Path.Combine(_wt, ".monarch", "knowledge", "link.md"), Path.Combine(_wt, ".monarch", "ledger", "x"));
    }

    public void Dispose() => Directory.Delete(_outer, true);

    /// <summary>Loest jeden Symlink im Pfad auf (macOS: /var ist ein Symlink auf /private/var).</summary>
    private static string Real(string path)
    {
        var cur = "/";
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            cur = Path.Combine(cur, part);
            if (new DirectoryInfo(cur).ResolveLinkTarget(true) is { } target) cur = target.FullName;
        }

        return cur;
    }

    private static PolicyContext Ctx(string root) =>
        new(root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string root = ToolCallPolicyTests.Root, string cwd = "") =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), Ctx(root));

    private static void Denied(string cmd, string root = ToolCallPolicyTests.Root, string cwd = "")
    {
        var d = Run(cmd, root, cwd);
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny for: {cmd} (was {d.Kind})");
    }

    private static void Allowed(string cmd, string root = ToolCallPolicyTests.Root, string cwd = "")
    {
        var d = Run(cmd, root, cwd);
        Assert.True(d.Kind == PolicyDecisionKind.Allow, $"expected allow for: {cmd} (was {d.Kind}: {d.Reason})");
    }

    // ---- #75
    [Fact]
    public void Tr_as_last_pipe_stage_is_allowed() =>
        Allowed("cd .monarch/quests && grep -l 'gate: G-0001' *.md | tr '\\n' ' '");

    [Theory]
    [InlineData("tr a b > .monarch/ledger/x")]
    [InlineData("tr a b .monarch/ledger/x")]
    [InlineData("cat f | tr a b > .monarch/quests/n")]
    [InlineData("cd .monarch/quests && cat f | tr a b | sh")]
    [InlineData("cd .monarch/quests && cat f | tr \"$X\" b")]
    [InlineData("cd .monarch/quests && cat f | tr a b c")]
    [InlineData("cd .monarch/quests && tr a b")]
    [InlineData("cd .monarch/quests && cat f | tr a b < f")]
    [InlineData("cd .monarch/quests && cat f | tr a b; tr a b")]
    [InlineData("cd .monarch/quests && cat f | env tr a b")]
    [InlineData("cd .monarch/quests && cat f | X=1 tr a b")]
    [InlineData("cd .monarch/quests && cat f | tr a b *")]
    [InlineData("cd .monarch/quests && cat f | tr -t a b")]
    public void Tr_counter_cases_stay_denied(string cmd) => Denied(cmd);

    // ---- #68.2: verschachtelte Worktree, beide Auswertungen
    private const string Issue = "gh issue create --title t --body-file .monarch/knowledge/x.md";

    [Fact]
    public void Issue_body_file_in_worktree_as_project_root_is_allowed() => Allowed(Issue, _wt, _wt);

    [Fact]
    public void Issue_body_file_in_worktree_with_surrounding_project_as_root_is_allowed() => Allowed(Issue, _outer, _wt);

    [Theory]
    [InlineData("gh issue create --title t --body-file .monarch/ledger/x")]
    [InlineData("gh issue create --title t --body-file .monarch/knowledge/../ledger/x")]
    [InlineData("gh issue create --title t --body-file .monarch/knowledge/link.md")]
    public void Issue_body_file_counter_cases_stay_denied_in_worktree(string cmd)
    {
        Denied(cmd, _wt, _wt);
        Denied(cmd, _outer, _wt);
    }

    [Fact]
    public void Issue_body_file_after_cd_with_or_is_denied()
    {
        Directory.CreateDirectory(Path.Combine(_outer, ".monarch", "knowledge"));
        Denied("cd . || cd ../../..; gh issue create --title t --body-file .monarch/knowledge/link.md", _outer, _wt);
    }

    // Im Projekt der Worktree sind diese Faelle heute erlaubt (bekannte Luecke bei .env, ADR-0017); der Hook nimmt das strengste Ergebnis,
    // also muss die Auswertung mit dem umgebenden Projekt verweigern.
    [Theory]
    [InlineData("gh issue create --title t --body-file .monarch/knowledge/.env")]
    [InlineData("gh issue edit 1 --body-file .monarch/knowledge/x.md")]
    [InlineData("gh pr create --title t --body-file .monarch/knowledge/x.md")]
    [InlineData("cd a || cd b; gh issue create --title t --body-file .monarch/knowledge/x.md")]
    public void Issue_body_file_counter_cases_stay_denied_with_surrounding_project(string cmd) => Denied(cmd, _outer, _wt);
}
