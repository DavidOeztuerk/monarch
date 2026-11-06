using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>Q-0132: the rules appear where they are needed (quest start, SessionStart briefing, scope-lock deny). The texts are fixed.</summary>
public class RulesAtPlaceTests
{
    internal const string Rule1 = "Regel 1: Der Scope gilt für jeden Schreibzugriff, auch Scratchpad, Temp-Ordner und Notizen. Was außerhalb liegt, geht nur über `gate_plan` (Folge-Quests), `knowledge_write` (Notizen) oder nach `quest submit`.";
    internal const string Rule2 = "Regel 2: Ein Deny heißt stoppen und den genannten Weg nehmen, keine Varianten.";
    internal const string MergeLine = "Merge nur über `monarch gate merge` in den Integrations-Branch; main/master nie.";
    internal const string PlanLine = "Folge-Quests laufen über `gate_plan`.";

    private static TempProject Project()
    {
        var p = new TempProject();
        File.WriteAllText(Path.Combine(p.Root, ".monarch", "quests", "Q-0003-demo.md"),
            "---\nid: Q-0003\ngate: G-0001\nkind: main\nrank: B\nstate: proposed\nestimate: { session_pct: 5, weekly_pct: 1 }\nscope:\n  include: [\"src/**\", \"tests/**\"]\n  exclude: [\"src/gen/**\"]\ndod: [\"tests green\"]\n---\n# Demo quest\n");
        return p;
    }

    [Fact]
    public void Quest_start_prints_scope_and_both_rules_after_start_ok()
    {
        using var p = Project();
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"]).Code);
        var r = p.Run(["quest", "start", "Q-0003"]);
        Assert.Equal(0, r.Code);
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal("Q-0003: start ok -> in_progress", lines[0]);
        Assert.Equal("Scope: include src/**, tests/**; exclude src/gen/**", lines[1]);
        Assert.Equal(Rule1, lines[2]);
        Assert.Equal(Rule2, lines[3]);
        Assert.Equal(4, lines.Length);
        Assert.DoesNotContain("Regel 1", p.Run(["quest", "submit", "Q-0003"]).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Briefing_with_an_active_quest_has_scope_rules_and_the_merge_line()
    {
        using var p = new TempProject(activeRank: "B", include: "src/**");
        var text = Briefing(p);
        Assert.Contains("Q-0003", text, StringComparison.Ordinal);
        Assert.Contains("Scope src/**", text, StringComparison.Ordinal);
        Assert.Contains(Rule1, text, StringComparison.Ordinal);
        Assert.Contains(Rule2, text, StringComparison.Ordinal);
        Assert.Contains(MergeLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain(PlanLine, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Briefing_without_a_quest_has_only_the_merge_line_and_the_gate_plan_hint()
    {
        using var p = new TempProject();
        var text = Briefing(p);
        Assert.Contains(MergeLine, text, StringComparison.Ordinal);
        Assert.Contains(PlanLine, text, StringComparison.Ordinal);
        Assert.DoesNotContain("Regel 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Regel 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Briefing_has_the_standard_rules_core_with_and_without_a_quest()
    {
        using var none = new TempProject();
        using var active = new TempProject(activeRank: "B", include: "src/**");
        foreach (var text in new[] { Briefing(none), Briefing(active) })
        {
            Assert.Contains(StandardRulesLine, text, StringComparison.Ordinal);
            Assert.Contains("Skill `monarch:regeln` vor der Arbeit lesen", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Regeln_skill_exists_and_names_all_six_topics()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "plugin", "skills", "regeln", "SKILL.md")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir, "plugin", "skills", "regeln", "SKILL.md"));
        Assert.Contains("name: regeln", text, StringComparison.Ordinal);
        foreach (var topic in new[] { "Worktree", "Prüfung nach Risiko", "gate merge", "[skip ci]", "--body-file", "Aufräumen" })
        {
            Assert.Contains(topic, text, StringComparison.Ordinal);
        }
    }

    internal const string StandardRulesLine = "Standardregeln: eine Worktree je Gate, Prüfung nach Risiko, Merge nur per `monarch gate merge`, kein `[skip ci]` vor dem Merge, Issue-Texte per `--body-file`, Aufräumen nach dem Merge; Skill `monarch:regeln` vor der Arbeit lesen.";

    [Fact]
    public void Hook_deny_for_a_write_outside_the_scope_names_the_way_forward()
    {
        using var p = new TempProject(activeRank: "B", include: "src/**");
        var r = p.Hook("pre-tool-use", TempProject.PreToolUse("Write", file: "notes/scratch.md"));
        var reason = JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecisionReason").GetString()!;
        Assert.Contains("liegt außerhalb des Scopes von Q-0003", reason, StringComparison.Ordinal);
        Assert.Contains("Folge-Quest per `gate_plan`, Notiz per `knowledge_write`, oder erst `quest submit`; nicht mit anderem Pfad/Tool wiederholen.", reason, StringComparison.Ordinal);
    }

    private static string Briefing(TempProject p)
    {
        var r = p.Hook("session-start", """{"session_id":"sess-1","hook_event_name":"SessionStart"}""");
        return JsonDocument.Parse(r.Out).RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext").GetString()!;
    }
}
