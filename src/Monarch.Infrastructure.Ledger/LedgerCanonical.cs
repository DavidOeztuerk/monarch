using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Canonical line form of the ledger. A line is the compact JSON object
/// <c>{"seq","at","actor","type","payload","prevHash","hash"}</c> with properties in exactly this order
/// (fixed by the source generator). The hash is the lowercase hex SHA-256 over the UTF-8 bytes of the line
/// serialized WITHOUT the <c>hash</c> property; because <c>hash</c> is the last property, the sealed line is the hashed
/// text with <c>,"hash":"&lt;hex&gt;"</c> inserted before the closing brace.
/// </summary>
public static class LedgerCanonical
{
    internal const string HashPrefix = ",\"hash\":\"";
    internal const int HexLength = 64;

    /// <summary>Gets the <c>prevHash</c> of the first event of a session.</summary>
    /// <param name="sessionId">The session id.</param>
    /// <returns><c>genesis:</c> followed by the session id.</returns>
    public static string Genesis(string sessionId) => "genesis:" + sessionId;

    /// <summary>Serializes a line without its hash property.</summary>
    /// <param name="seq">Sequence number.</param>
    /// <param name="at">Event time; normalized to UTC.</param>
    /// <param name="actor">Actor.</param>
    /// <param name="type">Type discriminator.</param>
    /// <param name="payload">Payload.</param>
    /// <param name="prevHash">Previous hash.</param>
    /// <returns>The hash input text.</returns>
    public static string Serialize(long seq, DateTimeOffset at, string actor, string type, JsonElement payload, string prevHash) =>
        JsonSerializer.Serialize(
            new LedgerLine { Seq = seq, At = at.ToUniversalTime(), Actor = actor, Type = type, Payload = payload, PrevHash = prevHash },
            LedgerJsonContext.Default.LedgerLine);

    /// <summary>Computes the lowercase hex SHA-256 of the UTF-8 bytes of a hash-less line.</summary>
    /// <param name="hashless">The text from <see cref="Serialize"/>.</param>
    /// <returns>The hash.</returns>
    public static string ComputeHash(string hashless) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(hashless)));

    /// <summary>Inserts the hash property into a hash-less line.</summary>
    /// <param name="hashless">The text from <see cref="Serialize"/>.</param>
    /// <param name="hash">The hash from <see cref="ComputeHash"/>.</param>
    /// <returns>The complete line without trailing newline.</returns>
    public static string Seal(string hashless, string hash) => string.Concat(hashless.AsSpan(0, hashless.Length - 1), HashPrefix, hash, "\"}");

    /// <summary>Splits a sealed line into hash-less text and hash.</summary>
    /// <param name="line">The sealed line without trailing newline.</param>
    /// <param name="hashless">The text that was hashed.</param>
    /// <param name="hash">The stored hash.</param>
    /// <returns><see langword="true"/> when the line ends with a well-formed hash property.</returns>
    public static bool TrySplit(string line, out string hashless, out string hash)
    {
        hashless = hash = string.Empty;
        var hashStart = line.Length - 2 - HexLength;
        if (hashStart < HashPrefix.Length || !line.EndsWith("\"}", StringComparison.Ordinal)
            || string.CompareOrdinal(line, hashStart - HashPrefix.Length, HashPrefix, 0, HashPrefix.Length) != 0)
        {
            return false;
        }

        hash = line.Substring(hashStart, HexLength);
        hashless = string.Concat(line.AsSpan(0, hashStart - HashPrefix.Length), "}");
        return true;
    }
}
