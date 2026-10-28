using System.Text.Json;
using Monarch.Application.Net;
using Monarch.Application.Ports;

namespace Monarch.Cli.Tests;

internal sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;
}

internal sealed record RunResult(int Code, string Out, string Err);

/// <summary>A temp directory acting as project root plus a separate temp monarch home.</summary>
internal sealed class TempProject : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    public string Root { get; } = Path.Combine(Path.GetTempPath(), "monarch-p-" + Guid.NewGuid().ToString("N"));
    public string Home { get; } = Path.Combine(Path.GetTempPath(), "monarch-h-" + Guid.NewGuid().ToString("N"));
    public FakeClock Clock { get; } = new(Now);

    public HttpMessageHandler? Http { get; set; }

    public TempProject(bool awakened = true, string? activeRank = null, string include = "src/**", string? systemYaml = null)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Home);
        if (awakened)
        {
            Directory.CreateDirectory(Path.Combine(Root, ".monarch", "local"));
            Directory.CreateDirectory(Path.Combine(Root, ".monarch", "ledger"));
            Directory.CreateDirectory(Path.Combine(Root, ".monarch", "quests"));
        }

        if (systemYaml is not null)
        {
            File.WriteAllText(Path.Combine(Root, ".monarch", "system.yaml"), systemYaml);
        }

        if (activeRank is not null)
        {
            File.WriteAllText(LocalPath("active.json"),
                $$"""{"questId":"Q-0003","gateId":"G-0001","rank":"{{activeRank}}","kind":"main","scope":{"include":["{{include}}"],"exclude":[]},"acceptedAt":"2026-10-27T10:00:00Z"}""");
        }
    }

    public string LocalPath(string name) => Path.Combine(Root, ".monarch", "local", name);

    public RunResult Run(string[] args, string? stdin = null, string? cwd = null, bool tty = false)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = MonarchApp.Run(args, new CliEnv(cwd ?? Root, Home, new StringReader(stdin ?? string.Empty), o, e, Clock, Http, new GitHubTokenProvider(_ => "tok-test", () => null), tty));
        return new RunResult(code, o.ToString(), e.ToString());
    }

    public RunResult Hook(string evt, string json) => Run(["hook", evt], json);

    public static string PreToolUse(string tool, string? file = null, string? command = null, string? mode = "default", string? agentType = null, string session = "sess1")
    {
        var input = file is not null ? $$"""{"file_path":{{JsonSerializer.Serialize(file)}}}""" : $$"""{"command":{{JsonSerializer.Serialize(command ?? string.Empty)}}}""";
        var agent = agentType is null ? "" : $$""","agent_id":"a1","agent_type":"{{agentType}}" """;
        var m = mode is null ? "" : $$""","permission_mode":"{{mode}}" """;
        return $$"""{"session_id":"{{session}}","hook_event_name":"PreToolUse"{{m}}{{agent}},"tool_name":"{{tool}}","tool_input":{{input}}}""";
    }

    public List<(string Type, JsonElement Payload)> Events()
    {
        var list = new List<(string, JsonElement)>();
        var dir = Path.Combine(Root, ".monarch", "ledger");
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadAllLines(f))
            {
                var doc = JsonDocument.Parse(line).RootElement;
                list.Add((doc.GetProperty("type").GetString()!, doc.GetProperty("payload").Clone()));
            }
        }

        return list;
    }

    public void Dispose()
    {
        foreach (var d in new[] { Root, Home })
        {
            try { Directory.Delete(d, true); } catch (IOException) { }
        }
    }
}
