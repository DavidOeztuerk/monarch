namespace Monarch.Domain;

/// <summary>Rank für Gates, Quests und Shadows. Steuert Risiko-Policy, Modellwahl und Autonomie.</summary>
public enum Rank
{
    /// <summary>Trivial: höchstens eine Datei, keine Logik.</summary>
    E,
    /// <summary>Klein: bis fünf Dateien, ein Modul, Tests vorhanden.</summary>
    D,
    /// <summary>Mehrere Module oder neue Dependency.</summary>
    C,
    /// <summary>Service-übergreifend, öffentliche API oder additive Schema-Änderung.</summary>
    B,
    /// <summary>Breaking Change, Datenmigration, Auth/Security oder Infrastruktur.</summary>
    A,
    /// <summary>Irreversibel oder Produktion; nie autonom.</summary>
    S,
}

/// <summary>Spezialformen eines Gates mit eigener Policy.</summary>
public enum GateKind
{
    /// <summary>Epic oder Milestone.</summary>
    Standard,
    /// <summary>Incident oder Hotfix.</summary>
    Red,
    /// <summary>Spike oder Experiment im eigenen Worktree.</summary>
    Instance,
    /// <summary>Framework- oder Architektur-Migration.</summary>
    JobChange,
}

/// <summary>Quest-Typen; bestimmen Priorität, XP-Basis und Sperrlogik.</summary>
public enum QuestKind
{
    /// <summary>Aus der Gate-Decomposition.</summary>
    Main,
    /// <summary>Refactoring oder Nice-to-have.</summary>
    Side,
    /// <summary>Tägliche Hygiene.</summary>
    Daily,
    /// <summary>System-Trigger; hebt die Sperre auf.</summary>
    Penalty,
    /// <summary>Aus einem Red Gate; höchste Priorität.</summary>
    Emergency,
    /// <summary>System-Discovery; nur Vorschlag.</summary>
    Hidden,
}

/// <summary>Lifecycle einer Quest. Übergänge erzwingt ausschließlich <see cref="Quest"/>.</summary>
public enum QuestState
{
    /// <summary>Vorgeschlagen.</summary>
    Proposed,
    /// <summary>Einem Shadow zugewiesen.</summary>
    Accepted,
    /// <summary>In Arbeit.</summary>
    InProgress,
    /// <summary>Zur Prüfung eingereicht.</summary>
    InReview,
    /// <summary>Durch unabhängige Evidence verifiziert.</summary>
    Verified,
    /// <summary>Gemerged und abgeschlossen.</summary>
    Cleared,
    /// <summary>Gescheitert (terminal).</summary>
    Failed,
    /// <summary>Aufgegeben (terminal).</summary>
    Abandoned,
}

/// <summary>Basisklassen der Shadows; extrahierte Shadows spezialisieren eine Klasse.</summary>
public enum ShadowClass
{
    /// <summary>Liest und sucht.</summary>
    Scout,
    /// <summary>Plant.</summary>
    Strategist,
    /// <summary>Implementiert.</summary>
    Knight,
    /// <summary>Überwacht.</summary>
    Sentinel,
    /// <summary>Prüft (read-only).</summary>
    Warden,
    /// <summary>Dokumentiert.</summary>
    Scribe,
    /// <summary>Repariert.</summary>
    Healer,
    /// <summary>Große Migrationen.</summary>
    Siege,
}

/// <summary>Modell-Tier, runtime-neutral. Der Adapter mappt z. B. Light→Haiku, Standard→Sonnet, Heavy→Opus.</summary>
public enum ModelTier
{
    /// <summary>Günstig und schnell.</summary>
    Light,
    /// <summary>Standard.</summary>
    Standard,
    /// <summary>Stark.</summary>
    Heavy,
    /// <summary>Stärkstes verfügbares Modell.</summary>
    Frontier,
}

/// <summary>Quelle eines Belegs. Nur diese Arten erzeugen Progression.</summary>
public enum EvidenceKind
{
    /// <summary>CI-Lauf.</summary>
    CiRun,
    /// <summary>Test-Report.</summary>
    TestReport,
    /// <summary>Urteil eines unabhängigen Verifiers.</summary>
    VerifierVerdict,
    /// <summary>Merge-Commit.</summary>
    MergeCommit,
    /// <summary>Revert.</summary>
    Revert,
    /// <summary>Menschliche Freigabe.</summary>
    HumanApproval,
}

/// <summary>Kennung eines Gates.</summary>
/// <param name="Value">Der Bezeichner, z. B. G-0031.</param>
public readonly record struct GateId(string Value);

/// <summary>Kennung einer Quest.</summary>
/// <param name="Value">Der Bezeichner, z. B. Q-0142.</param>
public readonly record struct QuestId(string Value);

/// <summary>Kennung eines Shadows.</summary>
/// <param name="Value">Der Bezeichner, z. B. knight-efcore@1.3.0.</param>
public readonly record struct ShadowId(string Value);

/// <summary>Domänenfehler, z. B. ungültiger Übergang oder fehlende Evidence.</summary>
/// <param name="message">Fehlerbeschreibung.</param>
public sealed class DomainException(string message) : Exception(message);
