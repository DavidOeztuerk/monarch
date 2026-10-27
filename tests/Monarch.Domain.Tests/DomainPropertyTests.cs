using FsCheck.Xunit;
using Monarch.Domain;

namespace Monarch.Domain.Tests;

public class DomainPropertyTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static void Apply(Quest q, int op, int kind, bool empty)
    {
        var k = (EvidenceKind)(Math.Abs(kind % 6));
        var ev = new Evidence(k, empty ? "" : "src", empty ? "" : "dig", T);
        switch (Math.Abs(op % 9))
        {
            case 0: q.Accept(new ShadowId("s")); break;
            case 1: q.Start(); break;
            case 2: q.Submit(); break;
            case 3: q.Reject(); break;
            case 4: q.Verify(ev); break;
            case 5: q.Clear(ev); break;
            case 6: q.Fail(); break;
            case 7: q.Abandon(); break;
            default: q.Replan(new ManaCost(Math.Abs(op % 50), 1, 0)); break;
        }
    }

    [Property]
    public bool Random_sequences_keep_invariants(int[] ops, int[] kinds, bool[] empties)
    {
        var q = QuestTests.New();
        for (var i = 0; i < ops.Length; i++)
        {
            var before = q.State;
            var evCount = q.Evidence.Count;
            try
            {
                Apply(q, ops[i], kinds.Length == 0 ? 0 : kinds[i % kinds.Length], empties.Length != 0 && empties[i % empties.Length]);
            }
            catch (DomainException)
            {
                if (q.State != before || q.Evidence.Count != evCount) return false;
            }

            var independent = q.Evidence.Any(e => e.Kind is EvidenceKind.CiRun or EvidenceKind.TestReport or EvidenceKind.VerifierVerdict
                && !string.IsNullOrWhiteSpace(e.Source) && !string.IsNullOrWhiteSpace(e.Digest));
            if (q.State == QuestState.Verified && !independent) return false;
            if (q.State == QuestState.Cleared && !(independent && q.Evidence.Any(e => e.Kind == EvidenceKind.MergeCommit))) return false;
            if (q.Evidence.Any(e => e.Kind is EvidenceKind.Revert or EvidenceKind.HumanApproval)) return false;
        }
        return true;
    }

    [Property]
    public bool CanAfford_is_monotonic_in_cost(double s1, double w1, double ds, double dw, double used1, double used2)
    {
        static double N(double d) => double.IsFinite(d) ? Math.Abs(d % 120) : 0;
        var pool = new ManaPool(N(used1), T, N(used2), T, 0, 0);
        var big = new ManaCost(N(s1) + N(ds), N(w1) + N(dw), 0);
        var small = new ManaCost(N(s1), N(w1), 0);
        return !pool.CanAfford(big) || pool.CanAfford(small);
    }
}
