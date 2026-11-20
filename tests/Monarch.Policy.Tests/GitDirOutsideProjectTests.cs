using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>
/// Counter-proofs for <c>git -C &lt;dir&gt;</c> with a directory outside the project: today these stay deny or ask.
/// Anything not provably outside every protected zone is kept, a relative protected path reached from the fixed
/// directory is denied, and the home zone and symlinks into the project still count. The cases that are red today
/// (a pathless checkout or a relative ledger path in a repository outside the project) live in issue 105.
/// </summary>
public sealed class GitDirOutsideProjectTests : IDisposable
{
    private const string Root = ToolCallPolicyTests.Root;
    private const string Outside = "/scratch/warden/e1";
    private static readonly string Home = (Environment.GetEnvironmentVariable("HOME") ?? "/home/u").TrimEnd('/');
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "gitdir-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { /* best effort */ }
    }

    private static PolicyContext RankA(string root) =>
        new(root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string root = Root, string cwd = "") =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), RankA(root));

    private static void Kept(string cmd, PolicyDecisionKind kind, string root = Root)
    {
        var d = Run(cmd, root);
        Assert.True(d.Kind == kind, $"expected {kind} for: {cmd} (was {d.Kind}: {d.Reason})");
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
    }

    [Theory]
    [InlineData("git -C " + Root + " checkout -f")]
    [InlineData("git -C " + Root + "/src checkout -f")]
    [InlineData("git -C " + Outside + "/../../../work/proj checkout -f")]
    [InlineData("git -C $D checkout -f")]
    [InlineData("git -C ~/scratch checkout -f")]
    [InlineData("git -C /scratch/* checkout -f")]
    [InlineData("git -C src checkout -f")]
    [InlineData("git -C " + Outside + " --work-tree=" + Root + " checkout -f")]
    [InlineData("git -C " + Outside + " --work-tree " + Root + " checkout -f")]
    [InlineData("git -C " + Outside + " --git-dir=" + Root + "/.git checkout -f")]
    [InlineData("GIT_WORK_TREE=" + Root + " git -C " + Outside + " checkout -f")]
    [InlineData("git -c core.worktree=" + Root + " -C " + Outside + " checkout -f")]
    [InlineData("git -C " + Outside + " -C ../../../work/proj checkout -f")]
    [InlineData("checkout() { :; }; git -C " + Outside + " checkout -f")]
    public void Not_provably_outside_still_asks(string cmd) => Kept(cmd, PolicyDecisionKind.Ask);

    [Theory]
    [InlineData("git -C " + Outside + " rm " + Root + "/.monarch/ledger/x.jsonl")]
    [InlineData("git -C " + Outside + " rm ../../../work/proj/.monarch/ledger/x.jsonl")]
    [InlineData("git -C " + Root + " rm .monarch/ledger/x.jsonl")]
    [InlineData("git -C $D rm .monarch/ledger/x.jsonl")]
    [InlineData("git -C " + Outside + " rm .monarch/ledger/x.jsonl && rm .monarch/ledger/x.jsonl")]
    public void A_protected_path_reached_from_the_fixed_directory_is_still_denied(string cmd) => Kept(cmd, PolicyDecisionKind.Deny);

    [Fact]
    public void The_home_zone_still_counts_from_a_fixed_directory()
    {
        var d = Run($"git -C {Home} rm .monarch/bin/monarch");
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny (was {d.Kind}: {d.Reason})");
    }

    [Fact]
    public void A_fixed_directory_that_is_a_symlink_into_the_project_still_asks()
    {
        var project = Path.Combine(_tmp, "proj");
        Directory.CreateDirectory(Path.Combine(project, ".monarch", "ledger"));
        var link = Path.Combine(_tmp, "link");
        Directory.CreateSymbolicLink(link, project);

        Kept($"git -C {link} checkout -f", PolicyDecisionKind.Ask, project);
        Kept($"git -C {link} rm .monarch/ledger/x.jsonl", PolicyDecisionKind.Deny, project);
    }
}
