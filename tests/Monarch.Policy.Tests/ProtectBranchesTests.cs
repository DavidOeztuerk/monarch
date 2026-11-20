using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0113 / ADR-0007: protect_branches. Every form of a push to a protected branch, gh pr merge and gh api merges is denied.</summary>
public sealed class ProtectBranchesTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "monarch-pb-" + Guid.NewGuid().ToString("N"));

    public ProtectBranchesTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { }
    }

    private string Repo(string name, string head = "ref: refs/heads/main", string config = "[core]\n\tbare = false\n")
    {
        var dir = Path.Combine(_tmp, name);
        Directory.CreateDirectory(Path.Combine(dir, ".git"));
        File.WriteAllText(Path.Combine(dir, ".git", "HEAD"), head + "\n");
        File.WriteAllText(Path.Combine(dir, ".git", "config"), config);
        return dir;
    }

    private PolicyDecision Run(string cmd, string cwd, string? agent = null) =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, AgentType: agent, Cwd: cwd), new PolicyContext(_tmp, null, false, new PolicyOptions { DenyBash = [] }) { HomeDir = Path.Combine(_tmp, "home") });

    private void Denied(string cmd, string cwd, string? agent = null)
    {
        var d = Run(cmd, cwd, agent);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ProtectBranches, d.Rule);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    private void Allowed(string cmd, string cwd) => Assert.Equal(PolicyDecisionKind.Allow, Run(cmd, cwd).Kind);

    [Theory]
    [InlineData("git push origin main")]
    [InlineData("git push origin HEAD")]
    [InlineData("git push -u origin HEAD")]
    [InlineData("git push origin @")]
    [InlineData("git push origin HEAD:main")]
    [InlineData("git push origin x:refs/heads/main")]
    [InlineData("git push origin +main")]
    [InlineData("git push origin :main")]
    [InlineData("git push --delete origin main")]
    [InlineData("git push -d origin main")]
    [InlineData("git push origin --delete refs/heads/main")]
    [InlineData("git push origin MAIN")]
    [InlineData("git push origin HEAD:refs/heads/Master")]
    [InlineData("git push origin master")]
    [InlineData("git push --all")]
    [InlineData("git push --branches origin")]
    [InlineData("git push --mirror origin")]
    [InlineData("git push origin 'refs/heads/*:refs/heads/*'")]
    [InlineData("git push origin '+refs/heads/*:refs/heads/*'")]
    [InlineData("git push origin 'feature:refs/heads/ma*'")]
    [InlineData("git push")]
    [InlineData("git push origin")]
    [InlineData("git push --no-verify -u origin")]
    [InlineData("git push -fu origin HEAD")]
    [InlineData("bash -c 'git push origin main'")]
    [InlineData("bash -c \"git push origin HEAD\"")]
    [InlineData("eval \"git push origin main\"")]
    [InlineData("echo hi && git push origin main")]
    [InlineData("sudo git push origin main")]
    [InlineData("git push origin tag v1 main")]
    public void Push_to_a_protected_branch_is_denied(string cmd) => Denied(cmd, Repo("r"));

    [Fact]
    public void Git_dir_selection_C_git_dir_env_and_worktree_are_followed()
    {
        var repo = Repo("main-repo");
        var elsewhere = Path.Combine(_tmp, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        Denied($"git -C {repo} push origin HEAD", elsewhere);
        Denied($"git -C {repo} push", elsewhere);
        Denied($"git --git-dir={repo}/.git push origin HEAD", elsewhere);
        Denied($"git --git-dir {repo}/.git push origin HEAD", elsewhere);
        Denied($"GIT_DIR={repo}/.git git push origin HEAD", elsewhere);
        Denied($"env GIT_DIR={repo}/.git git push", elsewhere);
        Denied($"export GIT_DIR={repo}/.git; git push", elsewhere);
        Denied($"cd {repo} && git push origin HEAD", elsewhere);
        Denied("git -C main-repo push origin HEAD", _tmp);

        // worktree: .git is a file pointing at <main>/.git/worktrees/wt, the config lives in the common dir
        var wtGit = Path.Combine(repo, ".git", "worktrees", "wt");
        Directory.CreateDirectory(wtGit);
        File.WriteAllText(Path.Combine(wtGit, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(wtGit, "commondir"), "../..\n");
        var wt = Path.Combine(_tmp, "wt");
        Directory.CreateDirectory(wt);
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {wtGit}\n");
        Denied("git push origin HEAD", wt);
        Denied("git push", wt);
        Denied("git -C wt push origin HEAD", _tmp);
    }

    [Fact]
    public void Worktree_on_a_feature_branch_may_push_it()
    {
        var repo = Repo("main-repo2");
        var wtGit = Path.Combine(repo, ".git", "worktrees", "wt2");
        Directory.CreateDirectory(wtGit);
        File.WriteAllText(Path.Combine(wtGit, "HEAD"), "ref: refs/heads/monarch/G-0101-x\n");
        File.WriteAllText(Path.Combine(wtGit, "commondir"), "../..\n");
        var wt = Path.Combine(_tmp, "wt2");
        Directory.CreateDirectory(wt);
        File.WriteAllText(Path.Combine(wt, ".git"), $"gitdir: {wtGit}\n");
        Allowed("git push origin HEAD", wt);
        Allowed("git push -u origin HEAD", wt);
        Allowed("git push", wt);
    }

    [Theory]
    [InlineData("git push origin monarch/G-0101-x")]
    [InlineData("git push -u origin feature")]
    [InlineData("git push origin fix-main-menu")]
    [InlineData("git push origin feature/main")]
    [InlineData("git push --delete origin monarch/G-0101-alt")]
    [InlineData("git push origin HEAD:feature-main")]
    [InlineData("git push origin 'refs/heads/feature/*:refs/heads/feature/*'")]
    [InlineData("git push origin tag main")]
    [InlineData("git push origin refs/tags/main")]
    [InlineData("git merge main")]
    [InlineData("git merge --no-ff origin/main")]
    [InlineData("git rebase main")]
    [InlineData("git fetch origin main")]
    [InlineData("git pull origin main")]
    [InlineData("git checkout main")]
    [InlineData("echo git push origin main")]
    [InlineData("gh pr view 5")]
    [InlineData("gh pr create --title 'merge it' --body x")]
    [InlineData("gh api repos/o/r/pulls/5")]
    [InlineData("gh api repos/o/r/git/ref/heads/main")]
    [InlineData("gh api -X GET repos/o/r/git/refs/heads/main")]
    [InlineData("gh api -X PATCH repos/o/r/git/refs/heads/feature -f sha=abc")]
    [InlineData("gh api graphql -f query='query { viewer { login } }'")]
    [InlineData("monarch gate show G-0101")]
    public void Ordinary_commands_stay_allowed(string cmd) => Allowed(cmd, Repo("r"));

    [Fact]
    public void Push_without_refspec_on_a_feature_branch_is_allowed_unless_config_says_otherwise()
    {
        var feature = Repo("f", "ref: refs/heads/feature");
        Allowed("git push", feature);
        Allowed("git push origin", feature);
        Allowed("git push origin HEAD", feature);
        Allowed("git push -u origin HEAD", feature);
        Allowed("git push origin @", feature);

        // simple (default): upstream with another name makes git refuse, the rule does not guess
        var tracksMain = Repo("tm", "ref: refs/heads/feature", "[branch \"feature\"]\n\tremote = origin\n\tmerge = refs/heads/main\n");
        Allowed("git push", tracksMain);

        var upstream = Repo("up", "ref: refs/heads/feature", "[push]\n\tdefault = upstream\n[branch \"feature\"]\n\tremote = origin\n\tmerge = refs/heads/main\n");
        Denied("git push", upstream);
        var tracking = Repo("tr", "ref: refs/heads/feature", "[push]\n\tdefault = tracking\n[branch \"feature\"]\n\tmerge = refs/heads/main\n");
        Denied("git push origin", tracking);

        var matching = Repo("ma", "ref: refs/heads/feature", "[push]\n\tdefault = matching\n");
        Denied("git push", matching);

        var remotePush = Repo("rp", "ref: refs/heads/feature", "[remote \"origin\"]\n\turl = x\n\tpush = refs/heads/feature:refs/heads/feature\n\tpush = refs/heads/feature:refs/heads/main\n");
        Denied("git push", remotePush);
        Denied("git push origin", remotePush);

        var harmless = Repo("rh", "ref: refs/heads/feature", "[remote \"origin\"]\n\turl = x\n\tpush = refs/heads/feature:refs/heads/feature\n");
        Allowed("git push", harmless);

        var mirror = Repo("mi", "ref: refs/heads/feature", "[remote \"origin\"]\n\turl = x\n\tmirror = true\n");
        Denied("git push", mirror);
        Denied("git push origin", mirror);
        Allowed("git push origin feature", mirror);
    }

    [Fact]
    public void Unreadable_head_or_config_asks_instead_of_allowing()
    {
        var detached = Repo("d", "0123456789abcdef0123456789abcdef01234567");
        Assert.Equal(PolicyDecisionKind.Ask, Run("git push", detached).Kind);
        Assert.Equal(PolicyDecisionKind.Ask, Run("git push origin HEAD", detached).Kind);
        Assert.Equal(PolicyRules.ProtectBranches, Run("git push", detached).Rule);

        var badConfig = Repo("bc", "ref: refs/heads/feature");
        File.Delete(Path.Combine(badConfig, ".git", "config"));
        Directory.CreateDirectory(Path.Combine(badConfig, ".git", "config"));
        Assert.Equal(PolicyDecisionKind.Ask, Run("git push", badConfig).Kind);

        // an explicit target needs neither HEAD nor config
        Allowed("git push origin feature", detached);
        Allowed("git push origin feature", badConfig);

        // no git directory at all: git push fails by itself
        var none = Path.Combine(_tmp, "none");
        Directory.CreateDirectory(none);
        Allowed("git push", none);
    }

    [Fact]
    public void Direct_push_to_the_integration_branch_asks()
    {
        var repo = Repo("r");
        foreach (var cmd in new[] { "git push origin develop", "git push origin HEAD:develop", "git push origin +develop", "git push origin :develop", "git push --delete origin develop" })
        {
            var d = Run(cmd, repo);
            Assert.Equal(PolicyDecisionKind.Ask, d.Kind);
            Assert.Equal(PolicyRules.ProtectBranches, d.Rule);
        }

        var onDevelop = Repo("od", "ref: refs/heads/develop");
        Assert.Equal(PolicyDecisionKind.Ask, Run("git push origin HEAD", onDevelop).Kind);

        // deny outranks ask
        Assert.Equal(PolicyDecisionKind.Deny, Run("git push origin develop main", repo).Kind);
    }

    [Fact]
    public void Protected_and_integration_branches_are_configurable()
    {
        var repo = Repo("r", "ref: refs/heads/feature");
        var o = new PolicyOptions { ProtectedBranches = ["main", "master", "release"], IntegrationBranch = "trunk" };
        var ctx = new PolicyContext(_tmp, null, false, o);
        PolicyDecision Eval(string c) => ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: c, Cwd: repo), ctx);
        Assert.Equal(PolicyDecisionKind.Deny, Eval("git push origin release").Kind);
        Assert.Equal(PolicyDecisionKind.Ask, Eval("git push origin trunk").Kind);
        Assert.Equal(PolicyDecisionKind.Allow, Eval("git push origin develop").Kind);
    }

    [Theory]
    [InlineData("gh pr merge 5")]
    [InlineData("gh pr merge")]
    [InlineData("gh pr merge --squash --auto 5")]
    [InlineData("gh pr merge 5 --admin --delete-branch")]
    [InlineData("gh -R o/r pr merge 5")]
    [InlineData("gh pr --repo o/r merge 5")]
    [InlineData("/opt/homebrew/bin/gh pr merge 5")]
    [InlineData("bash -c 'gh pr merge 5'")]
    [InlineData("echo ok; gh pr merge 5")]
    [InlineData("gh api repos/o/r/pulls/5/merge -X PUT")]
    [InlineData("gh api -X PUT repos/{owner}/{repo}/pulls/5/merge")]
    [InlineData("gh api -XPUT repos/o/r/pulls/5/merge")]
    [InlineData("gh api --method PUT /repos/o/r/pulls/5/merge")]
    [InlineData("gh api --method=PUT repos/o/r/pulls/{number}/merge")]
    [InlineData("gh api --method PUT https://api.github.com/repos/o/r/pulls/5/merge")]
    [InlineData("gh api repos/o/r/pulls/5/merge -f merge_method=squash")]
    [InlineData("gh api repos/o/r/merges -f base=main -f head=x")]
    [InlineData("gh api -X POST repos/{owner}/{repo}/merges")]
    [InlineData("gh api -X PATCH repos/o/r/git/refs/heads/main -f sha=abc")]
    [InlineData("gh api --method DELETE repos/o/r/git/refs/heads/main")]
    [InlineData("gh api -X POST repos/o/r/git/refs/heads/master")]
    [InlineData("gh api -X PATCH repos/o/r/git/refs/heads/MAIN -f sha=abc")]
    [InlineData("gh api -X PATCH https://api.github.com/repos/{owner}/{repo}/git/refs/heads/main -f sha=abc")]
    [InlineData("gh api repos/o/r/git/refs/heads/main -f sha=abc")]
    [InlineData("gh api graphql -f query='mutation { mergePullRequest(input:{pullRequestId:\"x\"}) { clientMutationId } }'")]
    [InlineData("gh api graphql -f query='mutation { enablePullRequestAutoMerge(input:{pullRequestId:\"x\"}) { clientMutationId } }'")]
    [InlineData("gh api graphql -f query='mutation { enqueuePullRequest(input:{pullRequestId:\"x\"}) { clientMutationId } }'")]
    [InlineData("gh api graphql -f query='mutation { mergeBranch(input:{repositoryId:\"x\"}) { clientMutationId } }'")]
    [InlineData("gh api graphql -f query='mutation { MergePullRequest(input:{pullRequestId:\"x\"}) { clientMutationId } }'")]
    [InlineData("gh api graphql -F query=@- <<'EOF'\nmutation { mergePullRequest(input:{pullRequestId:\"x\"}) { clientMutationId } }\nEOF")]
    [InlineData("gh api graphql -f query=\"$(cat <<'EOF'\nmutation { mergePullRequest(input:{pullRequestId:\"x\"}) { clientMutationId } }\nEOF\n)\"")]
    [InlineData("gh api graphql --input - <<EOF\n{\"query\":\"mutation { enablePullRequestAutoMerge(input:{}) { clientMutationId } }\"}\nEOF")]
    public void Merging_through_gh_is_denied(string cmd) => Denied(cmd, Repo("r", "ref: refs/heads/feature"));

    [Theory]
    [InlineData("monarch gate merge G-0101")]
    [InlineData("monarch gate check G-0101")]
    [InlineData("~/.monarch/bin/monarch gate merge G-0101")]
    [InlineData("monarch gate verdict G-0101 --verdict approve --source https://x")]
    [InlineData("bash -c 'monarch gate merge G-0101'")]
    public void Gate_merge_and_verdict_are_main_only(string cmd)
    {
        var repo = Repo("r", "ref: refs/heads/feature");
        Denied(cmd, repo, agent: "monarch:knight");
        Denied(cmd, repo, agent: "monarch:warden");
        Denied(cmd, repo, agent: "warden");
        Allowed(cmd, repo);
    }

    // Q-0129 (Design-Auflage zu ADR-0008): einen persistenten Weckjob legt nur der Main an oder entfernt ihn, nie ein Shadow.
    [Theory]
    [InlineData("monarch wake schedule --session 2f1c7a0e-8a4b-4c55-9d3e-1b2c3d4e5f60 --at 2026-10-28T19:33:00Z")]
    [InlineData("~/.monarch/bin/monarch wake schedule --session 2f1c7a0e-8a4b-4c55-9d3e-1b2c3d4e5f60 --at 2026-10-28T19:33:00Z")]
    [InlineData("monarch wake cancel 2f1c7a0e-8a4b-4c55-9d3e-1b2c3d4e5f60")]
    [InlineData("monarch wake fire 2f1c7a0e-8a4b-4c55-9d3e-1b2c3d4e5f60 --at 2026-10-28T19:33:00Z")]
    [InlineData("bash -c 'monarch wake schedule --session 2f1c7a0e-8a4b-4c55-9d3e-1b2c3d4e5f60 --at 2026-10-28T19:33:00Z'")]
    public void Wake_schedule_and_cancel_are_main_only(string cmd)
    {
        var repo = Repo("r", "ref: refs/heads/feature");
        Denied(cmd, repo, agent: "monarch:knight");
        Denied(cmd, repo, agent: "monarch:warden");
        Denied(cmd, repo, agent: "scout");
        Allowed(cmd, repo);
    }
}
