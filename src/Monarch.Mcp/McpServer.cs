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

    /// <summary>Initializes the server.</summary>
    /// <param name="tools">The tools to offer.</param>
    /// <param name="name">Server name reported in <c>initialize</c>.</param>
    /// <param name="version">Server version reported in <c>initialize</c>.</param>
    public McpServer(IReadOnlyList<McpTool> tools, string name, string version)
    {
        ArgumentNullException.ThrowIfNull(tools);
        (_tools, _name, _version) = (tools, name, version);
    }

    /// <summary>Serves requests until the input ends. Every response is written as one line and flushed.</summary>
    /// <param name="input">Request lines.</param>
    /// <param name="output">Response lines.</param>
    /// <returns>Zero.</returns>
    public int Run(TextReader input, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        while (input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var response = Handle(line);
            if (response is not null)
            {
                output.Write(response);
                output.Write('\n');
                output.Flush();
            }
        }

        return 0;
    }

    /// <summary>Handles one message.</summary>
    /// <param name="line">The JSON-RPC message.</param>
    /// <returns>The response line, or null for notifications.</returns>
    public string? Handle(string line)
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
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var m) || m.ValueKind != JsonValueKind.String)
            {
                return Error(null, -32600, "Invalid request");
            }

            var hasId = root.TryGetProperty("id", out var id);
            var method = m.GetString()!;
            if (!hasId)
            {
                return null;
            }

            var args = root.TryGetProperty("params", out var p) ? p : default;
            return method switch
            {
                "initialize" => Result(id, w =>
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
                }),
                "ping" => Result(id, _ => { }),
                "tools/list" => Result(id, WriteTools),
                "tools/call" => Call(id, args),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
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

    private string Call(JsonElement id, JsonElement args)
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
        McpToolResult result;
        try
        {
            result = tool.Handler(arguments);
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
