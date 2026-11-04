using System.Diagnostics;
using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>Q-0140 / ADR-0007 Nachtrag (#48): git.merge_method, default from the history of the integration branch. Real git repos, fake GitHub.</summary>
public class MergeMethodTests
{
    private const string Review = "https://example.test/review/1";

    private static void G(string cwd, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-c", "user.name=t", "-c", "user.email=t@example.test", "-c", "commit.gpgsign=false", "-c", "core.hooksPath=/dev/null" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var proc = Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        var err = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        Assert.True(proc.ExitCode == 0, $"git {string.Join(' ', args)} failed: {err}");
    }

    /// <summary>A repo with <paramref name="merges"/> --no-ff merges and then <paramref name="linear"/> plain commits on <paramref name="branch"/> (first-parent line: 1 + merges + linear commits).</summary>
    private static void History(string root, string branch, int merges, int linear)
    {
        G(root, "init", "-q", "-b", branch);
        G(root, "commit", "-q", "--allow-empty", "-m", "root");
        for (var i = 0; i < merges; i++)
        {
            G(root, "checkout", "-q", "-b", $"f{i}");
            G(root, "commit", "-q", "--allow-empty", "-m", $"feature {i}");
            G(root, "checkout", "-q", branch);
            G(root, "merge", "-q", "--no-ff", "-m", $"Merge f{i}", $"f{i}");
        }

        for (var i = 0; i < linear; i++)
        {
            G(root, "commit", "-q", "--allow-empty", "-m", $"squash {i}");
        }
    }

    private static (TempProject P, FakeForge F) Setup(string yaml)
    {
        var p = new TempProject(systemYaml: yaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, false);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        f.Checks[FakeForge.DevelopSha] = f.Checks[FakeForge.Sha];
        f.Checks[FakeForge.MergeSha] = f.Checks[FakeForge.Sha];
        p.Http = f;
        Seed.Gate(p, "G-0001", "[Q-0001]", "C");
        Seed.Quest(p, "Q-0001", "in_review", rank: "C", extra: "board: { provider: github, pr: 7 }\n");
        return (p, f);
    }

    private static void Tick(TempProject p) => p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);

    private static RunResult MergeApproved(TempProject p)
    {
        Tick(p);
        Assert.Equal(0, p.Hook("subagent-stop", """{"session_id":"sess1","hook_event_name":"SubagentStop","agent_id":"a1","agent_type":"monarch:warden"}""").Code);
        Tick(p);
        Assert.Equal(0, p.Run(["gate", "verdict", "G-0001", "--verdict", "approve", "--source", Review]).Code);
        Tick(p);
        return p.Run(["gate", "merge", "G-0001"], vars: new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = "0", ["MONARCH_MERGE_POLL_SECONDS"] = "0" });
    }

    private static string Method(FakeForge f) => JsonDocument.Parse(Assert.Single(f.MergeBodies)).RootElement.GetProperty("merge_method").GetString()!;

    private static string RecordedMethod(TempProject p) => Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("method").GetString()!;

    // ---- config
    [Fact]
    public void Config_rejects_any_other_merge_method_than_squash_or_merge()
    {
        Assert.Throws<FormatException>(() => SystemConfigReader.Parse("git:\n  merge_method: rebase\n"));
        Assert.Throws<FormatException>(() => SystemConfigReader.Parse("git:\n  merge_method: fast-forward\n"));
    }

    [Theory]
    [InlineData("squash")]
    [InlineData("merge")]
    public void Config_accepts_squash_and_merge(string value) => SystemConfigReader.Parse($"git:\n  merge_method: {value}\n");

    // ---- gate merge
    [Fact]
    public void Configured_merge_sends_merge_with_the_checked_head_and_records_it()
    {
        var (p, f) = Setup(Seed.ForgeYaml + "git:\n  merge_method: merge\n");
        using (p)
        {
            var r = MergeApproved(p);
            Assert.Equal(0, r.Code);
            var body = JsonDocument.Parse(Assert.Single(f.MergeBodies)).RootElement;
            Assert.Equal("merge", body.GetProperty("merge_method").GetString());
            Assert.Equal(FakeForge.Sha, body.GetProperty("sha").GetString());
            Assert.Equal("merge", RecordedMethod(p));
            Assert.Contains("(merge ", r.Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Configured_squash_wins_over_a_merge_history()
    {
        var (p, f) = Setup(Seed.ForgeYaml + "git:\n  merge_method: squash\n");
        using (p)
        {
            History(p.Root, "develop", 6, 0);
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
            Assert.Equal("squash", RecordedMethod(p));
        }
    }

    [Fact]
    public void Invalid_merge_method_refuses_without_a_put()
    {
        var (p, f) = Setup(Seed.ForgeYaml + "git:\n  merge_method: rebase\n");
        using (p)
        {
            var r = MergeApproved(p);
            Assert.Equal(1, r.Code);
            Assert.Contains("merge_method", r.Err, StringComparison.Ordinal);
            Assert.Empty(f.MergeBodies);
        }
    }

    [Fact]
    public void Without_key_a_no_ff_history_gives_merge_and_names_the_source()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 8, 2);
            var r = MergeApproved(p);
            Assert.Equal(0, r.Code);
            Assert.Equal("merge", Method(f));
            Assert.Equal("merge", RecordedMethod(p));
            Assert.Contains("Historie", r.Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Without_key_a_linear_history_gives_squash()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 2, 8);
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
            Assert.Equal("squash", RecordedMethod(p));
        }
    }

    [Fact]
    public void Exactly_half_merge_commits_is_not_a_majority()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            // first-parent line: 1 root + 4 merges + 5 plain = 10 commits, 4 of them merges
            History(p.Root, "develop", 4, 5);
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
        }
    }

    [Fact]
    public void Without_integration_branch_in_the_repo_it_is_squash()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "trunk", 6, 0);
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
        }
    }

    [Fact]
    public void Without_a_git_repo_it_is_squash()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
        }
    }

    [Fact]
    public void The_origin_branch_is_read_before_the_local_one()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            // local develop: merges only; origin/develop: the linear root
            History(p.Root, "develop", 6, 0);
            G(p.Root, "update-ref", "refs/remotes/origin/develop", "develop~6");
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("squash", Method(f));
        }
    }

    // ---- awaken
    [Fact]
    public void Awaken_writes_merge_for_a_no_ff_history_and_asks_the_owner_to_confirm()
    {
        using var p = new TempProject(awakened: false);
        History(p.Root, "develop", 6, 0);
        G(p.Root, "checkout", "-q", "-b", "work");
        var r = p.Run(["awaken"]);
        Assert.Equal(0, r.Code);
        var yaml = File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml"));
        Assert.Contains("merge_method: merge", yaml, StringComparison.Ordinal);
        Assert.Contains("bestätigen", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Awaken_writes_squash_without_an_integration_branch_and_the_reader_reads_the_section()
    {
        using var p = new TempProject(awakened: false);
        History(p.Root, "trunk", 6, 0);
        Assert.Equal(0, p.Run(["awaken"]).Code);
        var yaml = File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml"));
        Assert.Contains("merge_method: squash", yaml, StringComparison.Ordinal);
        Assert.Equal("develop", SystemConfigReader.Parse(yaml).IntegrationBranch);
    }

    [Fact]
    public void Awaken_outside_a_git_repo_writes_squash()
    {
        using var p = new TempProject(awakened: false);
        Assert.Equal(0, p.Run(["awaken"]).Code);
        Assert.Contains("merge_method: squash", File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml")), StringComparison.Ordinal);
    }
}
