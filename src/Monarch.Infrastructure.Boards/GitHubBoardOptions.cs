using System.ComponentModel.DataAnnotations;

namespace Monarch.Infrastructure.Boards;

/// <summary>Options for the GitHub issues board.</summary>
public sealed class GitHubBoardOptions
{
    /// <summary>Gets or sets the repository owner.</summary>
    [Required]
    public string Owner { get; set; } = string.Empty;

    /// <summary>Gets or sets the repository name.</summary>
    [Required]
    public string Repo { get; set; } = string.Empty;

    /// <summary>Gets or sets the API base address.</summary>
    [Required]
    public string ApiBase { get; set; } = "https://api.github.com/";
}
