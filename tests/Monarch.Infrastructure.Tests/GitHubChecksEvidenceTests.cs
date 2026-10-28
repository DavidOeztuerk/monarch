using System.Net;
using System.Security.Cryptography;
using System.Text;
using Monarch.Application.Net;
using Monarch.Domain;
using Monarch.Infrastructure.Evidence;

namespace Monarch.Infrastructure.Tests;

public class GitHubChecksEvidenceTests
{
    private const string Sha = "abc123def456abc123def456abc123def456abcd";

    private const string ChecksJson = """
        {"total_count":3,"check_runs":[
          {"id":101,"name":"build","head_sha":"abc123def456abc123def456abc123def456abcd","status":"completed","conclusion":"success","started_at":"2026-10-27T09:00:00Z","completed_at":"2026-10-27T09:05:00Z","html_url":"https://github.com/o/r/runs/101"},
          {"id":102,"name":"test","head_sha":"abc123def456abc123def456abc123def456abcd","status":"completed","conclusion":"failure","completed_at":"2026-10-27T09:06:00Z","html_url":"https://github.com/o/r/runs/102"},
          {"id":103,"name":"deploy","head_sha":"abc123def456abc123def456abc123def456abcd","status":"in_progress","conclusion":null,"completed_at":null,"html_url":"https://github.com/o/r/runs/103"}
        ]}
        """;

    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static GitHubChecksEvidence Source(StubHandler h, string? token = "tok-xyz") =>
        new(new HttpClient(h) { BaseAddress = new Uri("https://api.github.com/") }, new GitHubTokenProvider(_ => token, () => null));

    private static string Sha256(string s) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [Fact]
    public async Task Check_runs_become_ci_evidence_with_digest_status_and_conclusion()
    {
        var h = new StubHandler(_ => Json(ChecksJson));
        var list = await Source(h).GetChecksAsync("o", "r", Sha, TestContext.Current.CancellationToken);
        Assert.Equal(3, list.Count);
        var build = list[0];
        Assert.True(build.IsCompleted);
        var deploy = list[2];
        Assert.Equal("in_progress", deploy.Status);
        Assert.False(deploy.IsCompleted);
        Assert.False(deploy.IsSuccess);
        Assert.Equal(EvidenceKind.CiRun, build.Evidence.Kind);
        Assert.Equal("success", build.Conclusion);
        Assert.True(build.IsSuccess);
        Assert.Equal("build", build.Name);
        Assert.Equal("https://github.com/o/r/runs/101", build.Evidence.Source);
        Assert.Equal(Sha256($"101|{Sha}|success|2026-10-27T09:05:00Z"), build.Evidence.Digest);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 9, 5, 0, TimeSpan.Zero), build.Evidence.At);
        Assert.False(list[1].IsSuccess);
        Assert.Equal("failure", list[1].Conclusion);
        Assert.StartsWith($"/repos/o/r/commits/{Sha}/check-runs", Assert.Single(h.Uris), StringComparison.Ordinal);
        Assert.Equal("Bearer tok-xyz", h.Auth[0]);
    }

    [Fact]
    public async Task Check_runs_are_paginated()
    {
        string Run(int id) => $$"""{"id":{{id}},"name":"c{{id}}","head_sha":"{{Sha}}","status":"completed","conclusion":"success","completed_at":"2026-10-27T09:00:00Z","html_url":"u{{id}}"}""";
        var h = new StubHandler(r =>
        {
            var page = r.RequestUri!.Query.Contains("page=2", StringComparison.Ordinal) ? 2 : 1;
            var ids = page == 1 ? Enumerable.Range(1, 100) : [101];
            return Json($$"""{"total_count":101,"check_runs":[{{string.Join(",", ids.Select(Run))}}]}""");
        });
        var list = await Source(h).GetChecksAsync("o", "r", Sha, TestContext.Current.CancellationToken);
        Assert.Equal(101, list.Count);
        Assert.Equal(2, h.Uris.Count);
    }

    [Fact]
    public async Task Merged_pull_request_yields_merge_commit_evidence()
    {
        var h = new StubHandler(_ => Json("""{"number":7,"merged":true,"merged_at":"2026-10-27T10:00:00Z","merge_commit_sha":"fedcba9876543210fedcba9876543210fedcba98","html_url":"https://github.com/o/r/pull/7"}"""));
        var e = await Source(h).GetMergeAsync("o", "r", 7, TestContext.Current.CancellationToken);
        Assert.NotNull(e);
        Assert.Equal(EvidenceKind.MergeCommit, e.Kind);
        Assert.Equal("fedcba9876543210fedcba9876543210fedcba98", e.Digest);
        Assert.Equal("https://github.com/o/r/pull/7", e.Source);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero), e.At);
        Assert.Equal("/repos/o/r/pulls/7", h.Uris[0]);
    }

    [Fact]
    public async Task Unmerged_pull_request_yields_nothing()
    {
        var h = new StubHandler(_ => Json("""{"number":8,"merged":false,"merged_at":null,"merge_commit_sha":"deadbeef","html_url":"u"}"""));
        Assert.Null(await Source(h).GetMergeAsync("o", "r", 8, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Missing_token_sends_no_auth_header_and_errors_do_not_leak_it()
    {
        var h = new StubHandler(_ => Json("""{"total_count":0,"check_runs":[]}"""));
        Assert.Empty(await Source(h, null).GetChecksAsync("o", "r", Sha, TestContext.Current.CancellationToken));
        Assert.Null(h.Auth[0]);
        var bad = new StubHandler(_ => Json("{}", HttpStatusCode.NotFound));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Source(bad).GetMergeAsync("o", "r", 9, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("tok-xyz", ex.Message, StringComparison.Ordinal);
    }
}

public class GitHubTokenProviderTests
{
    [Fact]
    public void Gh_token_wins_over_nothing_and_env_wins_over_gh()
    {
        var ghCalls = 0;
        string? Gh() { ghCalls++; return "from-gh\n"; }
        Assert.Equal("from-gh", new GitHubTokenProvider(_ => null, Gh).GetToken());
        Assert.Equal(1, ghCalls);
        Assert.Equal("gh1", new GitHubTokenProvider(k => k == "GH_TOKEN" ? "gh1" : "gh2", Gh).GetToken());
        Assert.Equal("gh2", new GitHubTokenProvider(k => k == "GITHUB_TOKEN" ? "gh2" : null, Gh).GetToken());
        Assert.Equal(1, ghCalls);
    }

    [Fact]
    public void Token_is_resolved_once_and_absent_is_null()
    {
        var calls = 0;
        var p = new GitHubTokenProvider(_ => null, () => { calls++; return null; });
        Assert.Null(p.GetToken());
        Assert.Null(p.GetToken());
        Assert.Equal(1, calls);
    }
}
