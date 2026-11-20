using System.Text.Json.Serialization;

namespace Monarch.Cli;

/// <summary>Claude Code hook input (only the fields Monarch needs; unknown fields are ignored).</summary>
internal sealed class HookInput
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }

    [JsonPropertyName("cwd")] public string? Cwd { get; set; }

    [JsonPropertyName("hook_event_name")] public string? HookEventName { get; set; }

    [JsonPropertyName("permission_mode")] public string? PermissionMode { get; set; }

    [JsonPropertyName("agent_id")] public string? AgentId { get; set; }

    [JsonPropertyName("agent_type")] public string? AgentType { get; set; }

    [JsonPropertyName("tool_name")] public string? ToolName { get; set; }

    [JsonPropertyName("tool_input")] public HookToolInput? ToolInput { get; set; }

    /// <summary>Raw <c>tool_response</c>; any JSON shape is accepted, so a wrong type never makes the whole input unreadable.</summary>
    [JsonPropertyName("tool_response")] public System.Text.Json.JsonElement? ToolResponse { get; set; }

    /// <summary>Raw <c>background_tasks</c> of SubagentStop (session-wide list); any JSON shape is accepted.</summary>
    [JsonPropertyName("background_tasks")] public System.Text.Json.JsonElement? BackgroundTasks { get; set; }

    /// <summary>Parses hook JSON.</summary>
    public static HookInput? Parse(string json) => System.Text.Json.JsonSerializer.Deserialize(json, CliJson.Default.HookInput);
}

/// <summary>The <c>tool_input</c> object of a tool hook.</summary>
internal sealed class HookToolInput
{
    [JsonPropertyName("file_path")] public string? FilePath { get; set; }

    [JsonPropertyName("notebook_path")] public string? NotebookPath { get; set; }

    [JsonPropertyName("command")] public string? Command { get; set; }

    /// <summary>Search path of the Grep tool (<c>tool_input.path</c>).</summary>
    [JsonPropertyName("path")] public string? Path { get; set; }

    /// <summary>File filter of the Grep/Glob tools (<c>tool_input.glob</c>).</summary>
    [JsonPropertyName("glob")] public string? Glob { get; set; }
}

/// <summary>Hook stdout envelope.</summary>
internal sealed class HookOutput
{
    [JsonPropertyName("hookSpecificOutput")] public HookSpecificOutput HookSpecific { get; set; } = new();
}

/// <summary>Body of <see cref="HookOutput"/>.</summary>
internal sealed class HookSpecificOutput
{
    [JsonPropertyName("hookEventName")] public string HookEventName { get; set; } = string.Empty;

    [JsonPropertyName("permissionDecision")] public string? PermissionDecision { get; set; }

    [JsonPropertyName("permissionDecisionReason")] public string? PermissionDecisionReason { get; set; }

    [JsonPropertyName("additionalContext")] public string? AdditionalContext { get; set; }
}

/// <summary>The session's active quest (<c>.monarch/local/active.json</c>).</summary>
internal sealed class ActiveFile
{
    public string QuestId { get; set; } = string.Empty;

    public string GateId { get; set; } = string.Empty;

    public string Rank { get; set; } = "E";

    public string Kind { get; set; } = "main";

    public ActiveScope Scope { get; set; } = new();

    public string AcceptedAt { get; set; } = string.Empty;

    public string? State { get; set; }
}

/// <summary>Scope globs of <see cref="ActiveFile"/>.</summary>
internal sealed class ActiveScope
{
    public List<string> Include { get; set; } = [];

    public List<string> Exclude { get; set; } = [];
}

/// <summary>Account-global mana snapshot (<c>~/.monarch/mana.json</c>).</summary>
internal sealed class ManaFile
{
    [JsonPropertyName("schema")] public int Schema { get; set; } = 1;

    [JsonPropertyName("written_at")] public string? WrittenAt { get; set; }

    [JsonPropertyName("session_id")] public string? SessionId { get; set; }

    [JsonPropertyName("model")] public string? Model { get; set; }

    [JsonPropertyName("context_window")] public ContextWindow? ContextWindow { get; set; }

    [JsonPropertyName("rate_limits")] public RateLimits? RateLimits { get; set; }

    [JsonPropertyName("rate_limits_at")] public string? RateLimitsAt { get; set; }

    [JsonPropertyName("rate_limits_stale")] public bool RateLimitsStale { get; set; }

