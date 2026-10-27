using System.Text;
using Microsoft.Extensions.Options;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Hosting;
using Monarch.Infrastructure.Boards.Frontmatter;

namespace Monarch.Infrastructure.Boards;

/// <summary>
/// Quest board backed by markdown files with YAML frontmatter:
/// <c>&lt;root&gt;/.monarch/quests/Q-xxxx-*.md</c> and <c>&lt;root&gt;/.monarch/gates/G-xxxx-*.md</c>.
/// Upserts only touch the keys they own; all other keys and the markdown body are preserved.
/// </summary>
public sealed class LocalFileBoard : IQuestBoard
{
    private static readonly UTF8Encoding Utf8 = new(false);
    private readonly string _root;

    /// <summary>Initializes a board for a project root.</summary>
    /// <param name="root">Project root.</param>
    public LocalFileBoard(string root)
    {
        ArgumentException.ThrowIfNullOrEmpty(root);
        _root = root;
    }

    /// <summary>Initializes a board from the Monarch options.</summary>
    /// <param name="options">Monarch options; <see cref="MonarchOptions.Root"/> is the project root.</param>
    public LocalFileBoard(IOptions<MonarchOptions> options)
        : this(options.Value.Root)
    {
    }

    private string QuestDir => Path.Combine(_root, ".monarch", "quests");

    private string GateDir => Path.Combine(_root, ".monarch", "gates");

