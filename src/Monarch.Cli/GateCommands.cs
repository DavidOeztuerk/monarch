using System.Globalization;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Infrastructure.Boards.Frontmatter;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch gate import|list|show|sync</c>.</summary>
internal static class GateCommands
{
    private static int Refuse(CliEnv env, string message)
    {
        env.Err.WriteLine($"refused: {message}");
        return 1;
    }

    /// <summary>Imports backlog files (<c>G-*.md</c>, <c>Q-*.md</c>) byte-for-byte; existing local files are never overwritten.</summary>
    public static int Import(CliEnv env, string dir, string? repo)
    {
        var paths = ProjectPaths.Locate(env.Cwd, env.Home);
        if (paths is null)
        {
            return Refuse(env, "not an awakened project (run 'monarch awaken').");
        }

        var source = Path.GetFullPath(dir, env.Cwd);
        if (!Directory.Exists(source))
        {
            return Refuse(env, $"directory {source} not found.");
        }

        var gates = new List<(string File, YamlMap Root, string Id)>();
        var quests = new List<(string File, YamlMap Root, string Id, string Gate)>();
        foreach (var file in Directory.EnumerateFiles(source, "*.md").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            var isGate = name.StartsWith("G-", StringComparison.Ordinal);
            if (!isGate && !name.StartsWith("Q-", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var root = FrontmatterDocument.Parse(File.ReadAllText(file)).Root;
                var id = root.GetString("id") ?? throw new FormatException("missing 'id'");
                if (isGate)
                {
                    gates.Add((file, root, id));
                }
                else
                {
                    quests.Add((file, root, id, root.GetString("gate") ?? throw new FormatException("missing 'gate'")));
                }
            }
            catch (FormatException ex)
            {
                return Refuse(env, $"{name}: {ex.Message} (nothing imported).");
            }
        }

        if (repo is not null)
        {
            quests = [.. quests.Where(q => (q.Root.Get("scope") as YamlMap)?.GetStrings("repo").Any(r => string.Equals(r, repo, StringComparison.OrdinalIgnoreCase)) == true)];
            var wanted = quests.Select(q => q.Gate).ToHashSet(StringComparer.Ordinal);
            gates = [.. gates.Where(g => wanted.Contains(g.Id))];
        }

        var board = new LocalFileBoard(paths.Root);
        var ledger = new ProjectLedger(paths, env.Clock);
        var session = ledger.SessionId();
        int newGates = 0, newQuests = 0, skipped = 0;
        foreach (var g in gates)
        {
            var dest = Path.Combine(paths.MonarchDir, "gates", Path.GetFileName(g.File));
            if (Directory.Exists(Path.GetDirectoryName(dest)) && Directory.EnumerateFiles(Path.GetDirectoryName(dest)!, g.Id + "-*.md").Any() || File.Exists(dest))
            {
                skipped++;
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(g.File, dest);
            ledger.Append(session, "cli", "GateOpened", ("gate", g.Id), ("title", g.Root.GetString("title")), ("rank", g.Root.GetString("rank")), ("source", "import"));
            newGates++;
        }

        foreach (var q in quests)
        {
            if (board.FindQuestFile(q.Id) is not null)
            {
                skipped++;
                continue;
            }

            var dest = Path.Combine(paths.MonarchDir, "quests", Path.GetFileName(q.File));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(q.File, dest);
            ledger.Append(session, "cli", "QuestProposed", ("quest", q.Id), ("gate", q.Gate), ("rank", q.Root.GetString("rank")), ("kind", q.Root.GetString("kind")), ("source", "import"));
            newQuests++;
        }

        env.Out.WriteLine($"imported {newGates} gate(s), {newQuests} quest(s); {skipped} already present");
        return 0;
    }

    /// <summary>Lists gates.</summary>
    public static int List(CliEnv env)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return Refuse(env, "not an awakened project.");
        }

        var board = new LocalFileBoard(paths.Root);
        var quests = board.ListQuestsAsync().GetAwaiter().GetResult();
        foreach (var g in board.ListGatesAsync().GetAwaiter().GetResult())
        {
            var mine = quests.Where(q => q.Gate == g.Id).ToList();
            var cleared = mine.Count(q => q.State == QuestState.Cleared);
            env.Out.WriteLine($"{g.Id.Value}  [{g.Rank}] {g.State,-10} quests {cleared}/{mine.Count} cleared  {g.Title}");
        }

        return 0;
    }

