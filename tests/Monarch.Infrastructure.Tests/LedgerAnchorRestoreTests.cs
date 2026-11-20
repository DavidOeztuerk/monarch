using System.Diagnostics;
using System.Text.Json;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>Q-0125 / N2: git stash and branch round trips must not leave a permanent HeadMismatch once the original content is back.</summary>
public sealed class LedgerAnchorRestoreTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private JsonlLedgerStore Store() => new(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _t.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "-c", "user.email=t@t", "-c", "user.name=t", "-c", "commit.gpgsign=false", "-c", "init.defaultBranch=main" }.Concat(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        var e = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, $"git {string.Join(' ', args)}: {e}");
        return o;
    }

    private void Notes(int n, string tag)
    {
        var s = Store();
        for (var i = 0; i < n; i++)
        {
            s.Append("s1", "knight", "Note", P.Json($$"""{"{{tag}}":{{i}}}"""));
        }
    }

    private string[] LedgerFiles() => Directory.GetFiles(Path.Combine(_t.Path, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories);

    /// <summary>3 committed lines, 3 uncommitted lines (anchor at 6), then <c>git stash</c> and 3 more appends. Returns the original file.</summary>
    private string StashedWithNewGeneration()
    {
        Git("init", "-q");
        Notes(3, "c");
        Git("add", ".monarch/ledger");
        Git("commit", "-q", "-m", "ledger");
        Notes(3, "u");
        var original = LedgerFiles().Single();
        Assert.Equal(6, File.ReadAllLines(original).Length);
        Git("stash");
        Assert.Equal(3, File.ReadAllLines(original).Length);
        Notes(3, "after");
        Assert.Equal(2, LedgerFiles().Length);
        return original;
    }

    private string ResetHash() => Assert.Single(LedgerVerifier.Verify(_t.Path).Resets!).Hash;

    private void Ack(string hash) =>
        Store().Append("s1", "cli", "LedgerAnchorResetAcknowledged", P.Json($$"""{"reset":"{{hash}}"}"""));

    private int Count(string type) => LedgerFiles().SelectMany(File.ReadAllLines).Count(l => l.Contains($"\"type\":\"{type}\"", StringComparison.Ordinal));

    [Fact]
    public void Stash_appends_ack_pop_verifies_green_and_the_next_append_records_the_restore()
    {
        StashedWithNewGeneration();
        Ack(ResetHash());
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
        Git("stash", "pop");

        var v = LedgerVerifier.Verify(_t.Path);
        Assert.True(v.Ok, string.Join("; ", v.Violations));
        Assert.Equal(0, Count("LedgerAnchorRestored"));

        Notes(1, "next");
        Assert.Equal(1, Count("LedgerAnchorRestored"));
        var again = LedgerVerifier.Verify(_t.Path);
        Assert.True(again.Ok, string.Join("; ", again.Violations));
        Notes(2, "more");
        Assert.Equal(1, Count("LedgerAnchorRestored"));
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Stash_appends_pop_without_ack_is_green_for_the_file_but_the_reset_stays_a_finding_until_acknowledged()
    {
        StashedWithNewGeneration();
        var hash = ResetHash();
        Git("stash", "pop");

        var v = LedgerVerifier.Verify(_t.Path);
        Assert.DoesNotContain(v.Violations, x => x.Kind == LedgerViolationKind.HeadMismatch);
        Assert.Single(v.Violations, x => x.Kind == LedgerViolationKind.AnchorReset);
        Assert.Single(v.Violations);

        Notes(1, "next");
        Assert.Equal(1, Count("LedgerAnchorRestored"));
        Assert.Equal([LedgerViolationKind.AnchorReset], LedgerVerifier.Verify(_t.Path).Violations.Select(x => x.Kind).ToArray());

        Ack(hash);
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Pop_with_changed_content_at_the_old_sequence_stays_a_finding()
    {
        var original = StashedWithNewGeneration();
        Ack(ResetHash());
        Git("stash", "pop");
        var lines = File.ReadAllLines(original);
        var forged = ForgeLine(6, lines[4].Split("\"hash\":\"")[1][..64], """{"u":"forged"}""");
        File.WriteAllLines(original, lines.Take(5).Append(forged));

        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
        Notes(1, "next");
        Assert.Equal(0, Count("LedgerAnchorRestored"));
        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Pop_with_extra_lines_beyond_the_old_head_stays_a_finding()
    {
        var original = StashedWithNewGeneration();
        Ack(ResetHash());
        Git("stash", "pop");
        var lines = File.ReadAllLines(original);
        File.AppendAllLines(original, [ForgeLine(7, lines[5].Split("\"hash\":\"")[1][..64], """{"u":"extra"}""")]);

        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
        Notes(1, "next");
        Assert.Equal(0, Count("LedgerAnchorRestored"));
        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Truncating_and_appending_without_restoring_records_no_restore_and_is_red_until_acknowledged()
    {
        var original = StashedWithNewGeneration();
        Notes(10, "x");
        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
        Ack(ResetHash());
        Notes(10, "y");
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
        Assert.Equal(3, File.ReadAllLines(original).Length);
        Assert.Equal(0, Count("LedgerAnchorRestored"));
    }

    [Fact]
    public void Branch_round_trip_feature_develop_feature_verifies_green()
    {
        Git("init", "-q");
        Notes(3, "base");
        Git("add", ".monarch/ledger");
        Git("commit", "-q", "-m", "develop");
        Git("checkout", "-q", "-b", "feature");
        Notes(3, "feat");
        Git("add", ".monarch/ledger");
        Git("commit", "-q", "-m", "feature");
        Notes(2, "wip");
        Git("stash");
        Git("checkout", "-q", "main");
        Notes(3, "develop");
        var hash = ResetHash();
        Ack(hash);
        // The develop-side ledger file stays uncommitted: a committed one would be deleted by checking out feature (open follow-up, see the Q-0125 report).
        Git("checkout", "-q", "feature");
        Git("stash", "pop");

        var v = LedgerVerifier.Verify(_t.Path);
        Assert.True(v.Ok, string.Join("; ", v.Violations));
        Notes(1, "back");
        var after = LedgerVerifier.Verify(_t.Path);
        Assert.True(after.Ok, string.Join("; ", after.Violations));
    }

    // Warden-Abschlussrunde zu Q-0125, Fall A5: Wird die aktuelle Schreiber-Datei gekürzt, während ein Restore ansteht, darf der
    // Restore-Append das dabei entstehende Anker-Reset nicht verwerfen (ADR-0010 Nr. 1).
    [Fact]
    public void Truncation_of_the_current_file_while_a_restore_is_pending_stays_a_finding()
    {
        var original = StashedWithNewGeneration();
        Ack(ResetHash());
        Notes(2, "after-ack");
        Git("stash", "pop");
        var current = LedgerFiles().Single(f => f != original);
        var lines = File.ReadAllLines(current);
        // Nur die zwei Zeilen hinter der Quittung fallen weg; die Quittung des alten Resets bleibt erhalten.
        Assert.Contains("LedgerAnchorResetAcknowledged", lines[^3], StringComparison.Ordinal);
        File.WriteAllLines(current, lines[..^2]);

        Notes(1, "next");

        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.AnchorReset);
    }

    // Fall A6: Ein ungültiger Ankereintrag im selben Fenster muss als klebendes Reset sichtbar bleiben, nicht still neu aufgebaut werden.
    [Fact]
    public void Invalid_anchor_while_a_restore_is_pending_stays_a_finding()
    {
        var original = StashedWithNewGeneration();
        Ack(ResetHash());
        Git("stash", "pop");
        var current = Path.GetFileName(LedgerFiles().Single(f => f != original));
        var heads = Path.Combine(_t.Path, ".monarch", "local", "ledger-heads.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(heads))!;
        var files = node["files"]!.AsObject();
        var key = files.Select(p => p.Key).Single(k => k.EndsWith(current, StringComparison.Ordinal));
        files[key]!["seq"] = 0;
        File.WriteAllText(heads, node.ToJsonString());

        Notes(1, "next");

        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.AnchorReset);
    }

    private static string ForgeLine(long seq, string prev, string payload)
    {
        var hashless = LedgerCanonical.Serialize(seq, new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero), "knight", "Note", JsonDocument.Parse(payload).RootElement, prev);
        return LedgerCanonical.Seal(hashless, LedgerCanonical.ComputeHash(hashless));
    }
}
