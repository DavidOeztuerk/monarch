using System.Diagnostics;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0018 / Q-0154: the CLI callers of <c>LedgerVerifier</c> pass the branches from <c>system.yaml</c> and refuse on <c>UnanchoredCommit</c>.</summary>
public class LedgerHistoryCliTests
{
    private const string Stop = """{"session_id":"sess1"}""";
    private const string Review = "https://example.test/review/1";

    private sealed class Scenario : IDisposable
    {
        private readonly string _tmp = Path.Combine(Path.GetTempPath(), "monarch-hist-" + Guid.NewGuid().ToString("N"));
        private readonly TempProject _p;

        /// <summary>Remote develop carries a foreign ledger file; the project root becomes a clone-like worktree on a gate branch. Must run before the first ledger append.</summary>
        public Scenario(TempProject p)
        {
            _p = p;
            Directory.CreateDirectory(_tmp);
            var bare = Path.Combine(_tmp, "remote.git");
            Directory.CreateDirectory(bare);
            Git(bare, "init", "-q", "--bare", "-b", "develop");
            var seed = Path.Combine(_tmp, "seed");
            Directory.CreateDirectory(seed);
            Git(seed, "init", "-q", "-b", "develop");
            Git(seed, "remote", "add", "origin", bare);
            var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = seed }, p.Clock);
            for (var i = 0; i < 3; i++)
            {
                store.Append("s9", "knight", "Note", System.Text.Json.JsonDocument.Parse("""{"foreign":true}""").RootElement.Clone());
            }

