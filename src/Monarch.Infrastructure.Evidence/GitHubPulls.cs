using System.Globalization;
using Monarch.Application.Net;

namespace Monarch.Infrastructure.Evidence;

/// <summary>A pull request as <c>monarch gate merge</c> needs it.</summary>
/// <param name="Number">Pull request number.</param>
/// <param name="HeadSha">Head commit sha.</param>
/// <param name="BaseRef">Name of the base branch.</param>
/// <param name="Merged">Whether the pull request is merged.</param>
/// <param name="MergeCommitSha">Merge commit sha, when merged.</param>
/// <param name="Title">Pull request title.</param>
public sealed record PullInfo(int Number, string HeadSha, string BaseRef, bool Merged, string? MergeCommitSha, string Title = "");

/// <summary>
/// GitHub pull request reads and the one merge call Monarch makes itself (<c>PUT …/pulls/{n}/merge</c>, squash or merge, bound to the checked head sha).
/// The hook denies every other route to that endpoint (ADR-0007).
/// </summary>
public sealed class GitHubPulls
{
    private readonly GitHubRestClient _api;

    /// <summary>Initializes the client.</summary>
    /// <param name="http">HTTP client; configure its base address (e.g. <c>https://api.github.com/</c>).</param>
    /// <param name="tokens">Token source.</param>
    public GitHubPulls(HttpClient http, GitHubTokenProvider tokens) => _api = new GitHubRestClient(http, tokens);

    private static string Repo(string owner, string repo) => $"repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}";

    /// <summary>Reads head, base and merge state of a pull request.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="number">Pull request number.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The pull request.</returns>
    public async Task<PullInfo> GetPullAsync(string owner, string repo, int number, CancellationToken cancellationToken = default)
    {
        var pull = await _api.GetAsync($"{Repo(owner, repo)}/pulls/{number.ToString(CultureInfo.InvariantCulture)}", EvidenceJsonContext.Default.PullDto, cancellationToken).ConfigureAwait(false);
        return new PullInfo(
            number,
            pull.Head?.Sha ?? throw new InvalidOperationException($"Pull request #{number} has no head sha."),
            pull.Base?.Ref ?? throw new InvalidOperationException($"Pull request #{number} has no base branch."),
            pull.Merged,
            pull.MergeCommitSha,
            pull.Title ?? string.Empty);
    }

    /// <summary>Reads the head commit of a branch (<c>GET …/git/ref/heads/{branch}</c>).</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="branch">Branch name (slashes allowed).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The head sha.</returns>
    public async Task<string> GetBranchHeadAsync(string owner, string repo, string branch, CancellationToken cancellationToken = default)
    {
        var escaped = string.Join('/', branch.Split('/').Select(Uri.EscapeDataString));
        var reference = await _api.GetAsync($"{Repo(owner, repo)}/git/ref/heads/{escaped}", EvidenceJsonContext.Default.GitRefDto, cancellationToken).ConfigureAwait(false);
        return reference.Object?.Sha ?? throw new InvalidOperationException($"Branch '{branch}' has no head sha.");
    }

    /// <summary>Reads the committer date of a commit.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="sha">Commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The committer date (author date when the committer date is missing).</returns>
    public async Task<DateTimeOffset> GetCommitTimeAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        var commit = await _api.GetAsync($"{Repo(owner, repo)}/commits/{Uri.EscapeDataString(sha)}", EvidenceJsonContext.Default.CommitDto, cancellationToken).ConfigureAwait(false);
        return commit.Commit?.Committer?.Date ?? commit.Commit?.Author?.Date ?? throw new InvalidOperationException($"Commit {sha} has no date.");
    }

    /// <summary>Reads the commit message of a commit.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="sha">Commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The full commit message; throws when the forge returns none.</returns>
    public async Task<string> GetCommitMessageAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        var commit = await _api.GetAsync($"{Repo(owner, repo)}/commits/{Uri.EscapeDataString(sha)}", EvidenceJsonContext.Default.CommitDto, cancellationToken).ConfigureAwait(false);
        return commit.Commit?.Message ?? throw new InvalidOperationException($"Commit {sha} has no readable message.");
    }

    /// <summary>Compares two commits (<c>GET …/compare/{base}...{head}</c>) and returns the forge's status of the head relative to the base.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="baseSha">Base commit sha.</param>
    /// <param name="headSha">Head commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>ahead</c> (base is a proper ancestor of head), <c>identical</c>, <c>behind</c> or <c>diverged</c>.</returns>
    public async Task<string> CompareAsync(string owner, string repo, string baseSha, string headSha, CancellationToken cancellationToken = default)
    {
        var compare = await _api.GetAsync($"{Repo(owner, repo)}/compare/{Uri.EscapeDataString(baseSha)}...{Uri.EscapeDataString(headSha)}", EvidenceJsonContext.Default.CompareDto, cancellationToken).ConfigureAwait(false);
        return compare.Status ?? throw new InvalidOperationException("The compare response has no status.");
    }

    /// <summary>Reads the first parent of a commit.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="sha">Commit sha.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sha of the first parent, or <see langword="null"/> when the forge reports none.</returns>
    public async Task<string?> GetFirstParentAsync(string owner, string repo, string sha, CancellationToken cancellationToken = default)
    {
        var commit = await _api.GetAsync($"{Repo(owner, repo)}/commits/{Uri.EscapeDataString(sha)}", EvidenceJsonContext.Default.CommitDto, cancellationToken).ConfigureAwait(false);
        return commit.Parents is { Count: > 0 } parents ? parents[0].Sha : null;
    }

    /// <summary>Merges a pull request with the given method; GitHub refuses when the head moved away from <paramref name="headSha"/>.</summary>
    /// <param name="owner">Repository owner.</param>
    /// <param name="repo">Repository name.</param>
    /// <param name="number">Pull request number.</param>
    /// <param name="method">The merge method, <c>squash</c> or <c>merge</c>.</param>
    /// <param name="headSha">The head sha that was checked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merge commit sha.</returns>
    /// <param name="commitTitle">Title of the squash commit; <see langword="null"/> leaves it to GitHub (merge method <c>merge</c>).</param>
    /// <param name="commitMessage">Body of the squash commit; <see langword="null"/> leaves it to GitHub.</param>
    public async Task<string> MergeAsync(string owner, string repo, int number, string method, string headSha, CancellationToken cancellationToken = default, string? commitTitle = null, string? commitMessage = null)
    {
        var response = await _api.SendJsonAsync(
            HttpMethod.Put,
            $"{Repo(owner, repo)}/pulls/{number.ToString(CultureInfo.InvariantCulture)}/merge",
            new MergeRequestDto { MergeMethod = method, Sha = headSha, CommitTitle = commitTitle, CommitMessage = commitMessage },
            EvidenceJsonContext.Default.MergeRequestDto,
            EvidenceJsonContext.Default.MergeResponseDto,
            cancellationToken).ConfigureAwait(false);
        if (!response.Merged || string.IsNullOrEmpty(response.Sha))
        {
            throw new InvalidOperationException($"GitHub did not merge pull request #{number}: {response.Message}");
        }

        return response.Sha;
    }
}