    [JsonPropertyName("sessions")] public Dictionary<string, SessionEntry>? Sessions { get; set; }

    [JsonPropertyName("last_lowered")] public WindowLowered? LastLowered { get; set; }
}

/// <summary>Per-session context usage (HP is session-local, not account-global) and the session's model.</summary>
internal sealed class SessionEntry
{
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }

    [JsonPropertyName("written_at")] public string? WrittenAt { get; set; }

    [JsonPropertyName("model")] public string? Model { get; set; }
}

/// <summary>Context usage.</summary>
internal sealed class ContextWindow
{
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }
}

/// <summary>The two rate-limit windows; either can be missing.</summary>
internal sealed class RateLimits
{
    [JsonPropertyName("five_hour")] public RateWindow? FiveHour { get; set; }

    [JsonPropertyName("seven_day")] public RateWindow? SevenDay { get; set; }
}

/// <summary>One rate-limit window.</summary>
internal sealed class RateWindow
{
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }

    [JsonPropertyName("resets_at")] public long? ResetsAt { get; set; }

    [JsonPropertyName("observed_at")] public string? ObservedAt { get; set; }

    /// <summary>The raw value of the latest statusline snapshot, whether or not it was kept.</summary>
    [JsonPropertyName("last_seen")] public WindowSighting? LastSeen { get; set; }

    /// <summary>Fresh, lower values per session, each waiting for confirmation by a later snapshot of the same session.</summary>
    [JsonPropertyName("candidates")] public Dictionary<string, WindowSighting>? Candidates { get; set; }
}

/// <summary>One sighting of a rate-limit window (<c>first_seen_at</c> is only set for candidates).</summary>
internal sealed class WindowSighting
{
    [JsonPropertyName("used_percentage")] public double? UsedPercentage { get; set; }

    [JsonPropertyName("resets_at")] public long? ResetsAt { get; set; }

    [JsonPropertyName("observed_at")] public string? ObservedAt { get; set; }

    [JsonPropertyName("first_seen_at")] public string? FirstSeenAt { get; set; }

    [JsonPropertyName("session_id")] public string? SessionId { get; set; }
}

/// <summary>Record of the latest automatic lowering of a window (kept in mana.json for projects without a ledger).</summary>
internal sealed class WindowLowered
{
    [JsonPropertyName("window")] public string? Window { get; set; }

    [JsonPropertyName("old_pct")] public double? OldPct { get; set; }

    [JsonPropertyName("new_pct")] public double? NewPct { get; set; }

    [JsonPropertyName("resets_at")] public long? ResetsAt { get; set; }

    [JsonPropertyName("first_seen_at")] public string? FirstSeenAt { get; set; }

    [JsonPropertyName("confirmed_at")] public string? ConfirmedAt { get; set; }

    [JsonPropertyName("session")] public string? Session { get; set; }
}

/// <summary>Claude Code statusline stdin (subset).</summary>
internal sealed class StatuslineInput
{
    [JsonPropertyName("session_id")] public string? SessionId { get; set; }

    [JsonPropertyName("cwd")] public string? Cwd { get; set; }

    [JsonPropertyName("workspace")] public StatuslineWorkspace? Workspace { get; set; }

    [JsonPropertyName("model")] public StatuslineModel? Model { get; set; }

    [JsonPropertyName("context_window")] public ContextWindow? ContextWindow { get; set; }

    [JsonPropertyName("rate_limits")] public RateLimits? RateLimits { get; set; }
}

/// <summary>Workspace block of the statusline input.</summary>
internal sealed class StatuslineWorkspace
{
    [JsonPropertyName("current_dir")] public string? CurrentDir { get; set; }
}

/// <summary>Model block of the statusline input.</summary>
internal sealed class StatuslineModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

/// <summary><c>~/.monarch/statusline.json</c>.</summary>
internal sealed class StatuslineConfig
{
    [JsonPropertyName("wrap")] public string? Wrap { get; set; }
}

/// <summary>Source-generated JSON metadata (AOT-safe).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HookInput))]
[JsonSerializable(typeof(HookOutput))]
[JsonSerializable(typeof(ActiveFile))]
[JsonSerializable(typeof(ManaFile))]
[JsonSerializable(typeof(StatuslineInput))]
[JsonSerializable(typeof(StatuslineConfig))]
internal sealed partial class CliJson : JsonSerializerContext
{
}
