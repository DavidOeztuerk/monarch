using System.Diagnostics;
using System.Text.Json;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>Q-0120: anchor robustness, git cross-check, no silent anchor rebuild.</summary>
public sealed class LedgerAnchorRobustnessTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private string Heads => Path.Combine(_t.Path, ".monarch", "local", "ledger-heads.json");

    private JsonlLedgerStore Store() => new(new JsonlLedgerOptions { Root = _t.Path }, new FixedClock());

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

    private string Seed(int n = 5, bool commit = false)
    {
        if (commit)
        {
            Git(_t.Path, "init", "-q");
        }

        var store = Store();
        string file = string.Empty;
        for (var i = 1; i <= n; i++)
        {
            file = store.Append("s1", "knight", "Note", P.Json($$"""{"n":{{i}}}""")).File;
        }

        if (commit)
        {
            Git(_t.Path, "add", ".monarch/ledger");
            Git(_t.Path, "commit", "-q", "-m", "ledger");
        }

        return file;
    }

    private void WriteHeads(string relative, long seq, string hash) =>
        File.WriteAllText(Heads, JsonSerializer.Serialize(new { files = new Dictionary<string, object> { [relative] = new { seq, hash } } }));

    private void DropAnchorEntry() => File.WriteAllText(Heads, """{"files":{}}""");

    // (1) deleting a committed file and its anchor entry
    [Fact]
    public void Deleted_tracked_file_with_removed_anchor_entry_is_reported()
    {
        var rel = Seed(commit: true);
        File.Delete(Path.Combine(_t.Path, rel));
        DropAnchorEntry();
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.File == rel && x.Kind == LedgerViolationKind.Truncated);
    }

    // (2) truncating a committed file and re-anchoring the head
    [Fact]
    public void Truncated_tracked_file_with_reanchored_head_is_reported()
    {
        var rel = Seed(commit: true);
        var path = Path.Combine(_t.Path, rel);
        var lines = File.ReadAllLines(path).Take(2).ToArray();
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Assert.True(LedgerCanonical.TrySplit(lines[1], out _, out var hash));
        WriteHeads(rel, 2, hash);
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.File == rel && x.Kind == LedgerViolationKind.Truncated);
    }

    [Fact]
    public void Rewritten_tracked_file_that_is_not_a_prefix_extension_is_reported()
    {
        var rel = Seed(commit: true);
        var path = Path.Combine(_t.Path, rel);
        var lines = File.ReadAllLines(path);
        lines[1] = lines[1].Replace("\"n\":2", "\"n\":9", StringComparison.Ordinal);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Assert.Contains(LedgerVerifier.Verify(_t.Path).Violations, x => x.File == rel && x.Kind == LedgerViolationKind.HeadMismatch && x.Detail.Contains("HEAD", StringComparison.Ordinal));
    }

    [Fact]
    public void Appending_to_a_tracked_file_after_commit_stays_ok()
    {
        var rel = Seed(commit: true);
        File.AppendAllText(Path.Combine(_t.Path, rel), string.Empty);
        Store().Append("s1", "knight", "Note", P.Json("{}"));
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
    }

    // Documented limit (ADR-0010): consistent forgery of an untracked file and its anchor is not detectable locally.
    [Fact]
    public void Consistent_forgery_of_untracked_file_and_anchor_is_not_detectable_locally()
    {
        var rel = Seed();
        var path = Path.Combine(_t.Path, rel);
        var lines = File.ReadAllLines(path).Take(2).ToArray();
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Assert.True(LedgerCanonical.TrySplit(lines[1], out _, out var hash));
        WriteHeads(rel, 2, hash);
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok);
    }

    // (3) corrupt anchor is never rebuilt silently
    [Fact]
    public void Corrupt_anchor_is_archived_and_recorded_as_reset_event_that_verify_reports_until_acknowledged()
    {
        var rel = Seed();
        File.WriteAllText(Heads, "{ not json");
        Store().Append("s1", "knight", "Note", P.Json("{}"));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Heads)!, "ledger-heads.json.corrupt-*"));
        var entries = Store().ReadSession("s1");
        var reset = Assert.Single(entries, e => e.Type == "LedgerAnchorReset");
        Assert.Contains("reason", reset.Payload.ToString(), StringComparison.Ordinal);
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.Detail.Contains(reset.Hash, StringComparison.Ordinal));
        Assert.Contains(rel, File.ReadAllText(Heads), StringComparison.Ordinal);

        Store().Append("s1", "cli", "LedgerAnchorResetAcknowledged", P.Json($$"""{"reset":"{{reset.Hash}}"}"""));
        Assert.True(LedgerVerifier.Verify(_t.Path).Ok, string.Join("; ", LedgerVerifier.Verify(_t.Path).Violations));
    }

    [Fact]
    public void Missing_anchor_with_existing_history_is_recorded_as_reset()
    {
        Seed();
        File.Delete(Heads);
        Store().Append("s1", "knight", "Note", P.Json("{}"));
        Assert.Contains(Store().ReadSession("s1"), e => e.Type == "LedgerAnchorReset");
        Assert.False(LedgerVerifier.Verify(_t.Path).Ok);
    }

    [Fact]
    public void Healthy_anchor_never_produces_a_reset()
    {
        Seed();
        Store().Append("s1", "knight", "Note", P.Json("{}"));
        Assert.DoesNotContain(Store().ReadSession("s1"), e => e.Type == "LedgerAnchorReset");
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Heads)!, "*.corrupt-*"));
    }

    // (4) verify never crashes on any anchor content
    public static TheoryData<string> BrokenAnchors() => new()
    {
        """{"files":{"X":{"seq":0,"hash":"abc"}}}""",
        """{"files":{"X":{"seq":-3,"hash":"abc"}}}""",
        """{"files":{"X":{"hash":"abc"}}}""",
        """{"files":{"X":{"seq":2}}}""",
        """{"files":{"X":{"seq":2,"hash":""}}}""",
        """{"files":{"X":{"seq":"2","hash":"abc"}}}""",
        """{"files":{"X":{"seq":2.5,"hash":"abc"}}}""",
        """{"files":{"X":{"seq":99999999999999999999,"hash":"abc"}}}""",
        """{"files":{"X":{"seq":2,"hash":7}}}""",
        """{"files":{"X":null}}""",
        """{"files":{"X":5}}""",
        """{"files":[1,2]}""",
        """{"files":null}""",
        """{"files":"x"}""",
        """{}""",
        """[]""",
        """null""",
        """42""",
        "",
        "   ",
        "\0\0\0",
    };

    [Theory]
    [MemberData(nameof(BrokenAnchors))]
    public void Verify_does_not_crash_on_broken_anchor_and_reports_a_finding(string content)
    {
        var rel = Seed(2);
        File.WriteAllText(Heads, content.Replace("\"X\"", $"\"{rel}\"", StringComparison.Ordinal));
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
    }

    [Fact]
    public void Verify_does_not_crash_when_anchor_is_unreadable()
    {
        Seed(2);
        File.Delete(Heads);
        Directory.CreateDirectory(Heads);
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
    }
}
