using Monarch.Domain;

namespace Monarch.Domain.Tests;

public class WakePlanTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

    [Fact]
    public void WakeTime_is_reset_plus_three_minutes()
    {
        var reset = new DateTimeOffset(2026, 10, 28, 19, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 19, 33, 0, TimeSpan.Zero), WakePlan.WakeTime(reset, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(57, 0)] // 12:57 + 3 = 13:00 -> 13:01
    [InlineData(27, 0)] // 12:27 + 3 = 12:30 -> 12:31
    public void WakeTime_never_lands_on_the_hour_or_half_hour(int minute, int second)
    {
        var reset = new DateTimeOffset(2026, 10, 28, 12, minute, second, TimeSpan.Zero);
        var wake = WakePlan.WakeTime(reset, TimeZoneInfo.Utc);
        Assert.NotEqual(0, wake.Minute);
        Assert.NotEqual(30, wake.Minute);
        Assert.Equal(1, (wake - reset.AddMinutes(3)).TotalMinutes);
    }

    [Fact]
    public void WakeTime_checks_the_minute_in_the_local_zone()
    {
        // Kolkata is UTC+5:30: 12:27Z is 17:57 local, +3 min = 18:00 local, so the wake moves on by one minute.
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        var reset = new DateTimeOffset(2026, 10, 28, 12, 27, 0, TimeSpan.Zero);
        var wake = WakePlan.WakeTime(reset, kolkata);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 12, 31, 0, TimeSpan.Zero), wake);
    }

    [Fact]
    public void Cron_is_minute_hour_day_month_in_local_time()
    {
        var wake = new DateTimeOffset(2026, 10, 28, 19, 33, 0, TimeSpan.Zero); // 20:33 CET
        Assert.Equal("33 20 28 10 *", WakePlan.Cron(wake, Berlin));
    }

    [Fact]
    public void Prompt_is_a_fixed_template_around_the_recap_path()
    {
        Assert.Equal("Mana-Reset: lies /p/.monarch/recap/RECAP.md und mach weiter.", WakePlan.Prompt("/p/.monarch/recap/RECAP.md"));
    }

    [Theory]
    [InlineData("da7d131d-0be1-43aa-9a10-991e823808ea", true)]
    [InlineData("DA7D131D-0BE1-43AA-9A10-991E823808EA", true)]
    [InlineData("da7d131d0be143aa9a10991e823808ea", false)]
    [InlineData("../../etc/passwd", false)]
    [InlineData("df2cad7c", false)]
    [InlineData("da7d131d-0be1-43aa-9a10-991e823808ea; rm -rf", false)]
    [InlineData("", false)]
    public void IsSessionId_accepts_only_canonical_uuids(string id, bool expected) => Assert.Equal(expected, WakePlan.IsSessionId(id));

    [Fact]
    public void Classify_separates_not_yet_due_and_stale()
    {
        var at = new DateTimeOffset(2026, 10, 28, 19, 33, 0, TimeSpan.Zero);
        Assert.Equal(WakeWindow.NotYet, WakePlan.Classify(at.AddMinutes(2), at));
        Assert.Equal(WakeWindow.NotYet, WakePlan.Classify(at.AddMinutes(-60), at));
        Assert.Equal(WakeWindow.Due, WakePlan.Classify(at.AddMinutes(4), at));
        Assert.Equal(WakeWindow.Due, WakePlan.Classify(at.AddHours(23), at));
        Assert.Equal(WakeWindow.Stale, WakePlan.Classify(at.AddHours(25), at));
    }

    [Fact]
    public void ResetsAt_is_the_wake_time_minus_slack_but_activity_cutoff_is_before_it()
    {
        var at = new DateTimeOffset(2026, 10, 28, 19, 33, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 19, 30, 0, TimeSpan.Zero), WakePlan.ResetsAt(at));
    }
}
