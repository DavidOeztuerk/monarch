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

    public PullBaseDto? Base { get; set; }

    public string? State { get; set; }
}

internal sealed class PullBaseDto
{
    public string? Ref { get; set; }
}

internal sealed class CommitDto
{
    public CommitDetailDto? Commit { get; set; }

    public List<CommitParentDto>? Parents { get; set; }
}

internal sealed class CommitParentDto
{
    public string? Sha { get; set; }
}

internal sealed class CompareDto
{
    public string? Status { get; set; }
}

internal sealed class CommitDetailDto
{
    public CommitPersonDto? Committer { get; set; }

    public CommitPersonDto? Author { get; set; }
}

internal sealed class CommitPersonDto
{
    public DateTimeOffset? Date { get; set; }
}

internal sealed class MergeRequestDto
{
    public string MergeMethod { get; set; } = "squash";

    public string Sha { get; set; } = string.Empty;
}

internal sealed class MergeResponseDto
{
    public string? Sha { get; set; }

    public bool Merged { get; set; }

    public string? Message { get; set; }
}

internal sealed class PullHeadDto
{
    public string? Sha { get; set; }
}

internal sealed class GitRefDto
{
    public GitRefObjectDto? Object { get; set; }
}

internal sealed class GitRefObjectDto
{
    public string? Sha { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(CheckRunsPage))]
[JsonSerializable(typeof(PullDto))]
[JsonSerializable(typeof(CommitDto))]
[JsonSerializable(typeof(CompareDto))]
[JsonSerializable(typeof(GitRefDto))]
[JsonSerializable(typeof(MergeRequestDto))]
[JsonSerializable(typeof(MergeResponseDto))]
internal sealed partial class EvidenceJsonContext : JsonSerializerContext
{
}
