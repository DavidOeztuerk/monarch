using System.Text.Json;
using Monarch.Domain;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

public class GateImportTests
{
    private static string QDir(TempProject p) => Path.Combine(p.Root, ".monarch", "quests");

    private static string GDir(TempProject p) => Path.Combine(p.Root, ".monarch", "gates");

    [Fact]
    public void Imports_real_files_byte_for_byte_and_is_idempotent()
    {
        using var p = new TempProject();
        var r = p.Run(["gate", "import", Seed.Fixtures]);
        Assert.Equal(0, r.Code);
        foreach (var f in Directory.EnumerateFiles(Seed.Fixtures, "*.md"))
        {
            var dest = Path.Combine(Path.GetFileName(f).StartsWith('G') ? GDir(p) : QDir(p), Path.GetFileName(f));
            Assert.Equal(File.ReadAllBytes(f), File.ReadAllBytes(dest));
        }

        Assert.Equal(4, p.Events().Count(e => e.Type == "GateOpened"));
        Assert.Equal(4, p.Events().Count(e => e.Type == "QuestProposed"));
        var again = p.Run(["gate", "import", Seed.Fixtures]);
        Assert.Equal(0, again.Code);
        Assert.Equal(4, p.Events().Count(e => e.Type == "GateOpened"));
        Assert.Equal(4, p.Events().Count(e => e.Type == "QuestProposed"));
        Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        Assert.Contains("Q-0001", p.Run(["quest", "list"]).Out);
    }

    [Fact]
    public void Repo_filter_keeps_matching_quests_and_their_gates_case_insensitively()
    {
        using var p = new TempProject();
        Assert.Equal(0, p.Run(["gate", "import", Seed.Fixtures, "--repo", "WorkerTransfer"]).Code);
        Assert.Equal(["Q-0001", "Q-0014"], Directory.GetFiles(QDir(p)).Select(f => Path.GetFileName(f)[..6]).Order().ToArray());
        Assert.Equal(["G-0001", "G-0004"], Directory.GetFiles(GDir(p)).Select(f => Path.GetFileName(f)[..6]).Order().ToArray());

        using var q = new TempProject();
        q.Run(["gate", "import", Seed.Fixtures, "--repo", "NOELIA"]);
        Assert.Equal(["Q-0014", "Q-0016"], Directory.GetFiles(QDir(q)).Select(f => Path.GetFileName(f)[..6]).Order().ToArray());
        Assert.Equal(["G-0004", "G-0006"], Directory.GetFiles(GDir(q)).Select(f => Path.GetFileName(f)[..6]).Order().ToArray());
    }

    [Fact]
    public void Existing_local_files_are_never_overwritten()
    {
        using var p = new TempProject();
        p.Run(["gate", "import", Seed.Fixtures, "--repo", "NoeliaControlPlane"]);
        var file = Directory.GetFiles(QDir(p)).Single();
        File.WriteAllText(file, File.ReadAllText(file).Replace("state: proposed", "state: accepted", StringComparison.Ordinal));
        p.Run(["gate", "import", Seed.Fixtures, "--repo", "NoeliaControlPlane"]);
        Assert.Equal("accepted", Seed.State(file));
    }

    [Fact]
    public void Missing_directory_or_bad_file_fails_without_importing()
    {
        using var p = new TempProject();
        Assert.Equal(1, p.Run(["gate", "import", Path.Combine(p.Root, "nope")]).Code);
        var dir = Path.Combine(p.Root, "bad");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Q-0001-x.md"), "no frontmatter");
        var r = p.Run(["gate", "import", dir]);
        Assert.Equal(1, r.Code);
        Assert.Empty(Directory.GetFiles(QDir(p)));
    }
}

public class GateViewTests
{
    [Fact]
    public void List_and_show_print_quests_with_state_rank_and_merge_rule()
    {
        using var p = new TempProject();
        p.Run(["gate", "import", Seed.Fixtures, "--repo", "workertransfer"]);
        var list = p.Run(["gate", "list"]);
        Assert.Equal(0, list.Code);
        Assert.Contains("G-0001", list.Out);
        Assert.Contains("G-0004", list.Out);
        var show = p.Run(["gate", "show", "G-0001"]);
        Assert.Equal(0, show.Code);
        Assert.Contains("Q-0001", show.Out);
        Assert.Contains("proposed", show.Out);
        Assert.Contains("OwnerApprovalRequired", show.Out);
        Assert.Equal(1, p.Run(["gate", "show", "G-9999"]).Code);
    }
}