            Git(seed, "add", "-A", ".monarch/ledger");
            Git(seed, "commit", "-q", "-m", "d1");
            Git(seed, "push", "-q", "origin", "develop");
            Git(p.Root, "init", "-q", "-b", "develop");
            Git(p.Root, "remote", "add", "origin", bare);
            Git(p.Root, "fetch", "-q", "origin");
            Git(p.Root, "checkout", "-q", "-b", "monarch/G-0001-x", "origin/develop");
            Foreign = Directory.GetFiles(Path.Combine(p.Root, ".monarch", "ledger"), "s9.*.jsonl", SearchOption.AllDirectories).Single();
        }

        public string Foreign { get; }

        /// <summary>K-delete-last, committed normally.</summary>
        public void Forge()
        {
            var lines = File.ReadAllLines(Foreign);
            File.WriteAllText(Foreign, string.Join("\n", lines[..^1]) + "\n");
            Git(_p.Root, "add", "-A", ".monarch/ledger");
            Git(_p.Root, "commit", "-q", "-m", "forged");
        }

        public void PushHeadTo(string branch) => Git(_p.Root, "push", "-q", "origin", "HEAD:refs/heads/" + branch);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tmp, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {err.Result}");
    }

    private static (TempProject P, FakeForge F, string Quest) QuestSetup()
    {
        var p = new TempProject(systemYaml: Seed.ForgeYaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, true);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        p.Http = f;
        Seed.Gate(p);
        return (p, f, Seed.Quest(p, "Q-0001", "in_review", extra: "board: { provider: github, pr: 7 }\n"));
    }

    [Fact]
    public void Ledger_verify_exits_1_on_an_unanchored_commit()
    {
        using var p = new TempProject();
        using var s = new Scenario(p);
        p.Hook("stop", Stop);
        Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
        s.Forge();
        var r = p.Run(["ledger", "verify"]);
        Assert.Equal(1, r.Code);
        Assert.Contains("UnanchoredCommit", r.Out, StringComparison.Ordinal);
        Assert.Contains("git fetch", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_reports_the_unanchored_commit_as_a_ledger_violation()
    {
        using var p = new TempProject();
        using var s = new Scenario(p);
        p.Hook("stop", Stop);
        s.Forge();
        Assert.Contains("Ledger: 1 violation(s)", p.Run(["status"]).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Quest_clear_is_refused_on_an_unanchored_commit()
    {
        var (p, _, quest) = QuestSetup();
        using (p)
        {
            using var s = new Scenario(p);
            Assert.Equal(0, p.Run(["quest", "verify", "Q-0001"]).Code);
            s.Forge();
            var r = p.Run(["quest", "clear", "Q-0001"]);
            Assert.Equal(1, r.Code);
            Assert.Contains("UnanchoredCommit", r.Err, StringComparison.Ordinal);
            Assert.Equal("verified", Seed.State(quest));
            Assert.Single(p.Events(), e => e.Type == "ClearRefusedLedgerInvalid");
        }
    }

    [Fact]
    public void Evidence_sync_refuses_the_clear_on_an_unanchored_commit()
    {
        var (p, _, quest) = QuestSetup();
        using (p)
        {
            using var s = new Scenario(p);
            p.Hook("stop", Stop);
            s.Forge();
            var r = p.Run(["evidence", "sync", "--quest", "Q-0001"]);
            Assert.Equal(1, r.Code);
            Assert.Contains("UnanchoredCommit", r.Out + r.Err, StringComparison.Ordinal);
            Assert.NotEqual("cleared", Seed.State(quest));
            Assert.Single(p.Events(), e => e.Type == "ClearRefusedLedgerInvalid");
        }
    }

    [Fact]
    public void Gate_merge_is_refused_on_an_unanchored_commit()
    {
        var p = new TempProject(systemYaml: Seed.ForgeYaml + "git:\n  merge_method: squash\n");
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, false);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", "success"));
        f.Checks[FakeForge.DevelopSha] = f.Checks[FakeForge.Sha];
        f.Checks[FakeForge.MergeSha] = f.Checks[FakeForge.Sha];
        p.Http = f;
        Seed.Gate(p, "G-0001", "[Q-0001]", "C");
        Seed.Quest(p, "Q-0001", "in_review", rank: "C", extra: "board: { provider: github, pr: 7 }\n");
        using (p)
        {
            using var s = new Scenario(p);
            p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);
            Assert.Equal(0, p.Hook("subagent-stop", """{"session_id":"sess1","hook_event_name":"SubagentStop","agent_id":"a1","agent_type":"monarch:warden"}""").Code);
            p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);
            Assert.Equal(0, p.Run(["gate", "verdict", "G-0001", "--verdict", "approve", "--source", Review]).Code);
            s.Forge();
            p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);
            var r = p.Run(["gate", "merge", "G-0001"], vars: new Dictionary<string, string?> { ["MONARCH_MERGE_WAIT_SECONDS"] = "0", ["MONARCH_MERGE_POLL_SECONDS"] = "0" });
            Assert.Equal(1, r.Code);
            Assert.StartsWith("refused:", r.Err, StringComparison.Ordinal);
            Assert.Contains("UnanchoredCommit", r.Err, StringComparison.Ordinal);
            Assert.Empty(f.MergeBodies);
            Assert.DoesNotContain(p.Events(), e => e.Type == "GateMerged");
        }
    }

    [Fact]
    public void The_trusted_branches_come_from_system_yaml()
    {
        // The forged commit is pushed to origin/trunk. With integration_branch: trunk it is on a trusted remote state; with the defaults it is not.
        using var configured = new TempProject(systemYaml: "git:\n  integration_branch: trunk\n");
        using var s1 = new Scenario(configured);
        configured.Hook("stop", Stop);
        s1.Forge();
        s1.PushHeadTo("trunk");
        Assert.Equal(0, configured.Run(["ledger", "verify"]).Code);

        using var defaults = new TempProject();
        using var s2 = new Scenario(defaults);
        defaults.Hook("stop", Stop);
        s2.Forge();
        s2.PushHeadTo("trunk");
        var r = defaults.Run(["ledger", "verify"]);
        Assert.Equal(1, r.Code);
        Assert.Contains("UnanchoredCommit", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Protected_branches_from_system_yaml_count_as_trusted_too()
    {
        using var p = new TempProject(systemYaml: "git:\n  protected_branches: [release]\n");
        using var s = new Scenario(p);
        p.Hook("stop", Stop);
        s.Forge();
        s.PushHeadTo("release");
        Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
    }
}
