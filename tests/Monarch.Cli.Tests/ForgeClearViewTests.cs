using System.Text.Json;
using Monarch.Application.Net;
using Monarch.Infrastructure.Boards;

namespace Monarch.Cli.Tests;

/// <summary>Q-0168 / Issue 72 way C: <c>quest list</c> and <c>status</c> derive "cleared (Forge)" read-only from merged PR plus green checks.</summary>
public class ForgeClearViewTests
{
    private const string Pr7 = "board: { provider: github, pr: 7 }\n";

    private static (TempProject P, FakeForge F) Setup(bool merged, string conclusion = "success", string status = "completed")
    {
        var p = new TempProject(systemYaml: Seed.ForgeYaml);
        var f = new FakeForge();
        f.Pulls[7] = (FakeForge.Sha, merged);
        f.SetChecks(FakeForge.Run(1, "build", "success"), FakeForge.Run(2, "test", conclusion, status));
        p.Http = f;
        return (p, f);
    }

    private static JsonElement StatusJson(TempProject p)
    {
        var r = p.Run(["status", "--json"]);
        Assert.Equal(0, r.Code);
        return JsonDocument.Parse(r.Out).RootElement.Clone();
    }

    [Theory]
    [InlineData("in_review")]
    [InlineData("verified")]
    public void Merged_and_green_is_shown_as_cleared_from_the_forge(string stored)
    {
        var (p, _) = Setup(merged: true);
        using (p)
        {
            var file = Seed.Quest(p, "Q-0001", stored, extra: Pr7);
            var list = p.Run(["quest", "list"]);
            Assert.Equal(0, list.Code);
            Assert.Contains(stored, list.Out, StringComparison.Ordinal);
            Assert.Contains("cleared (Forge)", list.Out, StringComparison.Ordinal);

            var text = p.Run(["status"]);
            Assert.Contains("Cleared (Forge): Q-0001", text.Out, StringComparison.Ordinal);
            Assert.Contains($"{stored} 1", text.Out, StringComparison.Ordinal);

            var json = StatusJson(p);
            Assert.Equal(1, json.GetProperty("quests").GetProperty(stored).GetInt32());
            Assert.False(json.GetProperty("quests").TryGetProperty("cleared", out _));
            Assert.Equal("Q-0001", Assert.Single(json.GetProperty("clearedForge").EnumerateArray()).GetString());
            Assert.Equal(JsonValueKind.Null, json.GetProperty("forgeNote").ValueKind);
            Assert.Equal(0, json.GetProperty("xp").GetInt64());

            Assert.Equal(stored, Seed.State(file));
            Assert.Empty(p.Events());
        }
    }

    [Theory]
    [InlineData("failure", "completed")]
    [InlineData("", "in_progress")]
    public void Merged_but_red_or_pending_is_not_cleared(string conclusion, string status)
    {
        var (p, _) = Setup(merged: true, conclusion, status);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review", extra: Pr7);
            Assert.DoesNotContain("cleared (Forge)", p.Run(["quest", "list"]).Out, StringComparison.Ordinal);
            Assert.Empty(StatusJson(p).GetProperty("clearedForge").EnumerateArray());
        }
    }

    [Fact]
    public void Not_merged_is_not_cleared()
    {
        var (p, _) = Setup(merged: false);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "verified", extra: Pr7);
            Assert.DoesNotContain("cleared (Forge)", p.Run(["quest", "list"]).Out, StringComparison.Ordinal);
            Assert.DoesNotContain("Cleared (Forge)", p.Run(["status"]).Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Forge_error_keeps_the_stored_state_with_a_hint_and_the_same_exit_code()
    {
        var (p, f) = Setup(merged: true);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review", extra: Pr7);
            f.Intercept = (_, _) => throw new HttpRequestException("down");
            var list = p.Run(["quest", "list"]);
            Assert.Equal(0, list.Code);
            Assert.Contains("in_review", list.Out, StringComparison.Ordinal);
            Assert.DoesNotContain("cleared (Forge)", list.Out, StringComparison.Ordinal);
            Assert.Contains("could not be derived", list.Err, StringComparison.Ordinal);

            var json = StatusJson(p);
            Assert.Empty(json.GetProperty("clearedForge").EnumerateArray());
            Assert.Contains("not reachable", json.GetProperty("forgeNote").GetString(), StringComparison.Ordinal);
            Assert.Contains("could not be derived", p.Run(["status"]).Out, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Missing_token_asks_nothing_and_hints()
    {
        var (p, f) = Setup(merged: true);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review", extra: Pr7);
            var o = new StringWriter();
            var e = new StringWriter();
            var env = new CliEnv(p.Root, p.Home, new StringReader(string.Empty), o, e, p.Clock, f, new GitHubTokenProvider(_ => null, () => null), false, TempProject.NoVars);
            Assert.Equal(0, QuestCommands.List(env, null));
            Assert.Empty(f.Paths);
            Assert.DoesNotContain("cleared (Forge)", o.ToString(), StringComparison.Ordinal);
            Assert.Contains("no GitHub token", e.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Deadline_expiry_keeps_the_stored_state_with_a_hint()
    {
        var (p, f) = Setup(merged: true);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review", extra: Pr7);
            f.Intercept = async (_, ct) =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return null;
            };
            var env = new CliEnv(p.Root, p.Home, new StringReader(string.Empty), new StringWriter(), new StringWriter(), p.Clock, f, new GitHubTokenProvider(_ => "tok-test", () => null), false, TempProject.NoVars);
            var paths = ProjectPaths.Locate(p.Root, p.Home)!;
            var quests = await new LocalFileBoard(p.Root).ListQuestsAsync(TestContext.Current.CancellationToken);
            var r = ForgeClearView.Derive(env, paths, quests, TimeSpan.FromMilliseconds(200));
            Assert.Empty(r.Cleared);
            Assert.Contains("in time", r.Note, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Two_quests_on_one_pr_share_the_requests()
    {
        var (single, fs) = Setup(merged: true);
        using (single)
        {
            Seed.Quest(single, "Q-0001", "in_review", extra: Pr7);
            single.Run(["quest", "list"]);
        }

        var (p, f) = Setup(merged: true);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review", extra: Pr7);
            Seed.Quest(p, "Q-0002", "verified", extra: Pr7);
            var list = p.Run(["quest", "list"]);
            Assert.Equal(2, list.Out.Split("cleared (Forge)").Length - 1);
            Assert.NotEmpty(fs.Paths);
            Assert.Equal(fs.Paths, f.Paths);
        }
    }

    [Fact]
    public void Quest_without_pr_and_stored_cleared_make_no_request()
    {
        var (p, f) = Setup(merged: true);
        using (p)
        {
            Seed.Quest(p, "Q-0001", "in_review");
            Seed.Quest(p, "Q-0002", "cleared", extra: Pr7);
            var list = p.Run(["quest", "list"]);
            Assert.Equal(0, list.Code);
            Assert.Empty(f.Paths);
            Assert.DoesNotContain("cleared (Forge)", list.Out, StringComparison.Ordinal);
            Assert.Contains("cleared ", list.Out, StringComparison.Ordinal);
            Assert.Empty(StatusJson(p).GetProperty("clearedForge").EnumerateArray());
            Assert.Empty(f.Paths);
        }
    }
}
