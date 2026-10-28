using Monarch.Domain;

namespace Monarch.Policy;

/// <summary>Leitet den Rank eines Gates aus Änderungsfakten und Risk Tags ab. Das System rankt, nicht das LLM.</summary>
public static class RankPolicy
{
    /// <summary>
    /// Bewertet eine Änderung. Das höchste zutreffende Kriterium gewinnt ("eines reicht"). Trifft kein Kriterium zu
    /// (z. B. mehr als fünf Dateien in einem Modul oder keine Tests), gilt konservativ C (Plan-Freigabe).
    /// Risk Tags heben den Mindest-Rank an, senken ihn nie.
    /// </summary>
    /// <param name="facts">Änderungsfakten.</param>
    /// <param name="tags">Risk-Tag-Regeln.</param>
    /// <param name="changedPaths">Geänderte Pfade, relativ zur Projektwurzel.</param>
    /// <returns>Rank mit Begründungen.</returns>
    public static RankDecision Assess(ChangeFacts facts, IReadOnlyList<RiskTagRule> tags, IReadOnlyList<string> changedPaths)
    {
        var rank = Rank.E;
        var reasons = new List<string>();
        void Raise(Rank r, string why)
        {
            if (r > rank) { rank = r; reasons.Clear(); }
            if (r == rank) reasons.Add(why);
        }

        if (facts.DestructiveMigration) Raise(Rank.S, "destructive migration");
        if (facts.Secrets) Raise(Rank.S, "secrets");
        if (facts.Payment) Raise(Rank.S, "payment");
        if (facts.ProdDeploy) Raise(Rank.S, "production deploy");
        if (facts.BreakingChange) Raise(Rank.A, "breaking change");
        if (facts.DataMigration) Raise(Rank.A, "data migration");
        if (facts.AuthSecurityCode) Raise(Rank.A, "auth/security code");
        if (facts.Infrastructure) Raise(Rank.A, "infrastructure");
        if (facts.CrossService) Raise(Rank.B, "cross-service change");
        if (facts.PublicApiChange) Raise(Rank.B, "public API change");
        if (facts.AdditiveSchemaChange) Raise(Rank.B, "additive schema change");
        if (facts.ModuleCount > 1) Raise(Rank.C, "multiple modules");
        if (facts.NewDependency) Raise(Rank.C, "new dependency");

        if (rank < Rank.C)
        {
            if (facts.FileCount <= 1 && !facts.LogicChange) Raise(Rank.E, "at most one file, no logic change");
            else if (facts.FileCount <= 5 && facts.ModuleCount <= 1 && facts.TestsPresent) Raise(Rank.D, "at most 5 files in one module with tests");
            else Raise(Rank.C, "no lower criterion met (too many files or no tests)");
        }

        foreach (var tag in tags)
        {
            foreach (var path in changedPaths)
            {
                if (!MatchesAny(tag.Paths, path)) continue;
                Raise(tag.MinRank, $"risk tag '{tag.Tag}' (min {tag.MinRank}) matched {path}");
                break;
            }
        }

        return new RankDecision(rank, reasons);
    }

    /// <summary>Überschreibt den Rank (Guild Master); die Begründung ist Pflicht und wird in den Reasons festgehalten.</summary>
    /// <param name="decision">Bisherige Entscheidung.</param>
    /// <param name="rank">Neuer Rank.</param>
    /// <param name="reason">Nicht leere Begründung für das Ledger.</param>
    /// <returns>Neue Entscheidung.</returns>
    /// <exception cref="ArgumentException">Begründung leer.</exception>
    public static RankDecision Override(RankDecision decision, Rank rank, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rank override requires a reason.", nameof(reason));
        return new RankDecision(rank, [.. decision.Reasons, $"override {decision.Rank}->{rank}: {reason.Trim()}"]);
    }

    private static bool MatchesAny(IReadOnlyList<string> globs, string path)
    {
        foreach (var g in globs) if (GlobMatcher.IsMatch(g, path)) return true;
        return false;
    }
}

/// <summary>Governance je Rank: Merge-Regel und Warden-Pflicht.</summary>
public static class RankGovernance
{
    /// <summary>
    /// E bis A: Merge in den Integrations-Branch über <c>monarch gate merge</c> bei grüner CI und Warden-Freigabe (ADR-0007 löst die frühere
    /// Owner-Freigabe für B und A ab); S: jede Mutation einzeln. Merge nach main/master bleibt in jedem Rank dem Owner vorbehalten.
    /// </summary>
    public static MergeRule For(Rank rank) => rank switch
    {
        <= Rank.A => MergeRule.AutoMergeWhenCiGreenAndWardenApproved,
        _ => MergeRule.EachMutationNeedsApproval,
    };

    /// <summary>True ab Rank D.</summary>
    public static bool WardenRequired(Rank rank) => rank >= Rank.D;
}
