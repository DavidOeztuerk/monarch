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
            RestoreReturnedFiles(sessionId);
            var entry = AppendCore(sessionId, actor, type, payload, out var reset);
            if (reset is not null)
            {
                // The anchor was rebuilt: leave a loud trace in the ledger itself. It stays a finding until acknowledged.
                AppendCore(sessionId, "ledger", "LedgerAnchorReset", ResetPayload(reset, entry.At), out _);
            }

            return entry;
        }
    }

    /// <summary>
    /// N2: a file that a stale-anchor reset moved away from may come back (git stash pop, branch round trip). If it carries exactly its old head,
    /// with an intact chain and a recorded reset, its anchor goes back to that head and a visible <c>LedgerAnchorRestored</c> event is written.
    /// The reset itself stays a finding until the owner acknowledges it.
    /// </summary>
    private void RestoreReturnedFiles(string sessionId)
    {
        var pending = LedgerAnchor.Read(options.Root).Heads.Restore;
        foreach (var (file, old) in pending.OrderBy(p => p.Key, StringComparer.Ordinal).ToList())
        {
            if (LedgerVerifier.FindRestore(options.Root, file, old) is not { } resetHash)
            {
                continue;
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartObject();
                w.WriteString("file", file);
                w.WriteNumber("seq", old.Seq);
                w.WriteString("hash", old.Hash);
                w.WriteString("reset", resetHash);
                w.WriteEndObject();
            }

            AppendCore(sessionId, "ledger", "LedgerAnchorRestored", JsonDocument.Parse(ms.ToArray()).RootElement.Clone(), out _);
            LedgerHeadStore.ApplyRestore(options.Root, file, old);
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
            if (reset.OldFile is not null && reset.OldHead is not null)
            {
                w.WriteStartObject("oldHead");
                w.WriteString("file", reset.OldFile);
                w.WriteNumber("seq", reset.OldHead.Seq);
                w.WriteString("hash", reset.OldHead.Hash);
                w.WriteEndObject();
            }

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
        var fs = LedgerFiles.OpenExclusive(full);
        try
        {
            var (lastSeq, prevHash) = ReadTail(fs, stem);
            string? staleRelative = null;
            LedgerHead? staleTail = null;
            if (LedgerAnchor.Read(root).Heads.Files.TryGetValue(relative, out var anchored)
                && (lastSeq < anchored.Seq || (lastSeq == anchored.Seq && prevHash != anchored.Hash)))
            {
                // The anchor is ahead of the file (e.g. a git stash took the uncommitted tail): never continue it, never advance the anchor.
                staleRelative = relative;
                staleTail = lastSeq >= 1 ? new LedgerHead { Seq = lastSeq, Hash = prevHash } : null;
                fs.Dispose();
                stem = $"{sessionId}.{LedgerFiles.WriterId(root, regenerate: true)}";
                relative = LedgerFiles.ResolveRelative(root, stem, at);
                full = LedgerFiles.Full(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                fs = LedgerFiles.OpenExclusive(full);
                (lastSeq, prevHash) = ReadTail(fs, stem);
            }

            var seq = lastSeq + 1;
            var hashless = LedgerCanonical.Serialize(seq, at, actor, type, payload, prevHash);
            var hash = LedgerCanonical.ComputeHash(hashless);
            var bytes = Encoding.UTF8.GetBytes(LedgerCanonical.Seal(hashless, hash) + "\n");
            fs.Seek(0, SeekOrigin.End);
            fs.Write(bytes);
            fs.Flush();
            reset = staleRelative is null
                ? LedgerHeadStore.Update(root, relative, seq, hash, at, hadWriterId)
                : LedgerHeadStore.RebaseAheadOfFile(root, staleRelative, staleTail, relative, seq, hash);
            return new LedgerEntry(relative, seq, at, actor, type, payload.Clone(), prevHash, hash);
        }
        finally
        {
            fs.Dispose();
        }
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
