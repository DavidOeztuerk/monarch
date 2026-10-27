using System.Globalization;
using System.Text;
using System.Text.Json;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Ledger;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>Atomic text-file writes.</summary>
internal static class Files
{
    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Writes via temp file and rename.</summary>
    public static void WriteAtomic(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, text, Utf8);
        File.Move(temp, path, overwrite: true);
    }
}

/// <summary>Reads and writes <c>active.json</c>.</summary>
internal static class ActiveStore
{
    /// <summary>Reads the active quest; null when none. Throws on a corrupt file.</summary>
    public static ActiveFile? Read(ProjectPaths paths) =>
        File.Exists(paths.ActiveJson) ? JsonSerializer.Deserialize(File.ReadAllText(paths.ActiveJson), CliJson.Default.ActiveFile) : null;

    /// <summary>Writes the active quest.</summary>
    public static void Write(ProjectPaths paths, ActiveFile active) =>
        Files.WriteAtomic(paths.ActiveJson, JsonSerializer.Serialize(active, CliJson.Default.ActiveFile));

    /// <summary>Clears the active quest.</summary>
    public static void Clear(ProjectPaths paths)
    {
        if (File.Exists(paths.ActiveJson))
        {
            File.Delete(paths.ActiveJson);
        }
    }

    /// <summary>Parses the stored rank.</summary>
    public static Rank RankOf(ActiveFile a) =>
        Enum.TryParse<Rank>(a.Rank, true, out var r) ? r : throw new FormatException($"active.json: unknown rank '{a.Rank}'.");

    /// <summary>Maps to the policy's view of the quest.</summary>
    public static ActiveQuest ToPolicy(ActiveFile a) => new(new QuestId(a.QuestId), RankOf(a), new QuestScope(a.Scope.Include, a.Scope.Exclude));
}

/// <summary>Reads and writes the account-global mana snapshot.</summary>
internal static class ManaStore
{
    /// <summary>After this age without fresh rate limits a snapshot counts as stale (ADR-0004).</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);

    /// <summary>Path of <c>mana.json</c>.</summary>
    public static string PathOf(string home) => Path.Combine(home, "mana.json");

    /// <summary>Reads the snapshot; null when missing or unreadable.</summary>
    public static ManaFile? Read(string home)
    {
        try
        {
            var path = PathOf(home);
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), CliJson.Default.ManaFile) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Formats a time stamp the way the file stores it.</summary>
    public static string Stamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>Parses a stored time stamp.</summary>
    public static DateTimeOffset? ParseStamp(string? s) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t : null;

    /// <summary>Converts to the domain snapshot; null when no rate limits are known.</summary>
    public static ManaSnapshot? ToSnapshot(ManaFile? f, DateTimeOffset now)
    {
        if (f?.RateLimits is not { FiveHour.UsedPercentage: { } five, SevenDay.UsedPercentage: { } seven } rl)
        {
            return null;
        }

        var at = ParseStamp(f.RateLimitsAt) ?? ParseStamp(f.WrittenAt) ?? now;
        var stale = f.RateLimitsStale || now - at > StaleAfter;
        return new ManaSnapshot(
            five, DateTimeOffset.FromUnixTimeSeconds(rl.FiveHour.ResetsAt ?? 0),
            seven, DateTimeOffset.FromUnixTimeSeconds(rl.SevenDay.ResetsAt ?? 0), stale);
    }
}

/// <summary>Appends events to the project's JSONL ledger.</summary>
internal sealed class ProjectLedger
{
    private readonly ProjectPaths _paths;
    private readonly IClock _clock;
    private readonly JsonlLedgerStore _store;

    public ProjectLedger(ProjectPaths paths, IClock clock)
    {
        _paths = paths;
        _clock = clock;
        _store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = paths.Root }, clock);
    }

    /// <summary>Makes an arbitrary string a valid ledger session id.</summary>
    public static string Sanitize(string s)
    {
        var chars = s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray();
        if (chars.Length == 0 || chars[0] == '.')
        {
            return "_" + new string(chars);
        }

        return new string(chars);
    }

    /// <summary>Session id: explicit input, else <c>current-session</c>, else <c>cli-yyyyMMdd</c>.</summary>
    public string SessionId(string? explicitId = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return Sanitize(explicitId.Trim());
        }

        try
        {
            if (File.Exists(_paths.CurrentSession) && File.ReadAllText(_paths.CurrentSession).Trim() is { Length: > 0 } s)
            {
                return Sanitize(s);
            }
        }
        catch (IOException)
        {
        }

        return "cli-" + _clock.UtcNow.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    /// <summary>Appends an event whose payload is built from key/value pairs (null values are skipped).</summary>
    public LedgerEntry Append(string session, string actor, string type, params (string Key, object? Value)[] fields)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var (key, value) in fields)
            {
                switch (value)
                {
                    case null: break;
                    case string s: w.WriteString(key, s); break;
                    case bool b: w.WriteBoolean(key, b); break;
                    case int i: w.WriteNumber(key, i); break;
                    case long l: w.WriteNumber(key, l); break;
                    case double d: w.WriteNumber(key, d); break;
                    default: w.WriteString(key, Convert.ToString(value, CultureInfo.InvariantCulture)); break;
                }
            }

            w.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return _store.Append(session, actor, type, doc.RootElement.Clone());
    }

    /// <summary>Entries of one session in order.</summary>
    public IReadOnlyList<LedgerEntry> ReadSession(string session) => _store.ReadSession(session);
}
