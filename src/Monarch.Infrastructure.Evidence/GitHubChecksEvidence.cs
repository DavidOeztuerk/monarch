using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Domain;
using DomainEvidence = Monarch.Domain.Evidence;

namespace Monarch.Infrastructure.Evidence;

/// <summary>
/// Evidence source reading GitHub check runs (all statuses: queued, in_progress, completed) and pull request merges. The digest of a check run is the lowercase hex SHA-256 of
/// <c>checkRunId|headSha|conclusion|completedAt</c> (completedAt as UTC <c>yyyy-MM-ddTHH:mm:ssZ</c>); the digest of a merge is
/// the <c>merge_commit_sha</c>. Only conclusion <c>success</c> may verify a quest (see <see cref="CheckEvidence.IsSuccess"/>).
/// </summary>
public sealed class GitHubChecksEvidence : IEvidenceSource
{
    private const int PerPage = 100;
    private readonly GitHubRestClient _api;

    /// <summary>Initializes the source.</summary>
    /// <param name="http">HTTP client; configure its base address (e.g. <c>https://api.github.com/</c>).</param>
    /// <param name="tokens">Token source.</param>
    public GitHubChecksEvidence(HttpClient http, GitHubTokenProvider tokens) => _api = new GitHubRestClient(http, tokens);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CheckEvidence>> GetChecksAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        var path = $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/commits/{Uri.EscapeDataString(sha)}/check-runs";
        var result = new List<CheckEvidence>();
        var seen = 0;
        for (var page = 1; ; page++)
        {
            var batch = await _api.GetAsync($"{path}?per_page={PerPage}&page={page}", EvidenceJsonContext.Default.CheckRunsPage, cancellationToken).ConfigureAwait(false);
            seen += batch.CheckRuns.Count;
            foreach (var run in batch.CheckRuns)
            {
                var head = run.HeadSha ?? sha;
                var status = run.Status ?? "queued";
                var id = run.Id.ToString(CultureInfo.InvariantCulture);
                var source = run.HtmlUrl ?? $"https://github.com/{owner}/{repo}/runs/{id}";
                if (status != "completed" || run.Conclusion is null || run.CompletedAt is not { } completed)
                {
                    var started = run.StartedAt ?? DateTimeOffset.UnixEpoch;
                    var pending = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{head}|{status}")));
                    result.Add(new CheckEvidence(new DomainEvidence(EvidenceKind.CiRun, source, pending, started), run.Name ?? string.Empty, string.Empty, status == "completed" ? "in_progress" : status));
                    continue;
                }

                var stamp = completed.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{id}|{head}|{run.Conclusion}|{stamp}")));
                result.Add(new CheckEvidence(new DomainEvidence(EvidenceKind.CiRun, source, digest, completed), run.Name ?? string.Empty, run.Conclusion));
            }

            if (batch.CheckRuns.Count < PerPage || seen >= batch.TotalCount)
            {
                return result;
            }
        }
    }

    /// <inheritdoc />
    public async Task<DomainEvidence?> GetMergeAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
    {
        var pull = await _api.GetAsync(
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/pulls/{prNumber.ToString(CultureInfo.InvariantCulture)}", EvidenceJsonContext.Default.PullDto, cancellationToken).ConfigureAwait(false);
        if (!pull.Merged || string.IsNullOrEmpty(pull.MergeCommitSha))
        {
            return null;
        }

        return new DomainEvidence(EvidenceKind.MergeCommit, pull.HtmlUrl ?? $"https://github.com/{owner}/{repo}/pull/{prNumber.ToString(CultureInfo.InvariantCulture)}", pull.MergeCommitSha, pull.MergedAt ?? DateTimeOffset.UnixEpoch);
    }

    /// <inheritdoc />
    public async Task<string> GetPullHeadAsync(string owner, string repo, int prNumber, CancellationToken cancellationToken = default)
    {
        var pull = await _api.GetAsync(
            $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/pulls/{prNumber.ToString(CultureInfo.InvariantCulture)}", EvidenceJsonContext.Default.PullDto, cancellationToken).ConfigureAwait(false);
        return pull.Head?.Sha ?? throw new InvalidOperationException($"Pull request #{prNumber} has no head sha.");
    }
}
