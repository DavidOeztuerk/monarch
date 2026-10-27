using System.Text.Json;

namespace Monarch.Application.Ports;

/// <summary>One event of the hash-chained ledger as stored on disk.</summary>
/// <param name="File">Ledger file relative to the project root, using <c>/</c> separators.</param>
/// <param name="Seq">1-based, gapless sequence number within the file.</param>
/// <param name="At">Event time (UTC).</param>
/// <param name="Actor">Originator of the event.</param>
/// <param name="Type">Event type discriminator, e.g. <c>QuestCleared</c>.</param>
/// <param name="Payload">Event-specific JSON payload.</param>
/// <param name="PrevHash">Hash of the previous line, or <c>genesis:&lt;sessionId&gt;</c> for the first.</param>
/// <param name="Hash">Lowercase hex SHA-256 of the line without its hash property.</param>
public sealed record LedgerEntry(string File, long Seq, DateTimeOffset At, string Actor, string Type, JsonElement Payload, string PrevHash, string Hash);

/// <summary>Port for the append-only ledger of events.</summary>
public interface ILedgerStore
{
    /// <summary>Appends one event to the ledger file of a session.</summary>
    /// <param name="sessionId">Session identifier; becomes the file name.</param>
    /// <param name="actor">Originator of the event.</param>
    /// <param name="type">Event type discriminator.</param>
    /// <param name="payload">Event-specific JSON payload.</param>
    /// <returns>The stored entry including sequence number and hashes.</returns>
    LedgerEntry Append(string sessionId, string actor, string type, JsonElement payload);

    /// <summary>Reads all entries of a session in order.</summary>
    /// <param name="sessionId">Session identifier.</param>
    /// <returns>The entries; empty when the session has no ledger file.</returns>
    IReadOnlyList<LedgerEntry> ReadSession(string sessionId);
}
