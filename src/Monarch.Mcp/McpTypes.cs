using System.Text.Json;

namespace Monarch.Mcp;

/// <summary>Outcome of a tool call.</summary>
/// <param name="Text">Text content returned to the client.</param>
/// <param name="IsError">True when the call failed; reported as <c>isError</c>, not as a protocol error.</param>
public sealed record McpToolResult(string Text, bool IsError = false);

/// <summary>What the server knows about the client's roots at the time of a call (ADR-0013).</summary>
/// <param name="RootsDeclared">True when the client declared the <c>roots</c> capability.</param>
/// <param name="Roots">Absolute directories of the accepted <c>file://</c> roots; empty when none were delivered (error, timeout, empty list).</param>
/// <param name="ServerId">Id of this server run, used for the <c>mcp-&lt;id&gt;</c> session fallback.</param>
public sealed record McpCallContext(bool RootsDeclared, IReadOnlyList<string> Roots, string ServerId);

/// <summary>Server tuning that tests and the roots measurement inject.</summary>
/// <param name="Err">Receives <c>mcp: roots=[...]</c> lines; null discards them.</param>
/// <param name="RootsLogPath">When set, the same lines are appended to this file (measurement, <c>MONARCH_MCP_ROOTS_LOG</c>).</param>
/// <param name="RootsTimeoutOverride">Replaces the default wait for a roots answer (3 s); tests inject a short one.</param>
public sealed record McpServerOptions(TextWriter? Err = null, string? RootsLogPath = null, TimeSpan? RootsTimeoutOverride = null)
{
    /// <summary>How long a tool call waits for an outstanding <c>roots/list</c> answer.</summary>
    public TimeSpan RootsTimeout => RootsTimeoutOverride ?? TimeSpan.FromSeconds(3);
}

/// <summary>A tool offered by the server.</summary>
/// <param name="Name">Tool name.</param>
/// <param name="Description">Concise description.</param>
/// <param name="InputSchema">JSON Schema of the arguments as JSON text.</param>
/// <param name="ReadOnly">True for tools that never write.</param>
/// <param name="Handler">Invoked with the <c>arguments</c> object and the roots context.</param>
public sealed record McpTool(string Name, string Description, string InputSchema, bool ReadOnly, Func<JsonElement, McpCallContext, McpToolResult> Handler);

/// <summary>Parsing of MCP root URIs.</summary>
public static class McpRoots
{
    /// <summary>Accepts only <c>file://</c> with an empty host or <c>localhost</c>; the path is percent-decoded and must be absolute.</summary>
    /// <param name="uri">The root URI.</param>
    /// <param name="path">The decoded absolute path.</param>
    /// <returns>True when accepted.</returns>
    public static bool TryParseFileUri(string uri, out string path)
    {
        path = string.Empty;
        const string prefix = "file://";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var rest = uri[prefix.Length..];
        var cut = rest.IndexOfAny(['?', '#']);
        if (cut >= 0)
        {
            rest = rest[..cut];
        }

        var slash = rest.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0)
        {
            return false;
        }

        var host = rest[..slash];
        if (host.Length != 0 && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(rest[slash..]);
        }
        catch (UriFormatException)
        {
            return false;
        }

        if (decoded.Length == 0 || decoded[0] != '/' || decoded.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        path = decoded.Length > 1 ? decoded.TrimEnd('/') : decoded;
        return path.Length > 0;
    }
}
