using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

/// <summary>Q-0122 / ADR-0011 G3: <c>quest clear</c> and <c>evidence sync</c> run <c>ledger verify</c> first and refuse the clear on findings.</summary>
public class ClearLedgerVerifyTests
{
    private const string Stop = """{"session_id":"sess1"}""";

    private static (TempProject P, FakeForge F, string Quest) Setup(string state)
    {
        var p = new TempProject(systemYaml: Seed.ForgeYaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, true);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        p.Http = f;
        Seed.Gate(p);
        return (p, f, Seed.Quest(p, "Q-0001", state, extra: "board: { provider: github, pr: 7 }\n"));
    }

    // An unacknowledged anchor reset is a verify finding that still lets the hook append (see LedgerAckResetTests).
    private static void BreakLedger(TempProject p)
    {
        p.Hook("stop", Stop);
        File.WriteAllText(Path.Combine(p.Root, ".monarch", "local", "ledger-heads.json"), "garbage");
        p.Hook("stop", Stop);
        Assert.False(LedgerVerifier.Verify(p.Root).Ok);
    }

    [Fact]
    public void Clear_with_a_clean_ledger_still_clears()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            Assert.Equal(0, p.Run(["quest", "clear", "Q-0001"]).Code);
            Assert.Equal("cleared", Seed.State(quest));
            Assert.DoesNotContain(p.Events(), e => e.Type == "ClearRefusedLedgerInvalid");
        }
    }

    [Fact]
    public void Clear_is_refused_with_a_reason_and_logged_when_verify_has_findings()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            BreakLedger(p);
            var r = p.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(1, r.Code);
            Assert.Contains("ledger", r.Err, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("AnchorReset", r.Err, StringComparison.Ordinal);
            Assert.Equal("verified", Seed.State(quest));
            Assert.DoesNotContain(p.Events(), e => e.Type == "QuestCleared");
            var ev = Assert.Single(p.Events(), e => e.Type == "ClearRefusedLedgerInvalid");
            Assert.Equal("Q-0001", ev.Payload.GetProperty("quest").GetString());
        }
    }

    [Fact]
    public void Evidence_sync_refuses_the_clear_when_verify_has_findings()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            BreakLedger(p);
            var r = p.Run(["evidence", "sync", "--quest", "Q-0001"]);
            Assert.Equal(1, r.Code);
            Assert.Contains("ledger", r.Out + r.Err, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual("cleared", Seed.State(quest));
            Assert.DoesNotContain(p.Events(), e => e.Type == "QuestCleared");
            Assert.Single(p.Events(), e => e.Type == "ClearRefusedLedgerInvalid");
        }
    }

    [Fact]
    public void Evidence_sync_with_a_clean_ledger_clears()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            Assert.Equal(0, p.Run(["evidence", "sync", "--quest", "Q-0001"]).Code);
            Assert.Equal("cleared", Seed.State(quest));
        }
    }

    private static void TruncateLedgerTo(TempProject p, int lines)
    {
        var file = Directory.EnumerateFiles(Path.Combine(p.Root, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories)
            .OrderByDescending(f => File.ReadAllLines(f).Length).First();
        File.WriteAllLines(file, File.ReadAllLines(file).Take(lines));
    }

    [Fact]
    public void Truncated_ledger_stays_red_and_clear_refused_after_many_appends_until_ack_reset()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            for (var i = 0; i < 4; i++)
            {
                p.Hook("stop", Stop);
            }

            TruncateLedgerTo(p, 2);
            for (var i = 0; i < 10; i++)
            {
                p.Hook("stop", Stop);
                Assert.Equal(1, p.Run(["ledger", "verify"]).Code);
                var r = p.Run(["quest", "clear", "Q-0001"]);
                Assert.Equal(1, r.Code);
                Assert.Equal("verified", Seed.State(quest));
            }

            Assert.DoesNotContain(p.Events(), e => e.Type == "QuestCleared");
            var reset = Assert.Single(LedgerVerifier.Verify(p.Root).Resets!, x => !x.Acknowledged);
            Assert.Contains("anchor ahead of file", reset.Reason, StringComparison.OrdinalIgnoreCase);

            Assert.Equal(1, p.Run(["ledger", "ack-reset", reset.Hash], tty: false).Code);
            Assert.Equal(1, p.Run(["ledger", "verify"]).Code);
            Assert.Equal(0, p.Run(["ledger", "ack-reset", reset.Hash], tty: true).Code);
            Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
            Assert.Equal(0, p.Run(["quest", "clear", "Q-0001"]).Code);
        }
    }

    [Fact]
    public void Normal_append_history_produces_no_finding()
    {
        var (p, _, _) = Setup("in_review");
        using (p)
        {
            for (var i = 0; i < 12; i++)
            {
                p.Hook("stop", Stop);
            }

            Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
            Assert.DoesNotContain(p.Events(), e => e.Type == "LedgerAnchorReset");
        }
    }

    [Fact]
    public void Evidence_sync_with_a_ledger_finding_writes_neither_verified_nor_cleared()
    {
        var (p, _, quest) = Setup("in_review");
        using (p)
        {
            BreakLedger(p);
            var r = p.Run(["evidence", "sync", "--quest", "Q-0001"]);
            Assert.Equal(1, r.Code);
            Assert.Equal("in_review", Seed.State(quest));
            Assert.DoesNotContain(p.Events(), e => e.Type is "QuestVerified" or "QuestCleared");
        }
    }
}
