using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>#103 (Q-0159): verify compares with the tree of the HEAD commit, not with the git index (ADR-0010, addendum 15.11.2026).</summary>
public sealed class LedgerHeadTreeTests : IDisposable
{
    private readonly HistoryRepo _t = new();

    public void Dispose() => _t.Dispose();

    public static TheoryData<string> Flags => new() { "--assume-unchanged", "--skip-worktree" };

    [Theory]
    [MemberData(nameof(Flags))]
    public void A_project_in_a_subfolder_detects_a_forgery_with_an_index_flag(string flag)
    {
        using var sub = new HistoryRepo(sub: "sub");
        sub.Append();
        sub.W("update-index", flag, "--", sub.ForeignRel);
        HistoryRepo.AppendChained(sub.ForeignFull);

        var v = sub.Verify();

        Assert.Contains(v.Violations, x => x.File == sub.ForeignRel && x.Kind == LedgerViolationKind.UnknownFile);
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void A_project_in_a_subfolder_detects_a_truncation_with_an_index_flag(string flag)
    {
        using var sub = new HistoryRepo(sub: "sub");
        sub.Append();
        sub.W("update-index", flag, "--", sub.ForeignRel);
        HistoryRepo.DeleteLastLine(sub.ForeignFull);

        var v = sub.Verify();

        Assert.Contains(v.Violations, x => x.File == sub.ForeignRel && x.Kind == LedgerViolationKind.Truncated);
    }

    [Fact]
    public void A_project_in_a_subfolder_keeps_an_unchanged_foreign_file_anchored_by_git()
    {
        using var sub = new HistoryRepo(sub: "sub");
        sub.Append();

        var v = sub.Verify();

        Assert.True(v.Ok, string.Join("; ", v.Violations));
        Assert.Equal(1, v.AnchoredByGit);
    }

    [Fact]
    public void A_symlink_below_the_ledger_folder_in_the_tree_is_a_finding()
    {
        _t.Append();
        var link = Path.Combine(Path.GetDirectoryName(_t.ForeignFull)!, "link.jsonl");
        File.CreateSymbolicLink(link, Path.GetFileName(_t.ForeignFull));
        _t.Commit("symlink");
        _t.W("push", "-q", "origin", "HEAD:develop");
        var rel = Path.GetRelativePath(_t.Work, link).Replace('\\', '/');

        var v = _t.Verify();

        Assert.Contains(v.Violations, x => x.File == rel && x.Kind == LedgerViolationKind.HeadMismatch);
    }

    [Fact]
    public void A_foreign_file_with_another_extension_is_compared_too()
    {
        _t.Append();
        var upper = Path.Combine(Path.GetDirectoryName(_t.ForeignFull)!, "x.JSONL");
        File.WriteAllText(upper, "a\nb\n");
        _t.Commit("upper");
        _t.W("push", "-q", "origin", "HEAD:develop");
        File.WriteAllText(upper, "c\n");

        var v = _t.Verify();

        Assert.Contains(v.Violations, x => x.File.EndsWith("x.JSONL", StringComparison.Ordinal) && x.Kind == LedgerViolationKind.Truncated);
    }

    [Fact]
    public void A_repository_without_a_commit_leaves_a_foreign_file_unknown()
    {
        _t.Append();
        _t.W("checkout", "-q", "--orphan", "void");

        var v = _t.Verify();

        Assert.Contains(v.Violations, x => x.File == _t.ForeignRel && x.Kind == LedgerViolationKind.UnknownFile);
    }

    [Fact]
    public void A_missing_blob_in_an_existing_repository_is_a_finding_not_unchanged()
    {
        _t.Append();
        var loose = LooseBlob();
        File.Delete(loose);

        var v = _t.Verify();

        Assert.False(v.Ok, "verify treated a blob that git cannot read as unchanged");
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.HeadMismatch && x.File == _t.ForeignRel);
    }

