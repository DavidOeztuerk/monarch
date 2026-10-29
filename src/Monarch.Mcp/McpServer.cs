using System.Text.Json;

namespace Monarch.Mcp;

/// <summary>
/// Minimal MCP server (protocol 2025-06-18) over newline-delimited JSON-RPC 2.0. Hand-written with <see cref="Utf8JsonWriter"/>
/// and <see cref="JsonDocument"/> so it is Native-AOT clean: no reflection, no tool discovery by attributes.
/// </summary>
public sealed class McpServer
{
    /// <summary>The protocol version this server speaks.</summary>
    public const string ProtocolVersion = "2025-06-18";

    private readonly IReadOnlyList<McpTool> _tools;
    private readonly string _name;
    private readonly string _version;
    private readonly McpServerOptions _options;
    private readonly string _serverId = Guid.NewGuid().ToString("N")[..8];
    private readonly Queue<string> _deferred = new();
    private TextWriter? _output;
    private bool _rootsDeclared;
    private bool _eof;
    private IReadOnlyList<string> _roots = [];
    private string? _pendingId;
    private long _pendingSince;
    private int _rootsSeq;

    /// <summary>Initializes the server.</summary>
    /// <param name="tools">The tools to offer.</param>
    /// <param name="name">Server name reported in <c>initialize</c>.</param>
    /// <param name="version">Server version reported in <c>initialize</c>.</param>
    /// <param name="options">Stderr, roots log and the roots timeout; null uses defaults.</param>
    public McpServer(IReadOnlyList<McpTool> tools, string name, string version, McpServerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        (_tools, _name, _version) = (tools, name, version);
        _options = options ?? new McpServerOptions();
    }

    /// <summary>Serves requests until the input ends. Every response is written as one line and flushed.</summary>
    /// <param name="input">Request lines.</param>
    /// <param name="output">Response lines.</param>
    /// <returns>Zero.</returns>
    public int Run(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
        var queue = new System.Collections.Concurrent.BlockingCollection<string?>();
        var reader = new Thread(() =>
        {
            try
            {
                while (input.ReadLine() is { } l)
                {
                    queue.Add(l);
                }
            }
            catch (IOException)
            {
                // Input closed under us: same as end of input.
            }
            finally
            {
                queue.Add(null);
            }
        })
        { IsBackground = true };
        reader.Start();
        while (Next(queue) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = Handle(line, queue);
            if (response is not null)
            {
                Send(response);
            }
        }

        return 0;
    }

    private string? Next(System.Collections.Concurrent.BlockingCollection<string?> queue)
    {
        if (_deferred.Count > 0)
        {
            return _deferred.Dequeue();
        }

        return _eof ? null : queue.Take();
    }

    private void Send(string line)
    {
        _output!.Write(line);
        _output.Write('\n');
        _output.Flush();
    }

    /// <summary>Handles one message without a request queue (no roots round trip is awaited).</summary>
    /// <param name="line">The JSON-RPC message.</param>
    /// <returns>The response line, or null for notifications.</returns>
    public string? Handle(string line) => Handle(line, null);

