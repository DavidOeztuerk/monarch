using System.Text;
using System.Text.Json.Serialization.Metadata;
using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Domain;

namespace Monarch.Infrastructure.Boards;

/// <summary>
/// Quest board backed by GitHub: a gate is a milestone titled <c>G-0001: &lt;title&gt;</c>, a quest is an issue titled
/// <c>Q-0003: &lt;title&gt;</c> labelled <c>monarch</c>, <c>rank:A</c>, <c>kind:main</c>, <c>state:&lt;state&gt;</c> whose body carries
/// the marker <c>&lt;!-- monarch:Q-0003 --&gt;</c> and the definition of done as checklist. Upserts are idempotent and
/// find existing items by marker (paginated). Cleared and abandoned quests close their issue.
/// Not stored on GitHub: a quest's path scope (listed quests have an empty scope).
/// </summary>
public sealed class GitHubIssuesBoard : IQuestBoard
{
    private const int PerPage = 100;
    private const string DodHeading = "### Definition of Done";

    private readonly GitHubRestClient _api;
    private readonly string _repoPath;

    /// <summary>Initializes a board.</summary>
    /// <param name="http">HTTP client; configure its base address (e.g. <c>https://api.github.com/</c>).</param>
    /// <param name="options">Board options.</param>
    /// <param name="tokens">Token source.</param>
    public GitHubIssuesBoard(HttpClient http, GitHubBoardOptions options, GitHubTokenProvider tokens)
    {
        ArgumentNullException.ThrowIfNull(options);
        _api = new GitHubRestClient(http, tokens);
        _repoPath = $"repos/{Uri.EscapeDataString(options.Owner)}/{Uri.EscapeDataString(options.Repo)}";
    }

    private static string Marker(string id) => $"<!-- monarch:{id} -->";

