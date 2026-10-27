using System.Text;
using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Verifies the hash chain of every ledger file and compares the files with the head anchor
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
            scanned[relative] = ScanFile(root, relative, violations);
        }

        LedgerHeads? heads = null;
        try
        {
            heads = LedgerFiles.ReadHeads(root);
        }
        catch (JsonException)
        {
            violations.Add(new LedgerViolation(LedgerFiles.HeadsPath, 0, LedgerViolationKind.Unparseable, "Head anchor is not valid JSON."));
        }

        var known = heads?.Files ?? [];
        var headsBroken = violations.Any(v => v.File == LedgerFiles.HeadsPath);
        foreach (var file in scanned.Keys)
        {
            if (!known.ContainsKey(file) && !headsBroken)
            {
                violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.UnknownFile, "Ledger file is not covered by the head anchor."));
            }
        }

        foreach (var (file, head) in known.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            if (!scanned.TryGetValue(file, out var scan))
            {
                violations.Add(new LedgerViolation(file, 0, LedgerViolationKind.Truncated, "Ledger file is missing but anchored."));
            }
            else if (scan.Hashes.Count < head.Seq)
            {
                violations.Add(new LedgerViolation(file, scan.Hashes.Count, LedgerViolationKind.Truncated, $"File has {scan.Hashes.Count} complete lines, anchor expects {head.Seq}."));
            }
            else if (scan.Hashes[(int)head.Seq - 1] != head.Hash)
            {
                violations.Add(new LedgerViolation(file, (int)head.Seq, LedgerViolationKind.HeadMismatch, "Hash at the anchored sequence differs from the anchor."));
            }
            else if (scan.Hashes.Count > head.Seq)
            {
                violations.Add(new LedgerViolation(file, (int)head.Seq + 1, LedgerViolationKind.HeadMismatch, "Lines exist beyond the anchored head."));
            }
        }

        return new LedgerVerification(violations);
    }

    private static FileScan ScanFile(string root, string relative, List<LedgerViolation> violations)
    {
        var session = Path.GetFileNameWithoutExtension(relative);
        var text = Encoding.UTF8.GetString(LedgerFiles.ReadAllShared(LedgerFiles.Full(root, relative)));
        var lines = text.Split('\n');
        var complete = lines.Length - 1;
        var hashes = new List<string>(complete);
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

        return new FileScan(hashes);
    }

    private sealed record FileScan(List<string> Hashes);
}
