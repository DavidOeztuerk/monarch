namespace Monarch.Infrastructure.Boards;

/// <summary>Options for the GitHub issues board.</summary>
public sealed class GitHubBoardOptions
{
    /// <summary>Gets or sets the repository owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Gets or sets the repository name.</summary>
    public string Repository { get; set; } = string.Empty;
}
