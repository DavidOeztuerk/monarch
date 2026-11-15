using System.Text.Json;

namespace Monarch.Cli.Tests;

public class StatuslineTests
{
    private static string Input(string root, bool limits = true, int five = 41, int week = 68, int ctx = 38)
    {
        var rl = limits
            ? ",\"rate_limits\":{\"five_hour\":{\"used_percentage\":" + five + ",\"resets_at\":" + TempProject.Now.AddMinutes(72).ToUnixTimeSeconds() + "},\"seven_day\":{\"used_percentage\":" + week + ",\"resets_at\":" + TempProject.Now.AddDays(3).ToUnixTimeSeconds() + "}}"
            : "";
        return "{\"session_id\":\"s1\",\"cwd\":" + JsonSerializer.Serialize(root) + ",\"model\":{\"id\":\"claude-opus-5-5\"},\"context_window\":{\"used_percentage\":" + ctx + "}" + rl + "}";
    }

    private static JsonElement Mana(TempProject p) => JsonDocument.Parse(File.ReadAllText(Path.Combine(p.Home, "mana.json"))).RootElement;

    [Fact]
    public void Prints_one_monarch_line_and_writes_the_snapshot()
    {
        using var p = new TempProject(activeRank: "A");
        var r = p.Run(["statusline"], Input(p.Root));
        Assert.Equal(0, r.Code);
        Assert.Equal("MP 41% ↻1h12 · W 68% · HP 62% · G-0001 [A] · Q-0003", r.Out.Trim());
        var m = Mana(p);
        Assert.Equal(41, m.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
        Assert.False(m.GetProperty("rate_limits_stale").GetBoolean());
        Assert.Empty(Directory.GetFiles(p.Home, "*.tmp"));
    }

    [Fact]
    public void Neutral_theme_uses_neutral_wording()
    {
        using var p = new TempProject(activeRank: "A", systemYaml: "theme: neutral\n");
        var r = p.Run(["statusline"], Input(p.Root));
        Assert.StartsWith("5h 41%", r.Out.Trim(), StringComparison.Ordinal);
        Assert.DoesNotContain("MP", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapped_command_output_comes_first_and_unchanged()
    {
        using var p = new TempProject();
        File.WriteAllText(Path.Combine(p.Home, "statusline.json"), """{"wrap":"echo WRAPPED-OUTPUT"}""");
        var r = p.Run(["statusline"], Input(p.Root));
        var lines = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("WRAPPED-OUTPUT", lines[0]);
        Assert.StartsWith("MP 41%", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Wrapped_command_receives_the_same_stdin()
    {
        using var p = new TempProject();
        File.WriteAllText(Path.Combine(p.Home, "statusline.json"), """{"wrap":"cat"}""");
        var input = Input(p.Root);
        var r = p.Run(["statusline"], input);
        Assert.StartsWith(input, r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_rate_limits_keep_previous_values_and_mark_stale()
    {
        using var p = new TempProject();
        p.Run(["statusline"], Input(p.Root));
        p.Clock.UtcNow = TempProject.Now.AddMinutes(1);
        var r = p.Run(["statusline"], Input(p.Root, limits: false, ctx: 50));
        Assert.Equal(0, r.Code);
        var m = Mana(p);
        Assert.True(m.GetProperty("rate_limits_stale").GetBoolean());
        Assert.Equal(41, m.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
        Assert.Equal(50, m.GetProperty("context_window").GetProperty("used_percentage").GetDouble());
        Assert.Contains("41%", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_rate_limits_without_history_never_invents_zero()
    {
        using var p = new TempProject();
        var r = p.Run(["statusline"], Input(p.Root, limits: false));
        Assert.Equal(0, r.Code);
        Assert.DoesNotContain("MP 0%", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_is_debounced_for_unchanged_values_but_not_for_changes()
    {
        using var p = new TempProject();
        p.Run(["statusline"], Input(p.Root));
        var first = File.ReadAllText(Path.Combine(p.Home, "mana.json"));
        p.Clock.UtcNow = TempProject.Now.AddSeconds(5);
        p.Run(["statusline"], Input(p.Root));
        Assert.Equal(first, File.ReadAllText(Path.Combine(p.Home, "mana.json")));
        p.Run(["statusline"], Input(p.Root, five: 42));
        Assert.NotEqual(first, File.ReadAllText(Path.Combine(p.Home, "mana.json")));
        p.Clock.UtcNow = TempProject.Now.AddSeconds(30);
        p.Run(["statusline"], Input(p.Root, five: 42));
        Assert.Contains("12:00:30", File.ReadAllText(Path.Combine(p.Home, "mana.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Errors_never_surface()
    {
        using var p = new TempProject();
        File.WriteAllText(Path.Combine(p.Home, "statusline.json"), """{"wrap":"echo STILL-HERE; exit 3"}""");
        var r = p.Run(["statusline"], "this is not json");
        Assert.Equal(0, r.Code);
        Assert.StartsWith("STILL-HERE", r.Out, StringComparison.Ordinal);
        Assert.Equal(string.Empty, r.Err);
    }

    private static string Multi(string root, string session, int five, long r5, int week, long r7, int ctx) =>
        "{\"session_id\":\"" + session + "\",\"cwd\":" + JsonSerializer.Serialize(root) + ",\"context_window\":{\"used_percentage\":" + ctx
        + "},\"rate_limits\":{\"five_hour\":{\"used_percentage\":" + five + ",\"resets_at\":" + r5 + "},\"seven_day\":{\"used_percentage\":" + week + ",\"resets_at\":" + r7 + "}}}";

    private static readonly long R1 = TempProject.Now.AddHours(2).ToUnixTimeSeconds();
    private static readonly long W1 = TempProject.Now.AddDays(3).ToUnixTimeSeconds();

    private static void Write(TempProject p, string session, int five, long r5, int week, long r7, int ctx)
    {
        p.Clock.UtcNow = p.Clock.UtcNow.AddSeconds(30);
        p.Run(["statusline"], Multi(p.Root, session, five, r5, week, r7, ctx));
    }

    [Fact]
    public void Idle_session_with_lower_value_and_same_reset_does_not_overwrite_five_hour()
    {
        using var p = new TempProject();
        Write(p, "A", 80, R1, 51, W1, 48);
        Write(p, "B", 76, R1, 40, W1, 0);
        var m = Mana(p);
        Assert.Equal(80, m.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
        Assert.Equal(51, m.GetProperty("rate_limits").GetProperty("seven_day").GetProperty("used_percentage").GetDouble());
        Assert.True(m.GetProperty("rate_limits").GetProperty("seven_day").TryGetProperty("observed_at", out _));
    }

    [Fact]
    public void Later_reset_replaces_the_window()
    {
        using var p = new TempProject();
        Write(p, "A", 80, R1, 51, W1, 48);
        Write(p, "B", 3, R1 + 18000, 51, W1, 0);
        var m = Mana(p);
        Assert.Equal(3, m.GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void Older_reset_never_replaces_a_newer_window()
    {
        using var p = new TempProject();
        Write(p, "A", 3, R1 + 18000, 51, W1, 48);
        Write(p, "B", 99, R1, 51, W1, 0);
        Assert.Equal(3, Mana(p).GetProperty("rate_limits").GetProperty("five_hour").GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void Hp_is_stored_per_session_and_the_statusline_uses_its_own()
    {
        using var p = new TempProject();
        Write(p, "A", 80, R1, 51, W1, 48);
        Write(p, "B", 76, R1, 40, W1, 0);
        var sessions = Mana(p).GetProperty("sessions");
        Assert.Equal(48, sessions.GetProperty("A").GetProperty("used_percentage").GetDouble());
        Assert.Equal(0, sessions.GetProperty("B").GetProperty("used_percentage").GetDouble());
        Assert.True(sessions.GetProperty("A").TryGetProperty("written_at", out _));
        p.Clock.UtcNow = p.Clock.UtcNow.AddSeconds(30);
        var r = p.Run(["statusline"], Multi(p.Root, "A", 80, R1, 51, W1, 48));
        Assert.Contains("HP 52%", r.Out, StringComparison.Ordinal);
        Assert.Contains("MP 80%", r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Session_entries_older_than_24h_are_dropped()
    {
        using var p = new TempProject();
        Write(p, "OLD", 10, R1, 10, W1, 5);
        p.Clock.UtcNow = p.Clock.UtcNow.AddHours(25);
        Write(p, "NEW", 10, R1 + 100, 10, W1 + 100, 7);
        var sessions = Mana(p).GetProperty("sessions");
        Assert.False(sessions.TryGetProperty("OLD", out _));
        Assert.True(sessions.TryGetProperty("NEW", out _));
    }

    [FsCheck.Xunit.Property(MaxTest = 25)]
    public bool Any_interleaving_stores_the_maximum_of_the_latest_window(byte[] ops)
    {
        if (ops.Length == 0)
        {
            return true;
        }

        using var p = new TempProject();
        (long Reset, int Used)? best = null;
        foreach (var b in ops.Take(12))
        {
            var session = "S" + (b & 3);
            var reset = R1 + (((b >> 2) & 1) * 18000);
            var used = (b >> 3) % 100;
            Write(p, session, used, reset, 1, W1, used);
            if (best is null || reset > best.Value.Reset || (reset == best.Value.Reset && used > best.Value.Used))
            {
                best = (reset, used);
            }
        }

        var five = Mana(p).GetProperty("rate_limits").GetProperty("five_hour");
        return five.GetProperty("used_percentage").GetDouble() == best!.Value.Used && five.GetProperty("resets_at").GetInt64() == best.Value.Reset;
    }

    private static void At(TempProject p, int minutes, string session, int five, long r5, int week, long r7)
    {
        p.Clock.UtcNow = TempProject.Now.AddMinutes(minutes);
        p.Run(["statusline"], Multi(p.Root, session, five, r5, week, r7, 10));
    }

    private static void WeekOnly(TempProject p, int minutes, string session, int week)
    {
        p.Clock.UtcNow = TempProject.Now.AddMinutes(minutes);
        p.Run(["statusline"], "{\"session_id\":\"" + session + "\",\"cwd\":" + JsonSerializer.Serialize(p.Root) + ",\"rate_limits\":{\"seven_day\":{\"used_percentage\":" + week + ",\"resets_at\":" + W1 + "}}}");
    }

    private static JsonElement Week(TempProject p) => Mana(p).GetProperty("rate_limits").GetProperty("seven_day");

    private static TempProject Held()
    {
        var p = new TempProject();
        At(p, 0, "A", 50, R1, 92, W1);
        return p;
    }

    [Fact]
    public void Two_fresh_snapshots_of_one_session_ten_minutes_apart_lower_the_week()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
        At(p, 11, "A", 51, R1, 57, W1);
        Assert.Equal(57, Week(p).GetProperty("used_percentage").GetDouble());
        Assert.Equal("2026-10-27T12:11:00Z", Week(p).GetProperty("observed_at").GetString());
        var lowered = p.Events().Single(e => e.Type == "ManaWindowLowered").Payload;
        Assert.Equal("seven_day", lowered.GetProperty("window").GetString());
        Assert.Equal(92, lowered.GetProperty("old_pct").GetDouble());
        Assert.Equal(57, lowered.GetProperty("new_pct").GetDouble());
        Assert.Equal(W1, lowered.GetProperty("resets_at").GetInt64());
        Assert.Equal("2026-10-27T12:01:00Z", lowered.GetProperty("first_seen_at").GetString());
        Assert.Equal("2026-10-27T12:11:00Z", lowered.GetProperty("confirmed_at").GetString());
        Assert.Equal("A", lowered.GetProperty("session").GetString());
        Assert.Equal(57, Mana(p).GetProperty("last_lowered").GetProperty("new_pct").GetDouble());
    }

    [Fact]
    public void A_single_snapshot_is_not_adopted()
    {
        using var p = Held();
        At(p, 30, "A", 50, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
        Assert.DoesNotContain(p.Events(), e => e.Type == "ManaWindowLowered");
    }

    [Fact]
    public void Snapshots_less_than_ten_minutes_apart_are_not_adopted()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 10, "A", 50, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void Different_sessions_are_not_adopted()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 20, "B", 50, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_missing_five_hour_window_in_the_second_snapshot_blocks_adoption()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        WeekOnly(p, 20, "A", 57);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_missing_five_hour_window_in_the_first_snapshot_blocks_adoption()
    {
        using var p = Held();
        WeekOnly(p, 1, "A", 57);
        At(p, 20, "A", 50, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_five_hour_window_with_lower_usage_is_no_proof_of_freshness()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 20, "A", 49, R1, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_five_hour_window_with_an_earlier_reset_is_no_proof_of_freshness()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1 - 100, 57, W1);
        At(p, 20, "A", 50, R1 - 100, 57, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_slightly_higher_second_value_is_adopted_as_is()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 12, "A", 50, R1, 58, W1);
        Assert.Equal(58, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_value_below_the_candidate_restarts_the_candidate()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 12, "A", 50, R1, 55, W1);
        Assert.Equal(92, Week(p).GetProperty("used_percentage").GetDouble());
        Assert.Equal(55, Week(p).GetProperty("candidate").GetProperty("used_percentage").GetDouble());
        Assert.Equal("2026-10-27T12:12:00Z", Week(p).GetProperty("candidate").GetProperty("first_seen_at").GetString());
        At(p, 23, "A", 50, R1, 55, W1);
        Assert.Equal(55, Week(p).GetProperty("used_percentage").GetDouble());
    }

    [Fact]
    public void A_value_at_or_above_the_maximum_clears_the_candidate()
    {
        using var p = Held();
        At(p, 1, "A", 50, R1, 57, W1);
        At(p, 2, "A", 50, R1, 92, W1);
        Assert.False(Week(p).TryGetProperty("candidate", out _));
    }

    [Fact]
    public void The_raw_value_is_stored_and_shown_by_mana()
    {
        using var p = Held();
        At(p, 18, "A", 50, R1, 57, W1);
        var seen = Week(p).GetProperty("last_seen");
        Assert.Equal(57, seen.GetProperty("used_percentage").GetDouble());
        Assert.Equal("2026-10-27T12:18:00Z", seen.GetProperty("observed_at").GetString());
        Assert.Equal("A", seen.GetProperty("session_id").GetString());
        var r = p.Run(["mana"]);
        Assert.Contains("7d: 92% used", r.Out, StringComparison.Ordinal);
        Assert.Contains("Statusline zuletzt 57% um 12:18Z", r.Out, StringComparison.Ordinal);
        Assert.Contains("Kandidat seit 12:18Z", r.Out, StringComparison.Ordinal);
    }
}
