using Monarch.Domain;

namespace Monarch.Domain.Tests;

public class QuestTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static Evidence Ev(EvidenceKind k, string src = "run/1", string digest = "abc") => new(k, src, digest, T);

    internal static Quest New() => new(new QuestId("Q-1"), new GateId("G-1"), QuestKind.Main, Rank.C,
        new ManaCost(5, 1, 0), ["dod"], new QuestScope(["src/**"], []));

    private static Quest At(QuestState target)
    {
        var q = New();
        if (target == QuestState.Proposed) return q;
        q.Accept(new ShadowId("s"));
        if (target == QuestState.Accepted) return q;
        q.Start();
        if (target == QuestState.InProgress) return q;
        q.Submit();
        if (target == QuestState.InReview) return q;
        q.Verify(Ev(EvidenceKind.CiRun));
        if (target == QuestState.Verified) return q;
        q.Clear(Ev(EvidenceKind.MergeCommit));
        return q;
    }

    [Fact]
    public void Happy_path_reaches_cleared()
    {
        var q = At(QuestState.Cleared);
        Assert.Equal(QuestState.Cleared, q.State);
        Assert.Equal(2, q.Evidence.Count);
        Assert.Equal(new ShadowId("s"), q.Assignee);
    }

    [Fact]
    public void Scope_defaults_to_empty_and_is_kept()
    {
        Assert.Equal(["src/**"], New().Scope.Include);
        var q = new Quest(new QuestId("Q"), new GateId("G"), QuestKind.Side, Rank.E, default, []);
        Assert.Empty(q.Scope.Include);
    }

    [Fact]
    public void Reject_returns_to_in_progress()
    {
        var q = At(QuestState.InReview);
        q.Reject();
        Assert.Equal(QuestState.InProgress, q.State);
    }

    [Theory]
    [InlineData(EvidenceKind.CiRun)]
    [InlineData(EvidenceKind.TestReport)]
    [InlineData(EvidenceKind.VerifierVerdict)]
    public void Verify_accepts_independent_evidence(EvidenceKind k)
    {
        var q = At(QuestState.InReview);
        q.Verify(Ev(k));
        Assert.Equal(QuestState.Verified, q.State);
    }

    [Theory]
    [InlineData(EvidenceKind.MergeCommit)]
    [InlineData(EvidenceKind.Revert)]
    [InlineData(EvidenceKind.HumanApproval)]
    public void Verify_rejects_other_evidence(EvidenceKind k)
    {
        var q = At(QuestState.InReview);
        Assert.Throws<DomainException>(() => q.Verify(Ev(k)));
        Assert.Equal(QuestState.InReview, q.State);
        Assert.Empty(q.Evidence);
    }

    [Theory]
    [InlineData(EvidenceKind.CiRun)]
    [InlineData(EvidenceKind.TestReport)]
    [InlineData(EvidenceKind.VerifierVerdict)]
    [InlineData(EvidenceKind.Revert)]
    [InlineData(EvidenceKind.HumanApproval)]
    public void Clear_requires_merge_commit(EvidenceKind k)
    {
        var q = At(QuestState.Verified);
        Assert.Throws<DomainException>(() => q.Clear(Ev(k)));
        Assert.Equal(QuestState.Verified, q.State);
    }

    [Theory]
    [InlineData("", "d")]
    [InlineData("s", "")]
    [InlineData(" ", "d")]
    [InlineData("s", "  ")]
    public void Verify_rejects_empty_source_or_digest(string src, string digest)
    {
        var q = At(QuestState.InReview);
        Assert.Throws<DomainException>(() => q.Verify(Ev(EvidenceKind.CiRun, src, digest)));
        Assert.Equal(QuestState.InReview, q.State);
        Assert.Empty(q.Evidence);
    }

    [Theory]
    [InlineData("", "d")]
    [InlineData("s", "")]
    public void Clear_rejects_empty_source_or_digest(string src, string digest)
    {
        var q = At(QuestState.Verified);
        Assert.Throws<DomainException>(() => q.Clear(Ev(EvidenceKind.MergeCommit, src, digest)));
        Assert.Equal(QuestState.Verified, q.State);
    }

    [Theory]
    [InlineData(QuestState.Accepted)]
    [InlineData(QuestState.InProgress)]
    public void Fail_and_abandon_are_terminal_from_accepted_or_in_progress(QuestState from)
    {
        var f = At(from);
        f.Fail();
        Assert.Equal(QuestState.Failed, f.State);
        var a = At(from);
        a.Abandon();
        Assert.Equal(QuestState.Abandoned, a.State);
        Assert.Throws<DomainException>(() => f.Start());
        Assert.Throws<DomainException>(() => a.Abandon());
        Assert.Throws<DomainException>(() => f.Fail());
        Assert.Throws<DomainException>(() => a.Accept(new ShadowId("x")));
    }

    [Theory]
    [InlineData(QuestState.Proposed)]
    [InlineData(QuestState.InReview)]
    [InlineData(QuestState.Verified)]
    [InlineData(QuestState.Cleared)]
    public void Fail_and_abandon_are_invalid_elsewhere(QuestState from)
    {
        var q = At(from);
        Assert.Throws<DomainException>(() => q.Fail());
        Assert.Throws<DomainException>(() => q.Abandon());
        Assert.Equal(from, q.State);
    }

    [Fact]
    public void Replan_returns_to_accepted_and_replaces_estimate()
    {
        var q = At(QuestState.InProgress);
        var est = new ManaCost(12, 3, 0);
        q.Replan(est);
        Assert.Equal(QuestState.Accepted, q.State);
        Assert.Equal(est, q.Estimate);
        Assert.Equal(1, q.Replans);
        q.Start();
        q.Replan(new ManaCost(20, 5, 0));
        Assert.Equal(2, q.Replans);
        Assert.Equal(new ShadowId("s"), q.Assignee);
    }

    [Theory]
    [InlineData(QuestState.Proposed)]
    [InlineData(QuestState.Accepted)]
    [InlineData(QuestState.InReview)]
    [InlineData(QuestState.Verified)]
    [InlineData(QuestState.Cleared)]
    public void Replan_only_from_in_progress(QuestState from)
    {
        var q = At(from);
        var before = q.Estimate;
        Assert.Throws<DomainException>(() => q.Replan(new ManaCost(99, 99, 0)));
        Assert.Equal(from, q.State);
        Assert.Equal(before, q.Estimate);
        Assert.Equal(0, q.Replans);
    }

    [Fact]
    public void Invalid_transitions_throw_and_keep_state()
    {
        foreach (QuestState s in Enum.GetValues<QuestState>())
        {
            if (s is QuestState.Failed or QuestState.Abandoned) continue;
            var ops = new Dictionary<string, (QuestState Valid, Action<Quest> Do)>
            {
                ["accept"] = (QuestState.Proposed, q => q.Accept(new ShadowId("x"))),
                ["start"] = (QuestState.Accepted, q => q.Start()),
                ["submit"] = (QuestState.InProgress, q => q.Submit()),
                ["reject"] = (QuestState.InReview, q => q.Reject()),
                ["verify"] = (QuestState.InReview, q => q.Verify(Ev(EvidenceKind.CiRun))),
                ["clear"] = (QuestState.Verified, q => q.Clear(Ev(EvidenceKind.MergeCommit))),
            };
            foreach (var (name, (valid, run)) in ops)
            {
                if (valid == s) continue;
                var q = At(s);
                var ev = q.Evidence.Count;
                Assert.Throws<DomainException>(() => run(q));
                Assert.True(q.State == s, $"{name} from {s} changed state");
                Assert.Equal(ev, q.Evidence.Count);
            }
        }
    }

    [Fact]
    public void Ledger_events_carry_their_payload()
    {
        var e = new QuestCleared(1, T, "engine", "h0", new QuestId("Q-1"), 100, [Ev(EvidenceKind.MergeCommit)]);
        Assert.IsAssignableFrom<LedgerEvent>(e);
        Assert.Equal(100, e.Xp);
        LedgerEvent[] all =
        [
            new QuestAccepted(1, T, "a", "p", new QuestId("Q"), new ShadowId("s")),
            new ShadowExtracted(2, T, "a", "p", new ShadowId("s"), new QuestId("Q"), "d"),
            new PolicyViolated(3, T, "a", "p", "protect_paths", "x"),
        ];
        Assert.Equal(3, all.Length);
    }
}
