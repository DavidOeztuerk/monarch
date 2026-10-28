using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Monarch.Cli;

/// <summary>Statusline sensor and display (ADR-0004). Never fails visibly.</summary>
internal static class Statusline
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(10);
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
        var next = Merge(prev, input, now);
        if (!Unchanged(prev, next, now))
        {
            Files.WriteAtomic(ManaStore.PathOf(env.Home), JsonSerializer.Serialize(next, CliJson.Default.ManaFile));
        }

        var cwd = input.Workspace?.CurrentDir ?? input.Cwd ?? env.Cwd;
        var paths = ProjectPaths.Locate(cwd, env.Home);
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

    private static ManaFile Merge(ManaFile? prev, StatuslineInput input, DateTimeOffset now)
    {
        string stamp;
        var fresh5 = input.RateLimits?.FiveHour is { UsedPercentage: not null };
        var fresh7 = input.RateLimits?.SevenDay is { UsedPercentage: not null };
        stamp = ManaStore.Stamp(now);
        var limits = new RateLimits
        {
            FiveHour = MergeWindow(prev?.RateLimits?.FiveHour, fresh5 ? input.RateLimits!.FiveHour : null, stamp),
            SevenDay = MergeWindow(prev?.RateLimits?.SevenDay, fresh7 ? input.RateLimits!.SevenDay : null, stamp),
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
        };
    }

    /// <summary>Rate limits are account-global: the later <c>resets_at</c> wins, and within one window the higher usage wins (usage is monotonic per window).</summary>
    private static RateWindow? MergeWindow(RateWindow? prev, RateWindow? incoming, string stamp)
    {
        if (incoming is null)
        {
            return prev;
        }

        var fresh = new RateWindow { UsedPercentage = incoming.UsedPercentage, ResetsAt = incoming.ResetsAt, ObservedAt = stamp };
        if (prev?.UsedPercentage is not { } pu)
        {
            return fresh;
        }

        var pr = prev.ResetsAt ?? 0;
        var ir = incoming.ResetsAt ?? 0;
        if (ir > pr || (ir == pr && incoming.UsedPercentage > pu))
        {
            return fresh;
        }

        return prev;
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
            result[sid] = new SessionEntry { UsedPercentage = used, WrittenAt = ManaStore.Stamp(now) };
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
            && prev.RateLimits?.SevenDay?.ResetsAt == next.RateLimits?.SevenDay?.ResetsAt;
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
