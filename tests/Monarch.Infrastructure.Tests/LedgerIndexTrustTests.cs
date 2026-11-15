using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

/// <summary>
/// #103: the comparison with HEAD must not trust the git index. With <c>assume-unchanged</c> or <c>skip-worktree</c>
/// set, <c>git diff HEAD</c> reports a tracked file as unchanged although its bytes differ from the HEAD blob.
/// A forged, uncommitted foreign ledger file must still be a finding (ADR-0010, point 3).
/// </summary>
public sealed class LedgerIndexTrustTests : IDisposable
{
    private readonly HistoryRepo _t = new();

    public void Dispose() => _t.Dispose();

    public static TheoryData<string> Flags => new() { "--assume-unchanged", "--skip-worktree" };

    [Theory]
    [MemberData(nameof(Flags))]
    public void K_append_chained_on_a_foreign_file_with_an_index_flag_is_a_finding(string flag)
    {
        _t.Append();
        _t.W("update-index", flag, "--", _t.ForeignRel);
        HistoryRepo.AppendChained(_t.ForeignFull);

        var v = _t.Verify();

        Assert.False(v.Ok, "verify trusted the index flag " + flag);
        Assert.Contains(v.Violations, x => x.File == _t.ForeignRel);
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void K_delete_last_on_a_foreign_file_with_an_index_flag_is_a_finding(string flag)
    {
        _t.Append();
        _t.W("update-index", flag, "--", _t.ForeignRel);
        HistoryRepo.DeleteLastLine(_t.ForeignFull);

        var v = _t.Verify();

        Assert.False(v.Ok, "verify trusted the index flag " + flag);
        Assert.Contains(v.Violations, x => x.File == _t.ForeignRel);
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void K_append_chained_in_a_fresh_clone_with_an_index_flag_is_a_finding(string flag)
    {
        _t.W("update-index", flag, "--", _t.ForeignRel);
        HistoryRepo.AppendChained(_t.ForeignFull);

        var v = _t.Verify();

        Assert.False(v.Ok, "verify trusted the index flag " + flag);
        Assert.Contains(v.Violations, x => x.File == _t.ForeignRel);
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void Control_an_index_flag_without_a_change_stays_ok(string flag)
    {
        _t.Append();
        _t.W("update-index", flag, "--", _t.ForeignRel);

        var v = _t.Verify();

        Assert.True(v.Ok, string.Join("; ", v.Violations));
    }

    [Fact]
    public void Control_the_same_forgery_without_an_index_flag_is_a_finding()
    {
        _t.Append();
        HistoryRepo.AppendChained(_t.ForeignFull);

        var v = _t.Verify();

        Assert.False(v.Ok);
        Assert.Contains(v.Violations, x => x.File == _t.ForeignRel);
    }
}
