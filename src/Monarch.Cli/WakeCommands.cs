using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using Monarch.Domain;

namespace Monarch.Cli;

/// <summary>The job a platform installs: it runs <c>monarch wake fire</c> once, after the wake time plus grace.</summary>
/// <param name="Env">Calling environment (PATH, HOME, clock).</param>
/// <param name="Paths">The project.</param>
/// <param name="Session">Session UUID.</param>
/// <param name="At">Wake time (resets_at + 3 min).</param>
/// <param name="MonarchBin">Absolute path of the monarch binary.</param>
/// <param name="ClaudeBin">Absolute path of the claude binary.</param>
internal sealed record WakeJob(CliEnv Env, ProjectPaths Paths, string Session, DateTimeOffset At, string MonarchBin, string ClaudeBin)
{
    /// <summary>Fire time: the wake time plus the grace that lets the in-session cron go first.</summary>
    public DateTimeOffset FireAt => At + WakePlan.Grace;

    /// <summary>Arguments after the binary: no shell, no free text.</summary>
    public IReadOnlyList<string> FireArguments => ["wake", "fire", Session, "--at", WakeCommands.Iso(At)];
}

/// <summary>A scheduler backend (launchd, systemd). Injected via <c>mana.wake</c>, so both run on every OS under test.</summary>
internal interface IWakePlatform
{
    /// <summary>Installs the job; returns an error text or null.</summary>
    string? Install(WakeJob job);

    /// <summary>Removes the job; always last, because on launchd a bootout also ends the running job.</summary>
    void Remove(WakeJob job);
}

