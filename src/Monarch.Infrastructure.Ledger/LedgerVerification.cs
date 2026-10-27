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
}

/// <summary>One finding of the ledger verification.</summary>
/// <param name="File">Ledger file relative to the root.</param>
/// <param name="Line">1-based line number, or 0 for file-level findings.</param>
/// <param name="Kind">Kind of finding.</param>
/// <param name="Detail">Human readable explanation.</param>
public sealed record LedgerViolation(string File, int Line, LedgerViolationKind Kind, string Detail);

/// <summary>Result of verifying all ledger files.</summary>
/// <param name="Violations">All findings; empty when the ledger is intact.</param>
public sealed record LedgerVerification(IReadOnlyList<LedgerViolation> Violations)
{
    /// <summary>Gets a value indicating whether no violation was found.</summary>
    public bool Ok => Violations.Count == 0;
}
