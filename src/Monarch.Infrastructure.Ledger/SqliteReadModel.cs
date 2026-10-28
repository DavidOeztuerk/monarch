using System.Text.Json;
using Microsoft.Data.Sqlite;
using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// SQLite read model projected from the ledger. Tables: <c>events</c>, <c>quests</c>, <c>gates</c>.
/// Recognised event types (payload keys are strings; <c>quest</c>/<c>gate</c> may also be <c>{"Value":"…"}</c>):
/// <c>GateOpened{gate,rank,title}</c>, <c>QuestProposed{quest,gate,rank,kind}</c>, <c>QuestAccepted</c>, <c>QuestStarted</c>,
/// <c>QuestSubmitted</c>, <c>QuestRejected</c>, <c>QuestReplanned</c>, <c>QuestVerified</c>, <c>QuestCleared{quest,xp}</c>,
/// <c>QuestFailed</c>, <c>QuestAbandoned</c>. All other types are stored as events only.
/// </summary>
/// <param name="options">Options.</param>
public sealed class SqliteReadModel(SqliteReadModelOptions options) : IReadModel
{
    private static readonly object InitLock = new();
    private static bool initialized;

    private static readonly Dictionary<string, string> StateByEvent = new(StringComparer.Ordinal)
    {
        ["QuestAccepted"] = "accepted",
        ["QuestStarted"] = "in_progress",
        ["QuestRejected"] = "in_progress",
        ["QuestSubmitted"] = "in_review",
        ["QuestReplanned"] = "accepted",
        ["QuestVerified"] = "verified",
        ["QuestCleared"] = "cleared",
        ["QuestFailed"] = "failed",
        ["QuestAbandoned"] = "abandoned",
    };

    /// <inheritdoc />
    public void Rebuild(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        var entries = new List<LedgerEntry>();
        foreach (var file in LedgerFiles.EnumerateRelative(root))
        {
            entries.AddRange(LedgerFiles.ReadEntries(root, file));
        }

        entries.Sort((a, b) =>
        {
            var c = a.At.CompareTo(b.At);
            if (c != 0)
            {
                return c;
            }

            c = string.CompareOrdinal(a.File, b.File);
            return c != 0 ? c : a.Seq.CompareTo(b.Seq);
        });

        using var db = Open();
        using var tx = db.BeginTransaction();
        Exec(db, "DROP TABLE IF EXISTS events; DROP TABLE IF EXISTS quests; DROP TABLE IF EXISTS gates;");
        CreateSchema(db);
        foreach (var entry in entries)
        {
            ApplyCore(db, entry);
        }

        tx.Commit();
    }

    /// <inheritdoc />
    public void Apply(LedgerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        using var db = Open();
        using var tx = db.BeginTransaction();
        ApplyCore(db, entry);
        tx.Commit();
    }

    /// <inheritdoc />
    public IReadOnlyList<QuestRow> GetQuests() =>
        Query("SELECT id, gate, state, rank, kind, xp, updated_at FROM quests ORDER BY id",
            r => new QuestRow(r.GetString(0), Str(r, 1), r.GetString(2), Str(r, 3), Str(r, 4), r.GetInt64(5), r.GetString(6)));

    /// <inheritdoc />
    public IReadOnlyList<GateRow> GetGates() =>
        Query("SELECT id, rank, title FROM gates ORDER BY id", r => new GateRow(r.GetString(0), Str(r, 1), Str(r, 2)));

    /// <inheritdoc />
    public IReadOnlyList<EventRow> GetEvents() =>
        Query("SELECT file, seq, type, at, actor, payload FROM events ORDER BY at, file, seq",
            r => new EventRow(r.GetString(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5)));