internal static class WakeProcess
{
    /// <summary>Numeric user id for <c>gui/&lt;uid&gt;</c>, from the system <c>id -u</c> (absolute path, no PATH lookup); null when unavailable.</summary>
    public static string? Uid(string workingDirectory)
    {
        var (code, output) = Run("/usr/bin/id", ["-u"], workingDirectory);
        return code == 0 && uint.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var uid) ? uid.ToString(CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Finds an executable on the PATH of the calling environment; returns an absolute path or null.</summary>
    public static string? Which(CliEnv env, string name)
    {
        foreach (var dir in (env.GetVar("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Path.IsPathRooted(dir) && Path.Combine(dir, name) is var candidate && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>Runs a process without a shell; a timeout kills it and counts as failure.</summary>
    public static (int Code, string Out) Run(string file, IReadOnlyList<string> args, string workingDirectory)
    {
        var psi = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
            {
                return (-1, string.Empty);
            }

            p.StandardInput.Close();
            var stderr = p.StandardError.ReadToEndAsync();
            var stdout = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(TimeSpan.FromSeconds(120)))
            {
                p.Kill(true);
                return (-1, string.Empty);
            }

            stderr.GetAwaiter().GetResult();
            return (p.ExitCode, stdout.GetAwaiter().GetResult());
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (-1, string.Empty);
        }
    }
}

/// <summary>macOS: a user LaunchAgent in <c>$HOME/Library/LaunchAgents</c>.</summary>
internal sealed class LaunchdPlatform : IWakePlatform
{
    /// <summary>Minimal PATH of the job; the absolute claude path travels separately.</summary>
    public const string JobPath = "/usr/bin:/bin:/usr/local/bin:/opt/homebrew/bin";

    private static string? Home(CliEnv env) => env.GetVar("HOME") is { Length: > 0 } h && Path.IsPathRooted(h) ? h : null;

    public static string Label(string session) => "dev.monarch.wake." + session;

    public static string? PlistPath(CliEnv env, string session) => Home(env) is { } h ? Path.Combine(h, "Library", "LaunchAgents", Label(session) + ".plist") : null;

    /// <inheritdoc />
    public string? Install(WakeJob job)
    {
        if (PlistPath(job.Env, job.Session) is not { } plist)
        {
            return "HOME is not set; refusing to guess a LaunchAgents directory.";
        }

        if (WakeProcess.Which(job.Env, "launchctl") is not { } launchctl)
        {
            return "launchctl not found on PATH.";
        }

        var local = TimeZoneInfo.ConvertTime(job.FireAt, TimeZoneInfo.Local);
        var sb = new StringBuilder();
        string S(string v) => SecurityElement.Escape(v);
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        sb.AppendLine("<plist version=\"1.0\">");
        sb.AppendLine("<dict>");
        sb.AppendLine($"  <key>Label</key><string>{S(Label(job.Session))}</string>");
        sb.AppendLine("  <key>ProgramArguments</key>");
        sb.AppendLine("  <array>");
        sb.AppendLine($"    <string>{S(job.MonarchBin)}</string>");
        foreach (var a in job.FireArguments)
        {
            sb.AppendLine($"    <string>{S(a)}</string>");
        }

        sb.AppendLine("  </array>");
        sb.AppendLine($"  <key>WorkingDirectory</key><string>{S(job.Paths.Root)}</string>");
        sb.AppendLine("  <key>EnvironmentVariables</key>");
        sb.AppendLine("  <dict>");
        sb.AppendLine($"    <key>PATH</key><string>{JobPath}</string>");
        sb.AppendLine($"    <key>MONARCH_CLAUDE_BIN</key><string>{S(job.ClaudeBin)}</string>");
        sb.AppendLine("  </dict>");
        sb.AppendLine("  <key>RunAtLoad</key><true/>");
        sb.AppendLine("  <key>StartCalendarInterval</key>");
        sb.AppendLine("  <dict>");
        sb.AppendLine($"    <key>Month</key><integer>{local.Month}</integer>");
        sb.AppendLine($"    <key>Day</key><integer>{local.Day}</integer>");
        sb.AppendLine($"    <key>Hour</key><integer>{local.Hour}</integer>");
        sb.AppendLine($"    <key>Minute</key><integer>{local.Minute}</integer>");
        sb.AppendLine("  </dict>");
        sb.AppendLine("</dict>");
        sb.AppendLine("</plist>");
        Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
        File.WriteAllText(plist, sb.ToString());

        if (WakeProcess.Uid(job.Paths.Root) is not { } uid)
        {
            File.Delete(plist);
            return "cannot determine the user id (id -u).";
        }

        var (code, _) = WakeProcess.Run(launchctl, ["bootstrap", "gui/" + uid, plist], job.Paths.Root);
        if (code != 0)
        {
            File.Delete(plist);
            return $"launchctl bootstrap failed (exit {code}).";
        }

        return null;
    }

    /// <inheritdoc />
    public void Remove(WakeJob job)
    {
        if (PlistPath(job.Env, job.Session) is { } plist && File.Exists(plist))
        {
            File.Delete(plist);
        }

        if (WakeProcess.Which(job.Env, "launchctl") is { } launchctl && WakeProcess.Uid(job.Paths.Root) is { } uid)
        {
            job.Env.Out.Flush();
            WakeProcess.Run(launchctl, ["bootout", $"gui/{uid}/{Label(job.Session)}"], job.Paths.Root);
        }
    }
}

/// <summary>Linux: a transient <c>systemd-run --user</c> timer. It does not survive a reboot (ADR-0008, limit).</summary>
internal sealed class SystemdPlatform : IWakePlatform
{
    public static string Unit(string session) => "monarch-wake-" + session;

    /// <inheritdoc />
    public string? Install(WakeJob job)
    {
        if (WakeProcess.Which(job.Env, "systemd-run") is not { } run)
        {
            return "systemd-run not found on PATH.";
        }

        var when = job.FireAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
        List<string> args =
        [
            "--user", "--unit=" + Unit(job.Session), "--on-calendar=" + when, "--collect",
            "--working-directory=" + job.Paths.Root,
            "--setenv=MONARCH_CLAUDE_BIN=" + job.ClaudeBin,
            job.MonarchBin, .. job.FireArguments,
        ];
        var (code, _) = WakeProcess.Run(run, args, job.Paths.Root);
        return code == 0 ? null : $"systemd-run failed (exit {code}).";
    }

    /// <inheritdoc />
    public void Remove(WakeJob job)
    {
        if (WakeProcess.Which(job.Env, "systemctl") is { } ctl)
        {
            job.Env.Out.Flush();
            WakeProcess.Run(ctl, ["--user", "stop", Unit(job.Session) + ".timer"], job.Paths.Root);
        }
    }
}

/// <summary>
/// <c>monarch wake schedule|fire|cancel</c> (ADR-0008): the out-of-session wake call. Opt-in via the protected <c>mana.wake</c> in system.yaml;
/// the prompt is a fixed template, never agent text.
/// </summary>
internal static class WakeCommands
{
    private static readonly string[] IdleStatuses = ["idle"];

    /// <summary>Round-trip UTC stamp used for <c>--at</c>.</summary>
    public static string Iso(DateTimeOffset t) => ManaSupport.Iso(t);

    private static int Refuse(CliEnv env, string message)
    {
        env.Err.WriteLine($"refused: {message}");
        return 1;
    }

    private static IWakePlatform? PlatformFor(string wake) => wake switch
    {
        "launchd" => new LaunchdPlatform(),
        "systemd" => new SystemdPlatform(),
        _ => null,
    };

    private static string RecordFile(ProjectPaths paths, string session) => Path.Combine(paths.Local, "wake", session + ".json");

    private static bool TryAt(string text, out DateTimeOffset at) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out at);

    /// <summary>Common checks of every wake command; returns an error or null.</summary>
    private static string? Check(CliEnv env, string session, string atText, out ProjectPaths? paths, out DateTimeOffset at)
    {
        paths = null;
        at = default;
        if (!string.IsNullOrWhiteSpace(env.GetVar("MONARCH_AGENT_TYPE")))
        {
            return "wake commands are for the main session, not for a shadow.";
        }

        if (!WakePlan.IsSessionId(session))
        {
            return "--session must be a session UUID (8-4-4-4-12 hex).";
        }

        if (!TryAt(atText, out at))
        {
            return "--at must be an ISO-8601 timestamp.";
        }

        paths = ProjectPaths.Locate(env.Cwd, env.Home);
        return paths is null ? "not an awakened project." : null;
    }

    private static string? Platform(CliEnv env, ProjectPaths paths, out IWakePlatform? platform, out string name)
    {
        name = SystemConfigReader.Load(paths.Root).Wake;
        platform = PlatformFor(name);
        return platform is null ? "the wake call is off: the owner enables it with 'mana.wake: launchd|systemd' in system.yaml (default off)." : null;
    }

    /// <summary>Installs the one-shot job for a session.</summary>
    public static int Schedule(CliEnv env, string session, string atText)
    {
        if (Check(env, session, atText, out var paths, out var at) is { } error)
        {
            return Refuse(env, error);
        }

        if (Platform(env, paths!, out var platform, out var name) is { } off)
        {
            return Refuse(env, off);
        }

        if (WakePlan.Classify(env.Clock.UtcNow, at) == WakeWindow.Stale)
        {
            return Refuse(env, $"--at {Iso(at)} is older than {WakePlan.MaxAge.TotalHours:0} h.");
        }

        var record = RecordFile(paths!, session);
        if (File.Exists(record))
        {
            return Refuse(env, $"a wake job for {session} already exists (monarch wake cancel {session} first).");
        }

        var monarchBin = Path.Combine(env.Home, "bin", "monarch");
        if (!File.Exists(monarchBin))
        {
            return Refuse(env, $"monarch binary not found at {monarchBin}.");
        }

        if (WakeProcess.Which(env, "claude") is not { } claudeBin)
        {
            return Refuse(env, "claude not found on PATH.");
        }

        var job = new WakeJob(env, paths!, session, at, monarchBin, claudeBin);
        if (platform!.Install(job) is { } failure)
        {
            return Refuse(env, failure);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(record)!);
        Files.WriteAtomic(record, RecordJson(session, at, name, claudeBin));
        var ledger = new ProjectLedger(paths!, env.Clock);
        ledger.Append(ledger.SessionId(session), "cli", "WakeScheduled", ("session", session), ("at", Iso(at)), ("fireAt", Iso(job.FireAt)), ("platform", name));
        env.Out.WriteLine($"wake scheduled ({name}) for session {session}: wake {Iso(at)}, job fires at {Iso(job.FireAt)}.");
        return 0;
    }

    /// <summary>Removes the job of a session.</summary>
    public static int Cancel(CliEnv env, string session)
    {
        if (Check(env, session, Iso(env.Clock.UtcNow), out var paths, out _) is { } error)
        {
            return Refuse(env, error);
        }

        if (ReadRecord(paths!, session) is not { } rec)
        {
            return Refuse(env, $"no wake job for {session}.");
        }

        Remove(env, paths!, session, rec);
        var ledger = new ProjectLedger(paths!, env.Clock);
        ledger.Append(ledger.SessionId(session), "cli", "WakeCancelled", ("session", session), ("platform", rec.Platform));
        env.Out.WriteLine($"wake job for {session} cancelled.");
        return 0;
    }

    private static string RecordJson(string session, DateTimeOffset at, string platform, string claude)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("session", session);
            w.WriteString("at", Iso(at));
            w.WriteString("platform", platform);
            w.WriteString("claude", claude);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private sealed record Record(string Platform, DateTimeOffset At, string? Claude);

    private static Record? ReadRecord(ProjectPaths paths, string session)
    {
        var file = RecordFile(paths, session);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var r = doc.RootElement;
            return TryAt(r.GetProperty("at").GetString() ?? string.Empty, out var at)
                ? new Record(r.GetProperty("platform").GetString() ?? "off", at, r.TryGetProperty("claude", out var c) ? c.GetString() : null)
                : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>Deletes the record, then the job (last: a launchd bootout ends the running job).</summary>
    private static void Remove(CliEnv env, ProjectPaths paths, string session, Record rec)
    {
        var file = RecordFile(paths, session);
        if (File.Exists(file))
        {
            File.Delete(file);
        }

        if (PlatformFor(rec.Platform) is { } platform)
        {
            platform.Remove(new WakeJob(env, paths, session, rec.At, string.Empty, rec.Claude ?? string.Empty));
        }
    }

    /// <summary>Runs at wake time plus grace: resumes a background session that did not wake up by itself.</summary>
    public static int Fire(CliEnv env, string session, string atText)
    {
        if (Check(env, session, atText, out var paths, out var at) is { } error)
        {
            return Refuse(env, error);
        }

        if (ReadRecord(paths!, session) is not { } rec || rec.At != at)
        {
            return Refuse(env, $"no scheduled wake job for {session} at {Iso(at)}.");
        }

        var window = WakePlan.Classify(env.Clock.UtcNow, at);
        if (window == WakeWindow.NotYet)
        {
            env.Out.WriteLine($"not due yet (wake {Iso(at)} + grace); job stays.");
            return 0;
        }

        var ledger = new ProjectLedger(paths!, env.Clock);
        var sid = ledger.SessionId(session);

        int Skip(string reason)
        {
            ledger.Append(sid, "cli", "WakeSkipped", ("session", session), ("at", Iso(at)), ("reason", reason));
            env.Out.WriteLine($"wake skipped: {reason}");
            Remove(env, paths!, session, rec);
            return 0;
        }

        if (window == WakeWindow.Stale)
        {
            return Skip("stale");
        }

        var claude = env.GetVar("MONARCH_CLAUDE_BIN") is { Length: > 0 } c && Path.IsPathRooted(c) ? c : rec.Claude ?? WakeProcess.Which(env, "claude");
        if (claude is null || !File.Exists(claude))
        {
            return Skip("claude-not-found");
        }

        var (agentsCode, agentsOut) = WakeProcess.Run(claude, ["agents", "--json"], paths!.Root);
        if (agentsCode != 0 || FindSession(agentsOut, session) is not { } entry)
        {
            return Skip(agentsCode != 0 || !ValidJson(agentsOut) ? "agents-failed" : "session-not-listed");
        }

        if (entry.Busy)
        {
            return Skip("session-running");
        }

        if (!entry.Idle)
        {
            return Skip("session-not-idle");
        }

        var resetsAt = WakePlan.ResetsAt(at);
        if (ledger.ReadSession(sid).Any(e => e.At >= resetsAt && !e.Type.StartsWith("Wake", StringComparison.Ordinal)))
        {
            return Skip("activity-since-reset");
        }

        if (entry.Kind != "background")
        {
            return Skip(entry.Kind == "interactive" ? "interactive-session" : "unsupported-kind");
        }

        if (WakeProcess.Run(claude, ["stop", entry.Id], paths.Root).Code != 0)
        {
            return Skip("stop-failed");
        }

        var prompt = WakePlan.Prompt(paths.RecapFile);
        if (WakeProcess.Run(claude, ["--bg", "--resume", session, prompt], paths.Root).Code != 0)
        {
            return Skip("resume-failed");
        }

        ledger.Append(sid, "cli", "WakeFired", ("session", session), ("at", Iso(at)), ("action", "stop+resume"));
        env.Out.WriteLine($"wake fired: session {session} stopped and resumed in the background.");
        Remove(env, paths, session, rec);
        return 0;
    }

    private sealed record Entry(string Id, string Kind, bool Busy, bool Idle);

    private static bool ValidJson(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Entry? FindSession(string json, string session)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var e in doc.RootElement.EnumerateArray())
            {
                string? Str(string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!string.Equals(Str("sessionId"), session, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = Str("status");
                var state = Str("state");
                var busy = status == "busy" || state == "working";
                var idle = !busy && (status is not null && Array.IndexOf(IdleStatuses, status) >= 0 || state == "done");
                return new Entry(Str("id") ?? session, Str("kind") ?? string.Empty, busy, idle);
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