public class GateSyncAndPrTests
{
    [Fact]
    public void Sync_creates_milestone_and_issue_and_writes_numbers_back_idempotently()
    {
        using var p = new TempProject(systemYaml: Seed.ForgeYaml);
        var forge = new FakeForge();
        p.Http = forge;
        var gate = Seed.Gate(p);
        var quest = Seed.Quest(p, "Q-0001");
        var r = p.Run(["gate", "sync", "--github", "--owner", "o", "--repo", "r"]);
        Assert.Equal(0, r.Code);
        Assert.Single(forge.Board.Milestones);
        Assert.Single(forge.Board.Issues);
        Assert.Matches(@"board:\s*\{[^}]*provider: github[^}]*issue: 1", File.ReadAllText(quest));
        Assert.Matches(@"board:\s*\{[^}]*provider: github[^}]*milestone: 1", File.ReadAllText(gate));
        Assert.StartsWith("---\nid: Q-0001", File.ReadAllText(quest), StringComparison.Ordinal);
        var writes = forge.Board.Writes.Count();
        Assert.Equal(0, p.Run(["gate", "sync", "--github", "--owner", "o", "--repo", "r"]).Code);
        Assert.Equal(writes, forge.Board.Writes.Count());
    }

    [Fact]
    public void Quest_pr_links_a_pull_request_and_keeps_other_board_keys()
    {
        using var p = new TempProject();
        var quest = Seed.Quest(p, "Q-0001", extra: "board: { provider: github, issue: 4 }\n");
        Assert.Equal(0, p.Run(["quest", "pr", "Q-0001", "7"]).Code);
        var text = File.ReadAllText(quest);
        Assert.Contains("issue: 4", text, StringComparison.Ordinal);
        Assert.Contains("pr: 7", text, StringComparison.Ordinal);
        Assert.Equal(1, p.Run(["quest", "pr", "Q-0001", "abc"]).Code);
        Assert.Equal(1, p.Run(["quest", "pr", "Q-4040", "7"]).Code);
    }
}

public class VerifyClearTests
{
    private static (TempProject P, FakeForge F, string Quest) Setup(string state = "in_review", string yaml = Seed.ForgeYaml, string rank = "C")
    {
        var p = new TempProject(systemYaml: yaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, false);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        p.Http = f;
        Seed.Gate(p);
        return (p, f, Seed.Quest(p, "Q-0001", state, rank: rank, extra: "board: { provider: github, pr: 7 }\n"));
    }

    [Fact]
    public void Verify_with_all_checks_green_records_ci_evidence()
    {
        var (p, _, quest) = Setup();
        using (p)
        {
            var r = p.Run(["quest", "verify", "Q-0001"]);
            Assert.Equal(0, r.Code);
            Assert.Equal("verified", Seed.State(quest));
            var ev = Assert.Single(p.Events(), e => e.Type == "QuestVerified");
            Assert.Equal(2, ev.Payload.GetProperty("evidence").GetArrayLength());
            Assert.Equal(64, ev.Payload.GetProperty("evidence")[0].GetProperty("digest").GetString()!.Length);
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        }
    }

