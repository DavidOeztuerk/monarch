using Monarch.Domain;

namespace Monarch.Policy;

/// <summary>Fakten über eine Änderung, aus denen der Rank abgeleitet wird.</summary>
/// <param name="FileCount">Anzahl geänderter Dateien.</param>
/// <param name="ModuleCount">Anzahl berührter Module.</param>
/// <param name="LogicChange">True bei Logikänderung (nicht nur Docs/Config).</param>
/// <param name="TestsPresent">True, wenn Tests für die Änderung vorhanden sind.</param>
/// <param name="NewDependency">Neue Dependency.</param>
/// <param name="CrossService">Service-übergreifend.</param>
/// <param name="PublicApiChange">Öffentliche API betroffen.</param>
/// <param name="AdditiveSchemaChange">Additive Schema-Änderung.</param>
/// <param name="BreakingChange">Breaking Change.</param>
/// <param name="DataMigration">Datenmigration.</param>
/// <param name="AuthSecurityCode">Auth- oder Security-Code.</param>
/// <param name="Infrastructure">Infrastruktur.</param>
/// <param name="DestructiveMigration">Destruktive Migration.</param>
/// <param name="Secrets">Secrets betroffen.</param>
/// <param name="Payment">Payment betroffen.</param>
/// <param name="ProdDeploy">Produktions-Deploy.</param>
public sealed record ChangeFacts(
    int FileCount, int ModuleCount = 1, bool LogicChange = true, bool TestsPresent = false,
    bool NewDependency = false, bool CrossService = false, bool PublicApiChange = false, bool AdditiveSchemaChange = false,
    bool BreakingChange = false, bool DataMigration = false, bool AuthSecurityCode = false, bool Infrastructure = false,
    bool DestructiveMigration = false, bool Secrets = false, bool Payment = false, bool ProdDeploy = false);

/// <summary>Risk Tag: Pfad-Globs, die einen Mindest-Rank setzen.</summary>
/// <param name="Tag">Name, z. B. auth.</param>
/// <param name="MinRank">Mindest-Rank bei Treffer.</param>
/// <param name="Paths">Globs relativ zur Projektwurzel.</param>
public sealed record RiskTagRule(string Tag, Rank MinRank, IReadOnlyList<string> Paths);

/// <summary>Ergebnis der Rank-Bewertung.</summary>
/// <param name="Rank">Ermittelter Rank.</param>
/// <param name="Reasons">Begründungen, die den Rank bestimmt haben.</param>
public sealed record RankDecision(Rank Rank, IReadOnlyList<string> Reasons);

/// <summary>Merge-Regel je Rank.</summary>
public enum MergeRule
{
    /// <summary>Merge in den Integrations-Branch bei grüner CI und Warden-Freigabe (ADR-0007).</summary>
    AutoMergeWhenCiGreenAndWardenApproved,
    /// <summary>Owner-Freigabe nötig (vor ADR-0007 für B und A; heute von keinem Rank genutzt).</summary>
    OwnerApprovalRequired,
    /// <summary>Jede mutierende Aktion einzeln freigeben.</summary>
    EachMutationNeedsApproval,
}
