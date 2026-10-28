using System.Globalization;
using System.Text;
using System.Text.Json;
using Monarch.Domain;

namespace Monarch.Cli;

/// <summary>Snapshot and estimate captured at <c>quest accept</c>, compared at <c>quest submit</c>.</summary>
/// <param name="Model">Normalised model family.</param>
/// <param name="Effort">Normalised effort.</param>
/// <param name="Estimate5h">Raw quest estimate (5h window, pp).</param>
/// <param name="Five">5h usage at accept.</param>
/// <param name="Seven">7d usage at accept.</param>
/// <param name="FiveReset">5h reset (unix seconds) at accept.</param>
/// <param name="SevenReset">7d reset (unix seconds) at accept.</param>
/// <param name="ObservedAt">Time stamp of the snapshot.</param>
/// <param name="Stale">True when the snapshot was stale at accept.</param>
/// <param name="AcceptedAt">Time of the accept.</param>
/// <param name="SessionId">Session that accepted.</param>
internal sealed record PendingStep(string Model, string Effort, double Estimate5h, double Five, double Seven, long FiveReset, long SevenReset, string ObservedAt, bool Stale, string AcceptedAt, string SessionId);

/// <summary>Content of the calibration file: factor per pair plus the open observations.</summary>
internal sealed class CalibrationData
{
    /// <summary>Factor and n per pair key.</summary>
    public Dictionary<string, PairFactor> Pairs { get; } = new(StringComparer.Ordinal);

    /// <summary>Open observations per quest id.</summary>
    public Dictionary<string, PendingStep> Pending { get; } = new(StringComparer.Ordinal);

    /// <summary>Factor of a pair; the start factor when never observed.</summary>
    public PairFactor FactorOf(ModelEffort pair) => Pairs.TryGetValue(pair.Key, out var f) ? f : PairFactor.Start;
}

/// <summary>
/// Engine file API for the calibration file (ADR-0012). Only the binary writes it; the file lies under the project's local state directory,
/// which the tool-call policy protects against tool writes.
/// </summary>
internal static class CalibrationStore
{
    /// <summary>Path of the calibration file.</summary>
    public static string PathOf(ProjectPaths paths) => Path.Combine(paths.Root, ".monarch", "local", "mana-calibration.json");

