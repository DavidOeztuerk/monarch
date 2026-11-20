using Monarch.Application.Ports;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

public sealed class SqliteReadModelTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private SqliteReadModel Model(string name) => new(new SqliteReadModelOptions(Path.Combine(_t.Path, ".monarch", "local", name)));

    private List<LedgerEntry> Seed()
    {
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());
        var all = new List<LedgerEntry>();
        void A(string session, string type, string json) => all.Add(store.Append(session, "knight", type, P.Json(json)));
        A("s1", "GateOpened", """{"gate":"G-1","rank":"A","title":"Trust boundary"}""");
        A("s1", "QuestProposed", """{"quest":"Q-1","gate":"G-1","rank":"A","kind":"main"}""");
        A("s2", "QuestProposed", """{"quest":"Q-2","gate":"G-1","rank":"D","kind":"side"}""");
        A("s1", "QuestAccepted", """{"quest":"Q-1","shadow":"knight@1"}""");
        A("s1", "QuestStarted", """{"quest":"Q-1"}""");
        A("s2", "QuestAbandoned", """{"quest":"Q-2"}""");
        A("s1", "QuestSubmitted", """{"quest":"Q-1"}""");
        A("s1", "QuestVerified", """{"quest":{"Value":"Q-1"}}""");
        A("s1", "QuestCleared", """{"quest":"Q-1","xp":120}""");
        A("s1", "ManaSnapshotTaken", """{"pool":{}}""");
        return all;
    }

    [Fact]
    public void Rebuild_projects_quests_gates_and_events()
    {
        Seed();
        var m = Model("a.db");
        m.Rebuild(_t.Path);
        var q1 = Assert.Single(m.GetQuests(), q => q.Id == "Q-1");
        Assert.Equal(("G-1", "cleared", "A", "main", 120L), (q1.Gate, q1.State, q1.Rank, q1.Kind, q1.Xp));
        Assert.Equal("abandoned", m.GetQuests().Single(q => q.Id == "Q-2").State);
        Assert.Equal(new GateRow("G-1", "A", "Trust boundary"), Assert.Single(m.GetGates()));
        Assert.Equal(10, m.GetEvents().Count);
    }

    [Fact]
    public void Incremental_apply_equals_full_rebuild()
    {
        var entries = Seed();
        var inc = Model("inc.db");
        foreach (var e in entries)
        {
            inc.Apply(e);
        }

        var full = Model("full.db");
        full.Rebuild(_t.Path);
        Assert.Equal(full.GetQuests(), inc.GetQuests());
        Assert.Equal(full.GetGates(), inc.GetGates());
        Assert.Equal(full.GetEvents(), inc.GetEvents());
    }

    [Fact]
    public void Apply_is_idempotent_and_rebuild_replaces_content()
    {
        var entries = Seed();
        var m = Model("i.db");
        foreach (var e in entries.Concat(entries))
        {
            m.Apply(e);
        }

        Assert.Equal(10, m.GetEvents().Count);
        m.Rebuild(_t.Path);
        m.Rebuild(_t.Path);
        Assert.Equal(10, m.GetEvents().Count);
    }

    [Fact]
    public void Unknown_event_types_are_kept_as_events_only()
    {
        var m = Model("u.db");
        m.Rebuild(_t.Path);
        Assert.Empty(m.GetQuests());
    }
}
