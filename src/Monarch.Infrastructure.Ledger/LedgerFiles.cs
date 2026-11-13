using System.Text;
using System.Text.Json;
using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Layout and low-level IO helpers shared by store, verifier and read model.</summary>
internal static class LedgerFiles
{
    public const string LedgerDir = ".monarch/ledger";
    public const string HeadsPath = ".monarch/local/ledger-heads.json";
    private const int RetryBudgetMs = 200;

    public const string WriterIdPath = ".monarch/local/writer-id";

    /// <summary>ADR-0018: the commit this worktree started writing on; created once, never overwritten.</summary>
    public const string BasePath = ".monarch/local/ledger-base";

    /// <summary>Reads the per-worktree writer id without creating it; null when it does not exist or is malformed.</summary>
    public static string? TryWriterId(string root) => TryReadWriterId(Full(root, WriterIdPath));

    /// <summary>Reads (or creates once) the per-worktree writer id: 8 lowercase hex characters, git-ignored.</summary>
    public static string WriterId(string root, bool regenerate = false)
    {
        var path = Full(root, WriterIdPath);
        if (!regenerate && TryReadWriterId(path) is { } existing)
        {
            return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var id = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(4));
        if (regenerate)
        {
            File.WriteAllText(path, id);
            return id;
        }

        try
        {
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            fs.Write(System.Text.Encoding.ASCII.GetBytes(id));
            return id;
        }
        catch (IOException)
        {
            return TryReadWriterId(path) ?? throw new IOException("Cannot read or create the writer id.");
        }
    }

    private static string? TryReadWriterId(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = Encoding.ASCII.GetString(ReadAllShared(path)).Trim();
        return text.Length == 8 && text.All(char.IsAsciiHexDigitLower) ? text : null;
    }

    public static string Full(string root, string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    public static void ValidateSessionId(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        if (sessionId[0] == '.' || !sessionId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new ArgumentException("Session id may only contain letters, digits, '-', '_' and '.' and must not start with '.'.", nameof(sessionId));
        }
    }

    /// <summary>Finds the relative path of the session's file; falls back to the month folder of <paramref name="at"/>.</summary>
    public static string ResolveRelative(string root, string sessionId, DateTimeOffset at)
    {
        var current = $"{LedgerDir}/{at.UtcDateTime:yyyy-MM}/{sessionId}.jsonl";
        if (File.Exists(Full(root, current)))
        {
            return current;
        }

        var dir = Full(root, LedgerDir);
        if (Directory.Exists(dir))
        {
            foreach (var month in Directory.EnumerateDirectories(dir))
            {
                if (File.Exists(Path.Combine(month, sessionId + ".jsonl")))
                {
                    return $"{LedgerDir}/{Path.GetFileName(month)}/{sessionId}.jsonl";
                }
            }
        }

        return current;
    }

    public static List<string> EnumerateRelative(string root)
    {
        var dir = Full(root, LedgerDir);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        var rootFull = Path.GetFullPath(root);
        var list = Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(rootFull, Path.GetFullPath(f)).Replace(Path.DirectorySeparatorChar, '/'))
            .ToList();
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    public static byte[] ReadAllShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[fs.Length];
        fs.ReadExactly(buffer);
        return buffer;
    }

    /// <summary>Opens a file with exclusive sharing, retrying with short backoff for at most 200 ms.</summary>
    public static FileStream OpenExclusive(string path)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var delay = 1;
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1);
            }
            catch (IOException ex) when (ex is not FileNotFoundException and not DirectoryNotFoundException)
            {
                if (sw.ElapsedMilliseconds >= RetryBudgetMs)
                {
                    throw new IOException($"Ledger file '{path}' stayed locked for {RetryBudgetMs} ms.", ex);
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 16);
            }
        }
    }

    public static IReadOnlyList<LedgerEntry> ReadEntries(string root, string relative)
    {
        var text = Encoding.UTF8.GetString(ReadAllShared(Full(root, relative)));
        var entries = new List<LedgerEntry>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!LedgerCanonical.TrySplit(line, out _, out var hash))
            {
                throw new InvalidDataException($"Unparseable ledger line in {relative}.");
            }

            LedgerLine parsed;
            try
            {
                parsed = JsonSerializer.Deserialize(line, LedgerJsonContext.Default.LedgerLine) ?? throw new InvalidDataException("null line");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"Unparseable ledger line in {relative}.", ex);
            }

            entries.Add(new LedgerEntry(relative, parsed.Seq, parsed.At, parsed.Actor, parsed.Type, parsed.Payload.Clone(), parsed.PrevHash, hash));
        }

        return entries;
    }
}