    /// <summary>Reads the file; an unreadable file counts as empty (never fails a command).</summary>
    public static CalibrationData Read(ProjectPaths paths)
    {
        var data = new CalibrationData();
        try
        {
            var path = PathOf(paths);
            if (!File.Exists(path))
            {
                return data;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("pairs", out var pairs))
            {
                foreach (var p in pairs.EnumerateObject())
                {
                    data.Pairs[p.Name] = new PairFactor(p.Value.GetProperty("factor").GetDouble(), p.Value.GetProperty("n").GetInt32());
                }
            }

            if (doc.RootElement.TryGetProperty("pending", out var pending))
            {
                foreach (var p in pending.EnumerateObject())
                {
                    var v = p.Value;
                    data.Pending[p.Name] = new PendingStep(
                        v.GetProperty("model").GetString() ?? "unknown", v.GetProperty("effort").GetString() ?? "unknown",
                        v.GetProperty("estimate5h").GetDouble(), v.GetProperty("five").GetDouble(), v.GetProperty("seven").GetDouble(),
                        v.GetProperty("fiveReset").GetInt64(), v.GetProperty("sevenReset").GetInt64(),
                        v.GetProperty("observedAt").GetString() ?? string.Empty, v.GetProperty("stale").GetBoolean(),
                        v.GetProperty("acceptedAt").GetString() ?? string.Empty, v.GetProperty("sessionId").GetString() ?? string.Empty);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return new CalibrationData();
        }

        return data;
    }

    /// <summary>Writes the file atomically.</summary>
    public static void Write(ProjectPaths paths, CalibrationData data)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("schema", 1);
            w.WriteStartObject("pairs");
            foreach (var (key, f) in data.Pairs.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(key);
                w.WriteNumber("factor", f.Factor);
                w.WriteNumber("n", f.N);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteStartObject("pending");
            foreach (var (quest, s) in data.Pending.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                w.WriteStartObject(quest);
                w.WriteString("model", s.Model);
                w.WriteString("effort", s.Effort);
                w.WriteNumber("estimate5h", s.Estimate5h);
                w.WriteNumber("five", s.Five);
                w.WriteNumber("seven", s.Seven);
                w.WriteNumber("fiveReset", s.FiveReset);
                w.WriteNumber("sevenReset", s.SevenReset);
                w.WriteString("observedAt", s.ObservedAt);
                w.WriteBoolean("stale", s.Stale);
                w.WriteString("acceptedAt", s.AcceptedAt);
                w.WriteString("sessionId", s.SessionId);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        Files.WriteAtomic(PathOf(paths), Encoding.UTF8.GetString(ms.ToArray()) + "\n");
    }
}

/// <summary>Pair detection, accept-time capture and submit-time calibration (ADR-0012).</summary>
internal static class ManaCalibration
{
    /// <summary>Pair of the calling process: model from this session's entry in the mana file, effort from <c>CLAUDE_EFFORT</c>.</summary>
    public static (ModelEffort Pair, string ModelSource, string EffortSource, string SessionId) CurrentPair(CliEnv env, ManaFile? mana)
    {
        var sid = env.GetVar("CLAUDE_CODE_SESSION_ID") ?? string.Empty;
        string? model = null;
        if (sid.Length > 0 && mana?.Sessions is { } s && s.TryGetValue(sid, out var e))
        {
            model = e.Model;
        }

        var pair = ModelEffort.Normalize(model, env.GetVar("CLAUDE_EFFORT"));
        return (pair, pair.Model == "unknown" ? "unknown" : "mana.json:sessions", pair.Effort == "unknown" ? "unknown" : "CLAUDE_EFFORT", sid);
    }

    /// <summary>The pair and its current factor, for display and for scaling an estimate.</summary>
    public static (ModelEffort Pair, PairFactor Factor) Current(CliEnv env, ProjectPaths paths)
    {
        var pair = CurrentPair(env, ManaStore.Read(env.Home)).Pair;
        return (pair, CalibrationStore.Read(paths).FactorOf(pair));
    }

    /// <summary>Captures snapshot, pair and estimate at accept.</summary>
    public static void Begin(CliEnv env, ProjectPaths paths, string questId, double estimate5h)
    {
        var mana = ManaStore.Read(env.Home);
        var snap = ManaStore.ToSnapshot(mana, env.Clock.UtcNow);
        var data = CalibrationStore.Read(paths);
        if (snap is null || mana?.RateLimits is not { } rl)
        {
            data.Pending.Remove(questId);
        }
        else
        {
            var (pair, _, _, sid) = CurrentPair(env, mana);
            data.Pending[questId] = new PendingStep(
                pair.Model, pair.Effort, estimate5h, rl.FiveHour!.UsedPercentage!.Value, rl.SevenDay!.UsedPercentage!.Value,
                rl.FiveHour.ResetsAt ?? 0, rl.SevenDay.ResetsAt ?? 0, mana.RateLimitsAt ?? mana.WrittenAt ?? string.Empty,
                snap.IsStale, ManaStore.Stamp(env.Clock.UtcNow), sid);
        }

        CalibrationStore.Write(paths, data);
    }

    /// <summary>Computes the delta at submit, writes <c>ManaStepRecorded</c> and updates the factor of the accept-time pair when calibrated.</summary>
    public static void Finish(CliEnv env, ProjectPaths paths, ProjectLedger ledger, string questId)
    {
        var mana = ManaStore.Read(env.Home);
        var snap = ManaStore.ToSnapshot(mana, env.Clock.UtcNow);
        var data = CalibrationStore.Read(paths);
        var (nowPair, modelSource, effortSource, _) = CurrentPair(env, mana);
        var hasPending = data.Pending.Remove(questId, out var p);
        var pair = hasPending ? new ModelEffort(p!.Model, p.Effort) : nowPair;
        var estimate5h = hasPending ? p!.Estimate5h : 0;

        string? reason = null;
        var calibrated = false;
        double? actual5h = null;
        double? actual7d = null;
        var factor = data.FactorOf(pair);
        if (!hasPending)
        {
            reason = "no-accept-snapshot";
        }
        else if (snap is null || mana?.RateLimits is not { } rl)
        {
            reason = "no-snapshot";
        }
        else
        {
            actual5h = rl.FiveHour!.UsedPercentage!.Value - p!.Five;
            actual7d = rl.SevenDay!.UsedPercentage!.Value - p.Seven;
            var reset = (rl.FiveHour.ResetsAt ?? 0) != p.FiveReset || (rl.SevenDay.ResetsAt ?? 0) != p.SevenReset
                || env.Clock.UtcNow.ToUnixTimeSeconds() >= p.FiveReset || actual5h < 0;
            var acceptedAt = ManaStore.ParseStamp(p.AcceptedAt);
            var overlap = mana.Sessions?.Any(kv => kv.Key != p.SessionId && ManaStore.ParseStamp(kv.Value.WrittenAt) is { } w && acceptedAt is { } a && w >= a) == true;
            var outcome = ManaCalibrator.Observe(factor, estimate5h, actual5h.Value, reset, p.Stale || snap.IsStale, overlap);
            (factor, calibrated, reason) = (outcome.Factor, outcome.Calibrated, outcome.Reason);
            if (calibrated)
            {
                data.Pairs[pair.Key] = factor;
            }
        }

        CalibrationStore.Write(paths, data);
        ledger.Append(ledger.SessionId(), "cli", "ManaStepRecorded",
            ("quest", questId), ("model", pair.Model), ("effort", pair.Effort),
            ("modelSource", modelSource), ("effortSource", effortSource), ("source", "statusline-snapshots"),
            ("estimate5h", estimate5h), ("actual5h", actual5h), ("actual7d", actual7d),
            ("calibrated", calibrated), ("reason", reason), ("factor", factor.Factor), ("n", (long)factor.N));
    }

    /// <summary>Formats a number invariantly for display.</summary>
    public static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
