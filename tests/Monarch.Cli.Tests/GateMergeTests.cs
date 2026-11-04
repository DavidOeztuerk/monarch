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
        f.Checks[FakeForge.DevelopSha] = f.Checks[FakeForge.Sha];
        f.Checks[FakeForge.MergeSha] = f.Checks[FakeForge.Sha];
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

    // Q-0132: no waiting in tests; the post-merge check polls once (timeout 0) unless a test says otherwise.
    private static RunResult Merge(TempProject p, string wait = "0") { Tick(p); return p.Run(["gate", "merge", "G-0001"], vars: new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = wait, ["MONARCH_MERGE_POLL_SECONDS"] = "0" }); }

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

    // Q-0130 (Blocker Code-Phase Warden G-0101): ein integration_branch aus protected_branches darf gate merge nie nach main führen.
    [Theory]
    [InlineData("main", "main")]
    [InlineData("Main", "main")]
    [InlineData("master", "master")]
    public void Refuses_when_the_integration_branch_is_configured_as_a_protected_branch(string configured, string baseBranch)
    {
        var (p, f) = Setup(yaml: Seed.ForgeYaml + $"git:\n  integration_branch: {configured}\n");
        using (p)
        {
            f.Bases[7] = baseBranch;
            Approved(p);
            Refused(p, f, Merge(p), "protected");
        }
    }

    [Fact]
    public void Refuses_a_protected_base_added_in_system_yaml_even_if_it_is_the_integration_branch()
    {
        var (p, f) = Setup(yaml: Seed.ForgeYaml + "git:\n  protected_branches: [release]\n  integration_branch: release\n");
        using (p)
        {
            f.Bases[7] = "release";
            Approved(p);
            Refused(p, f, Merge(p), "protected");
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

    // ---- Q-0132: develop green before and after the merge
    [Theory]
    [InlineData("failure", "not green")]
    [InlineData("cancelled", "not green")]
    [InlineData("timed_out", "not green")]
    public void Refuses_when_the_integration_branch_is_not_green_before_the_merge(string conclusion, string reason)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":2,"check_runs":[{{FakeForge.Run(1, "build", "success")}},{{FakeForge.Run(2, "test", conclusion)}}]}""";
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "develop");
            Assert.Contains(reason, r.Err, StringComparison.Ordinal);
            Assert.Contains("GET /repos/o/r/git/ref/heads/develop", f.Paths);
            Assert.DoesNotContain(f.Paths, x => x.StartsWith("PUT", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Refuses_when_the_integration_branch_is_still_running_or_has_no_run()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(3, "build", "", "in_progress")}}]}""";
            Refused(p, f, Merge(p), "develop läuft noch");
            f.Checks.Remove(FakeForge.DevelopSha);
            Refused(p, f, Merge(p), "develop läuft noch");
            Assert.DoesNotContain(f.Paths, x => x.StartsWith("PUT", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_green_integration_branch_afterwards_records_IntegrationRunChecked_green()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            var r = Merge(p);
            Assert.Equal(0, r.Code);
            var e = Assert.Single(p.Events(), x => x.Type == "IntegrationRunChecked").Payload;
            Assert.Equal("green", e.GetProperty("result").GetString());
            Assert.Equal(FakeForge.MergeSha, e.GetProperty("sha").GetString());
            Assert.Equal("G-0001", e.GetProperty("gate").GetString());
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    [Theory]
    [InlineData("failure")]
    public void A_red_integration_branch_afterwards_is_loud_and_keeps_the_merge(string conclusion)
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", conclusion)}}]}""";
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("develop ist ROT nach dem Merge – zuerst klären", r.Err, StringComparison.Ordinal);
            Assert.Equal("red", Assert.Single(p.Events(), x => x.Type == "IntegrationRunChecked").Payload.GetProperty("result").GetString());
            Assert.Single(p.Events(), x => x.Type == "GateMerged");
            Assert.Single(f.MergeBodies);
        }
    }

    [Fact]
    public void A_timeout_afterwards_is_pending_with_a_hint_and_a_nonzero_exit()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", "", "in_progress")}}]}""";
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains($"GitHub-Checks des Merge-Commits {FakeForge.MergeSha}", r.Err, StringComparison.Ordinal);
            Assert.Contains("noch nicht abgeschlossen", r.Err, StringComparison.Ordinal);
            Assert.Equal("pending", Assert.Single(p.Events(), x => x.Type == "IntegrationRunChecked").Payload.GetProperty("result").GetString());
            Assert.Single(p.Events(), x => x.Type == "GateMerged");
        }
    }

    // ================= Q-0137 (ADR-0007 Nachtrag): belegter Fix-PR, abgebrochen ist unbelegt, Laufzeit, gate check =================
    private static string RealUrl(long run) => $"https://github.com/o/r/actions/runs/{run}/job/9{run}";

    private static void RedDevelop(FakeForge f, string conclusion = "failure", string? url = null) =>
        f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":2,"check_runs":[{{FakeForge.Run(1, "build", "success")}},{{FakeForge.Run(2, "test", conclusion, "completed", url)}}]}""";

    private static void ProvenFix(FakeForge f)
    {
        f.Compare[$"{FakeForge.DevelopSha}...{FakeForge.Sha}"] = "ahead";
        f.Parents[FakeForge.MergeSha] = FakeForge.DevelopSha;
    }

    private static RunResult MergeWith(TempProject p, Dictionary<string, string?> extra)
    {
        Tick(p);
        var vars = new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = "0", ["MONARCH_MERGE_POLL_SECONDS"] = "0" };
        foreach (var (k, v) in extra) vars[k] = v;
        return p.Run(["gate", "merge", "G-0001"], vars: vars);
    }

    private static RunResult Check(TempProject p, Dictionary<string, string?>? extra = null)
    {
        Tick(p);
        var vars = new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = "0", ["MONARCH_MERGE_POLL_SECONDS"] = "0" };
        foreach (var (k, v) in extra ?? []) vars[k] = v;
        return p.Run(["gate", "check", "G-0001"], vars: vars);
    }

    private static string[] Results(TempProject p) => [.. p.Events().Where(e => e.Type == "IntegrationRunChecked").Select(e => e.Payload.GetProperty("result").GetString()!)];

    private static string[] Names(JsonElement array) => [.. array.EnumerateArray().Select(x => x.GetString()!)];

    [Fact]
    public void A_proven_fix_pr_merges_into_a_red_develop_and_records_what_it_overrode()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            Approved(p);
            var r = Merge(p);
            Assert.Equal(0, r.Code);
            var body = JsonDocument.Parse(Assert.Single(f.MergeBodies)).RootElement;
            Assert.Equal(FakeForge.Sha, body.GetProperty("sha").GetString());
            var merged = Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload;
            Assert.Equal(["test"], Names(merged.GetProperty("overridden")));
            Assert.Equal(FakeForge.DevelopSha, merged.GetProperty("integration_head").GetString());
            Assert.Contains("test", r.Out, StringComparison.Ordinal);
            Assert.Contains(FakeForge.DevelopSha[..8], r.Out, StringComparison.Ordinal);
            Assert.Equal(["green"], Results(p));
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    [Fact]
    public void A_proven_fix_pr_also_merges_with_required_checks_and_a_comma_in_the_name()
    {
        var (p, f) = Setup(yaml: Seed.ForgeYaml + "evidence:\n  required: [build, test]\n");
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            Approved(p);
            Assert.Equal(0, Merge(p).Code);
            Assert.Single(f.MergeBodies);
            Assert.Equal(["test"], Names(Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("overridden")));
        }

        var (q, g) = Setup(yaml: Seed.ForgeYaml + "evidence:\n  required: [\"test (a, b)\"]\n");
        using (q)
        {
            g.SetChecks(FakeForge.Run(2, "test (a, b)", "success"));
            g.Checks[FakeForge.MergeSha] = g.Checks[FakeForge.Sha];
            g.Checks[FakeForge.DevelopSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(2, "test (a, b)", "failure")}}]}""";
            ProvenFix(g);
            Approved(q);
            Assert.Equal(0, Merge(q).Code);
            Assert.Equal(["test (a, b)"], Names(Assert.Single(q.Events(), e => e.Type == "GateMerged").Payload.GetProperty("overridden")));
        }
    }

    [Theory]
    [InlineData("diverged")]
    [InlineData("behind")]
    [InlineData("identical")]
    public void A_red_develop_without_the_ancestor_proof_refuses_and_names_it(string status)
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.Compare[$"{FakeForge.DevelopSha}...{FakeForge.Sha}"] = status;
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "not green");
            Assert.Contains("develop", r.Err, StringComparison.Ordinal);
            Assert.Contains(status, r.Err, StringComparison.Ordinal);
            Assert.Contains($"GET /repos/o/r/compare/{FakeForge.DevelopSha}...{FakeForge.Sha}", f.Paths);
        }
    }

    [Fact]
    public void A_red_develop_with_a_failing_compare_refuses_and_says_so()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            f.CompareFailsWith = 500;
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "not green");
            Assert.Contains("compare", r.Err, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void A_red_develop_whose_failing_check_is_missing_at_the_pr_head_refuses_and_names_the_check()
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":2,"check_runs":[{{FakeForge.Run(1, "build", "success")}},{{FakeForge.Run(3, "push-only", "failure")}}]}""";
            ProvenFix(f);
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "not green");
            Assert.Contains("push-only", r.Err, StringComparison.Ordinal);
            Assert.Contains("fehlt", r.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_fix_pr_is_refused_when_develop_moves_before_the_merge()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.DevelopReads.Enqueue(FakeForge.DevelopSha);
            f.DevelopReads.Enqueue("0ther000000000000000000000000000000000000");
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "develop");
            Assert.Contains("moved", r.Err, StringComparison.OrdinalIgnoreCase);
        }
    }

    // regression guards, green before Q-0137 as well
    [Fact]
    public void Guard_a_fix_pr_with_a_red_check_at_its_own_head_still_ends_in_step_5()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "failure"));
            Approved(p);
            Refused(p, f, Merge(p), "CI is not green");
        }
    }

    [Fact]
    public void Guard_a_fix_pr_without_a_verdict_or_with_rank_S_is_still_refused()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            Refused(p, f, Merge(p), "refused");
        }

        var (q, g) = Setup(gateRank: "S");
        using (q)
        {
            RedDevelop(g);
            ProvenFix(g);
            Approved(q);
            Refused(q, g, Merge(q), "rank S");
        }
    }

    [Fact]
    public void A_proven_fix_pr_without_a_verdict_is_refused_for_the_verdict()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            Refused(p, f, Merge(p), "no warden verdict");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cancelled_run_at_the_develop_head_stops_with_run_id_and_rerun_hint(bool mixedWithRed)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Checks[FakeForge.DevelopSha] = $$"""{"total_count":3,"check_runs":[{{FakeForge.Run(1, "build", mixedWithRed ? "failure" : "success")}},{{FakeForge.Run(2, "test", "cancelled", "completed", RealUrl(40721744761))}},{{FakeForge.Run(3, "lint", "success")}}]}""";
            ProvenFix(f); // even full proof must not help: without a result nobody knows what the PR fixes
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "not green");
            Assert.Contains("develop", r.Err, StringComparison.Ordinal);
            Assert.Contains("gh run rerun 40721744761", r.Err, StringComparison.Ordinal);
            Assert.Contains("abgebrochen", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(f.Paths, x => x.StartsWith("PUT", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_cancelled_run_without_a_run_id_in_its_url_names_check_and_url_and_no_rerun_command()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f, "cancelled", "https://ci.example/other/77");
            Approved(p);
            var r = Merge(p);
            Refused(p, f, r, "not green");
            Assert.Contains("test", r.Err, StringComparison.Ordinal);
            Assert.Contains("https://ci.example/other/77", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("gh run rerun", r.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_cancelled_run_afterwards_is_not_red_and_names_run_and_rerun()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", "cancelled", "completed", RealUrl(555000111))}}]}""";
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("abgebrochen", r.Err, StringComparison.Ordinal);
            Assert.Contains("555000111", r.Err, StringComparison.Ordinal);
            Assert.Contains("gh run rerun 555000111", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain("ROT", r.Err, StringComparison.Ordinal);
            Assert.Equal(["cancelled"], Results(p));
            Assert.Single(p.Events(), x => x.Type == "GateMerged");
            Assert.Single(f.MergeBodies);
        }
    }

    [Fact]
    public void A_mixed_red_and_cancelled_run_afterwards_stays_red()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":2,"check_runs":[{{FakeForge.Run(4, "test", "cancelled", "completed", RealUrl(5))}},{{FakeForge.Run(5, "build", "failure")}}]}""";
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("ROT", r.Err, StringComparison.Ordinal);
            Assert.Equal(["red"], Results(p));
        }
    }

    [Fact]
    public void A_fix_pr_whose_merge_commit_has_another_first_parent_warns_exits_nonzero_and_keeps_the_merge()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.Parents[FakeForge.MergeSha] = "0ther000000000000000000000000000000000000";
            Approved(p);
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("0ther000", r.Err, StringComparison.Ordinal);
            Assert.Single(p.Events(), e => e.Type == "GateMerged");
            Assert.Single(f.MergeBodies);
            var run = Assert.Single(p.Events(), e => e.Type == "IntegrationRunChecked").Payload;
            Assert.Equal("mismatch", run.GetProperty("parent_check").GetString());
            Assert.Equal("0ther000000000000000000000000000000000000", run.GetProperty("first_parent").GetString());
        }
    }

    [Fact]
    public void A_fix_pr_whose_first_parent_cannot_be_read_warns_exits_nonzero_and_records_it()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.Parents.Remove(FakeForge.MergeSha); // the commit reads without parents
            Approved(p);
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("Elternteil", r.Err, StringComparison.Ordinal);
            Assert.Single(p.Events(), e => e.Type == "GateMerged");
            Assert.Equal("unreadable", Assert.Single(p.Events(), e => e.Type == "IntegrationRunChecked").Payload.GetProperty("parent_check").GetString());
        }
    }

    [Fact]
    public void Default_wait_is_60_seconds_and_pending_names_the_recheck_command()
    {
        Assert.Equal(60, GateMergeCommands.DefaultWaitSeconds);
        Assert.Equal(30, GateMergeCommands.DefaultPollSeconds);
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", "", "in_progress")}}]}""";
            var r = Merge(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("monarch gate check G-0001", r.Err, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_post_merge_run_is_polled_at_least_once_even_with_a_zero_wait()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            var r = MergeWith(p, new() { ["MONARCH_MERGE_FORGE_SECONDS"] = "1" });
            Assert.Equal(0, r.Code);
            Assert.Equal(["green"], Results(p));
            Assert.Contains(f.Paths, x => x.EndsWith($"/commits/{FakeForge.MergeSha}/check-runs", StringComparison.Ordinal));
        }
    }

    private static Dictionary<string, string?> Forge1 => new() { ["MONARCH_MERGE_FORGE_SECONDS"] = "1" };

    [Fact]
    public void A_hanging_forge_call_before_the_put_refuses_without_a_put()
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Intercept = async (req, ct) =>
            {
                if (req.RequestUri!.AbsolutePath.Contains("/git/ref/heads/", StringComparison.Ordinal))
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }

                return null;
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Refused(p, f, r, "frist");
        }
    }

    [Fact]
    public void A_hanging_put_whose_merge_landed_writes_GateMerged_and_goes_on_to_the_run()
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", "", "in_progress")}}]}""";
            f.Intercept = async (req, ct) =>
            {
                if (req.Method == HttpMethod.Put)
                {
                    f.Pulls[7] = (FakeForge.Sha, true);
                    await Task.Delay(Timeout.Infinite, ct);
                }

                return null;
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Equal(FakeForge.MergeSha, Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("merge_commit_sha").GetString());
            Assert.Equal(["pending"], Results(p));
        }
    }

    [Fact]
    public void A_hanging_put_whose_merge_did_not_land_is_status_unknown_not_a_plain_refusal()
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Intercept = async (req, ct) =>
            {
                if (req.Method == HttpMethod.Put)
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }

                return null;
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("Status unbekannt", r.Err, StringComparison.Ordinal);
            Assert.Contains(f.Paths, x => x.StartsWith("PUT", StringComparison.Ordinal));
            Assert.DoesNotContain(p.Events(), e => e.Type == "GateMerged");
        }
    }

    [Fact]
    public void A_hanging_put_and_a_hanging_last_read_exit_nonzero_with_status_unknown()
    {
        var (p, f) = Setup();
        using (p)
        {
            var put = false;
            f.Intercept = async (req, ct) =>
            {
                if (req.Method == HttpMethod.Put)
                {
                    put = true;
                    await Task.Delay(Timeout.Infinite, ct);
                }

                if (put && req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/pulls/7", StringComparison.Ordinal))
                {
                    await Task.Delay(Timeout.Infinite, ct);
                }

                return null;
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("Status unbekannt", r.Err, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void An_answered_put_with_both_reads_failing_still_writes_GateMerged_with_the_put_sha(bool hang)
    {
        var (p, f) = Setup();
        using (p)
        {
            var put = false;
            f.Intercept = async (req, ct) =>
            {
                if (req.Method == HttpMethod.Put)
                {
                    put = true;
                    return null;
                }

                if (put && req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath.EndsWith("/pulls/7", StringComparison.Ordinal))
                {
                    if (hang)
                    {
                        await Task.Delay(Timeout.Infinite, ct);
                    }

                    return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
                }

                return null;
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("Status unbekannt", r.Err, StringComparison.Ordinal);
            Assert.Equal(FakeForge.MergeSha, Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("merge_commit_sha").GetString());
            Assert.Single(f.MergeBodies);
        }
    }

    [Theory]
    [InlineData("success", "green", 0)]
    [InlineData("failure", "red", 1)]
    [InlineData("cancelled", "cancelled", 1)]
    [InlineData("", "pending", 1)]
    public void Gate_check_reads_GateMerged_and_appends_the_result_of_the_run_on_the_merge_commit(string conclusion, string expected, int code)
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", "", "in_progress")}}]}""";
            Assert.Equal(1, Merge(p).Code); // pending
            f.Checks[FakeForge.MergeSha] = $$"""{"total_count":1,"check_runs":[{{FakeForge.Run(4, "test", conclusion, conclusion == "" ? "in_progress" : "completed", RealUrl(99))}}]}""";
            var r = Check(p);
            Assert.Equal(code, r.Code);
            Assert.Equal(["pending", expected], Results(p));
            var last = p.Events().Last(e => e.Type == "IntegrationRunChecked").Payload;
            Assert.Equal(FakeForge.MergeSha, last.GetProperty("sha").GetString());
            Assert.Equal("G-0001", last.GetProperty("gate").GetString());
            Assert.Single(f.MergeBodies);
            Assert.Single(p.Events(), e => e.Type == "GateMerged");
        }
    }

    [Fact]
    public void Gate_check_without_GateMerged_fails_and_writes_nothing()
    {
        var (p, f) = Setup();
        using (p)
        {
            var r = Check(p);
            Assert.Equal(1, r.Code);
            Assert.Contains("GateMerged", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(p.Events(), e => e.Type == "IntegrationRunChecked");
            Assert.DoesNotContain(f.Paths, x => x.Contains("check-runs", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Gate_check_works_for_a_closed_gate_and_can_be_repeated()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            Assert.Equal(0, Merge(p).Code);
            var gate = Directory.GetFiles(Path.Combine(p.Root, ".monarch", "gates"), "G-0001*").Single();
            File.WriteAllText(gate, File.ReadAllText(gate).Replace("state: proposed", "state: cleared", StringComparison.Ordinal));
            Assert.Equal(0, Check(p).Code);
            Assert.Equal(0, Check(p).Code);
            Assert.Equal(["green", "green", "green"], Results(p));
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    [Fact]
    public void Gate_check_refuses_a_shadow_and_writes_nothing()
    {
        var (p, f) = Setup();
        using (p)
        {
            Approved(p);
            Assert.Equal(0, Merge(p).Code);
            var before = Results(p).Length;
            var r = Check(p, new() { ["MONARCH_AGENT_TYPE"] = "monarch:knight" });
            Assert.NotEqual(0, r.Code);
            Assert.Contains("shadow", r.Err, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(before, Results(p).Length);
        }
    }

    [Fact]
    public void Hook_denies_gate_check_for_shadows_only()
    {
        using var p = new TempProject();
        var shadow = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "monarch gate check G-0101", agentType: "monarch:knight"));
        Assert.Equal("deny", JsonDocument.Parse(shadow.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
        var main = p.Hook("pre-tool-use", TempProject.PreToolUse("Bash", command: "monarch gate check G-0101"));
        Assert.DoesNotContain("deny", main.Out, StringComparison.Ordinal);
    }

    // ---- Warden code round Q-0137
    [Theory]
    [InlineData(502)]
    [InlineData(500)]
    [InlineData(0)] // transport error: no status code
    public void A_failed_put_whose_merge_landed_is_recorded_like_an_expired_one(int status)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Intercept = (req, ct) =>
            {
                if (req.Method != HttpMethod.Put) return Task.FromResult<HttpResponseMessage?>(null);
                f.Pulls[7] = (FakeForge.Sha, true);
                if (status == 0) throw new HttpRequestException("connection reset");
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage((System.Net.HttpStatusCode)status));
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.Equal(0, r.Code);
            Assert.Equal(FakeForge.MergeSha, Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("merge_commit_sha").GetString());
            Assert.Equal(["green"], Results(p));
        }
    }

    [Theory]
    [InlineData(502)]
    [InlineData(0)]
    public void A_failed_put_whose_merge_did_not_land_is_status_unknown(int status)
    {
        var (p, f) = Setup();
        using (p)
        {
            f.Intercept = (req, ct) =>
            {
                if (req.Method != HttpMethod.Put) return Task.FromResult<HttpResponseMessage?>(null);
                if (status == 0) throw new HttpRequestException("connection reset");
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage((System.Net.HttpStatusCode)status));
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("Status unbekannt", r.Err, StringComparison.Ordinal);
            Assert.DoesNotContain(p.Events(), e => e.Type == "GateMerged");
        }
    }

    [Fact]
    public void An_answered_put_with_a_lagging_read_records_the_put_sha_not_the_test_merge_sha()
    {
        var (p, f) = Setup();
        using (p)
        {
            var put = false;
            f.Intercept = (req, ct) =>
            {
                if (req.Method == HttpMethod.Put) { put = true; return Task.FromResult<HttpResponseMessage?>(null); }
                if (put && req.RequestUri!.AbsolutePath.EndsWith("/pulls/7", StringComparison.Ordinal))
                {
                    var json = $$$"""{"number":7,"merged":false,"merge_commit_sha":"7e570000000000000000000000000000000000aa","head":{"sha":"{{{FakeForge.Sha}}}"},"base":{"ref":"develop"}}""";
                    return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") });
                }

                return Task.FromResult<HttpResponseMessage?>(null);
            };
            Approved(p);
            var r = MergeWith(p, Forge1);
            Assert.NotEqual(0, r.Code);
            Assert.Equal(FakeForge.MergeSha, Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("merge_commit_sha").GetString());
        }
    }

    private static void BothReadsFailAfterPut(FakeForge f)
    {
        var put = false;
        f.Intercept = (req, ct) =>
        {
            if (req.Method == HttpMethod.Put) { put = true; return Task.FromResult<HttpResponseMessage?>(null); }
            if (put && req.RequestUri!.AbsolutePath.EndsWith("/pulls/7", StringComparison.Ordinal))
            {
                return Task.FromResult<HttpResponseMessage?>(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
            }

            return Task.FromResult<HttpResponseMessage?>(null);
        };
    }

    [Fact]
    public void Gate_check_after_status_unknown_uses_the_configured_branch_when_GateMerged_has_no_base()
    {
        var (p, f) = Setup();
        using (p)
        {
            BothReadsFailAfterPut(f);
            Approved(p);
            Assert.NotEqual(0, MergeWith(p, Forge1).Code);
            var merged = Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload;
            Assert.False(merged.TryGetProperty("base", out _));
            f.Intercept = null;
            var r = Check(p);
            Assert.Equal(0, r.Code);
            Assert.Equal("develop", Assert.Single(p.Events(), e => e.Type == "IntegrationRunChecked").Payload.GetProperty("branch").GetString());
        }
    }

    [Fact]
    public void Gate_check_after_status_unknown_catches_up_the_first_parent_of_a_fix_pr()
    {
        var (p, f) = Setup();
        using (p)
        {
            RedDevelop(f);
            ProvenFix(f);
            f.Parents[FakeForge.MergeSha] = "0ther000000000000000000000000000000000000";
            BothReadsFailAfterPut(f);
            Approved(p);
            Assert.NotEqual(0, MergeWith(p, Forge1).Code);
            Assert.Equal(FakeForge.DevelopSha, Assert.Single(p.Events(), e => e.Type == "GateMerged").Payload.GetProperty("integration_head").GetString());
            f.Intercept = null;
            var r = Check(p);
            Assert.NotEqual(0, r.Code);
            Assert.Contains("0ther000", r.Err, StringComparison.Ordinal);
            var run = Assert.Single(p.Events(), e => e.Type == "IntegrationRunChecked").Payload;
            Assert.Equal("mismatch", run.GetProperty("parent_check").GetString());
        }
    }
}
