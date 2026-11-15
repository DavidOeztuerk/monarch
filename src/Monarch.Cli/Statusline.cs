using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Monarch.Cli;

/// <summary>Statusline sensor and display (ADR-0004). Never fails visibly.</summary>
internal static class Statusline
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LowerConfirm = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan WrapTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Prints the wrapped output (if any) and the Monarch line; always returns 0.</summary>
    public static int Run(CliEnv env)
    {
        var raw = string.Empty;
        try
        {
            raw = env.In.ReadToEnd();
        }
        catch (IOException)
        {
        }

        var wrapped = Safe(() => RunWrapped(env, raw)) ?? string.Empty;
        var line = Safe(() => Build(env, raw)) ?? string.Empty;
        try
        {
            if (wrapped.Length > 0)
            {
                env.Out.Write(wrapped);
                if (!wrapped.EndsWith('\n'))
                {
                    env.Out.Write('\n');
                }
            }

            if (line.Length > 0)
            {
                env.Out.WriteLine(line);
            }
        }
        catch (IOException)
        {
        }

        return 0;
    }

    private static string? Safe(Func<string?> f)
    {
        try
        {
            return f();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? RunWrapped(CliEnv env, string stdin)
    {
        var cfgPath = Path.Combine(env.Home, "statusline.json");
        if (!File.Exists(cfgPath))
        {
            return null;
        }

        var cmd = JsonSerializer.Deserialize(File.ReadAllText(cfgPath), CliJson.Default.StatuslineConfig)?.Wrap;
        if (string.IsNullOrWhiteSpace(cmd))
        {
            return null;
        }

        var psi = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(cmd);
        using var proc = Process.Start(psi);
        if (proc is null)
        {
            return null;
        }

        var stdout = proc.StandardOutput.ReadToEndAsync();
        _ = proc.StandardError.ReadToEndAsync();
        try
        {
            proc.StandardInput.Write(stdin);
            proc.StandardInput.Close();
        }
        catch (IOException)
        {
        }

        if (!proc.WaitForExit(WrapTimeout))
        {
            proc.Kill(true);
            return null;
        }

        return stdout.GetAwaiter().GetResult();
    }

    private static string? Build(CliEnv env, string raw)
    {
        var input = JsonSerializer.Deserialize(raw, CliJson.Default.StatuslineInput) ?? new StatuslineInput();
        var now = env.Clock.UtcNow;
        var prev = ManaStore.ReadForMerge(env.Home);
        var next = Merge(prev, input, now, out var lowered);
        if (!Unchanged(prev, next, now))
        {
            Files.WriteAtomic(ManaStore.PathOf(env.Home), JsonSerializer.Serialize(next, CliJson.Default.ManaFile));
        }

        var cwd = input.Workspace?.CurrentDir ?? input.Cwd ?? env.Cwd;
        var paths = ProjectPaths.Locate(cwd, env.Home);
        if (lowered is not null && paths is not null)
        {
            LogLowered(env, paths, input.SessionId, lowered);
        }

        var neutral = false;
        ActiveFile? active = null;
        if (paths is not null)
        {
            neutral = Safe(() => string.Equals(SystemConfigReader.Load(paths.Root).Theme, "neutral", StringComparison.OrdinalIgnoreCase) ? "n" : null) is not null;
            try
            {
                active = ActiveStore.Read(paths);
            }
            catch (JsonException)
            {
            }
        }

        return Format(next, active, neutral, now, ManaStore.ContextOf(next, input.SessionId));
    }

    /// <summary>A lowering is a ledger event; a ledger failure never reaches the statusline.</summary>
    private static void LogLowered(CliEnv env, ProjectPaths paths, string? sessionId, WindowLowered l)
    {
        try
        {
            var ledger = new ProjectLedger(paths, env.Clock);
            ledger.Append(ledger.SessionId(sessionId), "statusline", "ManaWindowLowered",
                ("window", l.Window), ("old_pct", l.OldPct), ("new_pct", l.NewPct), ("resets_at", l.ResetsAt),
                ("first_seen_at", l.FirstSeenAt), ("confirmed_at", l.ConfirmedAt), ("session", l.Session));
        }
        catch (Exception)
        {
            // Best effort: mana.json keeps last_lowered.
        }
    }

    private static ManaFile Merge(ManaFile? prev, StatuslineInput input, DateTimeOffset now, out WindowLowered? lowered)
    {
        string stamp;
        var fresh5 = input.RateLimits?.FiveHour is { UsedPercentage: not null };
        var fresh7 = input.RateLimits?.SevenDay is { UsedPercentage: not null };
        stamp = ManaStore.Stamp(now);
        var proof = IsFreshFiveHour(prev?.RateLimits?.FiveHour, input.RateLimits?.FiveHour);
        var limits = new RateLimits
        {
            FiveHour = MergeWindow(prev?.RateLimits?.FiveHour, fresh5 ? input.RateLimits!.FiveHour : null, stamp, now, null, false, "five_hour", out _),
            SevenDay = MergeWindow(prev?.RateLimits?.SevenDay, fresh7 ? input.RateLimits!.SevenDay : null, stamp, now, input.SessionId, proof, "seven_day", out lowered),
        };
        var fresh = fresh5 && fresh7;
        return new ManaFile
        {
            Schema = 1,
            WrittenAt = ManaStore.Stamp(now),
            SessionId = input.SessionId ?? prev?.SessionId,
            Model = input.Model?.Id ?? prev?.Model,
            ContextWindow = input.ContextWindow ?? prev?.ContextWindow,
            Sessions = MergeSessions(prev, input, now),
            RateLimits = limits.FiveHour is null && limits.SevenDay is null ? null : limits,
            RateLimitsAt = fresh ? ManaStore.Stamp(now) : prev?.RateLimitsAt,
            RateLimitsStale = !fresh,
            LastLowered = lowered ?? prev?.LastLowered,
        };
    }

    /// <summary>A snapshot is fresh proof when it carries a 5h window at least as new as the stored one (later reset, or same reset and at least the same usage).</summary>
    private static bool IsFreshFiveHour(RateWindow? stored, RateWindow? incoming)
    {
        if (incoming is not { UsedPercentage: { } iu, ResetsAt: { } ir })
        {
            return false;
        }

        if (stored?.UsedPercentage is not { } su)
        {
            return true;
        }

        var sr = stored.ResetsAt ?? 0;
        return ir > sr || (ir == sr && iu >= su);
    }

    /// <summary>Rate limits are account-global: the later <c>resets_at</c> wins, and within one window the higher usage wins (usage is monotonic per window).</summary>
    /// <remarks>
    /// Exception (<paramref name="canLower"/>): a lower value in the same window is adopted when two fresh snapshots of one session lie at least <see cref="LowerConfirm"/> apart
    /// (the later one not below the first, both below the held maximum); the later value is kept.
    /// </remarks>
    private static RateWindow? MergeWindow(RateWindow? prev, RateWindow? incoming, string stamp, DateTimeOffset now, string? session, bool canLower, string name, out WindowLowered? lowered)
    {
        lowered = null;
        if (incoming is null)
        {
            return prev;
        }

        var seen = new WindowSighting { UsedPercentage = incoming.UsedPercentage, ResetsAt = incoming.ResetsAt, ObservedAt = stamp, SessionId = session };
        var fresh = new RateWindow { UsedPercentage = incoming.UsedPercentage, ResetsAt = incoming.ResetsAt, ObservedAt = stamp, LastSeen = seen };
        if (prev?.UsedPercentage is not { } pu)
        {
            return fresh;
        }

        var pr = prev.ResetsAt ?? 0;
        var ir = incoming.ResetsAt ?? 0;
        var iu = incoming.UsedPercentage ?? 0;
        if (ir > pr || (ir == pr && iu > pu))
        {
            return fresh;
        }

        RateWindow Keep(Dictionary<string, WindowSighting>? candidates) => new() { UsedPercentage = prev.UsedPercentage, ResetsAt = prev.ResetsAt, ObservedAt = prev.ObservedAt, LastSeen = seen, Candidates = candidates is { Count: > 0 } ? candidates : null };
        if (ir < pr)
        {
            return Keep(prev.Candidates);
        }

        if (iu == pu)
        {
            // At the maximum: only a fresh snapshot (any session) withdraws the candidates.
            return Keep(canLower ? null : prev.Candidates);
        }

        // Same window, lower value.
        if (!canLower || session is null)
        {
            return Keep(prev.Candidates);
        }

        var candidates = prev.Candidates is null ? new Dictionary<string, WindowSighting>(StringComparer.Ordinal) : new Dictionary<string, WindowSighting>(prev.Candidates, StringComparer.Ordinal);
        if (candidates.TryGetValue(session, out var c) && c is { UsedPercentage: { } cu, ResetsAt: { } cr } && cr == ir && iu >= cu
            && ManaStore.ParseStamp(c.FirstSeenAt) is { } first && first <= now.AddMinutes(1))
        {
            if (ManaStore.ParseStamp(stamp) is { } at && at - first >= LowerConfirm)
            {
                lowered = new WindowLowered { Window = name, OldPct = pu, NewPct = iu, ResetsAt = ir, FirstSeenAt = c.FirstSeenAt, ConfirmedAt = stamp, Session = session };
                return new RateWindow { UsedPercentage = iu, ResetsAt = ir, ObservedAt = stamp, LastSeen = seen };
            }

            return Keep(candidates);
        }

        candidates[session] = new WindowSighting { UsedPercentage = iu, ResetsAt = ir, FirstSeenAt = stamp, SessionId = session };
        return Keep(candidates);
    }

    private static Dictionary<string, SessionEntry> MergeSessions(ManaFile? prev, StatuslineInput input, DateTimeOffset now)
    {
        var result = new Dictionary<string, SessionEntry>(StringComparer.Ordinal);
        foreach (var (id, e) in prev?.Sessions ?? [])
        {
            if (ManaStore.ParseStamp(e.WrittenAt) is { } at && now - at <= SessionTtl && at <= now.AddMinutes(1))
            {
                result[id] = e;
            }
        }

        if (input.SessionId is { } sid && input.ContextWindow?.UsedPercentage is { } used)
        {
            result[sid] = new SessionEntry { UsedPercentage = used, WrittenAt = ManaStore.Stamp(now), Model = input.Model?.Id ?? (prev?.Sessions is { } ps && ps.TryGetValue(sid, out var old) ? old.Model : null) };
        }

        return result;
    }

    private static bool Unchanged(ManaFile? prev, ManaFile next, DateTimeOffset now)
    {
        if (prev is null || ManaStore.ParseStamp(prev.WrittenAt) is not { } at || now - at >= Debounce || at > now)
        {
            return false;
        }

        return prev.RateLimitsStale == next.RateLimitsStale
            && prev.ContextWindow?.UsedPercentage == next.ContextWindow?.UsedPercentage
            && prev.RateLimits?.FiveHour?.UsedPercentage == next.RateLimits?.FiveHour?.UsedPercentage
            && prev.RateLimits?.FiveHour?.ResetsAt == next.RateLimits?.FiveHour?.ResetsAt
            && prev.RateLimits?.SevenDay?.UsedPercentage == next.RateLimits?.SevenDay?.UsedPercentage
            && prev.RateLimits?.SevenDay?.ResetsAt == next.RateLimits?.SevenDay?.ResetsAt
            && prev.RateLimits?.FiveHour?.LastSeen?.UsedPercentage == next.RateLimits?.FiveHour?.LastSeen?.UsedPercentage
            && prev.RateLimits?.SevenDay?.LastSeen?.UsedPercentage == next.RateLimits?.SevenDay?.LastSeen?.UsedPercentage
            && SameCandidates(prev.RateLimits?.SevenDay?.Candidates, next.RateLimits?.SevenDay?.Candidates)
            && prev.LastLowered?.ConfirmedAt == next.LastLowered?.ConfirmedAt;
    }

    private static bool SameCandidates(Dictionary<string, WindowSighting>? a, Dictionary<string, WindowSighting>? b)
    {
        if ((a?.Count ?? 0) != (b?.Count ?? 0))
        {
            return false;
        }

        return a is null || b is null || a.All(kv => b.TryGetValue(kv.Key, out var o) && o.UsedPercentage == kv.Value.UsedPercentage && o.FirstSeenAt == kv.Value.FirstSeenAt);
    }

    private static string Format(ManaFile m, ActiveFile? active, bool neutral, DateTimeOffset now, double? context)
    {
        var parts = new List<string>();
        var mark = m.RateLimitsStale ? "~" : "";
        if (m.RateLimits?.FiveHour is { UsedPercentage: { } five } w5)
        {
            var reset = w5.ResetsAt is { } r ? Remaining(DateTimeOffset.FromUnixTimeSeconds(r) - now) : "";
            parts.Add($"{(neutral ? "5h" : "MP")} {Pct(five)}{mark}{(reset.Length > 0 ? " ↻" + reset : "")}");
        }

        if (m.RateLimits?.SevenDay is { UsedPercentage: { } seven })
        {
            parts.Add($"{(neutral ? "7d" : "W")} {Pct(seven)}{mark}");
        }

        if (context is { } ctx)
        {
            parts.Add(neutral ? $"ctx {Pct(ctx)}" : $"HP {Pct(100 - ctx)}");
        }

        if (active is not null)
        {
            parts.Add($"{active.GateId} [{active.Rank}]");
            parts.Add(active.QuestId);
        }

        return string.Join(" · ", parts);
    }

    private static string Pct(double v) => Math.Round(v).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static string Remaining(TimeSpan t)
    {
        if (t <= TimeSpan.Zero)
        {
            return string.Empty;
        }

        return t.TotalHours >= 1
            ? FormattableString.Invariant($"{(int)t.TotalHours}h{t.Minutes:00}")
            : FormattableString.Invariant($"{(int)Math.Ceiling(t.TotalMinutes)}m");
    }
}
