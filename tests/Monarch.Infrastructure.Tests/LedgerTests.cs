using System.Diagnostics;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

public sealed class LedgerTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    private JsonlLedgerStore NewStore(FixedClock? clock = null) => new(new JsonlLedgerOptions { Root = _t.Path }, clock ?? new FixedClock());

    private string LedgerFile(string session = "s1") => Path.Combine(_t.Path, ".monarch", "ledger", "2026-10", session + "." + WriterId + ".jsonl");

    private string WriterId => File.ReadAllText(Path.Combine(_t.Path, ".monarch", "local", "writer-id")).Trim();

    private string HeadsFile => Path.Combine(_t.Path, ".monarch", "local", "ledger-heads.json");

    private string Seed(int n = 5, string session = "s1")
    {
        var store = NewStore();
        for (var i = 1; i <= n; i++)
        {
            store.Append(session, "knight", "Note", P.Json($$"""{"note":"alpha{{i}}"}"""));
        }

        return LedgerFile(session);
    }

    private static bool Has(LedgerVerification v, LedgerViolationKind k) => v.Violations.Any(x => x.Kind == k);

    [Fact]
    public void First_event_uses_genesis_and_lands_in_month_folder()
    {
        var e = NewStore().Append("s1", "knight", "Note", P.Json("""{"a":1}"""));
        Assert.Equal(1, e.Seq);
        Assert.Equal($"genesis:s1.{WriterId}", e.PrevHash);
        Assert.Equal($".monarch/ledger/2026-10/s1.{WriterId}.jsonl", e.File);
        Assert.True(File.Exists(LedgerFile()));
        Assert.Equal(64, e.Hash.Length);
        Assert.Equal(e.Hash, e.Hash.ToLowerInvariant());
    }

    [Fact]
    public void Events_chain_and_seq_is_gapless_across_store_instances()
    {
        var a = NewStore().Append("s1", "knight", "Note", P.Json("{}"));
        var b = NewStore().Append("s1", "knight", "Note", P.Json("{}"));
        Assert.Equal(2, b.Seq);
        Assert.Equal(a.Hash, b.PrevHash);
        Assert.Equal(2, NewStore().ReadSession("s1").Count);
        Assert.Empty(NewStore().ReadSession("nope"));
    }

    [Fact]
    public void Hash_is_sha256_of_line_without_hash_property()
    {
        var path = Seed(1);
        var line = File.ReadAllLines(path)[0];
        Assert.EndsWith("\"}", line);
        var idx = line.LastIndexOf(",\"hash\":\"", StringComparison.Ordinal);
        var hashless = line[..idx] + "}";
        Assert.Equal(line.Substring(idx + 9, 64), LedgerCanonical.ComputeHash(hashless));
        Assert.StartsWith("{\"seq\":1,\"at\":\"2026-10-27T09:00:01", hashless);
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("a/b")]
    [InlineData("")]
    [InlineData(".hidden")]
    public void Unsafe_session_ids_are_rejected(string id) =>
        Assert.Throws<ArgumentException>(() => NewStore().Append(id, "a", "Note", P.Json("{}")));

    [Fact]
    public void Clean_ledger_verifies() => Assert.True(Verify(Seed()).Ok);

    private LedgerVerification Verify(string _) => LedgerVerifier.Verify(_t.Path);

    [Fact]
    public void Empty_root_verifies() => Assert.True(LedgerVerifier.Verify(_t.Path).Ok);

    [Fact]
    public void Flipped_payload_byte_is_a_hash_mismatch()
    {
        var path = Seed();
        File.WriteAllText(path, File.ReadAllText(path).Replace("alpha3", "alphb3"));
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.HashMismatch && x.Line == 3);
    }

    [Fact]
    public void Deleted_middle_line_is_seq_gap_and_prev_hash_mismatch()
    {
        var path = Seed();
        var lines = File.ReadAllLines(path).ToList();
        lines.RemoveAt(2);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.True(Has(v, LedgerViolationKind.SeqGap) || Has(v, LedgerViolationKind.PrevHashMismatch));
        Assert.Contains(v.Violations, x => x.Line == 3);
    }

    [Fact]
    public void Forged_line_with_recomputed_hash_but_wrong_prev_hash_is_detected()
    {
        var path = Seed();
        var lines = File.ReadAllLines(path).ToList();
        var hashless = LedgerCanonical.Serialize(3, new DateTimeOffset(2026, 10, 27, 9, 0, 2, TimeSpan.Zero), "evil", "Note", P.Json("""{"note":"forged"}"""), "wrong-prev");
        lines.Insert(2, LedgerCanonical.Seal(hashless, LedgerCanonical.ComputeHash(hashless)));
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.PrevHashMismatch && x.Line == 3);
        Assert.DoesNotContain(v.Violations, x => x.Kind == LedgerViolationKind.HashMismatch && x.Line == 3);
    }

    [Fact]
    public void Swapped_lines_are_detected()
    {
        var path = Seed();
        var lines = File.ReadAllLines(path);
        (lines[1], lines[2]) = (lines[2], lines[1]);
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.False(v.Ok);
        Assert.True(Has(v, LedgerViolationKind.SeqGap) && Has(v, LedgerViolationKind.PrevHashMismatch));
    }

    [Fact]
    public void Removing_the_last_line_is_truncation_via_heads()
    {
        var path = Seed();
        var lines = File.ReadAllLines(path);
        File.WriteAllText(path, string.Join("\n", lines.Take(4)) + "\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.Truncated && x.File.EndsWith(".jsonl", StringComparison.Ordinal));
    }

    [Fact]
    public void Deleting_the_whole_file_is_detected()
    {
        File.Delete(Seed());
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.Truncated && x.Line == 0);
    }

    [Fact]
    public void Valid_forged_append_without_heads_update_is_detected()
    {
        var path = Seed();
        var last = File.ReadAllLines(path)[^1];
        Assert.True(LedgerCanonical.TrySplit(last, out _, out var lastHash));
        var hashless = LedgerCanonical.Serialize(6, new DateTimeOffset(2026, 10, 27, 9, 5, 0, TimeSpan.Zero), "evil", "Note", P.Json("""{"note":"forged"}"""), lastHash);
        File.AppendAllText(path, LedgerCanonical.Seal(hashless, LedgerCanonical.ComputeHash(hashless)) + "\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Single(v.Violations);
        Assert.Equal(LedgerViolationKind.HeadMismatch, v.Violations[0].Kind);
    }

    [Fact]
    public void Replacing_the_last_line_with_a_valid_forgery_is_detected()
    {
        var path = Seed();
        var lines = File.ReadAllLines(path);
        Assert.True(LedgerCanonical.TrySplit(lines[3], out _, out var prev));
        var hashless = LedgerCanonical.Serialize(5, new DateTimeOffset(2026, 10, 27, 9, 0, 5, TimeSpan.Zero), "evil", "Note", P.Json("""{"note":"x"}"""), prev);
        lines[4] = LedgerCanonical.Seal(hashless, LedgerCanonical.ComputeHash(hashless));
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        Assert.Contains(LedgerVerifier.Verify(_t.Path).Violations, x => x.Kind == LedgerViolationKind.HeadMismatch);
    }

    [Fact]
    public void Unknown_file_and_unparseable_lines_are_reported()
    {
        var path = Seed();
        File.Copy(path, LedgerFile("other"));
        File.AppendAllText(path, "this is not json\n");
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.UnknownFile);
        Assert.Contains(v.Violations, x => x.Kind == LedgerViolationKind.Unparseable && x.Line == 6);
    }

    [Fact]
    public void Torn_last_line_without_newline_is_truncated()
    {
        var path = Seed();
        var text = File.ReadAllText(path);
        File.WriteAllText(path, text.TrimEnd('\n'));
        Assert.Contains(LedgerVerifier.Verify(_t.Path).Violations, x => x.Kind == LedgerViolationKind.Truncated);
    }

    [Fact]
    public void Heads_anchor_is_written_and_matches()
    {
        Seed(3);
        var heads = File.ReadAllText(HeadsFile);
        Assert.Contains($"\".monarch/ledger/2026-10/s1.{WriterId}.jsonl\"", heads);
        Assert.Contains("\"seq\":3", heads);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(HeadsFile)!, "*.tmp"));
    }

    [Fact]
    public async Task Twenty_parallel_appenders_keep_the_chain_valid_and_seq_gapless()
    {
        const int perTask = 5;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() =>
        {
            var store = NewStore();
            for (var j = 0; j < perTask; j++)
            {
                store.Append("par", $"actor{i}", "Note", P.Json($$"""{"i":{{i}},"j":{{j}}}"""));
            }
        })));
        var v = LedgerVerifier.Verify(_t.Path);
        Assert.True(v.Ok, string.Join("; ", v.Violations));
        var entries = NewStore().ReadSession("par");
        Assert.Equal(100, entries.Count);
        Assert.Equal(Enumerable.Range(1, 100).Select(x => (long)x), entries.Select(e => e.Seq));
    }

    [Fact(Skip = "Quarantäne (Q-0125): timing-abhängig; im vollen, parallelen Testlauf bei Load ~20 median 12 ms gemessen, isoliert 0,34 ms (Q-0121). Wieder aktiv mit separatem Perf-Lauf (Follow-up).")]
    public void Single_append_latency_median_is_small()
    {
        var store = NewStore();
        for (var i = 0; i < 20; i++)
        {
            store.Append("lat", "a", "Note", P.Json("""{"warm":true}"""));
        }

        var times = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var sw = Stopwatch.StartNew();
            store.Append("lat", "a", "Note", P.Json("""{"note":"measure"}"""));
            times.Add(sw.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        var median = times[25];
        var outFile = Environment.GetEnvironmentVariable("MONARCH_BENCH_OUT");
        if (outFile is not null)
        {
            File.AppendAllText(outFile, $"append median={median:F3}ms p95={times[47]:F3}ms max={times[49]:F3}ms{Environment.NewLine}");
        }

        Assert.True(median < 5, $"median {median} ms");
    }
}
