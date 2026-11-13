using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>ADR-0018 / Q-0154: <c>verify</c> checks branch-local commits against the remote states and the worktree's own writes (#71).</summary>
public sealed class LedgerHistoryTests
{
    // ---- red before the implementation: forgery by a normal commit

    [Fact]
    public void K_delete_last_committed_normally_is_found()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        var v = r.Verify();
        Assert.False(v.Ok);
        var found = Assert.Single(v.Violations, x => x.Kind == LedgerViolationKind.UnanchoredCommit);
        Assert.Equal(r.ForeignRel, found.File);
        Assert.Equal(0, found.Line);
        Assert.Contains("git fetch", found.Detail, StringComparison.Ordinal);
        Assert.Contains(r.W("rev-parse", "HEAD")[..7], found.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void K_append_chained_committed_normally_is_found()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.AppendChained(r.ForeignFull);
        r.Commit("forged");
        var v = r.Verify();
        Assert.False(v.Ok);
        Assert.True(HistoryRepo.Unanchored(v, r.ForeignRel));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_forgery_stays_a_finding_after_a_push_to_an_unprotected_remote_branch(bool deleteLast)
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        if (deleteLast)
        {
            HistoryRepo.DeleteLastLine(r.ForeignFull);
        }
        else
        {
            HistoryRepo.AppendChained(r.ForeignFull);
        }

        r.Commit("forged");
        r.W("push", "-q", "origin", "monarch/G-1-x");
        Assert.Equal(r.W("rev-parse", "HEAD"), r.W("rev-parse", "refs/remotes/origin/monarch/G-1-x"));
        Assert.True(HistoryRepo.Unanchored(r.Verify(), r.ForeignRel));
    }