    /// <summary>Shows a gate with its quests and the merge rule of every rank.</summary>
    public static int Show(CliEnv env, string id)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return Refuse(env, "not an awakened project.");
        }

        var board = new LocalFileBoard(paths.Root);
        var gate = board.ListGatesAsync().GetAwaiter().GetResult().FirstOrDefault(g => g.Id.Value == id);
        if (gate is null)
        {
            return Refuse(env, $"gate {id} not found.");
        }

        env.Out.WriteLine($"{gate.Id.Value}  [{gate.Rank}] {gate.State}  {gate.Title}");
        env.Out.WriteLine($"merge rule: {RankGovernance.For(gate.Rank)}; warden required: {(RankGovernance.WardenRequired(gate.Rank) ? "yes" : "no")}");
        if (gate.Boss is not null)
        {
            env.Out.WriteLine($"boss: {gate.Boss}");
        }

        var all = board.ListQuestsAsync().GetAwaiter().GetResult();
        foreach (var q in all.Where(q => q.Gate == gate.Id || gate.Quests.Contains(q.Id)).OrderBy(q => q.Id.Value, StringComparer.Ordinal))
        {
            env.Out.WriteLine($"  {q.Id.Value}  [{q.Rank}] {QuestCommands.StateName(q.State),-12} {q.Kind.ToString().ToLowerInvariant(),-9} {RankGovernance.For(q.Rank)}  {q.Title}");
        }

        foreach (var missing in gate.Quests.Where(gq => all.All(q => q.Id != gq)))
        {
            env.Out.WriteLine($"  {missing.Value}  (not imported)");
        }

        return 0;
    }

    /// <summary>Upserts milestones and issues on GitHub and writes their numbers back into the files.</summary>
    public static int Sync(CliEnv env, bool github, string? owner, string? repo)
    {
        if (ProjectPaths.Locate(env.Cwd, env.Home) is not { } paths)
        {
            return Refuse(env, "not an awakened project.");
        }

        if (!github)
        {
            return Refuse(env, "specify a target: --github.");
        }

        var forge = owner is not null && repo is not null ? new ForgeRef(owner, repo) : Forge.Resolve(paths);
        if (forge is null)
        {
            return Refuse(env, "no repository: pass --owner and --repo, or configure boards.github in system.yaml.");
        }

        try
        {
            var remote = Forge.Board(env, forge);
            var local = new LocalFileBoard(paths.Root);
            var gates = local.ListGatesAsync().GetAwaiter().GetResult();
            foreach (var g in gates)
            {
                var number = remote.SyncGateAsync(g).GetAwaiter().GetResult();
                var file = Directory.EnumerateFiles(Path.Combine(paths.MonarchDir, "gates"), g.Id.Value + "-*.md").FirstOrDefault()
                    ?? Path.Combine(paths.MonarchDir, "gates", g.Id.Value + ".md");
                BoardFiles.Edit(file, r => BoardFiles.SetBoard(r, ("provider", "github"), ("milestone", number.ToString(CultureInfo.InvariantCulture))));
            }

            var quests = local.ListQuestsAsync().GetAwaiter().GetResult();
            foreach (var q in quests)
            {
                var number = remote.SyncQuestAsync(q).GetAwaiter().GetResult();
                BoardFiles.Edit(local.FindQuestFile(q.Id.Value)!, r => BoardFiles.SetBoard(r, ("provider", "github"), ("issue", number.ToString(CultureInfo.InvariantCulture))));
            }

            env.Out.WriteLine($"synced {gates.Count} gate(s) and {quests.Count} quest(s) to {forge.Owner}/{forge.Repo}");
            return 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or FormatException)
        {
            return Refuse(env, $"sync failed: {ex.Message}");
        }
    }
}
