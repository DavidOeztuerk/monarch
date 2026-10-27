using System.Text;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;

namespace Monarch.Infrastructure.Tests;

public sealed class LocalFileBoardTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private LocalFileBoard Seeded()
    {
        foreach (var (name, dir) in new[]
        {
            ("Q-0003-wt-ports-loopback.md", "quests"), ("Q-0008-wt-abmelden-ueberall.md", "quests"), ("Q-0040-cp-lizenz-token-hygiene.md", "quests"),
            ("G-0001-wt-interne-vertrauensgrenze.md", "gates"), ("G-0012-cp-lizenz.md", "gates"),
        })
        {
            var target = Path.Combine(_t.Path, ".monarch", dir);
            Directory.CreateDirectory(target);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", name), Path.Combine(target, name));
        }

        return new LocalFileBoard(_t.Path);
    }

    [Fact]
    public async Task Quests_are_listed_from_real_backlog_files()
    {
        var quests = await Seeded().ListQuestsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["Q-0003", "Q-0008", "Q-0040"], quests.Select(q => q.Id.Value));
        var q = quests[0];
        Assert.Equal(new GateId("G-0001"), q.Gate);
        Assert.Equal((Rank.A, QuestKind.Main, QuestState.Proposed), (q.Rank, q.Kind, q.State));
        Assert.StartsWith("Compose: Dienst-", q.Title, StringComparison.Ordinal);
        Assert.Equal(3, q.DefinitionOfDone.Count);
        Assert.Equal(4, q.Scope.Include.Count);
        Assert.Empty(q.Scope.Exclude);
        Assert.Equal(4, quests[1].DefinitionOfDone.Count);
        Assert.Equal(Rank.S, quests[2].Rank);
        Assert.Single(quests[2].Scope.Exclude);
    }

    [Fact]
    public async Task Gates_are_listed_from_real_backlog_files()
    {
        var gates = await Seeded().ListGatesAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["G-0001", "G-0012"], gates.Select(g => g.Id.Value));
        Assert.Equal("Workertransfer – interne Vertrauensgrenze", gates[0].Title);
        Assert.Equal(Rank.A, gates[0].Rank);
        Assert.Equal(4, gates[0].Quests.Count);
        Assert.Equal(Rank.S, gates[1].Rank);
        Assert.Equal(3, gates[1].Quests.Count);
        Assert.NotNull(gates[1].Boss);
    }

    [Fact]
    public async Task Upsert_existing_quest_changes_state_but_keeps_other_keys_and_body()
    {
        var board = Seeded();
        var file = Directory.GetFiles(Path.Combine(_t.Path, ".monarch", "quests"), "Q-0003-*.md").Single();
        var before = File.ReadAllText(file);
        var bodyBefore = before[(before.IndexOf("\n---\n", 3, StringComparison.Ordinal) + 5)..];
        var q = (await board.ListQuestsAsync(TestContext.Current.CancellationToken))[0];
        await board.UpsertQuestAsync(q with { State = QuestState.InReview }, TestContext.Current.CancellationToken);
        var after = File.ReadAllText(file);
        Assert.Contains("state: in_review", after, StringComparison.Ordinal);
        Assert.Contains("risk_tags: [infra, auth]", after, StringComparison.Ordinal);
        Assert.Contains("severity: medium", after, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetBytes(bodyBefore), Encoding.UTF8.GetBytes(after[(after.IndexOf("\n---\n", 3, StringComparison.Ordinal) + 5)..]));
        Assert.Equal(QuestState.InReview, (await board.ListQuestsAsync(TestContext.Current.CancellationToken))[0].State);
        Assert.Single(Directory.GetFiles(Path.Combine(_t.Path, ".monarch", "quests"), "Q-0003*"));
    }

    [Fact]
    public async Task Upsert_new_quest_creates_a_file_and_roundtrips()
    {
        var board = new LocalFileBoard(_t.Path);
        var quest = new BoardQuest(new QuestId("Q-0099"), new GateId("G-0001"), "Neue Quest: Ärger", QuestKind.Side, Rank.C, QuestState.Accepted,
            ["Erstes: \"Ziel\"", "Zweites, mit Komma"], new QuestScope(["src/**"], ["**/bin/**"]));
        await board.UpsertQuestAsync(quest, TestContext.Current.CancellationToken);
        await board.UpsertQuestAsync(quest, TestContext.Current.CancellationToken);
        var file = Assert.Single(Directory.GetFiles(Path.Combine(_t.Path, ".monarch", "quests")));
        Assert.StartsWith("Q-0099-", Path.GetFileName(file), StringComparison.Ordinal);
        var read = Assert.Single(await board.ListQuestsAsync(TestContext.Current.CancellationToken));
        Assert.Equal(quest with { DefinitionOfDone = [], Scope = QuestScope.Empty }, read with { DefinitionOfDone = [], Scope = QuestScope.Empty });
        Assert.Equal(quest.DefinitionOfDone, read.DefinitionOfDone);
        Assert.Equal(quest.Scope.Include, read.Scope.Include);
        Assert.Equal(quest.Scope.Exclude, read.Scope.Exclude);
    }

    [Fact]
    public async Task Upsert_gate_creates_and_updates()
    {
        var board = new LocalFileBoard(_t.Path);
        var gate = new BoardGate(new GateId("G-0042"), "Titel", Rank.B, "proposed", [new QuestId("Q-1")], "Boss");
        await board.UpsertGateAsync(gate, TestContext.Current.CancellationToken);
        await board.UpsertGateAsync(gate with { State = "active" }, TestContext.Current.CancellationToken);
        var read = Assert.Single(await board.ListGatesAsync(TestContext.Current.CancellationToken));
        Assert.Equal("active", read.State);
        Assert.Equal("Titel", read.Title);
        Assert.Equal(Rank.B, read.Rank);
        Assert.Equal(["Q-1"], read.Quests.Select(q => q.Value));
        Assert.Equal("Boss", read.Boss);
    }

    [Fact]
    public async Task Empty_board_lists_nothing()
    {
        var board = new LocalFileBoard(_t.Path);
        Assert.Empty(await board.ListQuestsAsync(TestContext.Current.CancellationToken));
        Assert.Empty(await board.ListGatesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Malformed_file_names_the_path()
    {
        var dir = Path.Combine(_t.Path, ".monarch", "quests");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Q-0001-bad.md"), "no frontmatter");
        var ex = await Assert.ThrowsAsync<FormatException>(() => new LocalFileBoard(_t.Path).ListQuestsAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Q-0001-bad.md", ex.Message, StringComparison.Ordinal);
    }
}
