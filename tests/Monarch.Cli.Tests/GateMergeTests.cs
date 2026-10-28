using System.Text.Json;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

/// <summary>Q-0113 / ADR-0007: <c>gate verdict</c>, <c>gate merge</c>, the hook rule and the config keys. Fake GitHub, no network.</summary>
public class GateMergeTests
{
    private const string Review = "https://example.test/review/1";

    private static (TempProject P, FakeForge F) Setup(string gateRank = "C", string questRank = "C", string? yaml = null, bool link = true)
    {
        var p = new TempProject(systemYaml: yaml ?? Seed.ForgeYaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, false);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        p.Http = f;
        Seed.Gate(p, "G-0001", "[Q-0001]", gateRank);
        Seed.Quest(p, "Q-0001", "in_review", rank: questRank, extra: link ? "board: { provider: github, pr: 7 }\n" : "");
        return (p, f);
    }

    private static void Tick(TempProject p) => p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);

    private static void Warden(TempProject p, string agentType = "monarch:warden")
    {
        Tick(p);
        var r = p.Hook("subagent-stop", $$"""{"session_id":"sess1","hook_event_name":"SubagentStop","agent_id":"a1","agent_type":"{{agentType}}"}""");
        Assert.Equal(0, r.Code);
    }

    private static RunResult Verdict(TempProject p, string verdict = "approve", string source = Review)
    {
        Tick(p);
        return p.Run(["gate", "verdict", "G-0001", "--verdict", verdict, "--source", source]);
    }

    private static RunResult Merge(TempProject p) { Tick(p); return p.Run(["gate", "merge", "G-0001"]); }

    private static void Approved(TempProject p)
    {
        Warden(p);
        Assert.Equal(0, Verdict(p).Code);
    }

    private static void Refused(TempProject p, FakeForge f, RunResult r, string reason)
    {
        Assert.Equal(1, r.Code);
        Assert.StartsWith("refused:", r.Err, StringComparison.Ordinal);
        Assert.Contains(reason, r.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(f.MergeBodies);
        Assert.DoesNotContain(p.Events(), e => e.Type == "GateMerged");
    }

    // ---- success path
    [Theory]
    [InlineData("E", "E", "monarch:warden")]
    [InlineData("C", "C", "warden")]
    [InlineData("B", "B", "monarch:warden")]
    [InlineData("A", "A", "monarch:warden")]
    public void Merge_squashes_into_the_integration_branch_with_the_checked_head(string gateRank, string questRank, string agent)
    {
        var (p, f) = Setup(gateRank, questRank);
        using (p)
        {
            Warden(p, agent);
            var v = Verdict(p);
            Assert.Equal(0, v.Code);
            var recorded = Assert.Single(p.Events(), e => e.Type == "WardenVerdictRecorded").Payload;
            Assert.Equal("approve", recorded.GetProperty("verdict").GetString());
            Assert.Equal(FakeForge.Sha, recorded.GetProperty("sha").GetString());
            Assert.Equal(Review, recorded.GetProperty("source").GetString());
            Assert.Equal("G-0001", recorded.GetProperty("gate").GetString());

            var m = Merge(p);
            Assert.Equal(0, m.Code);
            Assert.Contains("develop", m.Out, StringComparison.Ordinal);
            var body = JsonDocument.Parse(Assert.Single(f.MergeBodies)).RootElement;
            Assert.Equal("squash", body.GetProperty("merge_method").GetString());
            Assert.Equal(FakeForge.Sha, body.GetProperty("sha").GetString());
            Assert.Contains("PUT /repos/o/r/pulls/7/merge", f.Paths);
            var merged = Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload;
            Assert.Equal(FakeForge.MergeSha, merged.GetProperty("merge_commit_sha").GetString());
            Assert.Equal("develop", merged.GetProperty("base").GetString());
            Assert.Equal(7, merged.GetProperty("pr").GetInt32());
            Assert.Equal(FakeForge.Sha, merged.GetProperty("sha").GetString());
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    // ---- refusals, one per condition
    [Fact]
    public void Refuses_when_the_ledger_has_findings()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            p.Hook("stop", """{"session_id":"sess1"}""");
            File.WriteAllText(Path.Combine(p.Root, ".monarch", "local", "ledger-heads.json"), "garbage");
            p.Hook("stop", """{"session_id":"sess1"}""");
            Assert.False(LedgerVerifier.Verify(p.Root).Ok);
            Refused(p, f, Merge(p), "ledger");
        }
    }

    [Fact]
    public void Refuses_without_a_linked_pull_request()
    {
        var (p, f) = Setup(link: false);
        using (p)
        {
            Refused(p, f, Merge(p), "no pull request");
            Assert.Equal(1, Verdict(p).Code);
        }
    }

    [Fact]
    public void Refuses_when_the_gate_points_at_two_different_pull_requests()
    {
        var (p, f) = Setup();
        using (p)
        {
            Seed.Gate(p, "G-0001", "[Q-0001, Q-0002]");
            Seed.Quest(p, "Q-0002", "in_review", extra: "board: { provider: github, pr: 8 }\n");
            f.Pulls[8] = (FakeForge.Sha, false);
            Refused(p, f, Merge(p), "more than one");
        }
    }

    [Fact]
    public void Two_quests_on_the_same_pull_request_are_one_pull_request()
    {
        var (p, f) = Setup();
        using (p)
        {
            Seed.Gate(p, "G-0001", "[Q-0001, Q-0002]");
            Seed.Quest(p, "Q-0002", "in_review", extra: "board: { provider: github, pr: 7 }\n");
            Approved(p);
            Assert.Equal(0, Merge(p).Code);
            Assert.Single(f.MergeBodies);
        }
    }

    [Theory]
    [InlineData("main")]
    [InlineData("master")]
    [InlineData("release")]
    public void Refuses_when_the_base_is_not_the_integration_branch(string baseBranch)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Bases[7] = baseBranch;
            Approved(p);
            Refused(p, f, Merge(p), "integration branch");
        }
    }

    [Fact]
    public void Integration_branch_comes_from_system_yaml()
    {
        var (p, f) = Setup(yaml: Seed.ForgeYaml + "git:\n  integration_branch: trunk\n");
        using (p)
        {
            Approved(p);
            Refused(p, f, Merge(p), "trunk");
            f.Bases[7] = "trunk";
            Assert.Equal(0, Merge(p).Code);
        }
    }

    [Theory]
    [InlineData("S", "C")]
    [InlineData("C", "S")]
    public void Refuses_rank_S_whether_gate_or_quest_has_it(string gateRank, string questRank)
    {
        var (p, f) = Setup(gateRank, questRank);
        using (p)
        {
            Approved(p);
            Refused(p, f, Merge(p), "rank S");
        }
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("neutral")]
    [InlineData("skipped")]
    [InlineData("cancelled")]
    public void Refuses_when_a_check_is_not_success(string conclusion)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", conclusion));
            Approved(p);
            Refused(p, f, Merge(p), "not green");
        }
    }

    [Fact]
    public void Refuses_while_checks_run_or_are_missing()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "", "in_progress"));
            Refused(p, f, Merge(p), "still running");
            f.SetChecks();
            Refused(p, f, Merge(p), "no check runs");
        }

        var (q, g) = Setup(yaml: Seed.ForgeYaml + "evidence:\n  required: [build, lint]\n");
        using (q)
        {
            Approved(q);
            Refused(q, g, Merge(q), "lint");
        }
    }

    [Fact]
    public void Refuses_without_a_verdict()
    {
        var (p, f) = Setup();
        using (p)
        {
            Warden(p);
            Refused(p, f, Merge(p), "verdict");
        }
    }

    [Fact]
    public void Refuses_a_changes_verdict()
    {
        var (p, f) = Setup();
        using (p)
        {
            Warden(p);
            Assert.Equal(0, Verdict(p, "changes").Code);
            Refused(p, f, Merge(p), "changes");
        }
    }

    [Fact]
    public void Refuses_a_verdict_for_an_older_head()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            const string newHead = "def456abc123def456abc123def456abc123def4";
            f.Pulls[7] = (newHead, false);
            f.Checks[newHead] = f.Checks[FakeForge.Sha];
            Refused(p, f, Merge(p), "head");
        }
    }

    [Fact]
    public void Refuses_without_a_warden_run()
    {
        var (p, f) = Setup();
        using (p)
        {
            Assert.Equal(0, Verdict(p).Code);
            Refused(p, f, Merge(p), "warden");
        }
    }

    [Theory]
    [InlineData("monarch:knight")]
    [InlineData("monarch:warden-extra")]
    [InlineData("Warden2")]
    public void Only_a_warden_run_counts(string agentType)
    {
        var (p, f) = Setup();
        using (p)
        {
            Warden(p, agentType);
            Assert.Equal(0, Verdict(p).Code);
            Refused(p, f, Merge(p), "warden");
        }
    }

    [Fact]
    public void The_warden_run_must_come_after_the_commit_and_before_the_verdict()
    {
        var (p, f) = Setup();
        using (p)
        {
            f.CommitDate = "2026-10-27T13:00:00Z"; // pushed after the review
            Approved(p);
            Refused(p, f, Merge(p), "after the commit");
        }

        var (q, g) = Setup();
        using (q)
        {
            Assert.Equal(0, Verdict(q).Code);
            Warden(q); // the run came after the verdict it should cover
            Refused(q, g, Merge(q), "warden");
        }
    }

    [Fact]
    public void One_warden_run_covers_exactly_one_verdict()
    {
        var (p, f) = Setup();
        using (p)
        {
            Warden(p);
            Assert.Equal(0, Verdict(p, "changes").Code);
            Assert.Equal(0, Verdict(p, "approve").Code); // no new run in between
            Refused(p, f, Merge(p), "warden");
            Warden(p);
            Assert.Equal(0, Verdict(p, "approve").Code);
            Assert.Equal(0, Merge(p).Code);
        }
    }

    [Fact]
    public void A_failing_merge_call_records_nothing()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.MergeFailsWith = 409;
            var r = Merge(p);
            Assert.Equal(1, r.Code);
            Assert.Contains("409", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(p.Events(), e => e.Type == "GateMerged");
        }
    }

    [Fact]
    public void A_base_that_moved_during_the_merge_is_reported_and_recorded()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.BaseAfterMerge = "main";
            var r = Merge(p);
            Assert.Equal(1, r.Code);
            Assert.Contains("main", r.Err, StringComparison.Ordinal);
            var merged = Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload;
            Assert.Equal("main", merged.GetProperty("base").GetString());
        }
    }

    [Fact]
    public void Unknown_gate_and_bad_verdict_arguments_are_refused()
    {
        var (p, f) = Setup();
        using (p)
        {
            Assert.Equal(1, p.Run(["gate", "merge", "G-9999"]).Code);
            Assert.Equal(1, p.Run(["gate", "verdict", "G-9999", "--verdict", "approve", "--source", Review]).Code);
            Assert.Equal(1, p.Run(["gate", "verdict", "G-0001", "--verdict", "maybe", "--source", Review]).Code);
            Assert.Equal(1, p.Run(["gate", "verdict", "G-0001", "--verdict", "approve", "--source", "not a url"]).Code);
            Assert.DoesNotContain(p.Events(), e => e.Type == "WardenVerdictRecorded");
            Assert.Empty(f.MergeBodies);
        }
    }

    // ---- hook rule and config
    [Theory]
    [InlineData("git push origin HEAD:main")]
    [InlineData("git push origin main")]
    [InlineData("git push --delete origin main")]
    [InlineData("gh pr merge 5 --squash")]
    [InlineData("gh api -X PUT repos/o/r/pulls/5/merge")]
    public void Hook_denies_pushes_and_merges_and_logs_the_rule(string command)
    {
        using var p = new TempProject();
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: command));
        var o = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("deny", o.GetProperty("permissionDecision").GetString());
        var ev = Assert.Single(p.Events(), e => e.Type == "PolicyDecided");
        Assert.Equal("protect_branches", ev.Payload.GetProperty("rule").GetString());
    }

    [Fact]
    public void Hook_allows_feature_branch_pushes_and_asks_for_the_integration_branch()
    {
        using var p = new TempProject();
        var ok = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "git push -u origin monarch/G-0101-x"));
        Assert.DoesNotContain("deny", ok.Out, StringComparison.Ordinal);
        var ask = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "git push origin develop"));
        Assert.Equal("ask", JsonDocument.Parse(ask.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
    }

    [Fact]
    public void Hook_denies_gate_merge_for_shadows_only()
    {
        using var p = new TempProject();
        var shadow = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "monarch gate merge G-0101", agentType: "monarch:knight"));
        Assert.Equal("deny", JsonDocument.Parse(shadow.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
        var main = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "monarch gate merge G-0101"));
        Assert.DoesNotContain("deny", main.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_git_section_only_adds_protected_branches()
    {
        Assert.Equal(["main", "master"], SystemConfigReader.Parse("theme: monarch\n").ToPolicyOptions().ProtectedBranches);
        Assert.Equal("develop", SystemConfigReader.Parse("theme: monarch\n").ToPolicyOptions().IntegrationBranch);
        var o = SystemConfigReader.Parse("git:\n  protected_branches: [release, MAIN]\n  integration_branch: trunk\n").ToPolicyOptions();
        Assert.Equal(["main", "master", "release", "MAIN"], o.ProtectedBranches);
        Assert.Equal("trunk", o.IntegrationBranch);
        var emptied = SystemConfigReader.Parse("git:\n  protected_branches: []\n").ToPolicyOptions();
        Assert.Equal(["main", "master"], emptied.ProtectedBranches);
    }

    [Fact]
    public void Hook_uses_the_configured_protected_branches()
    {
        using var p = new TempProject(systemYaml: "git:\n  protected_branches: [release]\n");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "git push origin release"));
        Assert.Equal("deny", JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
    }

    // ---- T3: clear evidence stays the forge merge, a merge without GateMerged is reported
    [Fact]
    public void Clear_reports_a_merge_that_did_not_go_through_gate_merge_and_stays_quiet_otherwise()
    {
        var (p, f) = Setup();
        using (p)
        {
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            f.Pulls[7] = (FakeForge.Sha, true); // merged by someone else
            var clear = p.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(0, clear.Code);
            Assert.Contains("GateMerged", clear.Out, StringComparison.Ordinal);
            Assert.Contains("GateMerged", p.Run(["ledger", "verify"]).Out, StringComparison.Ordinal);
        }

        var (q, g) = Setup();
        using (q)
        {
            Approved(q);
            Assert.Equal(0, Merge(q).Code);
            Assert.Equal(0, q.Run(["quest", "verify", "Q-0001"]).Code);
            var clear = q.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(0, clear.Code);
            Assert.DoesNotContain("GateMerged", clear.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("GateMerged", q.Run(["ledger", "verify"]).Out, StringComparison.Ordinal);
            Assert.Single(g.MergeBodies);
        }
    }
}