    /// <inheritdoc />
    public async Task<IReadOnlyList<BoardGate>> ListGatesAsync(CancellationToken cancellationToken = default)
    {
        var milestones = await ListAllAsync($"{_repoPath}/milestones?state=all", GitHubJsonContext.Default.ListGhMilestone, cancellationToken).ConfigureAwait(false);
        var gates = new List<BoardGate>();
        foreach (var m in milestones)
        {
            var colon = m.Title.IndexOf(':', StringComparison.Ordinal);
            var id = colon < 0 ? m.Title : m.Title[..colon];
            if (!id.StartsWith("G-", StringComparison.Ordinal))
            {
                continue;
            }

            var meta = Meta(m.Description);
            meta.TryGetValue("rank", out var rank);
            meta.TryGetValue("state", out var state);
            meta.TryGetValue("boss", out var boss);
            meta.TryGetValue("quests", out var quests);
            gates.Add(new BoardGate(
                new GateId(id), colon < 0 ? id : m.Title[(colon + 1)..].Trim(), BoardNames.ParseRank(rank), state ?? "proposed",
                [.. (quests ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(q => new QuestId(q))], boss));
        }

        return gates;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BoardQuest>> ListQuestsAsync(CancellationToken cancellationToken = default)
    {
        var issues = await ListAllAsync($"{_repoPath}/issues?labels=monarch&state=all", GitHubJsonContext.Default.ListGhIssue, cancellationToken).ConfigureAwait(false);
        var quests = new List<BoardQuest>();
        foreach (var issue in issues)
        {
            var id = MarkerId(issue.Body);
            if (id is null || !id.StartsWith("Q-", StringComparison.Ordinal))
            {
                continue;
            }

            var gate = issue.Milestone?.Title is { } mt ? (mt.Contains(':', StringComparison.Ordinal) ? mt[..mt.IndexOf(':', StringComparison.Ordinal)] : mt) : string.Empty;
            var title = issue.Title.StartsWith(id + ":", StringComparison.Ordinal) ? issue.Title[(id.Length + 1)..].Trim() : issue.Title;
            string? Label(string prefix) => issue.Labels.Select(l => l.Name).FirstOrDefault(n => n.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
            quests.Add(new BoardQuest(
                new QuestId(id), new GateId(gate), title, BoardNames.ParseKind(Label("kind:")), BoardNames.ParseRank(Label("rank:")),
                BoardNames.ParseState(Label("state:")), [.. Checklist(issue.Body).Select(c => c.Text)], QuestScope.Empty));
        }

        return quests;
    }

    /// <inheritdoc />
    public async Task UpsertGateAsync(BoardGate gate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var title = $"{gate.Id.Value}: {gate.Title}";
        var description = new StringBuilder().Append(Marker(gate.Id.Value)).Append("\nrank: ").Append(gate.Rank).Append("\nstate: ").Append(gate.State);
        if (gate.Boss is not null)
        {
            description.Append("\nboss: ").Append(gate.Boss.ReplaceLineEndings(" "));
        }

        if (gate.Quests.Count > 0)
        {
            description.Append("\nquests: ").Append(string.Join(", ", gate.Quests.Select(q => q.Value)));
        }

        var found = await FindMilestoneAsync(gate.Id.Value, cancellationToken).ConfigureAwait(false);
        if (found is null)
        {
            await _api.SendJsonAsync(HttpMethod.Post, $"{_repoPath}/milestones", new MilestoneWrite { Title = title, Description = description.ToString() },
                GitHubJsonContext.Default.MilestoneWrite, GitHubJsonContext.Default.GhMilestone, cancellationToken).ConfigureAwait(false);
        }
        else if (found.Title != title || found.Description != description.ToString())
        {
            await _api.SendJsonAsync(HttpMethod.Patch, $"{_repoPath}/milestones/{found.Number}", new MilestoneWrite { Title = title, Description = description.ToString() },
                GitHubJsonContext.Default.MilestoneWrite, GitHubJsonContext.Default.GhMilestone, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task UpsertQuestAsync(BoardQuest quest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quest);
        var id = quest.Id.Value;
        string[] wanted = ["monarch", $"rank:{quest.Rank}", $"kind:{quest.Kind.ToString().ToLowerInvariant()}", $"state:{BoardNames.State(quest.State)}"];
        await EnsureLabelsAsync(wanted, cancellationToken).ConfigureAwait(false);
        var milestone = await EnsureMilestoneAsync(quest.Gate.Value, cancellationToken).ConfigureAwait(false);

        var issues = await ListAllAsync($"{_repoPath}/issues?labels=monarch&state=all", GitHubJsonContext.Default.ListGhIssue, cancellationToken).ConfigureAwait(false);
        var marker = Marker(id);
        var existing = issues.FirstOrDefault(i => i.Body?.Contains(marker, StringComparison.Ordinal) == true);

        var title = $"{id}: {quest.Title}";
        var body = ComposeBody(quest, existing?.Body);
        var labels = (existing?.Labels.Select(l => l.Name).Where(n => !IsManaged(n)) ?? []).Concat(wanted).ToList();
        var closed = quest.State is QuestState.Cleared or QuestState.Abandoned;
        var state = closed ? "closed" : "open";

        if (existing is null)
        {
            var created = await _api.SendJsonAsync(HttpMethod.Post, $"{_repoPath}/issues",
                new IssueWrite { Title = title, Body = body, Labels = labels, Milestone = milestone }, GitHubJsonContext.Default.IssueWrite, GitHubJsonContext.Default.GhIssue, cancellationToken).ConfigureAwait(false);
            if (closed)
            {
                await CloseAsync(created.Number, quest.State, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        var same = existing.Title == title && existing.Body == body && existing.State == state && existing.Milestone?.Number == milestone
            && existing.Labels.Select(l => l.Name).Order(StringComparer.Ordinal).SequenceEqual(labels.Order(StringComparer.Ordinal));
        if (!same)
        {
            await _api.SendJsonAsync(HttpMethod.Patch, $"{_repoPath}/issues/{existing.Number}",
                new IssueWrite { Title = title, Body = body, Labels = labels, Milestone = milestone, State = state, StateReason = closed ? Reason(quest.State) : null },
                GitHubJsonContext.Default.IssueWrite, GitHubJsonContext.Default.GhIssue, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Reason(QuestState state) => state == QuestState.Cleared ? "completed" : "not_planned";

    private Task CloseAsync(int number, QuestState state, CancellationToken ct) =>
        _api.SendJsonAsync(HttpMethod.Patch, $"{_repoPath}/issues/{number}", new IssueWrite { State = "closed", StateReason = Reason(state) },
            GitHubJsonContext.Default.IssueWrite, GitHubJsonContext.Default.GhIssue, ct);

    private static bool IsManaged(string label) =>
        label == "monarch" || label.StartsWith("rank:", StringComparison.Ordinal) || label.StartsWith("kind:", StringComparison.Ordinal) || label.StartsWith("state:", StringComparison.Ordinal);

    private static string ComposeBody(BoardQuest quest, string? previous)
    {
        var done = new HashSet<string>(Checklist(previous).Where(c => c.Checked).Select(c => c.Text), StringComparer.Ordinal);
        var sb = new StringBuilder().Append(Marker(quest.Id.Value)).Append("\n\n").Append(DodHeading).Append('\n');
        foreach (var item in quest.DefinitionOfDone)
        {
            sb.Append(done.Contains(item) ? "- [x] " : "- [ ] ").Append(item.ReplaceLineEndings(" ")).Append('\n');
        }

        return sb.ToString();
    }

    private static List<(bool Checked, string Text)> Checklist(string? body)
    {
        var list = new List<(bool, string)>();
        if (body is null)
        {
            return list;
        }

        foreach (var line in body.Split('\n'))
        {
            var l = line.TrimEnd('\r');
            if (l.StartsWith("- [ ] ", StringComparison.Ordinal))
            {
                list.Add((false, l[6..]));
            }
            else if (l.StartsWith("- [x] ", StringComparison.OrdinalIgnoreCase))
            {
                list.Add((true, l[6..]));
            }
        }

        return list;
    }

    private static string? MarkerId(string? body)
    {
        const string open = "<!-- monarch:";
        var start = body?.IndexOf(open, StringComparison.Ordinal) ?? -1;
        if (start < 0)
        {
            return null;
        }

        var end = body!.IndexOf(" -->", start, StringComparison.Ordinal);
        return end < 0 ? null : body[(start + open.Length)..end];
    }

    private static Dictionary<string, string> Meta(string? description)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in (description ?? string.Empty).Split('\n'))
        {
            var colon = line.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0)
            {
                map[line[..colon].Trim()] = line[(colon + 2)..].Trim();
            }
        }

        return map;
    }

    private async Task<List<T>> ListAllAsync<T>(string path, JsonTypeInfo<List<T>> info, CancellationToken ct)
    {
        var all = new List<T>();
        for (var page = 1; ; page++)
        {
            var batch = await _api.GetAsync($"{path}&per_page={PerPage}&page={page}", info, ct).ConfigureAwait(false);
            all.AddRange(batch);
            if (batch.Count < PerPage)
            {
                return all;
            }
        }
    }

    private async Task<GhMilestone?> FindMilestoneAsync(string gateId, CancellationToken ct)
    {
        var all = await ListAllAsync($"{_repoPath}/milestones?state=all", GitHubJsonContext.Default.ListGhMilestone, ct).ConfigureAwait(false);
        return all.FirstOrDefault(m => m.Title == gateId || m.Title.StartsWith(gateId + ":", StringComparison.Ordinal));
    }

    private async Task<int> EnsureMilestoneAsync(string gateId, CancellationToken ct)
    {
        var found = await FindMilestoneAsync(gateId, ct).ConfigureAwait(false);
        if (found is not null)
        {
            return found.Number;
        }

        var created = await _api.SendJsonAsync(HttpMethod.Post, $"{_repoPath}/milestones", new MilestoneWrite { Title = gateId, Description = Marker(gateId) },
            GitHubJsonContext.Default.MilestoneWrite, GitHubJsonContext.Default.GhMilestone, ct).ConfigureAwait(false);
        return created.Number;
    }

    private async Task EnsureLabelsAsync(string[] wanted, CancellationToken ct)
    {
        var existing = (await ListAllAsync($"{_repoPath}/labels?", GitHubJsonContext.Default.ListGhLabel, ct).ConfigureAwait(false)).Select(l => l.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var name in wanted.Where(w => !existing.Contains(w)))
        {
            var color = name == "monarch" ? "5319e7" : name.StartsWith("rank:", StringComparison.Ordinal) ? "d93f0b" : name.StartsWith("kind:", StringComparison.Ordinal) ? "0e8a16" : "1d76db";
            await _api.SendJsonAsync(HttpMethod.Post, $"{_repoPath}/labels", new LabelWrite { Name = name, Color = color },
                GitHubJsonContext.Default.LabelWrite, GitHubJsonContext.Default.GhLabel, ct).ConfigureAwait(false);
        }
    }
}