    /// <inheritdoc />
    public Task<IReadOnlyList<BoardGate>> ListGatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BoardGate>>([.. Files(GateDir, "G-").Select(f => ToGate(f, Load(f).Doc))]);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BoardQuest>> ListQuestsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BoardQuest>>([.. Files(QuestDir, "Q-").Select(f => ToQuest(f, Load(f).Doc))]);
    }

    /// <inheritdoc />
    public Task UpsertGateAsync(BoardGate gate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gate);
        cancellationToken.ThrowIfCancellationRequested();
        var id = gate.Id.Value;
        var path = Find(GateDir, id);
        var doc = path is null
            ? new FrontmatterDocument(
                YamlMap.Empty.With("id", new YamlScalar(id)).With("type", new YamlScalar("standard")),
                $"# {gate.Title}\n")
            : Load(path).Doc;
        var before = doc;
        var existing = path is null ? null : ToGate(path, doc);
        var root = doc.Root;
        if (existing is null || existing.Title != gate.Title)
        {
            root = root.With("title", Text(gate.Title));
        }

        if (existing is null || existing.Rank != gate.Rank)
        {
            root = root.With("rank", new YamlScalar(gate.Rank.ToString()));
        }

        if (existing is null || existing.State != gate.State)
        {
            root = root.With("state", new YamlScalar(gate.State));
        }

        if (existing is null || !existing.Quests.SequenceEqual(gate.Quests))
        {
            root = root.With("quests", new YamlList([.. gate.Quests.Select(q => (YamlNode)new YamlScalar(q.Value))], true));
        }

        if (existing is null || existing.Boss != gate.Boss)
        {
            if (gate.Boss is not null)
            {
                root = root.With("boss", new YamlScalar(gate.Boss, YamlQuote.Double));
            }
        }

        doc = doc with { Root = root };
        if (path is null || doc != before)
        {
            Save(path ?? Path.Combine(GateDir, $"{id}-{Slug(gate.Title)}.md"), doc);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UpsertQuestAsync(BoardQuest quest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(quest);
        cancellationToken.ThrowIfCancellationRequested();
        var id = quest.Id.Value;
        var path = Find(QuestDir, id);
        var doc = path is null ? new FrontmatterDocument(YamlMap.Empty.With("id", new YamlScalar(id)), $"# {quest.Title}\n") : Load(path).Doc;
        var before = doc;
        var existing = path is null ? null : ToQuest(path, doc);
        var root = doc.Root;
        if (existing is null || existing.Gate != quest.Gate)
        {
            root = root.With("gate", new YamlScalar(quest.Gate.Value));
        }

        if (existing is null || existing.Kind != quest.Kind)
        {
            root = root.With("kind", new YamlScalar(quest.Kind.ToString().ToLowerInvariant()));
        }

        if (existing is null || existing.Rank != quest.Rank)
        {
            root = root.With("rank", new YamlScalar(quest.Rank.ToString()));
        }

        if (existing is null || existing.State != quest.State)
        {
            root = root.With("state", new YamlScalar(BoardNames.State(quest.State)));
        }

        if (existing is null || !existing.DefinitionOfDone.SequenceEqual(quest.DefinitionOfDone))
        {
            root = root.With("dod", new YamlList([.. quest.DefinitionOfDone.Select(d => (YamlNode)new YamlScalar(d))], false));
        }

        if ((existing is null && (quest.Scope.Include.Count > 0 || quest.Scope.Exclude.Count > 0))
            || (existing is not null && (!existing.Scope.Include.SequenceEqual(quest.Scope.Include) || !existing.Scope.Exclude.SequenceEqual(quest.Scope.Exclude))))
        {
            var scope = root.Get("scope") as YamlMap ?? YamlMap.Empty;
            scope = scope.With("include", Globs(quest.Scope.Include)).With("exclude", Globs(quest.Scope.Exclude));
            root = root.With("scope", scope);
        }

        doc = doc with { Root = root };
        if (path is null || doc != before)
        {
            Save(path ?? Path.Combine(QuestDir, $"{id}-{Slug(quest.Title)}.md"), doc);
        }

        return Task.CompletedTask;
    }

    private static YamlList Globs(IReadOnlyList<string> globs) => new([.. globs.Select(g => (YamlNode)new YamlScalar(g, YamlQuote.Double))], true);

    private static YamlScalar Text(string value) => new(value, value.Contains(':', StringComparison.Ordinal) || value.Contains('#', StringComparison.Ordinal) ? YamlQuote.Double : YamlQuote.None);

    private static string Slug(string title)
    {
        var sb = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > 40)
        {
            slug = slug[..40].Trim('-');
        }

        return slug.Length == 0 ? "item" : slug;
    }

    private static IEnumerable<string> Files(string dir, string prefix) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, prefix + "*.md").Order(StringComparer.Ordinal) : [];

    private static string? Find(string dir, string id) =>
        Files(dir, id).FirstOrDefault(f => Path.GetFileName(f).StartsWith(id + "-", StringComparison.Ordinal) || Path.GetFileName(f) == id + ".md");

    private static (FrontmatterDocument Doc, string Path) Load(string path)
    {
        try
        {
            return (FrontmatterDocument.Parse(File.ReadAllText(path, Utf8)), path);
        }
        catch (FormatException ex)
        {
            throw new FormatException($"{path}: {ex.Message}", ex);
        }
    }

    private static void Save(string path, FrontmatterDocument doc)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, doc.Write(), Utf8);
        File.Move(temp, path, overwrite: true);
    }

    private static string? Heading(FrontmatterDocument doc)
    {
        foreach (var line in doc.Body.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return null;
    }

    private static BoardQuest ToQuest(string path, FrontmatterDocument doc)
    {
        var r = doc.Root;
        var id = r.GetString("id") ?? throw new FormatException($"{path}: missing 'id'.");
        var gate = r.GetString("gate") ?? throw new FormatException($"{path}: missing 'gate'.");
        var scope = r.Get("scope") is YamlMap sm ? new QuestScope(sm.GetStrings("include"), sm.GetStrings("exclude")) : QuestScope.Empty;
        return new BoardQuest(
            new QuestId(id), new GateId(gate), r.GetString("title") ?? Heading(doc) ?? id,
            BoardNames.ParseKind(r.GetString("kind")), BoardNames.ParseRank(r.GetString("rank")), BoardNames.ParseState(r.GetString("state")),
            r.GetStrings("dod"), scope);
    }

    private static BoardGate ToGate(string path, FrontmatterDocument doc)
    {
        var r = doc.Root;
        var id = r.GetString("id") ?? throw new FormatException($"{path}: missing 'id'.");
        return new BoardGate(
            new GateId(id), r.GetString("title") ?? Heading(doc) ?? id, BoardNames.ParseRank(r.GetString("rank")),
            r.GetString("state") ?? "proposed", [.. r.GetStrings("quests").Select(q => new QuestId(q))], r.GetString("boss"));
    }
}
