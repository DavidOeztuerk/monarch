using System.Text.Json.Serialization;

namespace Monarch.Infrastructure.Evidence;

internal sealed class CheckRunDto
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? HeadSha { get; set; }

    public string? Status { get; set; }

    public string? Conclusion { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? HtmlUrl { get; set; }
}

internal sealed class CheckRunsPage
{
    public int TotalCount { get; set; }

    public List<CheckRunDto> CheckRuns { get; set; } = [];
}

internal sealed class PullDto
{
    public int Number { get; set; }

    public bool Merged { get; set; }

    public DateTimeOffset? MergedAt { get; set; }

    public string? MergeCommitSha { get; set; }

    public string? HtmlUrl { get; set; }

    public PullHeadDto? Head { get; set; }
}

internal sealed class PullHeadDto
{
    public string? Sha { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CheckRunsPage))]
[JsonSerializable(typeof(PullDto))]
internal sealed partial class EvidenceJsonContext : JsonSerializerContext
{
}
