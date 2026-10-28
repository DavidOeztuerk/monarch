using System.Text.Json;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

public class AwakenTests
{
    [Fact]
    public void Creates_structure_gitignore_config_and_event_idempotently()
    {
        using var p = new TempProject(awakened: false);
        var r = p.Run(["awaken", "--plan", "max5"]);
        Assert.Equal(0, r.Code);
        foreach (var d in new[] { "ledger", "quests", "gates", "recap", "knowledge", "local" })
        {
            Assert.True(Directory.Exists(Path.Combine(p.Root, ".monarch", d)), d);
        }

        var yaml = File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml"));
        Assert.Equal(Monarch.Domain.PlanKind.Max5, SystemConfigReader.Parse(yaml).Plan);
        Assert.Equal(new Monarch.Domain.ManaLimits(94, 97), SystemConfigReader.Parse(yaml).Limits);
        Assert.Contains(".monarch/local/", File.ReadAllLines(Path.Combine(p.Root, ".gitignore")));
        Assert.Single(p.Events(), e => e.Type == "Awakened");

        var before = File.ReadAllText(Path.Combine(p.Root, ".gitignore")) + yaml;
        var again = p.Run(["awaken", "--plan", "pro"]);
        Assert.Equal(0, again.Code);
        Assert.Contains("already", again.Out, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, File.ReadAllText(Path.Combine(p.Root, ".gitignore")) + File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml")));
        Assert.Single(p.Events(), e => e.Type == "Awakened");
        Assert.True(LedgerVerifier.Verify(p.Root).Ok);
    }

    [Fact]
    public void Gitignore_is_appended_not_duplicated_and_force_rewrites_config()
    {
        using var p = new TempProject(awakened: false);
        File.WriteAllText(Path.Combine(p.Root, ".gitignore"), "bin/\n");
        p.Run(["awaken"]);
        p.Run(["awaken", "--force", "--plan", "max20"]);
        var lines = File.ReadAllLines(Path.Combine(p.Root, ".gitignore"));
        Assert.Equal(1, lines.Count(l => l == ".monarch/local/"));
        Assert.Contains("bin/", lines);
        Assert.Equal(Monarch.Domain.PlanKind.Max20, SystemConfigReader.Load(p.Root).Plan);
    }

    [Fact]
    public void Warns_when_not_in_a_git_repository_and_merges_risk_tags()
    {
        using var p = new TempProject(awakened: false);
        var tags = Path.Combine(p.Home, "tags.yaml");
        File.WriteAllText(tags, "risk_tags:\n  crypto: { min_rank: S, paths: [\"src/**/Crypto/**\"] }\n");
        var r = p.Run(["awaken", "--risk-tags", tags]);
        Assert.Contains("git", r.Out + r.Err, StringComparison.OrdinalIgnoreCase);
        var cfg = SystemConfigReader.Load(p.Root);
        Assert.Contains(cfg.RiskTags, t => t.Tag == "crypto" && t.MinRank == Monarch.Domain.Rank.S);
        Assert.Contains(cfg.RiskTags, t => t.Tag == "auth");
    }
}

public class QuestCommandTests
{
    private static TempProject Project(double sessionPct = 5, string kind = "main")
    {
        var p = new TempProject();
        Directory.CreateDirectory(Path.Combine(p.Root, ".monarch", "quests"));
        File.WriteAllText(Path.Combine(p.Root, ".monarch", "quests", "Q-0003-demo.md"),
            $"---\nid: Q-0003\ngate: G-0001\nkind: {kind}\nrank: B\nstate: proposed\nestimate: {{ session_pct: {sessionPct}, weekly_pct: 1 }}\nscope:\n  include: [\"src/**\"]\n  exclude: []\ndod: [\"tests green\"]\n---\n# Demo quest\n");
        File.WriteAllText(Path.Combine(p.Home, "mana.json"),
            """{"schema":1,"written_at":"2026-10-27T11:59:00Z","rate_limits":{"five_hour":{"used_percentage":20,"resets_at":1793115600},"seven_day":{"used_percentage":43,"resets_at":1793185200}},"rate_limits_at":"2026-10-27T11:59:00Z","rate_limits_stale":false}""");
        return p;
    }

