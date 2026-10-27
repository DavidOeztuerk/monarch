using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;

namespace Monarch.Infrastructure.Tests;

public class GitHubIssuesBoardTests
{
    private static GitHubIssuesBoard NewBoard(FakeGitHub fake, string? token = "tok-123") =>
        new(new HttpClient(fake) { BaseAddress = new Uri("https://api.github.com/") },
            new GitHubBoardOptions { Owner = "o", Repo = "r" },
            new GitHubTokenProvider(_ => token, () => null));

    private static BoardQuest Quest(QuestState state = QuestState.Proposed, string title = "Compose: Ports nur Loopback") =>
        new(new QuestId("Q-0003"), new GateId("G-0001"), title, QuestKind.Main, Rank.A, state, ["Ports gebunden", "Control Plane 15/15"], QuestScope.Empty);

    [Fact]
    public async Task Upsert_creates_milestone_labels_and_issue_with_marker_and_checklist()
    {
        var fake = new FakeGitHub();
        await NewBoard(fake).UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken);
        Assert.Contains(fake.Milestones, m => m.Title.StartsWith("G-0001", StringComparison.Ordinal));
        Assert.Superset(new HashSet<string> { "monarch", "rank:A", "kind:main", "state:proposed" }, fake.Labels);
        var issue = Assert.Single(fake.Issues);
        Assert.Equal("Q-0003: Compose: Ports nur Loopback", issue.Title);
        Assert.Equal(new[] { "kind:main", "monarch", "rank:A", "state:proposed" }, issue.Labels.Order(StringComparer.Ordinal));
        Assert.Contains("<!-- monarch:Q-0003 -->", issue.Body, StringComparison.Ordinal);
        Assert.Contains("- [ ] Ports gebunden", issue.Body, StringComparison.Ordinal);
        Assert.Contains("- [ ] Control Plane 15/15", issue.Body, StringComparison.Ordinal);
        Assert.Equal("open", issue.State);
        Assert.Equal(fake.Milestones[0].Number, issue.Milestone);
    }

    [Fact]
    public async Task Second_upsert_is_a_no_op()
    {
        var fake = new FakeGitHub();
        var board = NewBoard(fake);
        await board.UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken);
        var writes = fake.Writes.Count();
        await board.UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken);
        Assert.Equal(writes, fake.Writes.Count());
        Assert.Single(fake.Issues);
        Assert.Single(fake.Milestones);
    }

    [Fact]
    public async Task Existing_issue_on_page_two_is_found_by_marker_and_updated()
    {
        var fake = new FakeGitHub();
        fake.Milestones.Add(new FakeGitHub.Milestone { Number = 1, Title = "G-0001: Gate" });
        fake.Labels.UnionWith(["monarch", "rank:A", "kind:main", "state:proposed", "state:accepted"]);
        for (var i = 1; i <= 100; i++)
        {
            fake.Issues.Add(new FakeGitHub.Issue { Number = i, Title = $"Q-{i + 100:D4}: other", Body = $"<!-- monarch:Q-{i + 100:D4} -->", Labels = ["monarch"] });
        }

        fake.Issues.Add(new FakeGitHub.Issue { Number = 101, Title = "old", Body = "<!-- monarch:Q-0003 -->\nx", Labels = ["monarch", "state:proposed", "bug"], Milestone = 1 });
        await NewBoard(fake).UpsertQuestAsync(Quest(QuestState.Accepted), TestContext.Current.CancellationToken);
        Assert.Equal(101, fake.Issues.Count);
        var issue = fake.Issues.Single(i => i.Number == 101);
        Assert.Equal("Q-0003: Compose: Ports nur Loopback", issue.Title);
        Assert.Contains("state:accepted", issue.Labels);
        Assert.DoesNotContain("state:proposed", issue.Labels);
        Assert.Contains("bug", issue.Labels);
        Assert.Contains(fake.Requests, r => r.Method == "GET" && r.Path.Contains("page=2", StringComparison.Ordinal) && r.Path.Contains("labels=monarch", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.Requests, r => r.Method == "POST");
    }

    [Theory]
    [InlineData(QuestState.Cleared)]
    [InlineData(QuestState.Abandoned)]
    public async Task Terminal_quests_close_the_issue(QuestState state)
    {
        var fake = new FakeGitHub();
        var board = NewBoard(fake);
        await board.UpsertQuestAsync(Quest(QuestState.InProgress), TestContext.Current.CancellationToken);
        Assert.Equal("open", fake.Issues[0].State);
        await board.UpsertQuestAsync(Quest(state), TestContext.Current.CancellationToken);
        Assert.Equal("closed", fake.Issues[0].State);
        Assert.Contains($"state:{state.ToString().ToLowerInvariant()}", fake.Issues[0].Labels);
        Assert.Single(fake.Issues);
    }

    [Fact]
    public async Task Checked_items_stay_checked_on_update()
    {
        var fake = new FakeGitHub();
        var board = NewBoard(fake);
        await board.UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken);
        fake.Issues[0].Body = fake.Issues[0].Body.Replace("- [ ] Ports gebunden", "- [x] Ports gebunden", StringComparison.Ordinal);
        await board.UpsertQuestAsync(Quest(QuestState.InReview), TestContext.Current.CancellationToken);
        Assert.Contains("- [x] Ports gebunden", fake.Issues[0].Body, StringComparison.Ordinal);
        Assert.Contains("- [ ] Control Plane 15/15", fake.Issues[0].Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gate_upsert_creates_then_renames_milestone_once()
    {
        var fake = new FakeGitHub();
        var board = NewBoard(fake);
        var gate = new BoardGate(new GateId("G-0001"), "Interne Vertrauensgrenze", Rank.A, "proposed", [], "Boss");
        await board.UpsertGateAsync(gate, TestContext.Current.CancellationToken);
        Assert.Equal("G-0001: Interne Vertrauensgrenze", Assert.Single(fake.Milestones).Title);
        var writes = fake.Writes.Count();
        await board.UpsertGateAsync(gate, TestContext.Current.CancellationToken);
        Assert.Equal(writes, fake.Writes.Count());
        await board.UpsertGateAsync(gate with { Title = "Neu" }, TestContext.Current.CancellationToken);
        Assert.Equal("G-0001: Neu", Assert.Single(fake.Milestones).Title);
    }

    [Fact]
    public async Task Listing_reads_back_what_was_written()
    {
        var fake = new FakeGitHub();
        var board = NewBoard(fake);
        await board.UpsertGateAsync(new BoardGate(new GateId("G-0001"), "Gate", Rank.A, "proposed", [], null), TestContext.Current.CancellationToken);
        await board.UpsertQuestAsync(Quest(QuestState.InReview), TestContext.Current.CancellationToken);
        var q = Assert.Single(await board.ListQuestsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("Q-0003", "G-0001", "Compose: Ports nur Loopback"), (q.Id.Value, q.Gate.Value, q.Title));
        Assert.Equal((QuestKind.Main, Rank.A, QuestState.InReview), (q.Kind, q.Rank, q.State));
        Assert.Equal(["Ports gebunden", "Control Plane 15/15"], q.DefinitionOfDone);
        var g = Assert.Single(await board.ListGatesAsync(TestContext.Current.CancellationToken));
        Assert.Equal(("G-0001", "Gate", Rank.A), (g.Id.Value, g.Title, g.Rank));
    }

    [Fact]
    public async Task Token_is_sent_as_bearer_and_missing_token_sends_nothing()
    {
        var fake = new FakeGitHub();
        await NewBoard(fake).UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken);
        Assert.All(fake.Requests, r => Assert.Equal("Bearer tok-123", r.Auth));
        var anon = new FakeGitHub();
        await NewBoard(anon, null).ListQuestsAsync(TestContext.Current.CancellationToken);
        Assert.All(anon.Requests, r => Assert.Null(r.Auth));
    }

    [Fact]
    public async Task Http_errors_throw_without_leaking_the_token()
    {
        var fake = new FakeGitHub { FailWith = 500 };
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => NewBoard(fake).UpsertQuestAsync(Quest(), TestContext.Current.CancellationToken));
        Assert.DoesNotContain("tok-123", ex.Message, StringComparison.Ordinal);
        Assert.Contains("500", ex.Message, StringComparison.Ordinal);
    }
}
