namespace Monarch.Domain;

/// <summary>Basis aller Ledger-Events. Hash-Kette pro Ledger-Datei macht Manipulation erkennbar.</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgänger-Events.</param>
public abstract record LedgerEvent(long Seq, DateTimeOffset At, string Actor, string PrevHash);

/// <summary>Eine Quest wurde einem Shadow zugewiesen.</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgängers.</param>
/// <param name="Quest">Quest.</param>
/// <param name="Shadow">Zugewiesener Shadow.</param>
public sealed record QuestAccepted(long Seq, DateTimeOffset At, string Actor, string PrevHash, QuestId Quest, ShadowId Shadow)
    : LedgerEvent(Seq, At, Actor, PrevHash);

/// <summary>Eine Quest wurde gecleart; hier wird XP gebucht.</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgängers.</param>
/// <param name="Quest">Quest.</param>
/// <param name="Xp">Gebuchte XP.</param>
/// <param name="Evidence">Belege.</param>
public sealed record QuestCleared(long Seq, DateTimeOffset At, string Actor, string PrevHash, QuestId Quest, int Xp, Evidence[] Evidence)
    : LedgerEvent(Seq, At, Actor, PrevHash);

/// <summary>Aus einer Quest wurde ein Shadow extrahiert (Arise).</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgängers.</param>
/// <param name="Shadow">Neuer Shadow.</param>
/// <param name="Source">Quelle-Quest.</param>
/// <param name="DefinitionDigest">Hash der Shadow-Definition.</param>
public sealed record ShadowExtracted(long Seq, DateTimeOffset At, string Actor, string PrevHash, ShadowId Shadow, QuestId Source, string DefinitionDigest)
    : LedgerEvent(Seq, At, Actor, PrevHash);

/// <summary>Mana-Momentaufnahme, u. a. vor und nach jedem Recap.</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgängers.</param>
/// <param name="Pool">Gemessene Budgets.</param>
public sealed record ManaSnapshotTaken(long Seq, DateTimeOffset At, string Actor, string PrevHash, ManaPool Pool)
    : LedgerEvent(Seq, At, Actor, PrevHash);

/// <summary>Eine Policy wurde verletzt oder hat einen Aufruf verweigert.</summary>
/// <param name="Seq">Laufende Nummer.</param>
/// <param name="At">Zeitpunkt.</param>
/// <param name="Actor">Urheber.</param>
/// <param name="PrevHash">Hash des Vorgängers.</param>
/// <param name="Rule">Name der Regel.</param>
/// <param name="Detail">Details.</param>
public sealed record PolicyViolated(long Seq, DateTimeOffset At, string Actor, string PrevHash, string Rule, string Detail)
    : LedgerEvent(Seq, At, Actor, PrevHash);