    [Fact]
    public void A_missing_head_commit_object_with_a_forgery_is_a_finding_not_no_commit()
    {
        _t.Append();
        ForgeUntracked();
        var head = _t.W("rev-parse", "HEAD");
        var loose = Path.Combine(_t.RepoDir, ".git", "objects", head[..2], head[2..]);
        Assert.True(File.Exists(loose), "test setup: the HEAD commit is not a loose object");
        File.Move(loose, loose + ".bak");

        var v = _t.Verify();

        Assert.False(v.Ok, "verify treated a missing HEAD commit as 'no commit yet'");
        Assert.Contains(v.Violations, x => x.File == ".monarch/ledger" && x.Kind == LedgerViolationKind.HeadMismatch);
    }

    [Fact]
    public void A_head_pointing_to_a_blob_with_a_forgery_is_a_finding_not_no_commit()
    {
        _t.Append();
        ForgeUntracked();
        var blob = _t.W("rev-parse", "HEAD:" + _t.ForeignRel);
        File.WriteAllText(Path.Combine(_t.RepoDir, ".git", "HEAD"), blob + "\n");

        var v = _t.Verify();

        Assert.False(v.Ok, "verify treated a HEAD that is no commit as 'no commit yet'");
        Assert.Contains(v.Violations, x => x.File == ".monarch/ledger" && x.Kind == LedgerViolationKind.HeadMismatch);
    }

    [Fact]
    public void A_gitlink_below_the_ledger_folder_is_a_head_mismatch_not_truncated()
    {
        _t.Append();
        var dir = Path.Combine(Path.GetDirectoryName(_t.ForeignFull)!, "sub.jsonl");
        _t.W("update-index", "--add", "--cacheinfo", "160000," + _t.W("rev-parse", "HEAD") + "," + Path.GetRelativePath(_t.Work, dir).Replace('\\', '/'));
        _t.W("commit", "-q", "-m", "gitlink");
        _t.W("push", "-q", "origin", "HEAD:develop");

        var v = _t.Verify();

        Assert.Contains(v.Violations, x => x.File.EndsWith("sub.jsonl", StringComparison.Ordinal) && x.Kind == LedgerViolationKind.HeadMismatch);
        Assert.DoesNotContain(v.Violations, x => x.File.EndsWith("sub.jsonl", StringComparison.Ordinal) && x.Kind == LedgerViolationKind.Truncated);
    }

    [Fact]
    public void A_tracked_ledger_file_that_cannot_be_read_is_a_head_mismatch_not_skipped()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
        {
            return; // permissions cannot deny the read here
        }

        _t.Append();
        File.SetUnixFileMode(_t.ForeignFull, UnixFileMode.None);
        try
        {
            var v = _t.Verify();

            Assert.Contains(v.Violations, x => x.File == _t.ForeignRel && x.Kind == LedgerViolationKind.HeadMismatch && x.Detail.Contains("could not be read", StringComparison.Ordinal));
        }
        finally
        {
            File.SetUnixFileMode(_t.ForeignFull, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private void ForgeUntracked()
    {
        _t.W("rm", "-q", "--cached", "--", _t.ForeignRel);
        File.Delete(_t.ForeignFull);
    }

    [Fact]
    public void A_corrupt_blob_in_an_existing_repository_is_a_finding_not_unchanged()
    {
        _t.Append();
        var loose = LooseBlob();
        File.Delete(loose);
        File.WriteAllText(loose, "garbage");

        var v = _t.Verify();

        Assert.False(v.Ok, "verify treated a blob that git cannot read as unchanged");
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.HeadMismatch && (x.File == _t.ForeignRel || x.File == ".monarch/ledger"));
    }

    private string LooseBlob()
    {
        var blob = _t.W("rev-parse", "HEAD:" + _t.ForeignRel);
        var loose = Path.Combine(_t.RepoDir, ".git", "objects", blob[..2], blob[2..]);
        Assert.True(File.Exists(loose), "test setup: the blob is not a loose object");
        return loose;
    }
}
