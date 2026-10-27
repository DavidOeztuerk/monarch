using System.Diagnostics;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>Ledger files that arrived via git are anchored by git history (Q-0108).</summary>
public sealed class GitAnchoredLedgerTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private static void Git(string dir, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
    }

    private string CommittedLedger()
    {
        Git(_t.Path, "init", "-q");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());
        for (var i = 0; i < 3; i++)
        {
            store.Append("s1", "knight", "Note", P.Json("""{"a":1}"""));
        }

        Git(_t.Path, "add", ".monarch/ledger");
        Git(_t.Path, "commit", "-q", "-m", "ledger");
        Directory.Delete(Path.Combine(_t.Path, ".monarch", "local"), true);
        return Directory.GetFiles(Path.Combine(_t.Path, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories).Single();
    }

    [Fact]
    public void Committed_unmodified_file_without_local_anchor_is_anchored_by_git()
    {
        CommittedLedger();
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.True(v.Ok, string.Join("; ", v.Violations));
        Assert.Equal(1, v.AnchoredByGit);
        Assert.Equal(0, v.AnchoredLocally);
    }

    [Fact]
    public void Forged_uncommitted_line_is_a_violation()
    {
        var file = CommittedLedger();
        File.AppendAllText(file, "{\"seq\":4,\"forged\":true}\n");
        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Truncated_committed_file_is_a_violation()
    {
        var file = CommittedLedger();
        File.WriteAllLines(file, File.ReadAllLines(file).Take(2));
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.UnknownFile);
        Assert.Equal(0, v.AnchoredByGit);
    }

    [Fact]
    public void Untracked_unanchored_file_is_unknown()
    {
        var file = CommittedLedger();
        Git(_t.Path, "rm", "-q", "--cached", "-r", ".monarch/ledger");
        Assert.True(File.Exists(file));
        Assert.Contains(LedgerVerifier.Verify(_t.Path).Violations, x => x.Kind == LedgerViolationKind.UnknownFile);
    }

    [Fact]
    public void Outside_a_git_repo_behaviour_is_unchanged()
    {
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());
        store.Append("s1", "knight", "Note", P.Json("{}"));
        Directory.Delete(Path.Combine(_t.Path, ".monarch", "local"), true);
        Assert.Contains(LedgerVerifier.Verify(_t.Path).Violations, x => x.Kind == LedgerViolationKind.UnknownFile);
    }

    [Fact]
    public void Two_worktrees_appending_the_same_day_use_different_files_and_never_touch_committed_ones()
    {
        var file = CommittedLedger();
        var before = File.ReadAllBytes(file);
        using var other = new TempDir();
        var rel = Path.GetRelativePath(_t.Path, file);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(other.Path, rel))!);
        File.Copy(file, Path.Combine(other.Path, rel));
        var a = new JsonlLedgerStore(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        var b = new JsonlLedgerStore(new JsonlLedgerOptions { Root = other.Path }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        Assert.NotEqual(a.File, b.File);
        Assert.NotEqual(".monarch/ledger/2026-10/s1.jsonl", a.File);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal(1, a.Seq);
        Assert.Equal(1, b.Seq);
    }

    [Fact]
    public void Store_never_appends_to_a_tracked_unanchored_file()
    {
        // Same writer id, committed file of that name, no local anchor (e.g. writer-id copied or file merged in).
        Git(_t.Path, "init", "-q");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());
        var first = store.Append("s1", "knight", "Note", P.Json("{}"));
        Git(_t.Path, "add", ".monarch/ledger");
        Git(_t.Path, "commit", "-q", "-m", "l");
        File.Delete(Path.Combine(_t.Path, ".monarch", "local", "ledger-heads.json"));
        var before = File.ReadAllBytes(Path.Combine(_t.Path, first.File));
        var next = store.Append("s1", "knight", "Note", P.Json("{}"));
        Assert.NotEqual(first.File, next.File);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_t.Path, first.File)));
    }
}