    private string? Handle(string line, System.Collections.Concurrent.BlockingCollection<string?>? queue)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return Error(null, -32700, "Parse error");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && !root.TryGetProperty("method", out _) && root.TryGetProperty("id", out var rid)
                && (root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _)))
            {
                OnResponse(rid, root);
                return null;
            }

            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String)
            {
                return Error(null, -32600, "Invalid request");
            }

            var hasId = root.TryGetProperty("id", out var id);
            var method = m.GetString()!;
            if (!hasId)
            {
                OnNotification(method);
                return null;
            }

            var args = root.TryGetProperty("params", out var p) ? p : default;
            return method switch
            {
                "initialize" => Initialize(id, args),
                "ping" => Result(id, _ => { }),
                "tools/list" => Result(id, WriteTools),
                "tools/call" => Call(id, args, queue),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
        }
    }

    private string Initialize(JsonElement id, JsonElement args)
    {
        _rootsDeclared = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("capabilities", out var caps)
            && caps.ValueKind == JsonValueKind.Object && caps.TryGetProperty("roots", out var r) && r.ValueKind == JsonValueKind.Object;
        return Result(id, w =>
        {
            w.WriteString("protocolVersion", ProtocolVersion);
            w.WriteStartObject("capabilities");
            w.WriteStartObject("tools");
            w.WriteBoolean("listChanged", false);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartObject("serverInfo");
            w.WriteString("name", _name);
            w.WriteString("version", _version);
            w.WriteEndObject();
        });
    }

    private void OnNotification(string method)
    {
        if (!_rootsDeclared)
        {
            return;
        }

        if (method is "notifications/initialized" or "notifications/roots/list_changed")
        {
            _pendingId = "roots-" + (++_rootsSeq).ToString(System.Globalization.CultureInfo.InvariantCulture);
            _pendingSince = System.Diagnostics.Stopwatch.GetTimestamp();
            Send($$"""{"jsonrpc":"2.0","id":"{{_pendingId}}","method":"roots/list"}""");
        }
    }

    private void OnResponse(JsonElement id, JsonElement root)
    {
        if (_pendingId is null || id.ValueKind != JsonValueKind.String || id.GetString() != _pendingId)
        {
            return;
        }

        _pendingId = null;
        var roots = new List<string>();
        string? failure = null;
        if (root.TryGetProperty("error", out var err))
        {
            failure = "roots/list failed" + (err.ValueKind == JsonValueKind.Object && err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String ? ": " + msg.GetString() : string.Empty);
        }
        else if (root.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.Object && res.TryGetProperty("roots", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("uri", out var u) && u.ValueKind == JsonValueKind.String
                    && McpRoots.TryParseFileUri(u.GetString()!, out var path))
                {
                    roots.Add(path);
                }
            }
        }

        _roots = roots;
        ReportRoots(roots, failure);
    }

    private void ReportRoots(IReadOnlyList<string> roots, string? note)
    {
        var line = "mcp: roots=[" + string.Join(", ", roots) + "]" + (note is null ? string.Empty : " (" + note + ")");
        _options.Err?.WriteLine(line);
        _options.Err?.Flush();
        if (!string.IsNullOrEmpty(_options.RootsLogPath))
        {
            try
            {
                File.AppendAllText(_options.RootsLogPath, line + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _options.Err?.WriteLine("mcp: cannot write roots log: " + ex.Message);
            }
        }
    }

    /// <summary>Blocks until the outstanding roots/list request is answered, the injected timeout passes or the input ends; other lines are deferred.</summary>
    private void AwaitRoots(System.Collections.Concurrent.BlockingCollection<string?>? queue)
    {
        while (_pendingId is not null)
        {
            if (queue is null || _eof)
            {
                Abandon("no answer before end of input");
                return;
            }

            var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(_pendingSince);
            var left = _options.RootsTimeout - elapsed;
            if (left <= TimeSpan.Zero || !queue.TryTake(out var line, left))
            {
                Abandon("timeout");
                return;
            }

            if (line is null)
            {
                _eof = true;
                continue;
            }

            if (IsResponse(line))
            {
                Handle(line, null);
            }
            else
            {
                _deferred.Enqueue(line);
            }
        }
    }

    private void Abandon(string why)
    {
        _pendingId = null;
        _roots = [];
        ReportRoots([], why);
    }

    private static bool IsResponse(string line)
    {
        try
        {
            using var d = JsonDocument.Parse(line);
            return d.RootElement.ValueKind == JsonValueKind.Object && !d.RootElement.TryGetProperty("method", out _) && d.RootElement.TryGetProperty("id", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private void WriteTools(Utf8JsonWriter w)
    {
        w.WriteStartArray("tools");
        foreach (var t in _tools)
        {
            w.WriteStartObject();
            w.WriteString("name", t.Name);
            w.WriteString("description", t.Description);
            w.WritePropertyName("inputSchema");
            using (var schema = JsonDocument.Parse(t.InputSchema))
            {
                schema.RootElement.WriteTo(w);
            }

            w.WriteStartObject("annotations");
            w.WriteBoolean("readOnlyHint", t.ReadOnly);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        w.WriteEndArray();
    }

    private string Call(JsonElement id, JsonElement args, System.Collections.Concurrent.BlockingCollection<string?>? queue)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("name", out var n) || n.ValueKind != JsonValueKind.String)
        {
            return Error(id, -32602, "tools/call requires params.name");
        }

        var tool = _tools.FirstOrDefault(t => t.Name == n.GetString());
        if (tool is null)
        {
            return Error(id, -32602, $"Unknown tool: {n.GetString()}");
        }

        var arguments = args.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object ? a : JsonDocument.Parse("{}").RootElement;
        AwaitRoots(queue);
        McpToolResult result;
        try
        {
            result = tool.Handler(arguments, new McpCallContext(_rootsDeclared, _roots, _serverId));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or JsonException or FormatException or UnauthorizedAccessException or HttpRequestException)
        {
            result = new McpToolResult($"error: {ex.Message}", true);
        }

        return Result(id, w =>
        {
            w.WriteStartArray("content");
            w.WriteStartObject();
            w.WriteString("type", "text");
            w.WriteString("text", result.Text);
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteBoolean("isError", result.IsError);
        });
    }

    private static string Result(JsonElement id, Action<Utf8JsonWriter> body)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            id.WriteTo(w);
            w.WriteStartObject("result");
            body(w);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    private static string Error(JsonElement? id, int code, string message)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("jsonrpc", "2.0");
            w.WritePropertyName("id");
            if (id is { } i)
            {
                i.WriteTo(w);
            }
            else
            {
                w.WriteNullValue();
            }

            w.WriteStartObject("error");
            w.WriteNumber("code", code);
            w.WriteString("message", message);
            w.WriteEndObject();
            w.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }
}
