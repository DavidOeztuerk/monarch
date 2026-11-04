using System.Diagnostics;
using System.Text.Json;
using Monarch.Infrastructure.Ledger;

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
            // first-parent line: 1 root + 5 merges + 4 plain = 10 commits, exactly half of them merges
            History(p.Root, "develop", 5, 4);
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

    // ---- warden round (Q-0140)
    private static RunResult MergeApprovedWith(TempProject p, Dictionary<string, string?> extra, Stopwatch? timer = null)
    {
        Tick(p);
        Assert.Equal(0, p.Hook("subagent-stop", """{"session_id":"sess1","hook_event_name":"SubagentStop","agent_id":"a1","agent_type":"monarch:warden"}""").Code);
        Tick(p);
        Assert.Equal(0, p.Run(["gate", "verdict", "G-0001", "--verdict", "approve", "--source", Review]).Code);
        Tick(p);
        var vars = new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = "0", ["MONARCH_MERGE_POLL_SECONDS"] = "0" };
        foreach (var (k, v) in extra) vars[k] = v;
        timer?.Start();
        var result = p.Run(["gate", "merge", "G-0001"], vars: vars);
        timer?.Stop();
        return result;
    }

    [Fact]
    public void A_relative_git_bin_is_ignored()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 6, 0);
            var r = MergeApprovedWith(p, new() { ["MONARCH_GIT_BIN"] = "no-such-dir/git" });
            Assert.Equal(0, r.Code);
            Assert.Equal("merge", Method(f));
            Assert.DoesNotContain("Warnung", r.Err, StringComparison.Ordinal);
        }
    }

    private static string HangingGit(string dir)
    {
        var path = Path.Combine(dir, "hanging-git.sh");
        File.WriteAllText(path, "#!/bin/sh\nsleep 30\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    [Fact]
    public void Only_the_newest_50_first_parent_commits_count()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            G(p.Root, "init", "-q", "-b", "develop");
            for (var i = 0; i < 60; i++) G(p.Root, "commit", "-q", "--allow-empty", "-m", $"old {i}");
            for (var i = 0; i < 30; i++)
            {
                G(p.Root, "checkout", "-q", "-b", $"f{i}");
                G(p.Root, "commit", "-q", "--allow-empty", "-m", $"feature {i}");
                G(p.Root, "checkout", "-q", "develop");
                G(p.Root, "merge", "-q", "--no-ff", "-m", $"Merge f{i}", $"f{i}");
            }

            // newest 50: 30 merges + 20 plain -> merge; over all 91 commits it would be squash
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Equal("merge", Method(f));
        }
    }

    [Theory]
    [InlineData("[rebase]")]
    [InlineData("{ a: b }")]
    public void Config_rejects_a_merge_method_that_is_not_a_scalar(string value) =>
        Assert.Throws<FormatException>(() => SystemConfigReader.Parse($"git:\n  merge_method: {value}\n"));

    [Fact]
    public void The_source_is_named_and_recorded_for_a_configured_method()
    {
        var (p, f) = Setup(Seed.ForgeYaml + "git:\n  merge_method: merge\n");
        using (p)
        {
            var r = MergeApproved(p);
            Assert.Contains("Konfiguration", r.Out, StringComparison.Ordinal);
            Assert.Contains("Konfiguration", Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("method_source").GetString(), StringComparison.Ordinal);
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    [Fact]
    public void A_derived_method_records_its_source_in_the_ledger()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 6, 0);
            Assert.Equal(0, MergeApproved(p).Code);
            Assert.Contains("Historie", Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("method_source").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_unreadable_history_warns_before_the_put_and_records_the_default()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            var r = MergeApproved(p);
            Assert.Equal(0, r.Code);
            Assert.Contains("Warnung", r.Err, StringComparison.Ordinal);
            Assert.Contains("squash", r.Err, StringComparison.Ordinal);
            Assert.Contains("Standard", Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("method_source").GetString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_proven_fix_pr_merges_with_the_merge_method_too()
    {
        var (p, f) = Setup(Seed.ForgeYaml + "git:\n  merge_method: merge\n");
        using (p)
        {
            f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":2,"check_runs":[{{FakeForge.Run(1, "build", "success")}},{{FakeForge.Run(2, "test", "failure")}}]}""";
            f.Compare[$"{FakeForge.DevelopSha}...{FakeForge.Sha}"] = "ahead";
            f.Parents[FakeForge.MergeSha] = FakeForge.DevelopSha;
            var r = MergeApproved(p);
            Assert.Equal(0, r.Code);
            Assert.Equal("merge", Method(f));
            var merged = Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload;
            Assert.Equal("merge", merged.GetProperty("method").GetString());
            Assert.Equal(FakeForge.DevelopSha, merged.GetProperty("integration_head").GetString());
        }
    }

    [Fact]
    public void A_hanging_git_ends_at_its_own_deadline_with_squash_and_a_warning()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 6, 0);
            var sw = new Stopwatch();
            var r = MergeApprovedWith(p, new() { ["MONARCH_GIT_BIN"] = HangingGit(p.Home), ["MONARCH_MERGE_GIT_SECONDS"] = "1" }, sw);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(25), sw.Elapsed.ToString());
            Assert.Equal(0, r.Code);
            Assert.Equal("squash", Method(f));
            Assert.Contains("Warnung", r.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_git_read_runs_under_the_deadline_of_phase_A()
    {
        var (p, f) = Setup(Seed.ForgeYaml);
        using (p)
        {
            History(p.Root, "develop", 6, 0);
            var sw = new Stopwatch();
            var r = MergeApprovedWith(p, new() { ["MONARCH_GIT_BIN"] = HangingGit(p.Home), ["MONARCH_MERGE_GIT_SECONDS"] = "25", ["MONARCH_MERGE_FORGE_SECONDS"] = "2" }, sw);
            // A single hanging ref alone takes 25 s at the git deadline, above the 15 s bound, so only the deadline of phase A (2 s) can end it in time. Only the merge call is timed, not the approval steps.
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), sw.Elapsed.ToString());
            Assert.Equal(1, r.Code);
            Assert.Contains("Forge-Frist", r.Err, StringComparison.Ordinal);
            Assert.Empty(f.MergeBodies);
        }
    }

    [Fact]
    public void The_awaken_comment_names_the_source()
    {
        using var derived = new TempProject(awakened: false);
        History(derived.Root, "develop", 6, 0);
        derived.Run(["awaken"]);
        Assert.Contains("aus der Historie abgeleitet", File.ReadAllText(Path.Combine(derived.Root, ".monarch", "system.yaml")), StringComparison.Ordinal);

        using var fallback = new TempProject(awakened: false);
        fallback.Run(["awaken"]);
        var yaml = File.ReadAllText(Path.Combine(fallback.Root, ".monarch", "system.yaml"));
        Assert.DoesNotContain("aus der Historie abgeleitet", yaml, StringComparison.Ordinal);
        Assert.Contains("Standard", yaml, StringComparison.Ordinal);
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
