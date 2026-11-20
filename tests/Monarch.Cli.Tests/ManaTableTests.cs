using System.Globalization;
using System.Text;
using System.Text.Json;
using Monarch.Domain;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0012 Nachtrag Q-0189 (Q-0190): the limit is the working limit, per plan, model and window; Ultra has no 5h window.</summary>
public class ManaTableTests
{
    private const string Sid = "s1";

    private static IReadOnlyDictionary<string, string?> Vars(string sid = Sid) =>
        new Dictionary<string, string?> { ["CLAUDE_CODE_SESSION_ID"] = sid, ["CLAUDE_EFFORT"] = "medium" };

    private static string Inv(FormattableString s) => FormattableString.Invariant(s);

    private static string Stamp(DateTimeOffset t) => ManaStore.Stamp(t);

    private static string Sessions(string? model, string sid = Sid) =>
        model is null ? "{}" : "{\"" + sid + "\":{\"used_percentage\":10,\"written_at\":\"" + Stamp(TempProject.Now) + "\",\"model\":\"" + model + "\"}}";

    /// <summary>mana.json with five and seven day windows, fresh at the fake clock.</summary>
    private static void WriteMana(TempProject p, double five, double seven, string? model = null, long? fiveReset = null)
    {
        var f = fiveReset ?? TempProject.Now.AddHours(2).ToUnixTimeSeconds();
        var s = TempProject.Now.AddDays(3).ToUnixTimeSeconds();
        var now = Stamp(TempProject.Now);
        File.WriteAllText(Path.Combine(p.Home, "mana.json"), Inv(
            $"{{\"schema\":1,\"written_at\":\"{now}\",\"rate_limits_at\":\"{now}\",\"rate_limits\":{{\"five_hour\":{{\"used_percentage\":{five},\"resets_at\":{f}}},\"seven_day\":{{\"used_percentage\":{seven},\"resets_at\":{s}}}}},\"sessions\":{Sessions(model)}}}"));
    }

    /// <summary>mana.json as the sensor writes it for Ultra: no five_hour, the stale flag set, rate_limits_at old.</summary>
    private static void WriteUltraMana(TempProject p, double seven, string observedAt, string? lastSeenAt, string? model = null, long? sevenReset = null)
    {
        var s = sevenReset ?? TempProject.Now.AddDays(3).ToUnixTimeSeconds();
        File.WriteAllText(Path.Combine(p.Home, "mana.json"), UltraJson(seven, s, observedAt, lastSeenAt, Stamp(TempProject.Now.AddHours(-3)), "true", Sessions(model)));
    }

    private static string UltraJson(double seven, long reset, string observedAt, string? lastSeenAt, string rateLimitsAt, string stale, string sessions)
    {
        var lastSeen = lastSeenAt is null ? string.Empty : Inv($",\"last_seen\":{{\"used_percentage\":{seven},\"resets_at\":{reset},\"observed_at\":\"{lastSeenAt}\"}}");
        return Inv($"{{\"schema\":1,\"written_at\":\"{Stamp(TempProject.Now)}\",\"rate_limits_at\":\"{rateLimitsAt}\",\"rate_limits_stale\":{stale},\"rate_limits\":{{\"seven_day\":{{\"used_percentage\":{seven},\"resets_at\":{reset},\"observed_at\":\"{observedAt}\"{lastSeen}}}}},\"sessions\":{sessions}}}");
    }

    private static string PlanYaml(string plan, string extra = "") => $"mana:\n  plan: {plan}\n{extra}";

    private static void WriteYaml(TempProject p, string yaml) => File.WriteAllText(Path.Combine(p.Root, ".monarch", "system.yaml"), yaml);

    private static string LimitsInUse(TempProject p, IReadOnlyDictionary<string, string?>? vars = null)
    {
        var r = p.Run(["mana"], vars: vars);
        Assert.Equal(0, r.Code);
        return r.Out.Split('\n').Single(l => l.StartsWith("Limits in use", StringComparison.Ordinal)).Trim();
    }

    private static ManaFile File_(string json) => JsonSerializer.Deserialize(json, CliJson.Default.ManaFile)!;

