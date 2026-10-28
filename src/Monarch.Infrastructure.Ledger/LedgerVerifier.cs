using System.Text;
using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Verifies the hash chain of every ledger file, compares the files with the git-tracked ledger files and with the head anchor
/// (<c>.monarch/local/ledger-heads.json</c>), which is the only way to notice truncation, deletion or a validly
/// re-chained forged tail. Note: a verification racing a concurrent append may report a transient head mismatch.
/// </summary>
public static class LedgerVerifier
{
    /// <summary>Verifies every ledger file below the project root against its chain and the head anchor.</summary>
    /// <param name="root">Project root.</param>
    /// <returns>The verification result.</returns>
    public static LedgerVerification Verify(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var violations = new List<LedgerViolation>();
        var scanned = new Dictionary<string, FileScan>(StringComparer.Ordinal);
        foreach (var relative in LedgerFiles.EnumerateRelative(root))
        {
            if (TryScanFile(root, relative, violations) is { } scan)
            {
                scanned[relative] = scan;
            }
        }

        var anchor = LedgerAnchor.Read(root);
        foreach (var problem in anchor.Problems)
        {
            violations.Add(new LedgerViolation(problem.File, 0, LedgerViolationKind.AnchorInvalid, problem.Detail));
        }

        var known = anchor.Heads.Files;
        var restorable = new List<string>();
        var allResets = scanned.SelectMany(s => s.Value.Resets.Select(r => r with { File = s.Key })).ToList();
        var anchorBroken = anchor.Problems.Any(p => p.File == LedgerFiles.HeadsPath);
        var invalidEntries = anchor.Problems.Select(p => p.File).ToHashSet(StringComparer.Ordinal);
        var byGit = 0;
        foreach (var file in scanned.Keys)
        {
            if (known.ContainsKey(file) || anchorBroken || invalidEntries.Contains(file))
            {
                continue;
            }

            if (IsRestored(file, scanned[file], allResets))
            {
                restorable.Add(file);
                continue;
            }

            if (GitAnchor.IsAnchoredByGit(root, file))
            {
                byGit++;
            }
            else
            {
                violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.UnknownFile, "Ledger file is not covered by the head anchor."));
            }
        }

        foreach (var (file, head) in known.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!scanned.TryGetValue(file, out var scan))
            {
                if (!File.Exists(LedgerFiles.Full(root, file)))
                {
                    violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.Truncated, "Ledger file is missing but anchored."));
                }
            }
            else if (scan.Hashes.Count < head.Seq)
            {
                violations.Add(new LedgerViolation(file, scan.Hashes.Count, LedgerViolationKind.Truncated, $"File has {scan.Hashes.Count} complete lines, anchor expects {head.Seq}."));
            }
            else if (scan.Hashes[(int)head.Seq - 1] != head.Hash)
            {
                violations.Add(new LedgerViolation(file, (int)head.Seq, LedgerViolationKind.HeadMismatch, "Hash at the anchored sequence differs from the anchor."));
            }
            else if (scan.Hashes.Count > head.Seq && IsRestored(file, scan, allResets))
            {
                // N2: the content that a stale-anchor reset moved away from is back; the next append anchors it and writes LedgerAnchorRestored.
                restorable.Add(file);
            }
            else if (scan.Hashes.Count > head.Seq)
            {
                violations.Add(new LedgerViolation(file, (int)head.Seq + 1, LedgerViolationKind.HeadMismatch, "Lines exist beyond the anchored head."));
            }
        }

        CheckAgainstGit(root, scanned, violations);
        CheckGenerations(scanned.Keys, violations);
        var resets = CollectResets(scanned, violations);
        return new LedgerVerification(violations, known.Keys.Count(scanned.ContainsKey), byGit, resets, restorable);
    }

    /// <summary>
    /// N2: whether <paramref name="file"/> holds exactly the head that a recorded <c>LedgerAnchorReset</c> moved away from (same sequence, same hash,
    /// intact chain, no lines beyond it). Anything else stays a finding.
    /// </summary>
    private static bool IsRestored(string file, FileScan scan, IEnumerable<LedgerReset> resets) =>
        scan.Clean && resets.Any(r => r.OldFile == file && r.OldSeq >= 1 && scan.Hashes.Count == r.OldSeq && scan.Hashes[(int)r.OldSeq - 1] == r.OldHash);

    /// <summary>The hash of the reset event that makes <paramref name="file"/> restorable on <paramref name="old"/>, or null.</summary>
    internal static string? FindRestore(string root, string file, LedgerHead old)
    {
        var full = LedgerFiles.Full(root, file);
        try
        {
            if (!File.Exists(full) || LedgerFiles.ReadAllShared(full).Count(b => b == (byte)'\n') != old.Seq)
            {
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var throwaway = new List<LedgerViolation>();
        var scans = new Dictionary<string, FileScan>(StringComparer.Ordinal);
        foreach (var relative in LedgerFiles.EnumerateRelative(root))
        {
            if (TryScanFile(root, relative, throwaway) is { } scan)
            {
                scans[relative] = scan;
            }
        }

        if (!scans.TryGetValue(file, out var target))
        {
            return null;
        }

        var resets = scans.SelectMany(s => s.Value.Resets.Select(r => r with { File = s.Key })).ToList();
        return IsRestored(file, target, resets.Where(r => r.OldSeq == old.Seq && r.OldHash == old.Hash))
            ? resets.First(r => r.OldFile == file && r.OldSeq == old.Seq && r.OldHash == old.Hash).Hash
            : null;
    }

    /// <summary>
    /// Second, independent source: the ledger files tracked by git. A tracked file must exist and must extend its
    /// <c>HEAD</c> blob byte for byte (append-only); fewer lines or a different prefix is tampering even when the local
    /// anchor was rewritten consistently. Silently skipped when git is unavailable.
    /// </summary>
    private static void CheckAgainstGit(string root, Dictionary<string, FileScan> scanned, List<LedgerViolation> violations)
    {
        if (GitAnchor.TrackedLedgerFiles(root) is not { } tracked)
        {
            return;
        }

        var changed = GitAnchor.ChangedAgainstHead(root) ?? [];
        foreach (var file in tracked.Where(f => f.EndsWith(".jsonl", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            if (!File.Exists(LedgerFiles.Full(root, file)))
            {
                violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.Truncated, "Ledger file is tracked by git but missing."));
                continue;
            }

            if (!changed.Contains(file) || !scanned.ContainsKey(file) || GitAnchor.HeadBlob(root, file) is not { } blob)
            {
                continue;
            }

            byte[] current;
            try
            {
                current = LedgerFiles.ReadAllShared(LedgerFiles.Full(root, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (current.AsSpan().StartsWith(blob))
            {
                continue;
            }

            var (have, expected) = (CountLines(current), CountLines(blob));
            violations.Add(have < expected
                ? new LedgerViolation(file, have, LedgerViolationKind.Truncated, $"File has {have} lines, the HEAD version has {expected}.")
                : new LedgerViolation(file, 0, LedgerViolationKind.HeadMismatch, "Content is not an append-only extension of the HEAD version."));
        }
    }

    private static int CountLines(byte[] data) => data.Count(b => b == (byte)'\n');

    /// <summary>A writer file name must be unique across month folders; the file name is the only generation marker in the data.</summary>
    private static void CheckGenerations(IEnumerable<string> files, List<LedgerViolation> violations)
    {
        foreach (var group in files.GroupBy(f => Path.GetFileName(f), StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            foreach (var file in group.Order(StringComparer.Ordinal))
            {
                violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.DuplicateGeneration, $"Writer file '{group.Key}' exists in more than one month folder."));
            }
        }
    }

    private static List<LedgerReset> CollectResets(Dictionary<string, FileScan> scanned, List<LedgerViolation> violations)
    {
        var acked = scanned.Values.SelectMany(s => s.Acks).ToHashSet(StringComparer.Ordinal);
        var resets = new List<LedgerReset>();
        foreach (var (file, scan) in scanned.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            foreach (var r in scan.Resets)
            {
                var reset = r with { File = file, Acknowledged = acked.Contains(r.Hash) };
                resets.Add(reset);
                if (!reset.Acknowledged)
                {
                    violations.Add(new LedgerViolation(file, reset.Line, LedgerViolationKind.AnchorReset,
                        $"Ledger anchor was reset ({reset.Reason}); not acknowledged. Event {reset.Hash}. Owner: im Terminal `monarch ledger ack-reset {reset.Hash}` ausführen."));
                }
            }
        }

        return resets;
    }

    private static FileScan? TryScanFile(string root, string relative, List<LedgerViolation> violations)
    {
        try
        {
            return ScanFile(root, relative, violations);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            violations.Add(new LedgerViolation(relative, 0, LedgerViolationKind.Unparseable, "Ledger file is unreadable: " + ex.GetType().Name + "."));
            return null;
        }
    }

    private static FileScan ScanFile(string root, string relative, List<LedgerViolation> violations)
    {
        var session = Path.GetFileNameWithoutExtension(relative);
        var violationsBefore = violations.Count;
        var text = Encoding.UTF8.GetString(LedgerFiles.ReadAllShared(LedgerFiles.Full(root, relative)));
        var lines = text.Split('\n');
        var complete = lines.Length - 1;
        var hashes = new List<string>(complete);
        var resets = new List<LedgerReset>();
        var acks = new List<string>();
        if (lines[^1].Length > 0)
        {
            violations.Add(new LedgerViolation(relative, lines.Length, LedgerViolationKind.Truncated, "File does not end with a newline."));
        }

        string? previous = LedgerCanonical.Genesis(session);
        for (var i = 1; i <= complete; i++)
        {
            var line = lines[i - 1];
            if (!LedgerCanonical.TrySplit(line, out var hashless, out var hash))
            {
                violations.Add(new LedgerViolation(relative, i, LedgerViolationKind.Unparseable, "Line does not end with a well-formed hash property."));
                hashes.Add(string.Empty);
                previous = null;
                continue;
            }

            hashes.Add(hash);
            if (LedgerCanonical.ComputeHash(hashless) != hash)
            {
                violations.Add(new LedgerViolation(relative, i, LedgerViolationKind.HashMismatch, "Stored hash does not match the line content."));
            }

            LedgerLine? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerLine);
            }
            catch (JsonException)
            {
            }

            if (parsed is null)
            {
                violations.Add(new LedgerViolation(relative, i, LedgerViolationKind.Unparseable, "Line is not valid ledger JSON."));
                previous = hash;
                continue;
            }

            if (parsed.Type == "LedgerAnchorReset")
            {
                var (oldFile, oldSeq, oldHash) = OldHead(parsed.Payload);
                resets.Add(new LedgerReset(relative, i, hash, StringProperty(parsed.Payload, "reason") ?? "no reason recorded", false, oldFile, oldSeq, oldHash));
            }
            else if (parsed.Type == "LedgerAnchorResetAcknowledged" && parsed.Actor == "cli" && StringProperty(parsed.Payload, "reset") is { } target)
            {
                acks.Add(target);
            }

            if (parsed.Seq != i)
            {
                violations.Add(new LedgerViolation(relative, i, LedgerViolationKind.SeqGap, $"Expected seq {i}, found {parsed.Seq}."));
            }

            if (previous is not null && parsed.PrevHash != previous)
            {
                violations.Add(new LedgerViolation(relative, i, LedgerViolationKind.PrevHashMismatch, "prevHash does not match the previous line."));
            }

            previous = hash;
        }

        return new FileScan(hashes, resets, acks, violations.Count == violationsBefore);
    }

    private static string? StringProperty(JsonElement payload, string name) =>
        payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static (string? File, long Seq, string? Hash) OldHead(JsonElement payload)
    {
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("oldHead", out var o) && o.ValueKind == JsonValueKind.Object
            && StringProperty(o, "file") is { } file && StringProperty(o, "hash") is { } hash
            && o.TryGetProperty("seq", out var seq) && seq.ValueKind == JsonValueKind.Number && seq.TryGetInt64(out var n))
        {
            return (file, n, hash);
        }

        return (null, 0, null);
    }

    private sealed record FileScan(List<string> Hashes, List<LedgerReset> Resets, List<string> Acks, bool Clean);
}
