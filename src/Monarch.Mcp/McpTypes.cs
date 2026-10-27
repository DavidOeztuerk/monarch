using System.Text.Json;

namespace Monarch.Mcp;

/// <summary>Outcome of a tool call.</summary>
/// <param name="Text">Text content returned to the client.</param>
/// <param name="IsError">True when the call failed; reported as <c>isError</c>, not as a protocol error.</param>
public sealed record McpToolResult(string Text, bool IsError = false);

/// <summary>A tool offered by the server.</summary>
/// <param name="Name">Tool name.</param>
/// <param name="Description">Concise description.</param>
/// <param name="InputSchema">JSON Schema of the arguments as JSON text.</param>
/// <param name="ReadOnly">True for tools that never write.</param>
/// <param name="Handler">Invoked with the <c>arguments</c> object.</param>
public sealed record McpTool(string Name, string Description, string InputSchema, bool ReadOnly, Func<JsonElement, McpToolResult> Handler);
