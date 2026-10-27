using Monarch.Domain;

namespace Monarch.Application.Ports;

/// <summary>Evidence from one completed check run together with its outcome.</summary>
/// <param name="Evidence">The evidence (kind <see cref="EvidenceKind.CiRun"/>).</param>
/// <param name="Name">Check run name.</param>
/// <param name="Conclusion">GitHub conclusion, e.g. <c>success</c>, <c>failure</c>, <c>cancelled</c>.</param>
public sealed record CheckEvidence(Evidence Evidence, string Name, string Conclusion)
{
    /// <summary>Gets a value indicating whether the conclusion is <c>success</c>; only these may verify a quest.</summary>
    public bool IsSuccess => Conclusion == "success";
}

/// <summary>Port for sources of verification evidence such as CI checks.</summary>
public interface IEvidenceSource
{
    /// <summary>Gets the completed check runs of a commit.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="sha">Commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One item per completed check run that has a conclusion.</returns>
    Task<IReadOnlyList<CheckEvidence>> GetChecksAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default);

    /// <summary>Gets the merge evidence of a pull request.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="prNumber">Pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence of kind <see cref="EvidenceKind.MergeCommit"/>, or <see langword="null"/> when not merged.</returns>
    Task<Evidence?> GetMergeAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default);
}