    [Fact]
    public void Verify_refuses_failing_missing_or_unlinked_cases_without_changing_anything()
    {
        var (p, f, quest) = Setup();
        using (p)
        {
            f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "failure"));
            Assert.Equal(1, p.Run(["quest", "verify", "Q-0001"]).Code);
            f.SetChecks();
            Assert.Equal(1, p.Run(["quest", "verify", "Q-0001"]).Code);
            Seed.Quest(p, "Q-0002", "in_progress", extra: "board: { provider: github, pr: 7 }\n");
            Assert.Equal(1, p.Run(["quest", "verify", "Q-0002"]).Code);
            Assert.DoesNotContain(p.Events(), e => e.Type == "QuestVerified" && e.Payload.GetProperty("quest").GetString() == "Q-0002");
            Seed.Quest(p, "Q-0003", "in_review");
            Assert.Equal(1, p.Run(["quest", "verify", "Q-0003"]).Code);
            _ = quest;
        }
    }

    [Fact]
    public void Required_checks_come_from_config()
    {
        var (p, f, quest) = Setup(yaml: Seed.ForgeYaml + "evidence:\n  required: [build]\n");
        using (p)
        {
            f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "failure"));
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            Assert.Equal("verified", Seed.State(quest));
        }

        var (q, g, quest2) = Setup(yaml: Seed.ForgeYaml + "evidence:\n  required: [build, lint]\n");
        using (q)
        {
            g.SetChecks(FakeForge.Run(1, "build", "success"));
            Assert.Equal(1, q.Run(["quest", "verify", "Q-0001"]).Code);
            Assert.Equal("in_review", Seed.State(quest2));
        }
    }

    [Fact]
    public void Full_lifecycle_clears_with_xp_closes_the_issue_and_clears_active()
    {
        var (p, f, quest) = Setup(state: "proposed");
        using (p)
        {
            Assert.Equal(0, p.Run(["gate", "sync", "--github", "--owner", "o", "--repo", "r"]).Code);
            foreach (var a in new[] { "accept", "start", "submit" })
            {
                Assert.Equal(0, p.Run(["quest", a, "Q-0001"]).Code);
            }

            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            var notMerged = p.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(1, notMerged.Code);
            Assert.Equal("verified", Seed.State(quest));
            f.Pulls[7] = (FakeForge.Sha, true);
            var r = p.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(0, r.Code);
            Assert.Equal("cleared", Seed.State(quest));
            var ev = Assert.Single(p.Events(), e => e.Type == "QuestCleared");
            Assert.Equal(75, ev.Payload.GetProperty("xp").GetInt32());
            Assert.Equal("MergeCommit", ev.Payload.GetProperty("evidence")[0].GetProperty("kind").GetString());
            Assert.False(File.Exists(p.LocalPath("active.json")));
            Assert.Equal("closed", f.Board.Issues.Single().State);
            Assert.Contains("XP 75", p.Run(["status"]).Out);
            Assert.True(LedgerVerifier.Verify(p.Root).Ok);
            Assert.Equal(1, p.Run(["quest", "clear", "Q-0001"]).Code);
        }
    }

    [Fact]
    public void Evidence_sync_verifies_and_clears_and_reports_pending()
    {
        var (p, f, quest) = Setup();
        using (p)
        {
            f.SetChecks(FakeForge.Run(1, "build", "success", status: "in_progress"));
            var pending = p.Run(["evidence", "sync"]);
            Assert.Equal(0, pending.Code);
            Assert.Contains("pending", pending.Out, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("in_review", Seed.State(quest));
            f.SetChecks(FakeForge.Run(1, "build", "success"));
            f.Pulls[7] = (FakeForge.Sha, true);
            Assert.Equal(0, p.Run(["evidence", "sync", "--quest", "Q-0001"]).Code);
            Assert.Equal("cleared", Seed.State(quest));
        }
    }

    [Theory]
    [InlineData(Rank.C, QuestKind.Main, 1, 75)]
    [InlineData(Rank.C, QuestKind.Main, 3, 42)]
    [InlineData(Rank.A, QuestKind.Side, 1, 300)]
    [InlineData(Rank.E, QuestKind.Emergency, 2, 15)]
    [InlineData(Rank.B, QuestKind.Penalty, 1, 0)]
    public void Xp_follows_the_concept_formula(Rank rank, QuestKind kind, int submissions, int expected) =>
        Assert.Equal(expected, XpCalculator.Compute(rank, kind, submissions).Xp);
}

