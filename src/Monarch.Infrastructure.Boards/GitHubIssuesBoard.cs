using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Boards;

/// <summary>Placeholder quest board backed by GitHub issues.</summary>
public sealed class GitHubIssuesBoard : IQuestBoard
{
    /// <summary>Initializes a new instance; not implemented yet.</summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public GitHubIssuesBoard() => throw new NotImplementedException();
}
