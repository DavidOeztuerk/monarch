using System.Globalization;
using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Why and how the anchor was rebuilt; becomes the payload of a <c>LedgerAnchorReset</c> event.</summary>
/// <param name="Reason">What was wrong with the anchor.</param>
/// <param name="ArchivedTo">File name the old anchor was moved to, or null when there was none.</param>
/// <param name="Reanchored">Ledger files that were re-anchored from their current tail.</param>
/// <param name="OldFile">File whose anchor lay ahead of its content, or null.</param>
/// <param name="OldHead">The anchored head of <paramref name="OldFile"/> before the reset; the file is accepted again if exactly this content returns (N2).</param>
internal sealed record AnchorReset(string Reason, string? ArchivedTo, IReadOnlyList<string> Reanchored, string? OldFile = null, LedgerHead? OldHead = null);

/// <summary>Maintains <c>ledger-heads.json</c>, the tamper anchor for truncation and deletion. Never rebuilds it silently.</summary>
internal static class LedgerHeadStore
{
    /// <summary>
    /// Records the new head of <paramref name="relative"/>. A corrupt anchor, or a missing one although ledger history
    /// exists, is archived as <c>ledger-heads.json.corrupt-&lt;time&gt;</c>, rebuilt, and reported to the caller.
    /// </summary>
    public static AnchorReset? Update(string root, string relative, long seq, string hash, DateTimeOffset at, bool hadWriterId)
    {
        var path = LedgerFiles.Full(root, LedgerFiles.HeadsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var guard = LedgerFiles.OpenExclusive(path + ".lock");
        var state = LedgerAnchor.Read(root);
        AnchorReset? reset = null;
        var heads = state.Heads;
        if (state.Status == AnchorStatus.Invalid
            || (state.Status == AnchorStatus.Missing && hadWriterId && (seq > 1 || LedgerFiles.EnumerateRelative(root).Any(f => f != relative))))
        {
            var reason = state.Status == AnchorStatus.Missing ? "Head anchor is missing although ledger history exists." : state.Describe();
            var archived = Archive(path, at);
            heads = new LedgerHeads();
            var reanchored = new List<string>();
            foreach (var other in LedgerFiles.EnumerateRelative(root).Where(f => f != relative))
            {
                if (TryTail(root, other) is { } tail)
                {
                    heads.Files[other] = tail;
                    reanchored.Add(other);
                }
            }

            reset = new AnchorReset(reason, archived, reanchored);
            heads.Files[relative] = new LedgerHead { Seq = seq, Hash = hash };
        }
        else if (!heads.Files.TryGetValue(relative, out var current) || current.Seq < seq)
        {
            heads.Files[relative] = new LedgerHead { Seq = seq, Hash = hash };
        }

        Write(path, heads);
        return reset;
    }

    /// <summary>
    /// The anchor of <paramref name="staleRelative"/> lies ahead of the file (fewer lines, or another hash at the anchored sequence).
    /// The stale file is re-anchored to its current tail (or its entry dropped when empty), the first line of the new generation
    /// <paramref name="relative"/> is anchored, and the caller gets a reset that stays a finding until the owner acknowledges it.
    /// </summary>
    public static AnchorReset RebaseAheadOfFile(string root, string staleRelative, LedgerHead? staleTail, string relative, long seq, string hash)
    {
        var path = LedgerFiles.Full(root, LedgerFiles.HeadsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var guard = LedgerFiles.OpenExclusive(path + ".lock");
        var heads = LedgerAnchor.Read(root).Heads;
        LedgerHead? oldHead = heads.Files.TryGetValue(staleRelative, out var previous) ? new LedgerHead { Seq = previous.Seq, Hash = previous.Hash } : null;
        if (oldHead is not null)
        {
            heads.Restore[staleRelative] = oldHead;
        }

        if (staleTail is null)
        {
            heads.Files.Remove(staleRelative);
        }
        else
        {
            heads.Files[staleRelative] = staleTail;
        }

        heads.Files[relative] = new LedgerHead { Seq = seq, Hash = hash };
        Write(path, heads);
        return new AnchorReset($"Anchor ahead of file: {staleRelative} is shorter than, or differs from, its anchored head; a new writer generation was started.", null, [staleRelative], oldHead is null ? null : staleRelative, oldHead);
    }

    /// <summary>The original content of <paramref name="relative"/> is back: anchors it on <paramref name="old"/> again and forgets the pending restore.</summary>
    public static void ApplyRestore(string root, string relative, LedgerHead old)
    {
        var path = LedgerFiles.Full(root, LedgerFiles.HeadsPath);
        using var guard = LedgerFiles.OpenExclusive(path + ".lock");
        var heads = LedgerAnchor.Read(root).Heads;
        heads.Files[relative] = new LedgerHead { Seq = old.Seq, Hash = old.Hash };
        heads.Restore.Remove(relative);
        Write(path, heads);
    }

    private static void Write(string path, LedgerHeads heads)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(heads, LedgerJsonContext.Default.LedgerHeads));
        File.Move(temp, path, overwrite: true);
    }

    private static string? Archive(string path, DateTimeOffset at)
    {
        var isDir = Directory.Exists(path);
        if (!isDir && !File.Exists(path))
        {
            return null;
        }

        var target = path + ".corrupt-" + at.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        if (File.Exists(target) || Directory.Exists(target))
        {
            target += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        if (isDir)
        {
            Directory.Move(path, target);
        }
        else
        {
            File.Move(path, target);
        }

        return Path.GetFileName(target);
    }

    private static LedgerHead? TryTail(string root, string relative)
    {
        try
        {
            var entries = LedgerFiles.ReadEntries(root, relative);
            return entries.Count == 0 ? null : new LedgerHead { Seq = entries[^1].Seq, Hash = entries[^1].Hash };
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
