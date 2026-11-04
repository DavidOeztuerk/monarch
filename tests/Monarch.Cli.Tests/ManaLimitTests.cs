using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0012 Nachtrag Q-0142: the limit never falls below the lower <c>clamp</c> value, and every command names the same value.</summary>
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
    public void Failure_of_30_10_clamp_96_99_five_free_recaps_and_step_8_keeps_7d_at_96()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5);
        Assert.Equal("Limits in use (adaptive): 5h 90%, 7d 96%; recaps measured 5 (adaptive from 3)", LimitsInUse(p));
    }

    [Fact]
    public void High_recap_costs_still_give_96()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5, recapCost7d: 10);
        Assert.Contains("7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Fact]
    public void Five_hour_clamp_90_97_never_falls_below_90()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 94, clamp: [90, 97]", "work_until_pct: 96, clamp: [96, 99]"));
        SeedLedger(p, 5); // adaptive 5h = 100 - 20 = 80
        Assert.Contains("5h 90%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(5)]
    public void Work_until_below_the_clamp_minimum_gives_the_clamp_minimum(int recaps)
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 85, clamp: [90, 97]", "work_until_pct: 92, clamp: [96, 99]"));
        SeedLedger(p, recaps);
        Assert.Contains("5h 90%, 7d 96%", LimitsInUse(p), StringComparison.Ordinal);
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
    public void Block_list_clamp_is_valid()
    {
        using var p = new TempProject(systemYaml: "mana:\n  plan: pro\n  limits:\n    seven_day:\n      work_until_pct: 98\n      clamp:\n        - 96\n        - 99\n");
        SeedLedger(p, 5);
        Assert.Contains("7d 96%", LimitsInUse(p), StringComparison.Ordinal); // adaptive 92 -> clamp minimum, not w
    }

    [Fact]
    public void Non_finite_work_until_counts_as_missing_and_the_plan_value_applies()
    {
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: NaN", "work_until_pct: Infinity"));
        SeedLedger(p, 5);
        Assert.Contains("5h 90%, 7d 96%", LimitsInUse(p), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("work_until_pct: 98, clamp: [96, 99]", "96")]
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
        using var p = new TempProject(systemYaml: Yaml("work_until_pct: 90, clamp: [90, 97]", "work_until_pct: 98, clamp: [96, 99]"));
        SeedLedger(p, 5);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 10, 96.5); // 96.5 + 1 > 96 (resolved) but <= 98 (raw)
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        Assert.Contains("Mana does not fit", accept.Out + accept.Err, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1, "98")] // adaptive 99 -> capped at w, not at the clamp maximum
    [InlineData(3, "97")] // adaptive 97 -> kept, not raised to w or lowered to the minimum
    [InlineData(8, "96")] // adaptive 92 -> clamp minimum
    public void Adaptive_value_is_capped_at_w_and_floored_at_the_clamp_minimum(int step7, string expected)
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
        SeedLedger(p, 5, step7: 3); // adaptive 97
        Assert.Contains("7d 98%", LimitsInUse(p), StringComparison.Ordinal); // invalid -> min = w = 98
    }

    [Theory]
    [InlineData("""{"seq":9,"type":"QuestCleared","payl""")] // truncated: JsonException
    [InlineData("""["QuestCleared"]""")] // non-object: InvalidOperationException
    [InlineData("42")]
    public void Defective_ledger_line_does_not_abort_status_briefing_accept_or_recap_and_gives_max_of_w_and_min(string bad)
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
