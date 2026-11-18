using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Monarch.Application;

namespace Monarch.Cli;

/// <summary>
/// Background shells of subagents (ADR-0001 addendum, Q-0166). post-tool-use remembers one small file per shell outside any repo,
/// subagent-stop reports the ones still running as a ledger event and ends nothing. Only program name, pid and runtime are
/// ever recorded: never arguments, never a command line (the repo is public).
/// </summary>
internal static class BackgroundShells
{
    internal const string Unknown = "unbekannt";

    private const int MaxProgram = 32;
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private static readonly TimeSpan PsDeadline = TimeSpan.FromSeconds(2);

    private sealed record Shell(string Program, DateTimeOffset Started, string Command, string File);

    private static string Root(CliEnv env) => Path.Combine(env.Home, "local", "background-shells");

    private static bool IdOk(string? id) => !string.IsNullOrEmpty(id) && id.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-');

    /// <summary>Program name of a command: whole first word checked first, then the file name; never shortened.</summary>
    internal static string ProgramName(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return Unknown;
        }

        var trimmed = command.Trim();
        var end = trimmed.IndexOfAny([' ', '\t', '\r', '\n']);
        var word = end < 0 ? trimmed : trimmed[..end];
        if (!word.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '+' or '/' or '-'))
        {
            return Unknown;
        }

        var name = word[(word.LastIndexOf('/') + 1)..];
        return name.Length is 0 or > MaxProgram ? Unknown : name;
    }

    /// <summary>post-tool-use: remembers a background Bash shell of a subagent. Without a hit only fields are checked, no IO.</summary>
    internal static void Remember(HookInput input, CliEnv env)
    {
        if (string.IsNullOrWhiteSpace(input.AgentId) || input.ToolName != "Bash" || input.ToolResponse is not { ValueKind: JsonValueKind.Object } response
            || !response.TryGetProperty("backgroundTaskId", out var idElement) || idElement.ValueKind != JsonValueKind.String)
        {
            return;
        }

        var taskId = idElement.GetString();
        if (!IdOk(input.AgentId) || !IdOk(taskId))
        {
            return;
        }

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("program", ProgramName(input.ToolInput?.Command));
            w.WriteString("started_at", env.Clock.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            w.WriteEndObject();
        }

        Files.WriteAtomic(Path.Combine(Root(env), input.AgentId, taskId + ".json"), Encoding.UTF8.GetString(ms.ToArray()));
    }

    /// <summary>subagent-stop: returns the shells to report (null when none) after cleaning up. Never throws for a single bad file.</summary>
    internal static JsonElement? Report(HookInput input, CliEnv env, Action<Exception> log)
    {
        var root = Root(env);
        if (!Directory.Exists(root))
        {
            return null;
        }

        var now = env.Clock.UtcNow;
        var running = Running(input.BackgroundTasks);
        var shells = new List<Shell>();
        var stale = new List<string>();
        if (!string.IsNullOrWhiteSpace(input.AgentId) && IdOk(input.AgentId) && Directory.Exists(Path.Combine(root, input.AgentId)))
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, input.AgentId), "*.json"))
            {
                try
                {
                    var (program, started) = ReadTask(file);
                    if (running.TryGetValue(Path.GetFileNameWithoutExtension(file), out var command))
                    {
                        shells.Add(new Shell(program, started, command, file));
                    }
                    else
                    {
                        stale.Add(file);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    log(ex);
                }
            }
        }

        JsonElement? result = null;
        if (shells.Count > 0)
        {
            int?[] pids;
            try
            {
                pids = FindPids(shells, env, log);
            }
            catch (Exception ex)
            {
                log(ex);
                pids = new int?[shells.Count];
            }

            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartArray();
                for (var i = 0; i < shells.Count; i++)
                {
                    w.WriteStartObject();
                    w.WriteString("program", shells[i].Program);
                    if (pids[i] is { } pid)
                    {
                        w.WriteNumber("pid", pid);
                    }

                    w.WriteNumber("runtime_s", Math.Max(0, (long)(now - shells[i].Started).TotalSeconds));
                    w.WriteEndObject();
                }

                w.WriteEndArray();
            }

            using var doc = JsonDocument.Parse(ms.ToArray());
            result = doc.RootElement.Clone();
            stale.AddRange(shells.Select(s => s.File));
        }

        foreach (var file in stale)
        {
            TryDelete(file, log);
        }

        Sweep(root, now, log);
        return result;
    }

    private static Dictionary<string, string> Running(JsonElement? tasks)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tasks is not { ValueKind: JsonValueKind.Array } list)
        {
            return result;
        }

        foreach (var t in list.EnumerateArray())
        {
            if (t.ValueKind == JsonValueKind.Object
                && t.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "shell"
                && t.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String && status.GetString() == "running"
                && t.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { Length: > 0 } key)
            {
                result[key] = t.TryGetProperty("command", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? string.Empty : string.Empty;
            }
        }

        return result;
    }

    private static (string Program, DateTimeOffset Started) ReadTask(string file)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var program = doc.RootElement.GetProperty("program").GetString();
        var started = DateTimeOffset.Parse(doc.RootElement.GetProperty("started_at").GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return (string.IsNullOrEmpty(program) ? Unknown : program, started);
    }

    private static void TryDelete(string file, Action<Exception> log)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log(ex);
        }
    }

    // Task files older than 24 h (all agents), so nothing stays when a SubagentStop never comes. A broken file is skipped and logged.
    private static void Sweep(string root, DateTimeOffset now, Action<Exception> log)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    try
                    {
                        var started = ReadTask(file).Started;
                        if (now - started > MaxAge)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or FormatException)
                    {
                        log(ex);
                        try
                        {
                            if (now - new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero) > MaxAge)
                            {
                                File.Delete(file);
                            }
                        }
                        catch (Exception inner) when (inner is IOException or UnauthorizedAccessException)
                        {
                            log(inner);
                        }
                    }
                }

                try
                {
                    Directory.Delete(dir, false); // only succeeds when empty
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log(ex);
        }
    }

    private static string Encode(string command) => "eval '" + command.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static int?[] FindPids(List<Shell> shells, CliEnv env, Action<Exception> log)
    {
        var text = env.ProcessList is { } inject ? inject() : RealPs(env);
        var result = new int?[shells.Count];
        if (text is null)
        {
            // Only the deadline, never a command line, goes to hook-errors.log.
            if (env.ProcessList is null)
            {
                log(new TimeoutException($"ps: Frist {(int)PsDeadline.TotalMilliseconds} ms überschritten"));
            }

            return result;
        }

        var rows = new List<(int Pid, int Ppid, string Line)>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var a = line.IndexOf(' ');
            if (a <= 0)
            {
                continue;
            }

            var rest = line[(a + 1)..].TrimStart();
            var b = rest.IndexOf(' ');
            if (b <= 0 || !int.TryParse(line[..a], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
                || !int.TryParse(rest[..b], NumberStyles.None, CultureInfo.InvariantCulture, out var ppid))
            {
                continue;
            }

            rows.Add((pid, ppid, rest[(b + 1)..].TrimStart()));
        }

        int? parent = env.HookParentPid;
        if (parent is null)
        {
            foreach (var r in rows)
            {
                if (r.Pid == Environment.ProcessId)
                {
                    parent = r.Ppid;
                }
            }
        }

        if (parent is null)
        {
            return result;
        }

        for (var i = 0; i < shells.Count; i++)
        {
            if (shells[i].Command.Length == 0)
            {
                continue;
            }

            var needle = Encode(shells[i].Command);
            var hits = rows.Where(r => r.Ppid == parent && r.Line.Contains(needle, StringComparison.Ordinal)).Take(2).ToList();
            if (hits.Count == 1)
            {
                result[i] = hits[0].Pid;
            }
            else
            {
                // Only the count, never the command line.
                log(new InvalidOperationException(hits.Count == 0 ? "ps: 0 Treffer für eine Hintergrund-Shell" : "ps: mehr als 1 Treffer für eine Hintergrund-Shell"));
            }
        }

        return result;
    }

    // ps from an absolute PATH entry (ADR-0016), output read via awaited tasks like GitAnchor; after the deadline the hook stops
    // waiting and does NOT end ps.
    private static string? RealPs(CliEnv env)
    {
        var ps = new ToolLocator(env.GetVar("PATH"), TextWriter.Null).Locate("ps") ?? throw new FileNotFoundException("ps nicht in PATH gefunden");
        var psi = new ProcessStartInfo(ps) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
        psi.ArgumentList.Add("-ww");
        psi.ArgumentList.Add("-A");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add("pid=,ppid=,command=");
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("ps nicht gestartet");
        var drainOut = p.StandardOutput.ReadToEndAsync();
        _ = p.StandardError.ReadToEndAsync();
        p.StandardInput.Close();
        return drainOut.Wait(PsDeadline) ? drainOut.Result : null;
    }
}
