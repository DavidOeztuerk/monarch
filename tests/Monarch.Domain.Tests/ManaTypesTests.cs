using Monarch.Domain;

namespace Monarch.Domain.Tests;

public class ManaTypesTests
{
    private static readonly DateTimeOffset T = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ManaCost_adds_componentwise() =>
        Assert.Equal(new ManaCost(3, 1.5, 2m), new ManaCost(1, 1, 1m) + new ManaCost(2, 0.5, 1m));

    [Fact]
    public void CanAfford_respects_reserve()
    {
        var pool = new ManaPool(70, T, 50, T, 0, 100);
        Assert.True(pool.CanAfford(new ManaCost(20, 0, 0)));
        Assert.False(pool.CanAfford(new ManaCost(20.1, 0, 0)));
        Assert.False(pool.CanAfford(new ManaCost(0, 46.1, 0)));
        Assert.False(pool.CanAfford(new ManaCost(0, 0, 91m)));
    }

    [Theory]
    [InlineData(PlanKind.Pro, 90, 96)]
    [InlineData(PlanKind.Max, 94, 98)]
    [InlineData(PlanKind.Ultra, 94, 98)]
    [InlineData(PlanKind.ApiKey, 90, 90)]
    public void Plan_defaults(PlanKind plan, double five, double seven) =>
        Assert.Equal(new ManaLimits(five, seven), ManaLimits.ForPlan(plan));

    // ADR-0012 Nachtrag Q-0189: Tabelle je Plan, Modell und Fenster.
    [Theory]
    [InlineData(PlanKind.Pro, ModelClass.Opus, 90, 96)]
    [InlineData(PlanKind.Pro, ModelClass.Sonnet, 93, 97)]
    [InlineData(PlanKind.Pro, ModelClass.Haiku, 95, 98)]
    [InlineData(PlanKind.Max, ModelClass.Opus, 94, 98)]
    [InlineData(PlanKind.Max, ModelClass.Sonnet, 96, 98)]
    [InlineData(PlanKind.Max, ModelClass.Haiku, 97, 98)]
    [InlineData(PlanKind.Ultra, ModelClass.Opus, 94, 98)]
    [InlineData(PlanKind.Ultra, ModelClass.Sonnet, 96, 98)]
    [InlineData(PlanKind.Ultra, ModelClass.Haiku, 97, 98)]
    [InlineData(PlanKind.ApiKey, ModelClass.Opus, 90, 90)]
    [InlineData(PlanKind.ApiKey, ModelClass.Sonnet, 90, 90)]
    [InlineData(PlanKind.ApiKey, ModelClass.Haiku, 90, 90)]
    public void Limits_per_plan_and_model(PlanKind plan, ModelClass model, double five, double seven) =>
        Assert.Equal(new ManaLimits(five, seven), ManaLimits.For(plan, model));

    [Theory]
    [InlineData("opus", ModelClass.Opus)]
    [InlineData("sonnet", ModelClass.Sonnet)]
    [InlineData("haiku", ModelClass.Haiku)]
    [InlineData("fable", ModelClass.Opus)]
    [InlineData("mythos", ModelClass.Opus)]
    [InlineData("unknown", ModelClass.Opus)]
    [InlineData("something-else", ModelClass.Opus)]
    [InlineData(null, ModelClass.Opus)]
    public void Model_family_maps_to_a_class_and_unknown_counts_as_opus(string? family, ModelClass expected) =>
        Assert.Equal(expected, ModelClasses.Of(family));

    [Fact]
    public void Snapshot_has_five_hour_by_default_and_can_lack_it()
    {
        Assert.True(new ManaSnapshot(1, T, 2, T).HasFiveHour);
        Assert.False(new ManaSnapshot(0, default, 2, T, HasFiveHour: false).HasFiveHour);
    }

    [Fact]
    public void Snapshot_effective_adds_estimate_only_when_stale()
    {
        var fresh = new ManaSnapshot(50, T, 40, T, false, 9, 9);
        Assert.Equal(50, fresh.Effective5h);
        var stale = fresh with { IsStale = true };
        Assert.Equal(59, stale.Effective5h);
        Assert.Equal(49, stale.Effective7d);
    }

    [Fact]
    public void ShadowStats_first_pass_rate()
    {
        Assert.Equal(0, new ShadowStats(0, 0, 0, 0).FirstPassRate);
        Assert.Equal(0.5, new ShadowStats(4, 2, 0, 3).FirstPassRate);
    }
}
