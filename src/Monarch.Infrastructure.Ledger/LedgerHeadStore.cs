using System.Globalization;
using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Why and how the anchor was rebuilt; becomes the payload of a <c>LedgerAnchorReset</c> event.</summary>
/// <param name="Reason">What was wrong with the anchor.</param>
/// <param name="ArchivedTo">File name the old anchor was moved to, or null when there was none.</param>
/// <param name="Reanchored">Ledger files that were re-anchored from their current tail.</param>
internal sealed record AnchorReset(string Reason, string? ArchivedTo, IReadOnlyList<string> Reanchored);

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

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(heads, LedgerJsonContext.Default.LedgerHeads));
        File.Move(temp, path, overwrite: true);
        return reset;
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
