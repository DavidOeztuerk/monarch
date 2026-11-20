namespace Monarch.Domain;

/// <summary>Unveränderlicher, prüfbarer Beleg. Digest = Hash des Quell-Artefakts (Check-Run, Report, Commit).</summary>
/// <param name="Kind">Art des Belegs.</param>
/// <param name="Source">Herkunft (URL, Run-Id, Commit).</param>
/// <param name="Digest">Hash des Quell-Artefakts.</param>
/// <param name="At">Zeitpunkt.</param>
public sealed record Evidence(EvidenceKind Kind, string Source, string Digest, DateTimeOffset At);

/// <summary>Messwerte eines Shadows, ausschließlich aus Ledger-Events projiziert.</summary>
/// <param name="Runs">Anzahl Läufe.</param>
/// <param name="FirstPassVerified">Davon beim ersten Versuch verifiziert.</param>
/// <param name="Reverts">Anzahl Reverts.</param>
/// <param name="AvgSessionPct">Durchschnittliche Session-Kosten in Prozentpunkten.</param>
public sealed record ShadowStats(int Runs, int FirstPassVerified, int Reverts, double AvgSessionPct)
{
    /// <summary>Anteil der Läufe, die im ersten Versuch verifiziert wurden.</summary>
    public double FirstPassRate => Runs == 0 ? 0 : (double)FirstPassVerified / Runs;
}

/// <summary>Pfad-Scope einer Quest als Include-/Exclude-Globs (relativ zur Projektwurzel, <c>/</c>-getrennt).</summary>
/// <param name="Include">Erlaubte Globs.</param>
/// <param name="Exclude">Ausgeschlossene Globs; gewinnen gegen Include.</param>
public sealed record QuestScope(IReadOnlyList<string> Include, IReadOnlyList<string> Exclude)
{
    /// <summary>Leerer Scope: nichts ist erlaubt.</summary>
    public static QuestScope Empty { get; } = new([], []);
}