    private static string State(TempProject p) =>
        File.ReadAllLines(Path.Combine(p.Root, ".monarch", "quests", "Q-0003-demo.md")).First(l => l.StartsWith("state:", StringComparison.Ordinal));

    [Fact]
    public void Accept_start_submit_persist_state_active_file_and_a_valid_ledger()
    {
        using var p = Project();
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"]).Code);
        Assert.Equal("state: accepted", State(p));
        var active = JsonDocument.Parse(File.ReadAllText(p.LocalPath("active.json"))).RootElement;
        Assert.Equal("Q-0003", active.GetProperty("questId").GetString());
        Assert.Equal("G-0001", active.GetProperty("gateId").GetString());
        Assert.Equal("B", active.GetProperty("rank").GetString());
        Assert.Equal("src/**", active.GetProperty("scope").GetProperty("include")[0].GetString());

        Assert.Equal(0, p.Run(["quest", "start", "Q-0003"]).Code);
        Assert.Equal("state: in_progress", State(p));
        Assert.True(File.Exists(p.LocalPath("active.json")));

        Assert.Equal(0, p.Run(["quest", "submit", "Q-0003"]).Code);
        Assert.Equal("state: in_review", State(p));
        Assert.False(File.Exists(p.LocalPath("active.json")));

        Assert.Equal(["QuestAccepted", "QuestStarted", "QuestSubmitted", "ManaStepRecorded"], p.Events().Select(e => e.Type).ToArray());
        Assert.True(LedgerVerifier.Verify(p.Root).Ok);
        Assert.Contains("Demo quest", File.ReadAllText(Path.Combine(p.Root, ".monarch", "quests", "Q-0003-demo.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_transition_is_refused_without_side_effects()
    {
        using var p = Project();
        var r = p.Run(["quest", "start", "Q-0003"]);
        Assert.NotEqual(0, r.Code);
        Assert.Equal("state: proposed", State(p));
        Assert.Empty(p.Events());
    }

    [Fact]
    public void Accept_is_refused_when_mana_does_not_fit()
    {
        using var p = Project(sessionPct: 80);
        var r = p.Run(["quest", "accept", "Q-0003"]);
        Assert.NotEqual(0, r.Code);
        Assert.Contains("Mana", r.Out + r.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("state: proposed", State(p));
        Assert.False(File.Exists(p.LocalPath("active.json")));
    }

    [Fact]
    public void Only_one_quest_can_be_active()
    {
        using var p = Project();
        p.Run(["quest", "accept", "Q-0003"]);
        Assert.NotEqual(0, p.Run(["quest", "accept", "Q-0003"]).Code);
    }

    [Fact]
    public void List_show_and_status_work()
    {
        using var p = Project();
        p.Run(["quest", "accept", "Q-0003"]);
        Assert.Contains("Q-0003", p.Run(["quest", "list"]).Out, StringComparison.Ordinal);
        Assert.DoesNotContain("Q-0003", p.Run(["quest", "list", "--state", "proposed"]).Out, StringComparison.Ordinal);
        Assert.Contains("Demo quest", p.Run(["quest", "show", "Q-0003"]).Out, StringComparison.Ordinal);
        var s = p.Run(["status"]);
        Assert.Equal(0, s.Code);
        Assert.True(s.Out.TrimEnd().Split('\n').Length <= 15);
        Assert.Contains("Q-0003", s.Out, StringComparison.Ordinal);
        var j = JsonDocument.Parse(p.Run(["status", "--json"]).Out).RootElement;
        Assert.Equal("Q-0003", j.GetProperty("activeQuest").GetString());
    }
}