    [Fact]
    public void A_new_forged_file_with_a_foreign_writer_id_is_found()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        var rel = ".monarch/ledger/2026-10/s7.deadbeef.jsonl";
        var line = LedgerCanonical.Serialize(1, DateTimeOffset.UnixEpoch, "forger", "QuestCleared", P.Json("{}"), LedgerCanonical.Genesis("s7.deadbeef"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(r.Work, rel))!);
        File.WriteAllText(Path.Combine(r.Work, rel), LedgerCanonical.Seal(line, LedgerCanonical.ComputeHash(line)) + "\n");
        r.Commit("forged");
        Assert.True(HistoryRepo.Unanchored(r.Verify(), rel));
    }

    [Fact]
    public void A_merge_that_takes_a_shorter_parent_version_is_found()
    {
        using var r = new HistoryRepo(HistoryRepo.Older.Short);
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x", r.D0!);
        r.Append();
        r.Commit("own");
        r.W("merge", "-q", "-s", "ours", "--no-edit", "origin/develop");
        var v = r.Verify();
        Assert.True(HistoryRepo.Unanchored(v, r.ForeignRel));
        Assert.Contains("merge", v.Violations.Single(x => x.Kind == LedgerViolationKind.UnanchoredCommit).Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_merge_that_drops_a_file_a_parent_has_is_found()
    {
        using var r = new HistoryRepo(HistoryRepo.Older.Missing);
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x", r.D0!);
        r.Append();
        r.Commit("own");
        r.W("merge", "-q", "-s", "ours", "--no-edit", "origin/develop");
        Assert.False(File.Exists(r.ForeignFull));
        Assert.True(HistoryRepo.Unanchored(r.Verify(), r.ForeignRel));
    }

    [Fact]
    public void A_merge_that_composes_a_foreign_file_differently_from_all_parents_is_found()
    {
        using var r = new HistoryRepo(HistoryRepo.Older.Short);
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x", r.D0!);
        r.Append();
        r.Commit("own");
        r.W("merge", "-q", "--no-commit", "--no-ff", "origin/develop");
        var first = File.ReadAllLines(r.ForeignFull)[0];
        File.WriteAllText(r.ForeignFull, first + "\n");
        var other = HistoryRepo.ChainedLine(r.ForeignFull, "Other", """{"x":1}""");
        File.AppendAllText(r.ForeignFull, other + "\n");
        r.Commit("merge");
        Assert.True(HistoryRepo.Unanchored(r.Verify(), r.ForeignRel));
    }

    [Fact]
    public void An_orphan_commit_with_a_forged_file_merged_with_unrelated_histories_is_found()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.W("checkout", "-q", "--orphan", "evil");
        r.W("rm", "-rfq", ".");
        var rel = ".monarch/ledger/2026-10/s7.deadbeef.jsonl";
        var line = LedgerCanonical.Serialize(1, DateTimeOffset.UnixEpoch, "forger", "QuestCleared", P.Json("{}"), LedgerCanonical.Genesis("s7.deadbeef"));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(r.Work, rel))!);
        File.WriteAllText(Path.Combine(r.Work, rel), LedgerCanonical.Seal(line, LedgerCanonical.ComputeHash(line)) + "\n");
        r.W("add", rel);
        r.W("commit", "-q", "-m", "evil root");
        r.W("checkout", "-q", "monarch/G-1-x");
        r.W("merge", "-q", "--allow-unrelated-histories", "--no-edit", "evil");
        Assert.True(HistoryRepo.Unanchored(r.Verify(), rel));
    }

    [Fact]
    public void A_deleted_base_stays_gone_and_the_forgery_is_still_found()
    {
        using var r = new HistoryRepo();
        r.Append();
        File.Delete(r.BasePath);
        r.Append();
        Assert.False(File.Exists(r.BasePath));
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        Assert.True(HistoryRepo.Unanchored(r.Verify(), r.ForeignRel));
        Assert.False(File.Exists(r.BasePath));
    }

    [Fact]
    public void Without_a_base_and_with_the_remote_states_deleted_the_forgery_is_a_finding_with_a_fetch_hint()
    {
        using var r = new HistoryRepo();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        Assert.False(File.Exists(r.BasePath));
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        foreach (var name in r.W("for-each-ref", "--format=%(refname)", "refs/remotes/origin").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            r.W("update-ref", "--no-deref", "-d", name);
        }

        Assert.Equal(string.Empty, r.W("for-each-ref", "refs/remotes"));
        Assert.NotEqual(string.Empty, r.W("remote", "get-url", "origin"));
        var v = r.Verify();
        var found = Assert.Single(v.Violations, x => x.Kind == LedgerViolationKind.UnanchoredCommit);
        Assert.Contains("git fetch", found.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_invalid_base_does_not_count_and_without_remote_states_is_a_finding()
    {
        using var r = new HistoryRepo();
        r.Append();
        File.WriteAllText(r.BasePath, "not-a-commit");
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Commit("own");
        foreach (var name in r.W("for-each-ref", "--format=%(refname)", "refs/remotes/origin").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            r.W("update-ref", "--no-deref", "-d", name);
        }

        Assert.True(HistoryRepo.Unanchored(r.Verify()));
    }

    [Fact]
    public void A_git_error_while_reading_the_history_is_a_finding_not_ok()
    {
        using var r = new HistoryRepo(HistoryRepo.Older.Short);
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x", r.D0!);
        r.Append();
        r.Commit("own");
        r.W("merge", "-q", "--no-edit", "origin/develop");
        var ok = r.Verify();
        Assert.True(ok.Ok, "legitimate merge of origin/develop: " + string.Join("; ", ok.Violations));
        var blob = r.W("rev-parse", "HEAD:./" + r.ForeignRel);
        var loose = Path.Combine(r.RepoDir, ".git", "objects", blob[..2], blob[2..]);
        Assert.True(File.Exists(loose), "blob object must be loose for this test");
        File.SetAttributes(loose, FileAttributes.Normal);
        File.Delete(loose);
        var v = r.Verify();
        Assert.False(v.Ok);
        Assert.True(HistoryRepo.Unanchored(v));
    }

    [Fact]
    public void A_git_timeout_is_a_finding_not_ok()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own");
        Assert.True(r.Verify().Ok);
        var v = LedgerVerifier.Verify(r.Work, HistoryRepo.Trusted, TimeSpan.FromMilliseconds(1));
        Assert.False(v.Ok);
        Assert.True(HistoryRepo.Unanchored(v));
    }

    [Fact]
    public void A_project_in_a_subfolder_of_the_repository_has_ok_own_commits_and_finds_the_forgery()
    {
        using var r = new HistoryRepo(sub: "proj");
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own");
        Assert.True(r.Verify().Ok);
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        var v = r.Verify();
        Assert.True(HistoryRepo.Unanchored(v, r.ForeignRel));
        Assert.Single(v.Violations, x => x.Kind == LedgerViolationKind.UnanchoredCommit);
    }

    // ---- ReadSession and the base

    [Fact]
    public void ReadSession_before_the_first_append_creates_no_writer_id_and_the_first_append_creates_the_base()
    {
        using var t = new TempDir();
        HistoryRepo.Git(t.Path, "init", "-q", "-b", "develop");
        HistoryRepo.Git(t.Path, "commit", "-q", "--allow-empty", "-m", "x");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = t.Path }, new FixedClock());
        Assert.Empty(store.ReadSession("s1"));
        Assert.False(File.Exists(Path.Combine(t.Path, ".monarch", "local", "writer-id")));
        store.Append("s1", "knight", "Note", P.Json("{}"));
        Assert.Equal(HistoryRepo.Git(t.Path, "rev-parse", "HEAD"), File.ReadAllText(Path.Combine(t.Path, ".monarch", "local", "ledger-base")).Trim());
        Assert.DoesNotContain(Directory.GetFiles(Path.Combine(t.Path, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories).SelectMany(File.ReadAllLines), l => l.Contains("LedgerAnchorReset", StringComparison.Ordinal));
    }

    [Fact]
    public void The_base_is_written_exactly_once_and_never_moved()
    {
        using var t = new TempDir();
        HistoryRepo.Git(t.Path, "init", "-q", "-b", "develop");
        HistoryRepo.Git(t.Path, "commit", "-q", "--allow-empty", "-m", "one");
        var first = HistoryRepo.Git(t.Path, "rev-parse", "HEAD");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = t.Path }, new FixedClock());
        store.Append("s1", "knight", "Note", P.Json("{}"));
        HistoryRepo.Git(t.Path, "commit", "-q", "--allow-empty", "-m", "two");
        store.Append("s1", "knight", "Note", P.Json("{}"));
        Assert.Equal(first, File.ReadAllText(Path.Combine(t.Path, ".monarch", "local", "ledger-base")).Trim());
    }

    [Fact]
    public void No_base_without_a_repository_or_without_a_commit()
    {
        using var plain = new TempDir();
        new JsonlLedgerStore(new JsonlLedgerOptions { Root = plain.Path }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        Assert.False(File.Exists(Path.Combine(plain.Path, ".monarch", "local", "ledger-base")));
        using var empty = new TempDir();
        HistoryRepo.Git(empty.Path, "init", "-q", "-b", "develop");
        new JsonlLedgerStore(new JsonlLedgerOptions { Root = empty.Path }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        Assert.False(File.Exists(Path.Combine(empty.Path, ".monarch", "local", "ledger-base")));
    }

    [Fact]
    public void No_base_after_an_anchor_reset_and_no_base_on_a_new_writer_generation()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.Commit("own");
        File.Delete(r.BasePath);
        File.WriteAllText(Path.Combine(r.Work, ".monarch", "local", "ledger-heads.json"), "garbage");
        r.Append();
        Assert.False(File.Exists(r.BasePath), "after a reset");
        File.Delete(Path.Combine(r.Work, ".monarch", "local", "ledger-heads.json"));
        r.Append();
        Assert.False(File.Exists(r.BasePath), "after the anchor vanished and a new writer generation started");
    }

    [Fact]
    public void An_existing_base_is_never_overwritten_by_a_later_start_from_zero()
    {
        using var r = new HistoryRepo();
        r.Append();
        var first = File.ReadAllText(r.BasePath);
        File.Delete(Path.Combine(r.Work, ".monarch", "local", "ledger-heads.json"));
        File.Delete(Path.Combine(r.Work, ".monarch", "local", "writer-id"));
        r.W("commit", "-q", "--allow-empty", "-m", "later");
        r.Append();
        Assert.Equal(first, File.ReadAllText(r.BasePath));
    }

    // ---- counter-checks that hold today and must keep holding

    [Fact]
    public void K_change_and_K_delete_mid_committed_stay_chain_findings()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        var lines = File.ReadAllLines(r.ForeignFull);
        File.WriteAllText(r.ForeignFull, lines[0] + "\n" + lines[1].Replace("foreign", "forgery") + "\n" + lines[2] + "\n");
        r.Commit("change");
        Assert.Contains(r.Verify().Violations, x => x.Kind == LedgerViolationKind.HashMismatch);
        File.WriteAllText(r.ForeignFull, lines[0] + "\n" + lines[2] + "\n");
        r.Commit("delete mid");
        var kinds = r.Verify().Violations.Select(x => x.Kind).ToList();
        Assert.Contains(LedgerViolationKind.SeqGap, kinds);
        Assert.Contains(LedgerViolationKind.PrevHashMismatch, kinds);
    }

    [Fact]
    public void Uncommitted_changes_and_a_deleted_file_keep_their_present_findings()
    {
        using var r = new HistoryRepo();
        r.Append();
        HistoryRepo.AppendChained(r.ForeignFull);
        var v = r.Verify();
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.UnknownFile && x.File == r.ForeignRel);
        Assert.False(HistoryRepo.Unanchored(v));
        File.Delete(r.ForeignFull);
        v = r.Verify();
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.Truncated && x.File == r.ForeignRel);
        Assert.False(HistoryRepo.Unanchored(v));
    }

    [Fact]
    public void Own_commits_of_the_gate_worktree_are_ok()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own 1");
        r.Append();
        r.Commit("own 2");
        var v = r.Verify();
        Assert.True(v.Ok, string.Join("; ", v.Violations));
    }

    [Fact]
    public void Merging_origin_develop_is_ok_also_with_an_older_or_missing_gate_side_version()
    {
        foreach (var older in new[] { HistoryRepo.Older.Short, HistoryRepo.Older.Missing })
        {
            using var r = new HistoryRepo(older);
            r.Append();
            r.W("checkout", "-q", "-b", "monarch/G-1-x", r.D0!);
            r.Append();
            r.Commit("own");
            r.W("merge", "-q", "--no-edit", "origin/develop");
            var v = r.Verify();
            Assert.True(v.Ok, older + ": " + string.Join("; ", v.Violations));
        }
    }

    [Fact]
    public void Merging_a_newer_origin_develop_into_a_gate_branch_is_ok()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own");
        r.PushMoreForeign(1);
        r.W("fetch", "-q", "origin");
        r.W("merge", "-q", "--no-edit", "origin/develop");
        r.Append();
        r.Commit("own 2");
        r.PushMoreForeign(1);
        r.W("fetch", "-q", "origin");
        var v = r.Verify();
        Assert.True(v.Ok, string.Join("; ", v.Violations));
    }

    [Fact]
    public void Rebasing_own_commits_onto_origin_develop_is_ok()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own 1");
        r.Append();
        r.Commit("own 2");
        r.PushMoreForeign(2);
        r.W("fetch", "-q", "origin");
        r.W("rebase", "-q", "origin/develop");
        var v = r.Verify();
        Assert.True(v.Ok, string.Join("; ", v.Violations));
    }

    [Fact]
    public void A_new_worktree_on_an_existing_gate_branch_takes_the_branch_state_as_its_base()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.AppendChained(r.ForeignFull);
        r.Append();
        r.Commit("own and an earlier foreign change");
        r.W("checkout", "-q", "--detach");
        var second = r.Path_("second");
        r.W("worktree", "add", "-q", second, "monarch/G-1-x");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = second }, new FixedClock());
        store.Append("s2", "knight", "Note", P.Json("{}"));
        Assert.Equal(HistoryRepo.Git(second, "rev-parse", "HEAD"), File.ReadAllText(Path.Combine(second, ".monarch", "local", "ledger-base")).Trim());
        var v = LedgerVerifier.Verify(second, HistoryRepo.Trusted);
        Assert.False(HistoryRepo.Unanchored(v), string.Join("; ", v.Violations));
    }

    [Fact]
    public void A_fresh_clone_and_a_worktree_without_base_but_with_remote_states_are_ok()
    {
        using var r = new HistoryRepo();
        var clone = r.Path_("clone");
        HistoryRepo.Git(r.Path_("."), "clone", "-q", r.Remote, clone);
        new JsonlLedgerStore(new JsonlLedgerOptions { Root = clone }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        Assert.True(LedgerVerifier.Verify(clone, HistoryRepo.Trusted).Ok);

        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        r.Append();
        r.Commit("own");
        File.Delete(r.BasePath);
        var v = r.Verify();
        Assert.True(v.Ok, string.Join("; ", v.Violations));
    }

    [Fact]
    public void A_repository_without_remote_and_without_base_skips_the_history_check()
    {
        using var t = new TempDir();
        HistoryRepo.Git(t.Path, "init", "-q", "-b", "develop");
        var store = new JsonlLedgerStore(new JsonlLedgerOptions { Root = t.Path }, new FixedClock());
        store.Append("s1", "knight", "Note", P.Json("{}"));
        HistoryRepo.Git(t.Path, "add", "-A", ".monarch/ledger");
        HistoryRepo.Git(t.Path, "commit", "-q", "-m", "first");
        Assert.False(File.Exists(Path.Combine(t.Path, ".monarch", "local", "ledger-base")));
        var foreign = Path.Combine(t.Path, ".monarch", "ledger", "2026-10", "s8.cafebabe.jsonl");
        var line = LedgerCanonical.Serialize(1, DateTimeOffset.UnixEpoch, "x", "Note", P.Json("{}"), LedgerCanonical.Genesis("s8.cafebabe"));
        File.WriteAllText(foreign, LedgerCanonical.Seal(line, LedgerCanonical.ComputeHash(line)) + "\n");
        HistoryRepo.Git(t.Path, "add", "-A", ".monarch/ledger");
        HistoryRepo.Git(t.Path, "commit", "-q", "-m", "foreign");
        var v = LedgerVerifier.Verify(t.Path, HistoryRepo.Trusted);
        Assert.False(HistoryRepo.Unanchored(v), string.Join("; ", v.Violations));
    }

    [Fact]
    public void Verify_without_branches_still_works_like_before()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        Assert.True(HistoryRepo.Unanchored(LedgerVerifier.Verify(r.Work)), "the base alone is enough; only commits after it count");
    }

    // ---- limits: not detectable, by design (ADR-0018 Grenzen)

    [Fact]
    public void Limit_a_clone_that_starts_after_the_forgery_takes_it_as_its_base()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        r.W("push", "-q", "origin", "monarch/G-1-x");
        var clone = r.Path_("late");
        HistoryRepo.Git(r.Path_("."), "clone", "-q", "-b", "monarch/G-1-x", r.Remote, clone);
        new JsonlLedgerStore(new JsonlLedgerOptions { Root = clone }, new FixedClock()).Append("s1", "knight", "Note", P.Json("{}"));
        Assert.True(LedgerVerifier.Verify(clone, HistoryRepo.Trusted).Ok);
    }

    [Fact]
    public void Limit_a_locally_moved_remote_state_is_not_detectable()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        Assert.True(HistoryRepo.Unanchored(r.Verify()));
        r.W("update-ref", "refs/remotes/origin/develop", "HEAD");
        Assert.False(HistoryRepo.Unanchored(r.Verify()));
    }

    [Fact]
    public void Limit_rewriting_the_forgery_into_the_base_is_not_detectable()
    {
        using var r = new HistoryRepo();
        r.Append();
        r.W("checkout", "-q", "-b", "monarch/G-1-x");
        HistoryRepo.DeleteLastLine(r.ForeignFull);
        r.Commit("forged");
        r.W("commit", "-q", "--amend", "--allow-empty", "--no-edit");
        File.WriteAllText(r.BasePath, r.W("rev-parse", "HEAD"));
        Assert.False(HistoryRepo.Unanchored(r.Verify()));
    }
}
