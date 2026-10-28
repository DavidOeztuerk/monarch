namespace Monarch.Domain;

/// <summary>Aggregate Root für eine Quest. Erzwingt den Lifecycle und die Evidence-Pflicht.</summary>
public sealed class Quest
{
    private readonly List<Evidence> _evidence = [];

    /// <summary>Kennung der Quest.</summary>
    public QuestId Id { get; }

    /// <summary>Gate, zu dem die Quest gehört.</summary>
    public GateId Gate { get; }

    /// <summary>Quest-Typ.</summary>
    public QuestKind Kind { get; }

    /// <summary>Rank der Quest.</summary>
    public Rank Rank { get; }

    /// <summary>Aktueller Lifecycle-Zustand.</summary>
    public QuestState State { get; private set; } = QuestState.Proposed;

    /// <summary>Zugewiesener Shadow, sobald akzeptiert.</summary>
    public ShadowId? Assignee { get; private set; }

    /// <summary>Aktuelle Mana-Schätzung; wird durch <see cref="Replan"/> ersetzt.</summary>
    public ManaCost Estimate { get; private set; }

    /// <summary>Anzahl der Replans (Double Dungeon).</summary>
    public int Replans { get; private set; }

    /// <summary>Pfad-Scope der Quest.</summary>
    public QuestScope Scope { get; }

    /// <summary>Definition of Done.</summary>
    public IReadOnlyList<string> DefinitionOfDone { get; }

    /// <summary>Bisher angenommene Belege.</summary>
    public IReadOnlyList<Evidence> Evidence => _evidence;

    /// <summary>Erzeugt eine Quest im Zustand <see cref="QuestState.Proposed"/>.</summary>
    /// <param name="id">Kennung.</param>
    /// <param name="gate">Gate.</param>
    /// <param name="kind">Typ.</param>
    /// <param name="rank">Rank.</param>
    /// <param name="estimate">Anfangsschätzung.</param>
    /// <param name="dod">Definition of Done.</param>
    /// <param name="scope">Pfad-Scope; ohne Angabe leer.</param>
    public Quest(QuestId id, GateId gate, QuestKind kind, Rank rank, ManaCost estimate, IReadOnlyList<string> dod, QuestScope? scope = null)
        => (Id, Gate, Kind, Rank, Estimate, DefinitionOfDone, Scope) = (id, gate, kind, rank, estimate, dod, scope ?? QuestScope.Empty);

    /// <summary>Weist einen Shadow zu (Proposed zu Accepted).</summary>
    /// <param name="shadow">Der Shadow.</param>
    public void Accept(ShadowId shadow) { Guard(QuestState.Proposed); Assignee = shadow; State = QuestState.Accepted; }

    /// <summary>Beginnt die Arbeit (Accepted zu InProgress).</summary>
    public void Start() { Guard(QuestState.Accepted); State = QuestState.InProgress; }

    /// <summary>Reicht zur Prüfung ein (InProgress zu InReview).</summary>
    public void Submit() { Guard(QuestState.InProgress); State = QuestState.InReview; }

    /// <summary>Verifiziert mit unabhängiger Evidence (InReview zu Verified).</summary>
    /// <param name="evidence">CiRun, TestReport oder VerifierVerdict.</param>
    public void Verify(Evidence evidence)
    {
        Guard(QuestState.InReview);
        if (evidence.Kind is not (EvidenceKind.CiRun or EvidenceKind.VerifierVerdict or EvidenceKind.TestReport))
            throw new DomainException("Verification requires independent evidence.");
        Validate(evidence);
        _evidence.Add(evidence);
        State = QuestState.Verified;
    }

    /// <summary>Cleart nach Merge (Verified zu Cleared).</summary>
    /// <param name="merge">Evidence vom Typ MergeCommit.</param>
    public void Clear(Evidence merge)
    {
        Guard(QuestState.Verified);
        if (merge.Kind != EvidenceKind.MergeCommit) throw new DomainException("Clear requires merge evidence.");
        Validate(merge);
        _evidence.Add(merge);
        State = QuestState.Cleared;
    }

    /// <summary>Weist die Einreichung zurück (InReview zu InProgress).</summary>
    public void Reject() { Guard(QuestState.InReview); State = QuestState.InProgress; }

    /// <summary>Scheitert (aus Accepted oder InProgress, terminal).</summary>
    public void Fail() => Terminate(QuestState.Failed);

    /// <summary>Gibt auf (aus Accepted oder InProgress, terminal).</summary>
    public void Abandon() => Terminate(QuestState.Abandoned);

    /// <summary>Double Dungeon: ersetzt die Schätzung und geht zurück nach Accepted (InProgress zu Accepted).</summary>
    /// <param name="newEstimate">Neue Schätzung.</param>
    public void Replan(ManaCost newEstimate)
    {
        Guard(QuestState.InProgress);
        Estimate = newEstimate;
        Replans++;
        State = QuestState.Accepted;
    }

    private void Terminate(QuestState target)
    {
        if (State is not (QuestState.Accepted or QuestState.InProgress))
            throw new DomainException($"Invalid transition from {State} to {target}.");
        State = target;
    }

    private static void Validate(Evidence e)
    {
        if (string.IsNullOrWhiteSpace(e.Source) || string.IsNullOrWhiteSpace(e.Digest))
            throw new DomainException("Evidence requires a non-empty source and digest.");
    }

    private void Guard(QuestState expected)
    {
        if (State != expected) throw new DomainException($"Invalid transition from {State}, expected {expected}.");
    }
}
