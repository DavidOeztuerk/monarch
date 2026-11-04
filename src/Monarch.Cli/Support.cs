using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Monarch.Application.Net;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Boards.Frontmatter;
using Monarch.Infrastructure.Evidence;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>One ledger event read back from disk.</summary>
/// <param name="Type">Event type.</param>
/// <param name="At">Event time.</param>
/// <param name="Payload">Payload.</param>
internal sealed record LedgerRow(string Type, DateTimeOffset At, JsonElement Payload)
{
    /// <summary>String payload property or null.</summary>
    public string? Str(string name) => Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Numeric payload property or null.</summary>
    public double? Num(string name) => Payload.ValueKind == JsonValueKind.Object && Payload.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}

/// <summary>Reads all ledger events of a project.</summary>
internal static class LedgerScan
{
    /// <summary>Reads every event of every session file, ordered by time (stable per file).</summary>
    public static List<LedgerRow> All(string root)
    {
        var rows = new List<LedgerRow>();
        var dir = Path.Combine(root, ".monarch", "ledger");
        if (!Directory.Exists(dir))
        {
            return rows;
        }

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file).Where(l => l.Length > 0))
            {
                using var doc = JsonDocument.Parse(line);
                var r = doc.RootElement;
                var type = r.TryGetProperty("type", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                var at = r.TryGetProperty("at", out var a) && a.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(a.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : DateTimeOffset.MinValue;
                rows.Add(new LedgerRow(type, at, r.TryGetProperty("payload", out var p) ? p.Clone() : default));
            }
        }

        return [.. rows.OrderBy(x => x.At)];
    }

    /// <summary>Builds a JSON array of objects whose fields are strings.</summary>
    public static JsonElement StringObjects(IEnumerable<IReadOnlyList<(string Key, string Value)>> items)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartArray();
            foreach (var item in items)
            {
                w.WriteStartObject();
                foreach (var (k, v) in item)
                {
                    w.WriteString(k, v);
                }

                w.WriteEndObject();
            }

            w.WriteEndArray();
        }

        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }
}

/// <summary>Frontmatter helpers for board files.</summary>
internal static class BoardFiles
{
    /// <summary>Edits the frontmatter of a file; the body stays byte-for-byte. Writes only when something changed.</summary>
    public static void Edit(string path, Func<YamlMap, YamlMap> edit)
    {
        var doc = FrontmatterDocument.Parse(File.ReadAllText(path));
        var next = doc with { Root = edit(doc.Root) };
        if (next != doc)
        {
            Files.WriteAtomic(path, next.Write());
        }
    }

    /// <summary>Sets keys of the <c>board</c> map, keeping the other keys.</summary>
    public static YamlMap SetBoard(YamlMap root, params (string Key, string Value)[] values)
    {
        var board = root.Get("board") as YamlMap ?? new YamlMap([], true);
        foreach (var (k, v) in values)
        {
            board = board.With(k, new YamlScalar(v));
        }

        return root.With("board", board);
    }