public class ManaCommandsTests
{
    [Fact]
    public void Recap_writes_sdd_file_events_and_keeps_next_step_and_decisions()
    {
        using var p = new TempProject(systemYaml: Seed.ForgeYaml);
        Seed.Mana(p, 40, 20);
        Seed.Gate(p);
        Seed.Quest(p, "Q-0001", "cleared", extra: "board: { provider: github, pr: 7 }\n");
        Seed.Quest(p, "Q-0002", "in_progress");
        var r = p.Run(["recap", "--next", "Do the thing", "--decision", "Use A or B?", "--decision", "Ship Friday?"]);
        Assert.Equal(0, r.Code);
        var text = File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md"));
        foreach (var h in new[] { "## Stand", "## Aktiv", "## Erledigt", "## Offen", "## Nächster konkreter Schritt", "## Offene Entscheidungen" })
        {
            Assert.Contains(h, text, StringComparison.Ordinal);
        }

        Assert.Contains("Q-0001", text, StringComparison.Ordinal);
        Assert.Contains("pull/7", text, StringComparison.Ordinal);
        Assert.Contains("first item", text, StringComparison.Ordinal);
        Assert.DoesNotContain("second item", text, StringComparison.Ordinal);
        Assert.Contains("Do the thing", text, StringComparison.Ordinal);
        Assert.Contains("Ship Friday?", text, StringComparison.Ordinal);
        Assert.Equal(2, p.Events().Count(e => e.Type == "ManaSnapshotTaken"));
        var rw = Assert.Single(p.Events(), e => e.Type == "RecapWritten");
        Assert.True(rw.Payload.TryGetProperty("cost5h", out _) && rw.Payload.TryGetProperty("cost7d", out _));

        Assert.Equal(0, p.Run(["recap"]).Code);
        var again = File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md"));
        Assert.Contains("Do the thing", again, StringComparison.Ordinal);
        Assert.Contains("Use A or B?", again, StringComparison.Ordinal);
        p.Run(["recap", "--next", "Other"]);
        var third = File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md"));
        Assert.DoesNotContain("Do the thing", third, StringComparison.Ordinal);
        Assert.Contains("Use A or B?", third, StringComparison.Ordinal);
        Assert.True(LedgerVerifier.Verify(p.Root).Ok);
    }

    [Fact]
    public void Rest_shows_resets_decision_and_resume_time_and_logs()
    {
        using var p = new TempProject();
        var reset = TempProject.Now.AddHours(2);
        Seed.Mana(p, 92, 30, reset);
        var r = p.Run(["rest"]);
        Assert.Equal(0, r.Code);
        Assert.Contains("RecapAndStop", r.Out, StringComparison.Ordinal);
        Assert.Contains(reset.AddMinutes(2).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture), r.Out, StringComparison.Ordinal);
        Assert.Single(p.Events(), e => e.Type == "RestStarted");
    }

    [Fact]
    public void Mana_shows_plan_limits_then_adaptive_after_three_recaps_and_judges_estimates()
    {
        using var p = new TempProject();
        Seed.Mana(p, 60, 20);
        var first = p.Run(["mana", "--estimate", "30"]);
        Assert.Equal(0, first.Code);
        Assert.Contains("plan default", first.Out, StringComparison.Ordinal);
        Assert.Contains("RecapAndStop", first.Out, StringComparison.Ordinal);
        for (var i = 0; i < 3; i++)
        {
            p.Run(["recap"]);
        }

        var later = p.Run(["mana", "--estimate", "5"]);
        Assert.Contains("adaptive", later.Out, StringComparison.Ordinal);
        Assert.Contains("Proceed", later.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Ledger_verify_passes_and_flags_tampering_with_exit_1()
    {
        using var p = new TempProject();
        p.Run(["recap"]);
        p.Run(["recap"]);
        var ok = p.Run(["ledger", "verify"]);
        Assert.Equal(0, ok.Code);
        var file = Directory.EnumerateFiles(Path.Combine(p.Root, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories).First();
        File.WriteAllText(file, File.ReadAllText(file).Replace("RecapWritten", "RecapWritteX", StringComparison.Ordinal));
        var bad = p.Run(["ledger", "verify"]);
        Assert.Equal(1, bad.Code);
        Assert.NotEmpty(bad.Out + bad.Err);
    }
}
