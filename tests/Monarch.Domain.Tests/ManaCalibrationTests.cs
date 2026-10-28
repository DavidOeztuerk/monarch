using Monarch.Domain;
using Xunit;

namespace Monarch.Domain.Tests;

public class ManaCalibrationTests
{
    private static CalibrationOutcome Observe(PairFactor cur, double est, double actual, bool reset = false, bool stale = false, bool overlap = false) =>
        ManaCalibrator.Observe(cur, est, actual, reset, stale, overlap);

    [Fact]
    public void Ema_starts_with_the_start_factor_and_uses_alpha_0_3()
    {
        var o = Observe(PairFactor.Start, est: 10, actual: 20);
        Assert.True(o.Calibrated);
        Assert.Equal(1.0 * 0.7 + 2.0 * 0.3, o.Factor.Factor, 9);
        Assert.Equal(1, o.Factor.N);
    }

    [Theory]
    [InlineData(100, 4.0)]
    [InlineData(0, 0.5)]
    public void Observation_and_factor_are_clamped_to_0_5_and_4(double actual, double bound)
    {
        var f = new PairFactor(bound, 5);
        var o = Observe(f, 10, actual);
        Assert.True(o.Calibrated);
        Assert.Equal(bound, o.Factor.Factor, 9);
    }

    [Theory]
    [InlineData(true, false, false, 10, "window-reset")]
    [InlineData(false, true, false, 10, "stale-snapshot")]
    [InlineData(false, false, false, 0.9, "estimate-below-1pp")]
    [InlineData(false, false, true, 10, "overlap-without-token-data")]
    public void Excluded_observations_are_recorded_but_do_not_change_the_factor(bool reset, bool stale, bool overlap, double est, string reason)
    {
        var start = new PairFactor(1.5, 2);
        var o = Observe(start, est, 30, reset, stale, overlap);
        Assert.False(o.Calibrated);
        Assert.Equal(reason, o.Reason);
        Assert.Equal(start, o.Factor);
    }

    [Fact]
    public void Calibration_status_needs_three_observations()
    {
        Assert.Contains("unkalibriert (n=2)", ManaCalibrator.Display(new ModelEffort("opus", "xhigh"), new PairFactor(1.2, 2)), StringComparison.Ordinal);
        Assert.Equal("Paar (opus, xhigh): Faktor 1.20, kalibriert (n=3)", ManaCalibrator.Display(new ModelEffort("opus", "xhigh"), new PairFactor(1.2, 3)));
    }

    [Theory]
    [InlineData("claude-opus-4-7[1m]", "xhigh", "opus", "xhigh")]
    [InlineData("claude-sonnet-5-5", null, "sonnet", "unknown")]
    [InlineData("lower-the-model", "switch", "unknown", "unknown")]
    [InlineData(null, "HIGH", "unknown", "high")]
    public void Pair_is_normalised_to_a_fixed_vocabulary(string? model, string? effort, string m, string e)
    {
        Assert.Equal(new ModelEffort(m, e), ModelEffort.Normalize(model, effort));
    }
}