    /// <summary>Reads the board link of a file.</summary>
    public static BoardLink Link(string path)
    {
        var root = FrontmatterDocument.Parse(File.ReadAllText(path)).Root;
        var board = root.Get("board") as YamlMap;
        int? Int(string key) => int.TryParse(board?.GetString(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
        return new BoardLink(Int("issue"), Int("pr"), Int("milestone"));
    }

    /// <summary>Unchecked checklist items of the body, else the frontmatter <c>dod</c>.</summary>
    public static IReadOnlyList<string> OpenDod(string path)
    {
        var doc = FrontmatterDocument.Parse(File.ReadAllText(path));
        var lines = doc.Body.Split('\n').Select(l => l.TrimEnd('\r').Trim()).Where(l => l.StartsWith("- [", StringComparison.Ordinal)).ToList();
        if (lines.Count > 0)
        {
            return [.. lines.Where(l => l.StartsWith("- [ ] ", StringComparison.Ordinal)).Select(l => l[6..])];
        }

        return doc.Root.GetStrings("dod");
    }
}

/// <summary>Links from a quest or gate file to the forge.</summary>
/// <param name="Issue">GitHub issue number.</param>
/// <param name="Pr">Pull request number.</param>
/// <param name="Milestone">Milestone number.</param>
internal sealed record BoardLink(int? Issue, int? Pr, int? Milestone);

/// <summary>Owner and repository of the forge.</summary>
/// <param name="Owner">Owner.</param>
/// <param name="Repo">Repository.</param>
internal sealed record ForgeRef(string Owner, string Repo);

/// <summary>Forge resolution and clients.</summary>
internal static partial class Forge
{
    /// <summary>Owner/repo from the <c>boards.github</c> entry of system.yaml, else from the git <c>origin</c> remote.</summary>
    public static ForgeRef? Resolve(ProjectPaths paths)
    {
        if (File.Exists(paths.SystemYaml))
        {
            var root = YamlDocument.Parse(File.ReadAllText(paths.SystemYaml));
            if ((root.Get("boards") as YamlMap)?.Get("github") is YamlMap gh && gh.GetString("owner") is { Length: > 0 } o && gh.GetString("repo") is { Length: > 0 } r)
            {
                return new ForgeRef(o, r);
            }
        }

        var url = Git.Run(paths.Root, "remote", "get-url", "origin");
        var m = url is null ? null : OriginPattern().Match(url);
        return m is { Success: true } ? new ForgeRef(m.Groups[1].Value, m.Groups[2].Value) : null;
    }

    /// <summary>Check names that must be green (<c>evidence.required</c>); empty means every completed check.</summary>
    public static IReadOnlyList<string> Required(ProjectPaths paths) =>
        File.Exists(paths.SystemYaml) && YamlDocument.Parse(File.ReadAllText(paths.SystemYaml)).Get("evidence") is YamlMap e ? e.GetStrings("required") : [];

    /// <summary>GitHub issues board for the repository.</summary>
    public static GitHubIssuesBoard Board(CliEnv env, ForgeRef r) =>
        new(Http(env), new GitHubBoardOptions { Owner = r.Owner, Repo = r.Repo }, env.Tokens ?? new GitHubTokenProvider());

    /// <summary>GitHub checks evidence source.</summary>
    public static GitHubChecksEvidence Evidence(CliEnv env) => new(Http(env), env.Tokens ?? new GitHubTokenProvider());

    /// <summary>GitHub pull request client (reads and the squash merge of <c>gate merge</c>).</summary>
    public static GitHubPulls Pulls(CliEnv env) => new(Http(env), env.Tokens ?? new GitHubTokenProvider());

    private static HttpClient Http(CliEnv env) => new(env.Http ?? new HttpClientHandler(), false) { BaseAddress = new Uri("https://api.github.com/") };

    [GeneratedRegex(@"github\.com[:/]([^/\s]+)/([^/\s]+?)(?:\.git)?/?$")]
    private static partial Regex OriginPattern();
}

/// <summary>XP of a cleared quest per the concept: XP = B(rank) * K(kind) * Q.</summary>
internal static class XpCalculator
{
    /// <summary>Result with its factors.</summary>
    /// <param name="Xp">Rounded XP.</param>
    /// <param name="Base">Rank base.</param>
    /// <param name="Kind">Kind factor.</param>
    /// <param name="Quality">Quality factor Q.</param>
    public sealed record Result(int Xp, int Base, double Kind, double Quality);

    /// <summary>
    /// Computes XP. Q = 1 + 0.25 [first pass] + 0.15 [mutation score >= threshold] - 0.3 [review rounds &gt; 2]. First pass and review rounds derive
    /// from the number of <c>QuestSubmitted</c> events. The mutation term needs a mutation score from CI, which is not available in phase 0,
    /// so it is always 0; the 14-day revert clawback needs git history and is not computed here.
    /// </summary>
    public static Result Compute(Rank rank, QuestKind kind, int submissions)
    {
        var b = rank switch { Rank.E => 10, Rank.D => 25, Rank.C => 60, Rank.B => 150, Rank.A => 400, _ => 1000 };
        var k = kind switch { QuestKind.Main => 1.0, QuestKind.Emergency => 1.5, QuestKind.Hidden => 0.8, QuestKind.Side => 0.6, QuestKind.Daily => 0.3, _ => 0.0 };
        var q = 1 + (submissions <= 1 ? 0.25 : 0) - (submissions > 2 ? 0.3 : 0);
        return new Result((int)Math.Round(b * k * q, MidpointRounding.AwayFromZero), b, k, q);
    }
}

/// <summary>Mana limits (plan default or adaptive) and snapshot events.</summary>
internal static class ManaSupport
{
    /// <summary>Default next step in the 5h window.</summary>
    public const double Step5h = 5;

    /// <summary>Default next step in the 7d window.</summary>
    public const double Step7d = 1;

    /// <summary>Resolved limits.</summary>
    /// <param name="Limits">Limits per window.</param>
    /// <param name="Source">"plan default" or "adaptive".</param>
    /// <param name="Recaps">Number of measured recaps.</param>
    /// <param name="Recap7d">EWMA of recap costs in the 7d window.</param>
    public sealed record Resolved(ManaLimits Limits, string Source, int Recaps, double Recap7d);

    /// <summary>
    /// The limit in force per window (ADR-0012, Q-0142): with <c>w</c> = work_until_pct and <c>min</c> = lower clamp value (else <c>w</c>), fewer than
    /// three recaps or an unreadable ledger give <c>max(w, min)</c>; otherwise the adaptive value clamped to [min, max(w, min)].
    /// </summary>
    public static Resolved Resolve(string root, SystemConfig config)
    {
        List<LedgerRow> rows;
        try
        {
            rows = LedgerScan.All(root);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        {
            rows = []; // a corrupt or non-object line is a finding of 'ledger verify', not a reason to abort
        }

        var start = config.Limits;
        var min = config.ClampMin ?? start;
        var top = new ManaLimits(Math.Max(start.FiveHourPct, min.FiveHourPct), Math.Max(start.SevenDayPct, min.SevenDayPct));
        var recaps = rows.Where(r => r.Type == "RecapWritten").ToList();
        var r5 = recaps.Select(r => r.Num("cost5h") ?? 0).ToList();
        var r7 = recaps.Select(r => r.Num("cost7d") ?? 0).ToList();
        if (recaps.Count < AdaptiveLimit.MinRecaps)
        {
            return new Resolved(top, "plan default", recaps.Count, AdaptiveLimit.Ewma(r7));
        }

        var snaps = rows.Where(r => r.Type == "ManaSnapshotTaken" && r.Num("fiveHourUsedPct") is not null).ToList();
        var s5 = Steps(snaps, "fiveHourUsedPct", "fiveHourResetsAt");
        var s7 = Steps(snaps, "sevenDayUsedPct", "sevenDayResetsAt");
        var limits = new ManaLimits(
            AdaptiveLimit.Compute(r5, s5, top.FiveHourPct, min.FiveHourPct, top.FiveHourPct),
            AdaptiveLimit.Compute(r7, s7, top.SevenDayPct, min.SevenDayPct, top.SevenDayPct));
        return new Resolved(limits, "adaptive", recaps.Count, AdaptiveLimit.Ewma(r7));
    }

    /// <summary>Formats a limit or usage value the same way everywhere (<c>0.#</c>, invariant).</summary>
    public static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static List<double> Steps(List<LedgerRow> snaps, string used, string reset)
    {
        var steps = new List<double>();
        for (var i = 1; i < snaps.Count; i++)
        {
            var (prev, cur) = (snaps[i - 1], snaps[i]);
            if (cur.Str("reason") == "recap-after" || prev.Num(reset) != cur.Num(reset))
            {
                continue;
            }

            var delta = (cur.Num(used) ?? 0) - (prev.Num(used) ?? 0);
            if (delta > 0)
            {
                steps.Add(delta);
            }
        }

        return steps;
    }

    /// <summary>Appends a <c>ManaSnapshotTaken</c> event; without measurements only the reason is recorded.</summary>
    public static void Snapshot(ProjectLedger ledger, string session, ManaSnapshot? s, string reason) =>
        ledger.Append(session, "cli", "ManaSnapshotTaken",
            ("reason", reason), ("known", s is not null),
            ("fiveHourUsedPct", s?.Effective5h), ("fiveHourResetsAt", s?.FiveHourResetsAt.ToUnixTimeSeconds()),
            ("sevenDayUsedPct", s?.Effective7d), ("sevenDayResetsAt", s?.SevenDayResetsAt.ToUnixTimeSeconds()), ("stale", s?.IsStale));

    /// <summary>ISO 8601 UTC with seconds.</summary>
    public static string Iso(DateTimeOffset t) => ManaStore.Stamp(t);

    /// <summary>SHA-256 hex of text.</summary>
    public static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
