using Monarch.Domain;

namespace Monarch.Application.Ports;

/// <summary>Evidence from one check run together with its status and outcome.</summary>
/// <param name="Evidence">The evidence (kind <see cref="EvidenceKind.CiRun"/>); for a run that is not completed, <c>At</c> is its start (or the Unix epoch when unknown).</param>
/// <param name="Name">Check run name.</param>
/// <param name="Conclusion">GitHub conclusion, e.g. <c>success</c>, <c>failure</c>, <c>cancelled</c>; empty while the run is not completed.</param>
/// <param name="Status">GitHub status: <c>queued</c>, <c>in_progress</c> or <c>completed</c>.</param>
public sealed record CheckEvidence(Evidence Evidence, string Name, string Conclusion, string Status = "completed")
{
    /// <summary>Gets a value indicating whether the run has finished (status <c>completed</c>).</summary>
    public bool IsCompleted => Status == "completed";

    /// <summary>
    /// Gets a value indicating whether the run completed with conclusion <c>success</c>; only these may verify a quest.
    /// <c>neutral</c>, <c>skipped</c>, <c>cancelled</c>, <c>timed_out</c>, <c>failure</c> and <c>action_required</c> do not count as green (strict).
    /// </summary>
    public bool IsSuccess => IsCompleted && Conclusion == "success";

    /// <summary>Gets a value indicating whether the run completed with conclusion <c>cancelled</c>: no result, neither green nor evidence of red (Q-0137).</summary>
    public bool IsCancelled => IsCompleted && Conclusion == "cancelled";
}

/// <summary>Port for sources of verification evidence such as CI checks.</summary>
public interface IEvidenceSource
{
    /// <summary>Gets all check runs of a commit, including queued and in-progress ones (check <see cref="CheckEvidence.Status"/>).</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="sha">Commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One item per check run, whatever its status.</returns>
    Task<IReadOnlyList<CheckEvidence>> GetChecksAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default);

    /// <summary>Gets the merge evidence of a pull request.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="prNumber">Pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Evidence of kind <see cref="EvidenceKind.MergeCommit"/>, or <see langword="null"/> when not merged.</returns>
    Task<Evidence?> GetMergeAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default);

    /// <summary>Gets the head commit sha of a pull request.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="prNumber">Pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The head sha as reported by the forge.</returns>
    Task<string> GetPullHeadAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default);
}
