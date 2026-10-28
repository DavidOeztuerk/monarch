namespace Monarch.Infrastructure.Ledger;

/// <summary>Kinds of tamper evidence found by <see cref="LedgerVerifier"/>.</summary>
public enum LedgerViolationKind
{
    /// <summary>The stored hash does not match the line content.</summary>
    HashMismatch,
    /// <summary>The line does not chain to the previous line.</summary>
    PrevHashMismatch,
    /// <summary>The sequence number is not the expected next one.</summary>
    SeqGap,
    /// <summary>The line is not a well-formed ledger line.</summary>
    Unparseable,
    /// <summary>The file is shorter than anchored, missing, or ends mid-line.</summary>
    Truncated,
    /// <summary>A ledger file exists that the head anchor does not know.</summary>
    UnknownFile,
    /// <summary>The file head differs from the anchor, e.g. lines were appended or replaced behind the store's back.</summary>
    HeadMismatch,
    /// <summary>The head anchor, or one of its entries, is unreadable, malformed or has impossible values.</summary>
    AnchorInvalid,
    /// <summary>The anchor was reset (corrupt or missing) and the owner has not acknowledged it yet.</summary>
    AnchorReset,
    /// <summary>The same writer file exists in more than one month folder, i.e. the file generations are not unique.</summary>
    DuplicateGeneration,
}

/// <summary>One finding of the ledger verification.</summary>
/// <param name="File">Ledger file relative to the root.</param>
/// <param name="Line">1-based line number, or 0 for file-level findings.</param>
/// <param name="Kind">Kind of finding.</param>
/// <param name="Detail">Human readable explanation.</param>
public sealed record LedgerViolation(string File, int Line, LedgerViolationKind Kind, string Detail);

/// <summary>Result of verifying all ledger files.</summary>
/// <param name="Violations">All findings; empty when the ledger is intact.</param>
/// <param name="AnchoredLocally">Number of ledger files covered by the local head anchor.</param>
/// <param name="AnchoredByGit">Number of ledger files without a local anchor entry that are committed and unmodified in git.</param>
/// <param name="Resets">All <c>LedgerAnchorReset</c> events found, acknowledged or not.</param>
/// <param name="Restorable">Files that carry exactly their pre-reset head again; the next append anchors them and writes <c>LedgerAnchorRestored</c>.</param>
public sealed record LedgerVerification(IReadOnlyList<LedgerViolation> Violations, int AnchoredLocally = 0, int AnchoredByGit = 0, IReadOnlyList<LedgerReset>? Resets = null, IReadOnlyList<string>? Restorable = null)
{
    /// <summary>Gets a value indicating whether no violation was found.</summary>
    public bool Ok => Violations.Count == 0;
}

/// <summary>One <c>LedgerAnchorReset</c> event.</summary>
/// <param name="File">Ledger file containing the event.</param>
/// <param name="Line">1-based line of the event.</param>
/// <param name="Hash">Event hash; the argument of <c>ledger ack-reset</c>.</param>
/// <param name="Reason">Recorded reason.</param>
/// <param name="Acknowledged">Whether a <c>LedgerAnchorResetAcknowledged</c> event by actor <c>cli</c> refers to it.</param>
/// <param name="OldFile">File whose anchor lay ahead of its content when this reset was written, or null.</param>
/// <param name="OldSeq">Anchored sequence of <paramref name="OldFile"/> before the reset.</param>
/// <param name="OldHash">Anchored hash of <paramref name="OldFile"/> before the reset.</param>
public sealed record LedgerReset(string File, int Line, string Hash, string Reason, bool Acknowledged, string? OldFile = null, long OldSeq = 0, string? OldHash = null);
