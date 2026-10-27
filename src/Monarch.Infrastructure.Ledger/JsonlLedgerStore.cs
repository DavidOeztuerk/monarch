using System.Text;
using System.Text.Json;
using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Hash-chained JSONL ledger store: one append-only file per session at
/// <c>&lt;root&gt;/.monarch/ledger/&lt;yyyy-MM&gt;/&lt;sessionId&gt;.&lt;writerId&gt;.jsonl</c>, where the writer id is a per-worktree random id. Appends are safe across processes
/// (exclusive file sharing with bounded retry) and update the head anchor
/// <c>&lt;root&gt;/.monarch/local/ledger-heads.json</c> atomically. See <see cref="LedgerCanonical"/> for the line form.
/// </summary>
/// <param name="options">Store options.</param>
/// <param name="clock">Time source.</param>
public sealed class JsonlLedgerStore(JsonlLedgerOptions options, IClock clock) : ILedgerStore
{
    private static readonly object Gate = new();

    /// <inheritdoc />
    public LedgerEntry Append(string sessionId, string actor, string type, JsonElement payload)
    {
        LedgerFiles.ValidateSessionId(sessionId);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrEmpty(type);
        lock (Gate)
        {
            var entry = AppendCore(sessionId, actor, type, payload, out var reset);
            if (reset is not null)
            {
                // The anchor was rebuilt: leave a loud trace in the ledger itself. It stays a finding until acknowledged.
                AppendCore(sessionId, "ledger", "LedgerAnchorReset", ResetPayload(reset, entry.At), out _);
            }

            return entry;
        }
    }

    private static JsonElement ResetPayload(AnchorReset reset, DateTimeOffset at)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("reason", reset.Reason);
            w.WriteString("at", at);
            if (reset.ArchivedTo is not null)
            {
                w.WriteString("archivedTo", reset.ArchivedTo);
            }

            w.WriteStartArray("reanchored");
            foreach (var f in reset.Reanchored)
            {
                w.WriteStringValue(f);
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return JsonDocument.Parse(ms.ToArray()).RootElement.Clone();
    }

    private LedgerEntry AppendCore(string sessionId, string actor, string type, JsonElement payload, out AnchorReset? reset)
    {
        var root = options.Root;
        var at = clock.UtcNow.ToUniversalTime();
        var hadWriterId = File.Exists(LedgerFiles.Full(root, LedgerFiles.WriterIdPath));
        var stem = $"{sessionId}.{LedgerFiles.WriterId(root)}";
        var relative = LedgerFiles.ResolveRelative(root, stem, at);
        if (File.Exists(LedgerFiles.Full(root, relative))
            && !LedgerAnchor.Read(root).Heads.Files.ContainsKey(relative)
            && GitAnchor.IsTracked(root, relative))
        {
            // A committed file this worktree has no anchor for belongs to history: never append, start a new file.
            stem = $"{sessionId}.{LedgerFiles.WriterId(root, regenerate: true)}";
            relative = LedgerFiles.ResolveRelative(root, stem, at);
        }

        var full = LedgerFiles.Full(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var fs = LedgerFiles.OpenExclusive(full);
        var (lastSeq, prevHash) = ReadTail(fs, stem);
        var seq = lastSeq + 1;
        var hashless = LedgerCanonical.Serialize(seq, at, actor, type, payload, prevHash);
        var hash = LedgerCanonical.ComputeHash(hashless);
        var bytes = Encoding.UTF8.GetBytes(LedgerCanonical.Seal(hashless, hash) + "\n");
        fs.Seek(0, SeekOrigin.End);
        fs.Write(bytes);
        fs.Flush();
        reset = LedgerHeadStore.Update(root, relative, seq, hash, at, hadWriterId);
        return new LedgerEntry(relative, seq, at, actor, type, payload.Clone(), prevHash, hash);
    }

    /// <inheritdoc />
    public IReadOnlyList<LedgerEntry> ReadSession(string sessionId)
    {
        LedgerFiles.ValidateSessionId(sessionId);
        var relative = LedgerFiles.ResolveRelative(options.Root, $"{sessionId}.{LedgerFiles.WriterId(options.Root)}", clock.UtcNow);
        return File.Exists(LedgerFiles.Full(options.Root, relative)) ? LedgerFiles.ReadEntries(options.Root, relative) : [];
    }

    private static (long Seq, string Hash) ReadTail(FileStream fs, string sessionId)
    {
        var length = fs.Length;
        if (length == 0)
        {
            return (0, LedgerCanonical.Genesis(sessionId));
        }

        var one = new byte[1];
        fs.Seek(length - 1, SeekOrigin.Begin);
        fs.ReadExactly(one);
        if (one[0] != (byte)'\n')
        {
            throw new InvalidDataException("Ledger file does not end with a newline (torn write or tampering); refusing to append.");
        }

        // Scan backwards in chunks for the newline that precedes the last line.
        var start = 0L;
        var chunk = new byte[4096];
        var pos = length - 1;
        var found = false;
        while (pos > 0 && !found)
        {
            var size = (int)Math.Min(chunk.Length, pos);
            fs.Seek(pos - size, SeekOrigin.Begin);
            fs.ReadExactly(chunk, 0, size);
            for (var i = size - 1; i >= 0; i--)
            {
                if (chunk[i] == (byte)'\n')
                {
                    start = pos - size + i + 1;
                    found = true;
                    break;
                }
            }

            pos -= size;
        }

        var lineBytes = new byte[length - 1 - start];
        fs.Seek(start, SeekOrigin.Begin);
        fs.ReadExactly(lineBytes);
        var line = Encoding.UTF8.GetString(lineBytes);
        if (!LedgerCanonical.TrySplit(line, out _, out var hash))
        {
            throw new InvalidDataException("Last ledger line is malformed; refusing to append.");
        }

        var parsed = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerLine)
            ?? throw new InvalidDataException("Last ledger line is malformed; refusing to append.");
        return (parsed.Seq, hash);
    }
}
