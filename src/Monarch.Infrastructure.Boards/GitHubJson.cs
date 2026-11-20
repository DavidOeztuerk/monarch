using System.Text.Json.Serialization;

namespace Monarch.Infrastructure.Boards;

internal sealed class GhMilestone
{
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? State { get; set; }
}

internal sealed class GhLabel
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class GhIssue
{
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public string State { get; set; } = "open";

    public List<GhLabel> Labels { get; set; } = [];

    public GhMilestone? Milestone { get; set; }
}

internal sealed class MilestoneWrite
{
    public string? Title { get; set; }

    public string? Description { get; set; }
}

internal sealed class LabelWrite
{
    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;
}

internal sealed class IssueWrite
{
    public string? Title { get; set; }

    public string? Body { get; set; }

    public List<string>? Labels { get; set; }

    public int? Milestone { get; set; }

    public string? State { get; set; }

    public string? StateReason { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(GhMilestone))]
[JsonSerializable(typeof(List<GhMilestone>))]
[JsonSerializable(typeof(GhLabel))]
[JsonSerializable(typeof(List<GhLabel>))]
[JsonSerializable(typeof(List<GhIssue>))]
[JsonSerializable(typeof(GhIssue))]
[JsonSerializable(typeof(MilestoneWrite))]
[JsonSerializable(typeof(LabelWrite))]
[JsonSerializable(typeof(IssueWrite))]
internal sealed partial class GitHubJsonContext : JsonSerializerContext
{
}