    private static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    private SqliteConnection Open()
    {
        lock (InitLock)
        {
            if (!initialized)
            {
                SQLitePCL.Batteries_V2.Init();
                initialized = true;
            }
        }

        var full = Path.GetFullPath(options.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var db = new SqliteConnection($"Data Source={full};Pooling=False");
        db.Open();
        CreateSchema(db);
        return db;
    }

    private static void CreateSchema(SqliteConnection db) => Exec(db, """
        CREATE TABLE IF NOT EXISTS events(file TEXT NOT NULL, seq INTEGER NOT NULL, type TEXT NOT NULL, at TEXT NOT NULL, actor TEXT NOT NULL, payload TEXT NOT NULL, PRIMARY KEY(file, seq));
        CREATE TABLE IF NOT EXISTS quests(id TEXT PRIMARY KEY, gate TEXT, state TEXT NOT NULL, rank TEXT, kind TEXT, xp INTEGER NOT NULL DEFAULT 0, updated_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS gates(id TEXT PRIMARY KEY, rank TEXT, title TEXT);
        """);

    private static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private List<T> Query<T>(string sql, Func<SqliteDataReader, T> map)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        using var reader = cmd.ExecuteReader();
        var list = new List<T>();
        while (reader.Read())
        {
            list.Add(map(reader));
        }

        return list;
    }

    private static void ApplyCore(SqliteConnection db, LedgerEntry e)
    {
        var at = e.At.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var payload = e.Payload.ValueKind == JsonValueKind.Undefined ? "null" : e.Payload.GetRawText();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "INSERT OR IGNORE INTO events(file, seq, type, at, actor, payload) VALUES($f, $s, $t, $a, $c, $p)";
            cmd.Parameters.AddWithValue("$f", e.File);
            cmd.Parameters.AddWithValue("$s", e.Seq);
            cmd.Parameters.AddWithValue("$t", e.Type);
            cmd.Parameters.AddWithValue("$a", at);
            cmd.Parameters.AddWithValue("$c", e.Actor);
            cmd.Parameters.AddWithValue("$p", payload);
            if (cmd.ExecuteNonQuery() == 0)
            {
                return;
            }
        }

        if (e.Payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        switch (e.Type)
        {
            case "GateOpened":
                if (Id(e.Payload, "gate") is { } gate)
                {
                    Run(db, "INSERT INTO gates(id, rank, title) VALUES($i, $r, $t) ON CONFLICT(id) DO UPDATE SET rank = COALESCE(excluded.rank, rank), title = COALESCE(excluded.title, title)",
                        ("$i", gate), ("$r", Id(e.Payload, "rank")), ("$t", Id(e.Payload, "title")));
                }

                break;
            case "QuestProposed":
                if (Id(e.Payload, "quest") is { } proposed)
                {
                    Run(db, """
                        INSERT INTO quests(id, gate, state, rank, kind, xp, updated_at) VALUES($i, $g, 'proposed', $r, $k, 0, $u)
                        ON CONFLICT(id) DO UPDATE SET gate = COALESCE(excluded.gate, gate), rank = COALESCE(excluded.rank, rank),
                          kind = COALESCE(excluded.kind, kind), state = 'proposed', updated_at = excluded.updated_at
                        """, ("$i", proposed), ("$g", Id(e.Payload, "gate")), ("$r", Id(e.Payload, "rank")), ("$k", Id(e.Payload, "kind")), ("$u", at));
                }

                break;
            default:
                if (StateByEvent.TryGetValue(e.Type, out var state) && Id(e.Payload, "quest") is { } quest)
                {
                    var xp = e.Type == "QuestCleared" && e.Payload.TryGetProperty("xp", out var x) && x.TryGetInt64(out var v) ? v : 0;
                    Run(db, """
                        INSERT INTO quests(id, state, xp, updated_at) VALUES($i, $s, $x, $u)
                        ON CONFLICT(id) DO UPDATE SET state = excluded.state, updated_at = excluded.updated_at,
                          xp = CASE WHEN $c = 1 THEN excluded.xp ELSE xp END
                        """, ("$i", quest), ("$s", state), ("$x", xp), ("$u", at), ("$c", e.Type == "QuestCleared" ? 1L : 0L));
                }

                break;
        }
    }

    private static void Run(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
        {
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }

    private static string? Id(JsonElement payload, string name)
    {
        if (!payload.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Object when p.TryGetProperty("Value", out var v) && v.ValueKind == JsonValueKind.String => v.GetString(),
            _ => null,
        };
    }
}
