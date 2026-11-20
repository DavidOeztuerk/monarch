using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0012 Nachtrag Q-0189 (Q-0190): without a measurement the set value always applies (<c>clamp</c> is not read), and every command names the same value.</summary>
public class ManaLimitTests
{
    private static string Yaml(string five, string seven) =>
        $"mana:\n  plan: pro\n  limits:\n    five_hour: {{ {five} }}\n    seven_day: {{ {seven} }}\n";

    private static string Row(int seq, string type, string payload, DateTimeOffset at) =>
        FormattableString.Invariant($$"""{"seq":{{seq}},"at":"{{at.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}}","actor":"cli","type":"{{type}}","payload":{{payload}}}""");

    /// <summary>Seeds a ledger file (not the session file) with recaps (cost 5h 0) and two snapshots in one window: 5h +20, 7d +8.</summary>
    private static void SeedLedger(TempProject p, int recaps, double recapCost7d = 0, IEnumerable<string>? extraLines = null, double step7 = 8)
    {
        var t = TempProject.Now.AddHours(-6);
        var lines = new List<string>
        {
            Row(1, "ManaSnapshotTaken", """{"reason":"mana","known":true,"fiveHourUsedPct":10,"fiveHourResetsAt":1000,"sevenDayUsedPct":10,"sevenDayResetsAt":2000,"stale":false}""", t),
            Row(2, "ManaSnapshotTaken", FormattableString.Invariant($$"""{"reason":"mana","known":true,"fiveHourUsedPct":30,"fiveHourResetsAt":1000,"sevenDayUsedPct":{{10 + step7}},"sevenDayResetsAt":2000,"stale":false}"""), t.AddHours(1)),
        };
        for (var i = 0; i < recaps; i++)
        {
            lines.Add(Row(3 + i, "RecapWritten", FormattableString.Invariant($$"""{"cost5h":0,"cost7d":{{recapCost7d}},"path":"x"}"""), t.AddHours(2).AddMinutes(i)));
        }

        // Defective lines go in the middle of the seed file, never at the end of the session file (the store refuses to append there).
        lines.InsertRange(1, extraLines ?? []);
        var dir = Path.Combine(p.Root, ".monarch", "ledger");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "seed.jsonl"), lines, new UTF8Encoding(false));
    }

    private static string LimitsInUse(TempProject p)
    {
        var r = p.Run(["mana"]);
        Assert.Equal(0, r.Code);
        return r.Out.Split('\n').Single(l => l.StartsWith("Limits in use", StringComparison.Ordinal)).Trim();
    }

    [Fact]
    public void Failure_of_30_10_five_free_recaps_and_step_8_keep_the_set_values()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5);
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p));
    }

    [Fact]
    public void High_recap_costs_still_give_the_set_96()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5, recapCost7d: 10);
        Assert.Contains("7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Fact]
    public void Five_hour_set_value_94_applies_whatever_the_ledger_and_clamp_say()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 94, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5); // the old adaptive 5h was 100 - 20 = 80 -> clamp minimum 90; now the set value 94 (clamp is not read)
        Assert.Contains("5h 94%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Work_until_below_the_old_clamp_minimum_gives_the_set_value(int recaps)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 85, clamp: [90, 97]", "work_until_pct: 92, clamp: [96, 99]"));
        SeedLedger(p, recaps);
        Assert.Contains("5h 85%, 7d 92%", LimitsInUse(p), StringComparison.Ordinal); // old: 90 and 96 (clamp minimum); clamp is not read
    }

    [Fact]
    public void Missing_clamp_gives_work_until_pct()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90", "work_until_pct: 96"));
        SeedLedger(p, 5);
        Assert.Contains("5h 90%, 7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("clamp: [-Infinity, 99]")]
    [InlineData("clamp: [960, 990]")]
    [InlineData("clamp: [99, 96]")]
    [InlineData("clamp: 96")]
    [InlineData("clamp: [90, 96, 99]")]
    [InlineData("clamp: [96, [99]]")]
    public void Invalid_clamp_gives_work_until_pct(string clamp)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, " + clamp, "work_until_pct: 96, " + clamp));
        SeedLedger(p, 5);
        Assert.Contains("5h 90%, 7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Fact]
    public void Block_list_clamp_is_not_read_and_the_set_98_applies()
    {
        using var p = new TempProject(systemYaml: "mana:\n  plan: pro\n  limits:\n    seven_day:\n      work_until_pct: 98\n      clamp:\n        - 96\n        - 99\n");
        SeedLedger(p, 5);
        Assert.Contains("7d 98%", LimitsInUse(p), StringComparison.Ordinal); // old: 96 (adaptive 92 -> clamp minimum); now the set value
    }

    [Fact]
    public void Non_finite_work_until_counts_as_missing_and_the_plan_value_applies()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: NaN", "work_until_pct: Infinity"));
        SeedLedger(p, 5);
        Assert.Contains("5h 90%, 7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("work_until_pct: 98, clamp: [96, 99]", "98")] // old expectation 96 came from the clamp minimum
    [InlineData("work_until_pct: 96.8, clamp: [96.8, 99]", "96.8")]
    public void Every_command_names_the_same_limit_in_the_same_state(string seven, string value)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", seven));
        SeedLedger(p, 5);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 10, 97.9);

        Assert.Contains($"7d {value}%", LimitsInUse(p), StringComparison.Ordinal);
        Assert.Contains($"limit {value}%)", p.Run(["rest"]).Out, StringComparison.Ordinal);
        var status = p.Run(["status"]);
        Assert.Contains($"7d: 98% / limit {value}%", status.Out, StringComparison.Ordinal);
        var json = JsonDocument.Parse(p.Run(["status", "--json"]).Out).RootElement.GetProperty("mana");
        Assert.Equal(double.Parse(value, CultureInfo.InvariantCulture), json.GetProperty("sevenDayLimitPct").GetDouble());
        Assert.Equal(90, json.GetProperty("fiveHourLimitPct").GetDouble());
        var briefing = p.Hook("session-start", """{"session_id":"s1","hook_event_name":"SessionStart"}""").Out;
        Assert.Contains($"7d: 98% (Limit {value}%)", briefing, StringComparison.Ordinal);
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        Assert.Contains($"vs {value}%", accept.Out + accept.Err, StringComparison.Ordinal);
        p.Run(["recap"]);
        Assert.Contains($"(Limit {value}%,", File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_guard_uses_the_resolved_limit()
    {
        // Old seed w 98 / clamp [96, 99] resolved to 96; clamp is not read now, so the rejection case uses w 96 and a w 98 case accepts.
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 10, 96.5); // 96.5 + 1 > 96
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        Assert.Contains("Mana does not fit", accept.Out + accept.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Accept_guard_accepts_within_the_set_98()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 98, clamp: [96, 99]"));
        SeedLedger(p, 5);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 10, 96.5); // 96.5 + 1 <= 98
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"]).Code);
    }

    [Fact]
    public void Stale_sensor_after_its_reset_is_named_as_stale_and_the_way_out_is_given()
    {
        // #87: der Stand ist vor dem Reset gemessen und nach resets_at veraltet. Die Ablehnung bleibt (keine Lockerung),
        // die Meldung nennt aber den Grund und den Weg statt nur PauseWithoutRecap.
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 90, clamp: [90, 97]"));
        SeedLedger(p, 5);
        Seed.Quest(p, "Q-0003");
        var fiveReset = p.Clock.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var sevenReset = p.Clock.UtcNow.AddDays(3).ToUnixTimeSeconds();
        File.WriteAllText(Path.Combine(p.Home, "mana.json"), FormattableString.Invariant(
            $$$"""{"schema":1,"written_at":"{{{ManaStore.Stamp(p.Clock.UtcNow)}}}","rate_limits_at":"{{{ManaStore.Stamp(p.Clock.UtcNow.AddHours(-2))}}}","rate_limits":{"five_hour":{"used_percentage":88,"resets_at":{{{fiveReset}}}},"seven_day":{"used_percentage":10,"resets_at":{{{sevenReset}}}}},"sessions":{}}"""));
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        var text = accept.Out + accept.Err;
        Assert.Contains("veraltet", text, StringComparison.Ordinal);
        Assert.Contains("interaktive Session", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "98")] // old adaptive 99 -> capped at w
    [InlineData(3, "98")] // old adaptive 97 -> 97; now the set value
    [InlineData(8, "98")] // old adaptive 92 -> clamp minimum 96; now the set value
    public void Steps_in_the_ledger_never_change_the_set_value(int step7, string expected)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 98, clamp: [96, 99]"));
        SeedLedger(p, 5, step7: step7);
        Assert.Contains($"7d {expected}%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[[1], 96, 99]")] // GetStrings would drop the nested list and leave two scalars
    [InlineData("[96, [99]]")]
    public void Nested_list_in_clamp_is_invalid(string clamp)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90", "work_until_pct: 98, clamp: " + clamp));
        SeedLedger(p, 5, step7: 3); // ledger steps never change the set value
        Assert.Contains("7d 98%", LimitsInUse(p), StringComparison.Ordinal); // clamp is not read: the set 98 applies
    }

    [Theory]
    [InlineData("""{"seq":9,"type":"QuestCleared","payl""")] // truncated: JsonException
    [InlineData("""["QuestCleared"]""")] // non-object: InvalidOperationException
    [InlineData("42")]
    public void Defective_ledger_line_does_not_abort_status_briefing_accept_or_recap_and_gives_the_set_value(string bad)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 98, clamp: [96, 99]"));
        SeedLedger(p, 5, extraLines: [bad]);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 10, 98.5);

        var status = p.Run(["status"]);
        Assert.Equal(0, status.Code);
        Assert.Contains("7d: 99% / limit 98%", status.Out, StringComparison.Ordinal);
        Assert.Contains("violation", status.Out, StringComparison.Ordinal);
        Assert.Equal(0, p.Run(["status", "--json"]).Code);
        var briefing = p.Hook("session-start", """{"session_id":"s1","hook_event_name":"SessionStart"}""");
        Assert.Equal(0, briefing.Code);
        Assert.Contains("(Limit 98%)", briefing.Out, StringComparison.Ordinal);
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        Assert.Contains("vs 98%", accept.Out + accept.Err, StringComparison.Ordinal);
        var recap = p.Run(["recap"]);
        Assert.True(recap.Code == 0, recap.Out + recap.Err);
        Assert.Contains("(Limit 98%,", File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md")), StringComparison.Ordinal);
    }
}