    private static string Ultra5hFree(double seven, string observedAt, string? lastSeenAt) =>
        UltraJson(seven, TempProject.Now.AddDays(3).ToUnixTimeSeconds(), observedAt, lastSeenAt, Stamp(TempProject.Now.AddHours(-3)), "true", "{}");

    // ---------- the guard: the limit is the working limit ----------

    [Fact]
    public void Case_of_19_11_5h_80_plus_step_8_at_limit_90_proceeds_and_the_output_names_no_recap()
    {
        using var p = new TempProject();
        Seed.Mana(p, 80, 20);
        var r = p.Run(["mana", "--estimate", "8"]);
        Assert.Equal(0, r.Code);
        Assert.Contains("Proceed (5h fits)", r.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("recap", r.Out, StringComparison.OrdinalIgnoreCase);
        Seed.Quest(p, "Q-0003");
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"]).Code); // 80 + 5 <= 90
    }

    [Fact]
    public void Step_83_plus_8_at_limit_90_stops_and_the_accept_message_names_no_recap()
    {
        using var p = new TempProject();
        Seed.Mana(p, 83, 20);
        Assert.Contains("RecapAndStop (5h does not fit)", p.Run(["mana", "--estimate", "8"]).Out, StringComparison.Ordinal);
        Seed.Quest(p, "Q-0003");
        Seed.Mana(p, 86, 20);
        var accept = p.Run(["quest", "accept", "Q-0003"]); // 86 + 5 > 90
        Assert.Equal(1, accept.Code);
        var text = accept.Out + accept.Err;
        Assert.Contains("Mana does not fit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("+ recap", text, StringComparison.OrdinalIgnoreCase); // the decision name RecapAndStop stays
    }

    // ---------- table, aliases, models ----------

    [Fact]
    public void Limits_follow_plan_and_model_of_the_session_in_every_command_that_names_them()
    {
        using var p = new TempProject();
        var models = new[] { ("claude-opus-4-7", 0), ("claude-sonnet-5-5", 1), ("claude-haiku-4-5", 2), ("claude-fable-5-2", 0), ("claude-mythos-5-2", 0) };
        var table = new Dictionary<string, (double[] Five, double[] Seven)>
        {
            ["pro"] = ([90, 93, 95], [96, 97, 98]),
            ["max"] = ([94, 96, 97], [98, 98, 98]),
            ["ultra"] = ([94, 96, 97], [98, 98, 98]),
            ["max5"] = ([94, 96, 97], [98, 98, 98]),
            ["max20"] = ([94, 96, 97], [98, 98, 98]),
            ["nonsense"] = ([90, 93, 95], [96, 97, 98]),
            ["api-key"] = ([90, 90, 90], [90, 90, 90]),
        };
        foreach (var (plan, (five, seven)) in table)
        {
            WriteYaml(p, PlanYaml(plan));
            foreach (var (model, i) in models)
            {
                WriteMana(p, 10, 10, model);
                Assert.Equal(FormattableString.Invariant($"Limits in use (plan default): 5h {five[i]}%, 7d {seven[i]}%"), LimitsInUse(p, Vars()));
            }
        }
    }

    [Fact]
    public void Missing_session_entry_or_session_id_gives_the_opus_limit_and_unknown_plan_gives_pro()
    {
        using var p = new TempProject();
        WriteYaml(p, PlanYaml("pro"));
        WriteMana(p, 10, 10, model: null);
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p, Vars()));
        WriteMana(p, 10, 10, "claude-sonnet-5-5");
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p)); // no session id in the process env
        File.Delete(Path.Combine(p.Root, ".monarch", "system.yaml"));
        Assert.Equal("Limits in use (plan default): 5h 93%, 7d 97%", LimitsInUse(p, Vars())); // no system.yaml: Pro, and the Sonnet entry is present
    }

    [Theory]
    [InlineData("max5", PlanKind.Max)]
    [InlineData("MAX5", PlanKind.Max)]
    [InlineData("max", PlanKind.Max)]
    [InlineData("max20", PlanKind.Ultra)]
    [InlineData("ultra", PlanKind.Ultra)]
    [InlineData("pro", PlanKind.Pro)]
    [InlineData("whatever", PlanKind.Pro)]
    [InlineData(null, PlanKind.Pro)]
    [InlineData("api-key", PlanKind.ApiKey)]
    public void ParsePlan_knows_the_aliases(string? text, PlanKind expected) =>
        Assert.Equal(expected, SystemConfigReader.ParsePlan(text));

    [Fact]
    public void Fable_session_checks_the_week_with_98_on_max_and_ultra_and_with_the_opus_value_on_pro()
    {
        using var p = new TempProject();
        foreach (var plan in new[] { "max", "ultra" })
        {
            WriteYaml(p, PlanYaml(plan));
            WriteMana(p, 10, 97.5, "claude-fable-5-2");
            Assert.Contains("RecapAndStop", p.Run(["mana", "--estimate", "5"], vars: Vars()).Out, StringComparison.Ordinal); // 97.5 + 1 > 98
            WriteMana(p, 10, 96.9, "claude-fable-5-2");
            Assert.Contains("Proceed", p.Run(["mana", "--estimate", "5"], vars: Vars()).Out, StringComparison.Ordinal); // 96.9 + 1 <= 98
        }

        WriteYaml(p, PlanYaml("pro"));
        WriteMana(p, 10, 95.5, "claude-fable-5-2");
        Assert.Contains("RecapAndStop", p.Run(["mana", "--estimate", "5"], vars: Vars()).Out, StringComparison.Ordinal); // 95.5 + 1 > 96 (Opus)
    }

    [Fact]
    public void Model_class_decides_the_guard_for_the_same_numbers()
    {
        using var p = new TempProject();
        Seed.Quest(p, "Q-0003");
        WriteMana(p, 90, 10, "claude-haiku-4-5"); // 90 + 5 = 95 <= Pro haiku 95
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"], vars: Vars()).Code);
        p.Run(["quest", "abandon", "Q-0003"], vars: Vars());
        WriteMana(p, 90, 10, "claude-opus-4-7"); // 95 > 90
        Seed.Quest(p, "Q-0004");
        Assert.Equal(1, p.Run(["quest", "accept", "Q-0004"], vars: Vars()).Code);
    }

    [Fact]
    public void Briefing_uses_the_hook_session_id_names_an_unknown_model_and_stays_quiet_for_a_known_one()
    {
        using var p = new TempProject();
        WriteMana(p, 10, 10, "claude-sonnet-5-5");
        var known = p.Hook("session-start", """{"session_id":"s1","hook_event_name":"SessionStart"}""").Out;
        Assert.Contains("(Limit 93%", known, StringComparison.Ordinal);
        Assert.Contains("(Limit 97%)", known, StringComparison.Ordinal);
        Assert.DoesNotContain("Modell unbekannt", known, StringComparison.Ordinal);
        var unknown = p.Hook("session-start", """{"session_id":"other","hook_event_name":"SessionStart"}""").Out;
        Assert.Contains("(Limit 90%", unknown, StringComparison.Ordinal);
        Assert.Contains("(Limit 96%)", unknown, StringComparison.Ordinal);
        Assert.Contains("Modell unbekannt, Opus-Grenze", unknown, StringComparison.Ordinal);
    }

    // ---------- without measurement the set value always applies ----------

    private static void SeedRecaps(TempProject p, int recaps, string? bad = null)
    {
        var lines = new List<string>
        {
            """{"seq":1,"at":"2026-10-27T05:00:00Z","actor":"cli","type":"ManaSnapshotTaken","payload":{"reason":"mana","known":true,"fiveHourUsedPct":10,"fiveHourResetsAt":1000,"sevenDayUsedPct":10,"sevenDayResetsAt":2000,"stale":false}}""",
            """{"seq":2,"at":"2026-10-27T06:00:00Z","actor":"cli","type":"ManaSnapshotTaken","payload":{"reason":"mana","known":true,"fiveHourUsedPct":50,"fiveHourResetsAt":1000,"sevenDayUsedPct":30,"sevenDayResetsAt":2000,"stale":false}}""",
        };
        for (var i = 0; i < recaps; i++)
        {
            lines.Add(FormattableString.Invariant($$$"""{"seq":{{{3 + i}}},"at":"2026-10-27T07:0{{{i}}}:00Z","actor":"cli","type":"RecapWritten","payload":{"cost5h":20,"cost7d":10,"path":"x"}}"""));
        }

        if (bad is not null)
        {
            lines.Insert(1, bad);
        }

        var dir = Path.Combine(p.Root, ".monarch", "ledger");
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, "seed.jsonl"), lines, new UTF8Encoding(false));
    }

    [Fact]
    public void Without_a_measurement_the_table_value_applies_whatever_recaps_steps_or_clamp_the_files_hold()
    {
        using var p = new TempProject();
        SeedRecaps(p, 5);
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p));
        WriteYaml(p, PlanYaml("pro", "  limits:\n    five_hour: { clamp: [50, 60] }\n    seven_day: { clamp: [50, 60] }\n"));
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p));
        SeedRecaps(p, 5, bad: """{"seq":9,"type":"QuestCleared","payl""");
        Assert.Equal("Limits in use (plan default): 5h 90%, 7d 96%", LimitsInUse(p));
    }

    [Fact]
    public void Resolve_gives_table_then_work_until_then_model_entry_and_never_reads_the_ledger()
    {
        var yaml = PlanYaml("pro", "  limits:\n    five_hour: { work_until_pct: 80, sonnet: 85 }\n    seven_day: { haiku: 99.5 }\n");
        var config = SystemConfigReader.Parse(yaml);
        Assert.Equal(new ManaLimits(80, 96), ManaSupport.Resolve(config, ModelClass.Opus).Limits); // work_until_pct replaces the table for all models
        Assert.Equal(new ManaLimits(85, 97), ManaSupport.Resolve(config, ModelClass.Sonnet).Limits); // entry per model wins
        Assert.Equal(new ManaLimits(80, 99.5), ManaSupport.Resolve(config, ModelClass.Haiku).Limits);
        Assert.Equal("plan default", ManaSupport.Resolve(config, ModelClass.Opus).Source);
    }

    [Fact]
    public void Work_until_pct_applies_to_all_models_and_a_model_entry_goes_first_in_the_commands()
    {
        using var p = new TempProject(systemYaml: PlanYaml("max", "  limits:\n    five_hour: { work_until_pct: 88, opus: 70 }\n    seven_day: { work_until_pct: 91, sonnet: 92 }\n"));
        WriteMana(p, 10, 10, "claude-haiku-4-5");
        Assert.Equal("Limits in use (plan default): 5h 88%, 7d 91%", LimitsInUse(p, Vars()));
        WriteMana(p, 10, 10, "claude-opus-4-7");
        Assert.Equal("Limits in use (plan default): 5h 70%, 7d 91%", LimitsInUse(p, Vars()));
        WriteMana(p, 10, 10, "claude-sonnet-5-5");
        Assert.Equal("Limits in use (plan default): 5h 88%, 7d 92%", LimitsInUse(p, Vars()));
    }

    [Theory]
    [InlineData("150")]
    [InlineData("-5")]
    [InlineData("100.5")]
    [InlineData("NaN")]
    [InlineData(".inf")]
    public void Values_outside_0_to_100_or_not_finite_count_as_missing(string bad)
    {
        var yaml = PlanYaml("pro", $"  limits:\n    five_hour: {{ work_until_pct: {bad}, sonnet: {bad} }}\n    seven_day: {{ work_until_pct: 97, haiku: {bad}, opus: {bad} }}\n");
        var config = SystemConfigReader.Parse(yaml);
        Assert.Equal(new ManaLimits(90, 97), config.Limits);
        Assert.Equal(new ManaLimits(93, 97), ManaSupport.Resolve(config, ModelClass.Sonnet).Limits); // sonnet entry invalid -> table 93, week from work_until_pct 97
        Assert.Equal(new ManaLimits(95, 97), ManaSupport.Resolve(config, ModelClass.Haiku).Limits);
        Assert.Equal(new ManaLimits(90, 97), ManaSupport.Resolve(config, ModelClass.Opus).Limits);
    }

    [Fact]
    public void Edge_values_0_and_100_are_valid()
    {
        var config = SystemConfigReader.Parse(PlanYaml("pro", "  limits:\n    five_hour: { work_until_pct: 0 }\n    seven_day: { work_until_pct: 100 }\n"));
        Assert.Equal(new ManaLimits(0, 100), config.Limits);
    }

    // ---------- awaken ----------

    [Theory]
    [InlineData("pro", "pro")]
    [InlineData("max", "max")]
    [InlineData("max5", "max")]
    [InlineData("ultra", "ultra")]
    [InlineData("max20", "ultra")]
    [InlineData("api-key", "api-key")]
    public void Awaken_writes_the_plan_name_and_neither_work_until_pct_nor_clamp(string arg, string written)
    {
        using var p = new TempProject(awakened: false);
        Assert.Equal(0, p.Run(["awaken", "--plan", arg]).Code);
        var yaml = File.ReadAllText(Path.Combine(p.Root, ".monarch", "system.yaml"));
        Assert.Contains($"  plan: {written} ", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("work_until_pct", yaml, StringComparison.Ordinal);
        Assert.DoesNotContain("clamp", yaml, StringComparison.Ordinal);
        Assert.Equal(SystemConfigReader.ParsePlan(arg), SystemConfigReader.Parse(yaml).Plan);
    }

    // ---------- Ultra: no five hour window ----------

    [Fact]
    public void Ultra_snapshot_without_five_hour_is_fresh_by_last_seen_even_when_observed_at_is_old_and_the_flag_says_stale()
    {
        var now = TempProject.Now;
        var f = File_(Ultra5hFree(50, observedAt: Stamp(now.AddHours(-5)), lastSeenAt: Stamp(now.AddMinutes(-2))));
        var s = ManaStore.ToSnapshot(f, now, PlanKind.Ultra)!;
        Assert.False(s.HasFiveHour);
        Assert.False(s.IsStale);
        Assert.Equal(50, s.Effective7d);
    }

    [Fact]
    public void Ultra_snapshot_is_stale_when_last_seen_is_older_than_10_minutes_and_falls_back_to_observed_at_without_last_seen()
    {
        var now = TempProject.Now;
        Assert.True(ManaStore.ToSnapshot(File_(Ultra5hFree(50, Stamp(now.AddMinutes(-1)), Stamp(now.AddMinutes(-11)))), now, PlanKind.Ultra)!.IsStale);
        Assert.False(ManaStore.ToSnapshot(File_(Ultra5hFree(50, Stamp(now.AddMinutes(-1)), null)), now, PlanKind.Ultra)!.IsStale);
        Assert.True(ManaStore.ToSnapshot(File_(Ultra5hFree(50, Stamp(now.AddMinutes(-11)), null)), now, PlanKind.Ultra)!.IsStale);
    }

    [Fact]
    public void Pro_and_max_without_five_hour_have_no_snapshot_and_keep_the_stale_last_value()
    {
        var now = TempProject.Now;
        var weekOnly = File_(Ultra5hFree(50, Stamp(now), Stamp(now)));
        Assert.Null(ManaStore.ToSnapshot(weekOnly, now, PlanKind.Pro));
        Assert.Null(ManaStore.ToSnapshot(weekOnly, now, PlanKind.Max));
        var withFive = File_(Inv($"{{\"schema\":1,\"written_at\":\"{Stamp(now)}\",\"rate_limits_at\":\"{Stamp(now)}\",\"rate_limits_stale\":true,\"rate_limits\":{{\"five_hour\":{{\"used_percentage\":40,\"resets_at\":{now.AddHours(2).ToUnixTimeSeconds()}}},\"seven_day\":{{\"used_percentage\":50,\"resets_at\":{now.AddDays(2).ToUnixTimeSeconds()}}}}}}}"));
        foreach (var plan in new[] { PlanKind.Pro, PlanKind.Max })
        {
            var s = ManaStore.ToSnapshot(withFive, now, plan)!;
            Assert.True(s.HasFiveHour);
            Assert.True(s.IsStale);
            Assert.Equal(40, s.FiveHourUsedPct);
        }
    }

    [Fact]
    public void Ultra_reported_five_hour_window_counts_only_until_its_reset()
    {
        var now = TempProject.Now;
        string Json(long reset) => Inv($"{{\"schema\":1,\"written_at\":\"{Stamp(now)}\",\"rate_limits_at\":\"{Stamp(now)}\",\"rate_limits\":{{\"five_hour\":{{\"used_percentage\":40,\"resets_at\":{reset}}},\"seven_day\":{{\"used_percentage\":50,\"resets_at\":{now.AddDays(2).ToUnixTimeSeconds()}}}}}}}");
        var live = ManaStore.ToSnapshot(File_(Json(now.AddHours(1).ToUnixTimeSeconds())), now, PlanKind.Ultra)!;
        Assert.True(live.HasFiveHour);
        Assert.Equal(40, live.FiveHourUsedPct);
        var gone = ManaStore.ToSnapshot(File_(Json(now.AddMinutes(-1).ToUnixTimeSeconds())), now, PlanKind.Ultra)!;
        Assert.False(gone.HasFiveHour);
    }

    [Fact]
    public void Ultra_mana_and_status_write_5h_not_present_and_the_week_decides()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        var now = TempProject.Now;
        WriteUltraMana(p, 50, Stamp(now.AddHours(-5)), Stamp(now.AddMinutes(-1)));
        var mana = p.Run(["mana", "--estimate", "5"]);
        Assert.Equal(0, mana.Code);
        Assert.Contains("5h: nicht vorhanden", mana.Out, StringComparison.Ordinal);
        Assert.Contains("Proceed", mana.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("(stale)", mana.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("1970", mana.Out, StringComparison.Ordinal);
        var status = p.Run(["status"]);
        Assert.Contains("5h: nicht vorhanden", status.Out, StringComparison.Ordinal);
        Assert.DoesNotContain("1970", status.Out, StringComparison.Ordinal);
        Assert.Equal(0, p.Run(["status", "--json"]).Code);

        WriteUltraMana(p, 97.5, Stamp(now), Stamp(now));
        Assert.Contains("RecapAndStop", p.Run(["mana", "--estimate", "5"]).Out, StringComparison.Ordinal); // 97.5 + 1 > 98
    }

    [Fact]
    public void Ultra_snapshot_events_leave_the_five_hour_fields_out_instead_of_writing_0()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        WriteUltraMana(p, 50, Stamp(TempProject.Now), Stamp(TempProject.Now));
        p.Run(["mana"]);
        var snap = p.Events().Last(e => e.Type == "ManaSnapshotTaken").Payload;
        Assert.False(snap.TryGetProperty("fiveHourUsedPct", out _));
        Assert.False(snap.TryGetProperty("fiveHourResetsAt", out _));
        Assert.Equal(50, snap.GetProperty("sevenDayUsedPct").GetDouble());
        p.Hook("session-end", """{"session_id":"s1","hook_event_name":"SessionEnd"}""");
        var end = p.Events().Last(e => e.Type == "ManaSnapshotTaken").Payload;
        Assert.False(end.TryGetProperty("five_hour_used_pct", out _));
        Assert.False(end.TryGetProperty("five_hour_resets_at", out _));
    }

    [Fact]
    public void Ultra_accept_and_submit_without_a_five_hour_window_do_not_throw_and_record_no_5h_window()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        Seed.Quest(p, "Q-0003");
        var now = TempProject.Now;
        WriteUltraMana(p, 50, Stamp(now), Stamp(now), "claude-opus-4-7");
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"], vars: Vars()).Code);
        Assert.Equal(0, p.Run(["quest", "start", "Q-0003"], vars: Vars()).Code);
        WriteUltraMana(p, 51, Stamp(now), Stamp(now), "claude-opus-4-7");
        Assert.Equal(0, p.Run(["quest", "submit", "Q-0003"], vars: Vars()).Code);
        var rec = p.Events().Single(e => e.Type == "ManaStepRecorded").Payload;
        Assert.Equal("no-5h-window", rec.GetProperty("reason").GetString());
        Assert.False(rec.GetProperty("calibrated").GetBoolean());
    }

    [Fact]
    public void Ultra_accept_is_refused_when_the_week_does_not_fit_and_the_message_names_no_epoch()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        Seed.Quest(p, "Q-0003");
        var now = TempProject.Now;
        WriteUltraMana(p, 98, Stamp(now), Stamp(now));
        var accept = p.Run(["quest", "accept", "Q-0003"]); // 98 + 1 > 98
        Assert.Equal(1, accept.Code);
        var text = accept.Out + accept.Err;
        Assert.Contains("Mana does not fit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("1970", text, StringComparison.Ordinal);
        Assert.DoesNotContain("5h 0%", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Ultra_rest_wakes_from_the_week_reset_without_epoch_0_and_recap_and_briefing_name_no_five_hour_reset()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        var now = TempProject.Now;
        WriteUltraMana(p, 99, Stamp(now), Stamp(now)); // week reset = now + 3 days = 2026-10-30
        var rest = p.Run(["rest"]);
        Assert.Equal(0, rest.Code);
        Assert.DoesNotContain("1970", rest.Out, StringComparison.Ordinal);
        Assert.Contains("5h: nicht vorhanden", rest.Out, StringComparison.Ordinal);
        Assert.Contains("Wake at 2026-10-30T", rest.Out, StringComparison.Ordinal);
        var resume = p.Events().Last(e => e.Type == "RestStarted").Payload.GetProperty("resumeAt").GetString();
        Assert.StartsWith("2026-10-30T", resume, StringComparison.Ordinal);

        p.Run(["recap"]);
        var recap = File.ReadAllText(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md"));
        Assert.DoesNotContain("1970", recap, StringComparison.Ordinal);
        Assert.Contains("Mana 5h: nicht vorhanden", recap, StringComparison.Ordinal);

        var briefing = p.Hook("session-start", """{"session_id":"s1","hook_event_name":"SessionStart"}""").Out;
        Assert.DoesNotContain("00:00 UTC", briefing, StringComparison.Ordinal);
        Assert.Contains("Mana 5h: nicht vorhanden", briefing, StringComparison.Ordinal);
        Assert.Contains("7d: 99% (Limit 98%)", briefing, StringComparison.Ordinal);
    }

    [Fact]
    public void Ultra_accept_names_a_stale_week_instead_of_a_five_hour_reset()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        Seed.Quest(p, "Q-0003");
        var now = TempProject.Now;
        // last seen 30 min ago: stale; week 97.9 + 1 > 98 is refused and the message says why
        WriteUltraMana(p, 97.9, Stamp(now.AddMinutes(-30)), Stamp(now.AddMinutes(-30)));
        var accept = p.Run(["quest", "accept", "Q-0003"]);
        Assert.Equal(1, accept.Code);
        Assert.DoesNotContain("1970", accept.Out + accept.Err, StringComparison.Ordinal);
    }

    [Fact]
    public void Ultra_with_a_reported_five_hour_window_uses_the_max_limits_until_its_reset()
    {
        using var p = new TempProject(systemYaml: PlanYaml("ultra"));
        WriteMana(p, 93, 10, "claude-opus-4-7");
        var r = p.Run(["mana", "--estimate", "1"], vars: Vars());
        Assert.Contains("Proceed (5h fits)", r.Out, StringComparison.Ordinal); // 93 + 1 <= 94 (Max Opus), Pro would stop at 90
        Assert.Contains("RecapAndStop", p.Run(["mana", "--estimate", "1.5"], vars: Vars()).Out, StringComparison.Ordinal);
        WriteMana(p, 99, 10, "claude-opus-4-7", fiveReset: TempProject.Now.AddMinutes(-5).ToUnixTimeSeconds()); // reset passed: 5h is gone
        var gone = p.Run(["mana", "--estimate", "1"], vars: Vars());
        Assert.Contains("5h: nicht vorhanden", gone.Out, StringComparison.Ordinal);
        Assert.Contains("Proceed", gone.Out, StringComparison.Ordinal);
    }
}
