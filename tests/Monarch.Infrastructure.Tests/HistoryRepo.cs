using System.Diagnostics;
using System.Text.Json;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>
/// Test setup for ADR-0018: a bare remote whose <c>develop</c> carries a foreign ledger file (written by a "seed" worktree),
/// and a clone ("work") whose own store writes its own ledger file and gets its base on the first append.
/// </summary>
internal sealed class HistoryRepo : IDisposable
{
    public const string BaseFile = ".monarch/local/ledger-base";
    public static readonly string[] Trusted = ["develop", "main", "master"];

    private readonly TempDir _dir = new();
    private readonly JsonlLedgerStore _seedStore;

    /// <summary>What the commit before the last one on develop (D0) holds of the foreign file.</summary>
    public enum Older
    {
        /// <summary>No commit before D1.</summary>
        None,

        /// <summary>D0 has the foreign file with two lines, D1 with three.</summary>
        Short,

        /// <summary>D0 has no foreign file at all.</summary>
        Missing,
    }

    public HistoryRepo(Older older = Older.None, string sub = "")
    {
        Remote = Path.Combine(_dir.Path, "remote.git");
        Directory.CreateDirectory(Remote);
        Git(Remote, "init", "-q", "--bare", "-b", "develop");
        var seedRepo = Path.Combine(_dir.Path, "seed");
        Directory.CreateDirectory(seedRepo);
        Git(seedRepo, "init", "-q", "-b", "develop");
        Git(seedRepo, "remote", "add", "origin", Remote);
        Seed = sub.Length == 0 ? seedRepo : Path.Combine(seedRepo, sub);
        Directory.CreateDirectory(Seed);
        _seedStore = new JsonlLedgerStore(new JsonlLedgerOptions { Root = Seed }, new FixedClock());
        if (older == Older.Missing)
        {
            File.WriteAllText(Path.Combine(Seed, "README.md"), "x");
            Git(Seed, "add", "README.md");
            Git(Seed, "commit", "-q", "-m", "readme");
            D0 = Git(Seed, "rev-parse", "HEAD");
        }

        for (var i = 0; i < 3; i++)
        {
            _seedStore.Append("s9", "knight", "Note", P.Json("""{"foreign":true}"""));
            if (older == Older.Short && i == 1)
            {
                CommitSeed("d0");
                D0 = Git(Seed, "rev-parse", "HEAD");
            }
        }

        CommitSeed("d1");
        Git(Seed, "push", "-q", "origin", "develop");
        ForeignRel = Path.GetRelativePath(Seed, Directory.GetFiles(Path.Combine(Seed, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories).Single()).Replace('\\', '/');
        Git(_dir.Path, "clone", "-q", Remote, "work");
        Work = sub.Length == 0 ? Path.Combine(_dir.Path, "work") : Path.Combine(_dir.Path, "work", sub);
        WorkStore = new JsonlLedgerStore(new JsonlLedgerOptions { Root = Work }, new FixedClock());
    }

    public string Remote { get; }

    public string Seed { get; }

    public string Work { get; }

    public string RepoDir => Path.Combine(_dir.Path, "work");

    public string BasePath => Path.Combine(Work, ".monarch", "local", "ledger-base");

    public string? D0 { get; private set; }

    public string ForeignRel { get; }

    public string ForeignFull => Path.Combine(Work, ForeignRel.Replace('/', Path.DirectorySeparatorChar));

    public JsonlLedgerStore WorkStore { get; }

    public string Path_(string name) => Path.Combine(_dir.Path, name);

    public static string Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)} failed: {err.Result}");
        return output.Trim();
    }

    public string W(params string[] args) => Git(Work, args);

    public LedgerEntry Append() => WorkStore.Append("s1", "knight", "Note", P.Json("{}"));

    public void Commit(string message)
    {
        W("add", "-A", ".monarch/ledger");
        W("commit", "-q", "--allow-empty", "-m", message);
    }

    public LedgerVerification Verify() => LedgerVerifier.Verify(Work, Trusted);

    public string PushMoreForeign(int lines)
    {
        for (var i = 0; i < lines; i++)
        {
            _seedStore.Append("s9", "knight", "Note", P.Json("""{"more":true}"""));
        }

        CommitSeed("d2");
        Git(Seed, "push", "-q", "origin", "develop");
        return Git(Seed, "rev-parse", "HEAD");
    }

    /// <summary>Removes the last line of a ledger file (K-delete-last).</summary>
    public static void DeleteLastLine(string file)
    {
        var lines = File.ReadAllLines(file);
        File.WriteAllText(file, string.Join("\n", lines[..^1]) + "\n");
    }

    /// <summary>A chain-correct line that follows the last line of <paramref name="file"/> (K-append-chained).</summary>
    public static string ChainedLine(string file, string type = "QuestCleared", string payload = "{}")
    {
        using var doc = JsonDocument.Parse(File.ReadAllLines(file)[^1]);
        var seq = doc.RootElement.GetProperty("seq").GetInt64();
        var hash = doc.RootElement.GetProperty("hash").GetString()!;
        var hashless = LedgerCanonical.Serialize(seq + 1, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), "forger", type, P.Json(payload), hash);
        return LedgerCanonical.Seal(hashless, LedgerCanonical.ComputeHash(hashless));
    }

    public static void AppendChained(string file) => File.AppendAllText(file, ChainedLine(file) + "\n");

    public static bool Unanchored(LedgerVerification v, string? file = null) =>
        v.Violations.Any(x => x.Kind == LedgerViolationKind.UnanchoredCommit && (file is null || x.File == file));

    public void Dispose() => _dir.Dispose();

    private void CommitSeed(string message)
    {
        Git(Seed, "add", "-A", ".monarch/ledger");
        Git(Seed, "commit", "-q", "-m", message);
    }
}
