namespace Monarch.Cli;

/// <summary>Fixed rule texts (Q-0132) shown at <c>quest start</c> and in the SessionStart briefing; no free text from the environment.</summary>
internal static class RuleTexts
{
    /// <summary>The visible plugin name (ADR-0019 decision 4); the one place in the code that names it. Policy file: changing it is a rank A quest.</summary>
    public const string PluginName = "umbrarch";

    public const string Rule1 = "Regel 1: Der Scope gilt für jeden Schreibzugriff, auch Scratchpad, Temp-Ordner und Notizen. Was außerhalb liegt, geht nur über `gate_plan` (Folge-Quests), `knowledge_write` (Notizen) oder nach `quest submit`.";
    public const string Rule2 = "Regel 2: Ein Deny heißt stoppen und den genannten Weg nehmen, keine Varianten.";
    public const string MergeLine = "Merge nur über `monarch gate merge` in den Integrations-Branch; main/master nie.";
    public const string StandardRulesLine = "Standardregeln: eine Worktree je Gate, Prüfung nach Risiko, Merge nur per `monarch gate merge`, kein `[skip ci]` vor dem Merge, Issue-Texte per `--body-file`, Aufräumen nach dem Merge; Skill `monarch:regeln` vor der Arbeit lesen.";
    public const string PlanLine = "Folge-Quests laufen über `gate_plan`.";

    /// <summary>The scope of the quest as one line.</summary>
    public static string ScopeLine(IEnumerable<string> include, IEnumerable<string> exclude) =>
        $"Scope: include {string.Join(", ", include)}; exclude {(exclude.Any() ? string.Join(", ", exclude) : "-")}";
}
