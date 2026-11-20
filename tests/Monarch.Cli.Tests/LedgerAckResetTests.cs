using Monarch.Infrastructure.Ledger;

namespace Monarch.Cli.Tests;

/// <summary>Q-0120: a reset anchor stays a finding until the owner acknowledges it via the CLI.</summary>
public class LedgerAckResetTests
{
    private const string Stop = """{"session_id":"sess1"}""";

    private static string ResetHash(TempProject p)
    {
        p.Hook("stop", Stop);
        File.WriteAllText(Path.Combine(p.Root, ".monarch", "local", "ledger-heads.json"), "garbage");
        p.Hook("stop", Stop);
        var v = LedgerVerifier.Verify(p.Root);
        return v.Resets!.Single().Hash;
    }

    [Fact]
    public void Corrupt_anchor_after_hook_append_fails_verify_until_ack_reset()
    {
        using var p = new TempProject();
        var hash = ResetHash(p);
        Assert.Single(p.Events(), e => e.Type == "LedgerAnchorReset");

        var bad = p.Run(["ledger", "verify"]);
        Assert.Equal(1, bad.Code);
        Assert.Contains(hash, bad.Out, StringComparison.Ordinal);

        var ack = p.Run(["ledger", "ack-reset", hash], tty: true);
        Assert.Equal(0, ack.Code);
        Assert.Single(p.Events(), e => e.Type == "LedgerAnchorResetAcknowledged");
        Assert.Equal(0, p.Run(["ledger", "verify"]).Code);
    }

    [Fact]
    public void Ack_reset_writes_the_event_with_actor_cli()
    {
        using var p = new TempProject();
        var hash = ResetHash(p);
        p.Run(["ledger", "ack-reset", hash], tty: true);
        var line = Directory.EnumerateFiles(Path.Combine(p.Root, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories)
            .SelectMany(File.ReadAllLines).Single(l => l.Contains("LedgerAnchorResetAcknowledged", StringComparison.Ordinal));
        Assert.Contains("\"actor\":\"cli\"", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Ack_reset_without_a_terminal_on_stdin_is_refused_and_writes_nothing()
    {
        using var p = new TempProject();
        var hash = ResetHash(p);
        var before = p.Events().Count;
        var r = p.Run(["ledger", "ack-reset", hash], tty: false);
        Assert.NotEqual(0, r.Code);
        Assert.Contains("Terminal", r.Err, StringComparison.Ordinal);
        Assert.Equal(before, p.Events().Count);
        Assert.DoesNotContain(p.Events(), e => e.Type == "LedgerAnchorResetAcknowledged");
        Assert.Equal(1, p.Run(["ledger", "verify"]).Code);
    }

    [Fact]
    public void Verify_message_addresses_the_owner_and_does_not_ask_the_agent()
    {
        using var p = new TempProject();
        var hash = ResetHash(p);
        var bad = p.Run(["ledger", "verify"]);
        Assert.Contains($"Owner: im Terminal `monarch ledger ack-reset {hash}` ausführen", bad.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("run 'monarch", bad.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Ack_reset_with_unknown_hash_is_refused_and_writes_nothing()
    {
        using var p = new TempProject();
        ResetHash(p);
        var r = p.Run(["ledger", "ack-reset", new string('a', 64)], tty: true);
        Assert.Equal(1, r.Code);
        Assert.DoesNotContain(p.Events(), e => e.Type == "LedgerAnchorResetAcknowledged");
        Assert.Equal(1, p.Run(["ledger", "verify"]).Code);
    }

    [Fact]
    public void Verify_with_zero_seq_anchor_exits_nonzero_instead_of_crashing()
    {
        using var p = new TempProject();
        p.Hook("stop", Stop);
        var heads = Path.Combine(p.Root, ".monarch", "local", "ledger-heads.json");
        File.WriteAllText(heads, File.ReadAllText(heads).Replace("\"seq\":1", "\"seq\":0", StringComparison.Ordinal));
        var r = p.Run(["ledger", "verify"]);
        Assert.Equal(1, r.Code);
        Assert.Contains("AnchorInvalid", r.Out, StringComparison.Ordinal);
    }
}
