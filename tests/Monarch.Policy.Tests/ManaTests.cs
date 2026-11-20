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

    // Q-0190: no recap term; each former case keeps its distance to the limit (old recap 1 or 0.5 moved into the step).
    [Fact] public void Run1_73_plus_24_stops_with_recap() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(73, 20, TimeSpan.FromHours(2)), 24, 1, Pro, Now));

    [Fact] public void Run1_74_plus_9_proceeds() =>
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(74, 20, TimeSpan.FromHours(2)), 9, 1, Pro, Now));

    [Fact] public void Run1_reset_in_5_min_pauses_without_recap() =>
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(Snap(82, 20, TimeSpan.FromMinutes(5)), 23, 1, Pro, Now));

    [Fact] public void Reset_exactly_10_min_still_recaps() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(82, 20, TimeSpan.FromMinutes(10)), 23, 1, Pro, Now));

    [Fact] public void Exactly_at_limit_proceeds()
    {
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(81, 20, TimeSpan.FromHours(1)), 9, 1, Pro, Now));
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(81, 20, TimeSpan.FromHours(1)), 9.1, 1, Pro, Now));
    }

    [Fact] public void Weekly_window_can_stop() =>
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(10, 95, TimeSpan.FromHours(1)), 1.5, 2, Pro, Now));

    [Fact] public void Weekly_step_counts_without_recap()
    {
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(10, 95, TimeSpan.FromHours(1)), 1, 1.1, Pro, Now));
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(10, 95, TimeSpan.FromHours(1)), 1, 1, Pro, Now));
    }

    [Fact] public void Weekly_breach_with_imminent_5h_reset_pauses() =>
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(Snap(10, 99, TimeSpan.FromMinutes(3)), 1.5, 1, Pro, Now));

    [Fact]
    public void Stale_snapshot_uses_last_value_plus_estimate()
    {
        var s = new ManaSnapshot(70, Now.AddHours(2), 20, Now.AddDays(3), IsStale: true, EstimatedSinceSnapshot5h: 10);
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(s, 13, 1, Pro, Now));
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(s with { IsStale = false }, 13, 1, Pro, Now));
    }

    [Fact]
    public void Case_of_19_11_80_plus_8_at_limit_90_proceeds_83_plus_8_stops_and_a_near_reset_pauses()
    {
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(Snap(80, 20, TimeSpan.FromHours(2)), 8, 1, Pro, Now));
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(Snap(83, 20, TimeSpan.FromHours(2)), 8, 1, Pro, Now));
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(Snap(83, 20, TimeSpan.FromMinutes(9)), 8, 1, Pro, Now));
    }

    [Fact]
    public void Without_a_five_hour_window_only_the_week_counts_and_its_reset_decides_the_pause()
    {
        var weekReset = Now.AddMinutes(5);
        var noFive = new ManaSnapshot(0, default, 97, weekReset, HasFiveHour: false);
        // 5h values are ignored, however large the step is
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(noFive with { SevenDayUsedPct = 90, SevenDayResetsAt = Now.AddDays(3) }, 500, 1, Pro, Now));
        Assert.Equal(ManaDecision.PauseWithoutRecap, ManaGuard.Decide(noFive, 0, 1.1, Pro, Now)); // 97 + 1.1 > 96, week reset in 5 min
        Assert.Equal(ManaDecision.RecapAndStop, ManaGuard.Decide(noFive with { SevenDayResetsAt = Now.AddDays(3) }, 0, 1.1, Pro, Now));
        Assert.Equal(ManaDecision.Proceed, ManaGuard.Decide(noFive with { SevenDayUsedPct = 97 }, 0, 1, new ManaLimits(90, 98), Now));
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

    [Property]
    public bool Guard_never_proceeds_beyond_limit(double c5, double c7, double s5, double s7)
    {
        static double N(double d) => double.IsFinite(d) ? Math.Abs(d % 150) : 0;
        var snap = Snap(N(c5), N(c7), TimeSpan.FromHours(1));
        var d = ManaGuard.Decide(snap, N(s5), N(s7), Pro, Now);
        var fits = N(c5) + N(s5) <= Pro.FiveHourPct && N(c7) + N(s7) <= Pro.SevenDayPct;
        return fits ? d == ManaDecision.Proceed : d != ManaDecision.Proceed;
    }

    [Property]
    public bool Guard_without_five_hour_window_never_proceeds_beyond_the_weekly_limit(double c7, double s5, double s7)
    {
        static double N(double d) => double.IsFinite(d) ? Math.Abs(d % 150) : 0;
        var snap = new ManaSnapshot(0, default, N(c7), Now.AddHours(1), HasFiveHour: false);
        var d = ManaGuard.Decide(snap, N(s5), N(s7), Pro, Now);
        var fits = N(c7) + N(s7) <= Pro.SevenDayPct;
        return fits ? d == ManaDecision.Proceed : d != ManaDecision.Proceed;
    }
}
