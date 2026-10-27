using FsCheck.Xunit;
using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

public class ManaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly ManaLimits Pro = ManaLimits.ForPlan(PlanKind.Pro);

    private static ManaSnapshot Snap(double five, double seven, TimeSpan resetIn) =>
        new(five, Now + resetIn, seven, Now.AddDays(3));

    [Fact] public void Run1_73_plus_23_stops_with_recap() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(73, 20, TimeSpan.FromHours(2)), 23, 1, 1, Pro, Now));

    [Fact] public void Run1_74_plus_8_proceeds() =>
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(74, 20, TimeSpan.FromHours(2)), 8, 1, 1, Pro, Now));

    [Fact] public void Run1_reset_in_5_min_pauses_without_recap() =>
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(Snap(82, 20, TimeSpan.FromMinutes(5)), 22, 1, 1, Pro, Now));

    [Fact] public void Reset_exactly_10_min_still_recaps() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(82, 20, TimeSpan.FromMinutes(10)), 22, 1, 1, Pro, Now));

    [Fact] public void Exactly_at_limit_proceeds() =>
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(80, 20, TimeSpan.FromHours(1)), 9, 1, 1, Pro, Now));

    [Fact] public void Weekly_window_can_stop() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(10, 95, TimeSpan.FromHours(1)), 1, 2, 0.5, Pro, Now));

    [Fact] public void Weekly_recap_estimate_counts() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(10, 95, TimeSpan.FromHours(1)), 1, 0.5, 0.5, Pro, Now, recapEstimate7d: 0.6));

    [Fact] public void Weekly_breach_with_imminent_5h_reset_pauses() =>
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(Snap(10, 99, TimeSpan.FromMinutes(3)), 1, 1, 0.5, Pro, Now));

    [Fact]
    public void Stale_snapshot_uses_last_value_plus_estimate()
    {
        var s = new ManaSnapshot(70, Now.AddHours(2), 20, Now.AddDays(3), IsStale: true, EstimatedSinceSnapshot5h: 10);
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(s, 12, 1, 1, Pro, Now));
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(s with { IsStale = false }, 12, 1, 1, Pro, Now));
    }

    [Fact]
    public void Pacing_flags_fast_weekly_burn()
    {
        var reset = Now.AddDays(3.5); // half of week elapsed
        Assert.Equal(PacingMode.Normal, ManaGuard.Pacing(65, reset, Now));
        Assert.Equal(PacingMode.MainAndEmergencyOnly, ManaGuard.Pacing(65.1, reset, Now));
        Assert.Equal(PacingMode.Normal, ManaGuard.Pacing(0, reset, Now));
    }

    [Fact]
    public void Pacing_handles_out_of_range_reset()
    {
        Assert.Equal(PacingMode.MainAndEmergencyOnly, ManaGuard.Pacing(20, Now.AddDays(30), Now)); // elapsed clamped to 0
        Assert.Equal(PacingMode.Normal, ManaGuard.Pacing(99, Now.AddDays(-1), Now)); // elapsed clamped to 1
    }

    [Fact]
    public void Adaptive_returns_start_with_fewer_than_three_recaps() =>
        Assert.Equal(90, AdaptiveLimit.Compute([1, 1], [2, 3], 90, 90, 97));

    [Fact]
    public void Adaptive_formula()
    {
        // ewma of constant 1 = 1; p90 of [1..10] nearest-rank = 9; 100 - (1.5*1 + 9) = 89.5 -> clamped up to 90
        Assert.Equal(90, AdaptiveLimit.Compute([1, 1, 1], [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], 90, 90, 97));
        // 100 - (1.5*1 + 1) = 97.5 -> clamped to 97
        Assert.Equal(97, AdaptiveLimit.Compute([1, 1, 1], [1], 90, 90, 97));
        Assert.Equal(97.5, AdaptiveLimit.Compute([1, 1, 1], [1], 90, 90, 99.9));
    }

    [Fact]
    public void Ewma_weights_recent_more_and_uses_last_five()
    {
        Assert.Equal(1, AdaptiveLimit.Ewma([1]));
        Assert.Equal(2, AdaptiveLimit.Ewma([1, 3])); // 0.5*3 + 0.5*1
        // only last five count: leading 100 ignored
        Assert.Equal(AdaptiveLimit.Ewma([1, 2, 3, 4, 5]), AdaptiveLimit.Ewma([100, 1, 2, 3, 4, 5]));
        Assert.Equal(0, AdaptiveLimit.Ewma([]));
    }

    [Fact]
    public void Percentile_nearest_rank()
    {
        Assert.Equal(9, AdaptiveLimit.Percentile([10, 1, 2, 3, 4, 5, 6, 7, 8, 9], 90));
        Assert.Equal(1, AdaptiveLimit.Percentile([1, 2, 3], 0));
        Assert.Equal(3, AdaptiveLimit.Percentile([1, 2, 3], 100));
        Assert.Equal(0, AdaptiveLimit.Percentile([], 90));
    }

    [Property]
    public bool Adaptive_always_within_bounds(double[] recaps, double[] steps, double a, double b)
    {
        static double N(double d) => double.IsFinite(d) ? Math.Abs(d % 200) : 0;
        var min = Math.Min(N(a), N(b));
        var max = Math.Max(N(a), N(b));
        var r = AdaptiveLimit.Compute(recaps.Select(N).ToArray(), steps.Select(N).ToArray(), min, min, max);
        return r >= min && r <= max;
    }

    [Property]
    public bool Guard_never_proceeds_beyond_limit(double c5, double c7, double s5, double s7, double r5, double r7)
    {
        static double N(double d) => double.IsFinite(d) ? Math.Abs(d % 150) : 0;
        var snap = Snap(N(c5), N(c7), TimeSpan.FromHours(1));
        var d = ManaGuard.Decide(snap, N(s5), N(s7), N(r5), Pro, Now, N(r7));
        var fits = N(c5) + N(s5) + N(r5) <= Pro.FiveHourPct && N(c7) + N(s7) + N(r7) <= Pro.SevenDayPct;
        return fits ? d == ManaDecision.Proceed : d != ManaDecision.Proceed;
    }
}
