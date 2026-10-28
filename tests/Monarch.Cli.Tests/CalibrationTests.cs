using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Monarch.Application.Net;
using Monarch.Policy;

namespace Monarch.Cli.Tests;

/// <summary>Mana estimate per (model, effort) pair (ADR-0012, Q-0127).</summary>
public class CalibrationTests
{
    private const string Deny = "senk|reduzier|niedriger|lower|reduce|switch|wechsel|/effort|/model|--effort";

    private static IReadOnlyDictionary<string, string?> Vars(string sid, string? effort) =>
        new Dictionary<string, string?> { ["CLAUDE_CODE_SESSION_ID"] = sid, ["CLAUDE_EFFORT"] = effort };

    /// <summary>Writes mana.json with fresh rate limits (stamped at the fake clock) and one session entry carrying the model.</summary>
    private static void WriteMana(TempProject p, double five, double seven, string sid, string model, long? fiveReset = null, string? otherSession = null)
    {
        var f = fiveReset ?? TempProject.Now.AddHours(2).ToUnixTimeSeconds();
        var s = TempProject.Now.AddDays(3).ToUnixTimeSeconds();
        var now = ManaStore.Stamp(p.Clock.UtcNow);
        var other = otherSession is null ? string.Empty : $$$""","{{{otherSession}}}":{"used_percentage":1,"written_at":"{{{now}}}","model":"claude-haiku-4-5"}""";
        File.WriteAllText(Path.Combine(p.Home, "mana.json"), FormattableString.Invariant(
            $$$"""{"schema":1,"written_at":"{{{now}}}","rate_limits_at":"{{{now}}}","rate_limits":{"five_hour":{"used_percentage":{{{five}}},"resets_at":{{{f}}}},"seven_day":{"used_percentage":{{{seven}}},"resets_at":{{{s}}}}},"sessions":{"{{{sid}}}":{"used_percentage":10,"written_at":"{{{now}}}","model":"{{{model}}}"}{{{other}}}}}"""));
    }

    private static void Cycle(TempProject p, string sid, string model, string? effort, double delta, double start = 20, long? resetAtSubmit = null, string? otherSessionAtSubmit = null)
    {
        Seed.Quest(p, "Q-0003", gate: "G-0001");
        var v = Vars(sid, effort);
        WriteMana(p, start, 10, sid, model);
        Assert.Equal(0, p.Run(["quest", "accept", "Q-0003"], vars: v).Code);
        Assert.Equal(0, p.Run(["quest", "start", "Q-0003"], vars: v).Code);
        p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(1);
        WriteMana(p, start + delta, 10, sid, model, resetAtSubmit, otherSessionAtSubmit);
        Assert.Equal(0, p.Run(["quest", "submit", "Q-0003"], vars: v).Code);
    }

    private static JsonElement Pairs(TempProject p) => JsonDocument.Parse(File.ReadAllText(p.LocalPath("mana-calibration.json"))).RootElement.GetProperty("pairs");

    [Fact]
    public void Two_pairs_are_calibrated_separately_and_shown_with_their_own_factor()
    {
        using var p = new TempProject();
        Cycle(p, "s-opus", "claude-opus-4-7[1m]", "xhigh", delta: 10); // estimate 5 -> observation 2.0
        Cycle(p, "s-sonnet", "claude-sonnet-5-5", "default", delta: 2.5); // observation 0.5
        var pairs = Pairs(p);
        Assert.Equal(1.3, pairs.GetProperty("opus|xhigh").GetProperty("factor").GetDouble(), 6);
        Assert.Equal(0.85, pairs.GetProperty("sonnet|default").GetProperty("factor").GetDouble(), 6);
        Assert.Equal(1, pairs.GetProperty("opus|xhigh").GetProperty("n").GetInt32());

        WriteMana(p, 30, 10, "s-opus", "claude-opus-4-7[1m]");
        var opus = p.Run(["mana", "--estimate", "10"], vars: Vars("s-opus", "xhigh")).Out;
        Assert.Contains("Paar (opus, xhigh): Faktor 1.30, unkalibriert (n=1)", opus, StringComparison.Ordinal);
        Assert.Contains("Step 13%", opus, StringComparison.Ordinal);
        WriteMana(p, 30, 10, "s-sonnet", "claude-sonnet-5-5");
        var sonnet = p.Run(["mana", "--estimate", "10"], vars: Vars("s-sonnet", "default")).Out;
        Assert.Contains("Paar (sonnet, default): Faktor 0.85, unkalibriert (n=1)", sonnet, StringComparison.Ordinal);
        Assert.Contains("Step 8.5%", sonnet, StringComparison.Ordinal);

        var rec = p.Events().Where(e => e.Type == "ManaStepRecorded").ToList();
        Assert.Equal(2, rec.Count);
        Assert.Equal("opus", rec[0].Payload.GetProperty("model").GetString());
        Assert.Equal("xhigh", rec[0].Payload.GetProperty("effort").GetString());
        Assert.Equal("CLAUDE_EFFORT", rec[0].Payload.GetProperty("effortSource").GetString());
        Assert.Equal("mana.json:sessions", rec[0].Payload.GetProperty("modelSource").GetString());
        Assert.True(rec[0].Payload.GetProperty("calibrated").GetBoolean());
        Assert.Equal(10, rec[0].Payload.GetProperty("actual5h").GetDouble(), 6);
    }

    [Fact]
    public void Status_becomes_calibrated_at_three_observations()
    {
        using var p = new TempProject();
        for (var i = 0; i < 3; i++)
        {
            Cycle(p, "s", "claude-opus-4-7", "high", delta: 5);
        }

        WriteMana(p, 30, 10, "s", "claude-opus-4-7");
        Assert.Contains("Paar (opus, high): Faktor 1.00, kalibriert (n=3)", p.Run(["mana"], vars: Vars("s", "high")).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_effort_and_session_fall_back_to_unknown_and_never_fail()
    {
        using var p = new TempProject();
        Cycle(p, "s", "claude-opus-4-7", null, delta: 5);
        var rec = p.Events().Single(e => e.Type == "ManaStepRecorded").Payload;
        Assert.Equal("unknown", rec.GetProperty("effort").GetString());
        Assert.Equal("unknown", rec.GetProperty("effortSource").GetString());
        WriteMana(p, 30, 10, "s", "claude-opus-4-7");
        Assert.Contains("Paar (unknown, unknown)", p.Run(["mana"]).Out, StringComparison.Ordinal); // no session id in the process env
    }

    [Fact]
    public void Window_reset_in_the_interval_is_recorded_but_not_calibrated()
    {
        using var p = new TempProject();
        Cycle(p, "s", "claude-opus-4-7", "high", delta: 30, resetAtSubmit: TempProject.Now.AddHours(7).ToUnixTimeSeconds());
        var rec = p.Events().Single(e => e.Type == "ManaStepRecorded").Payload;
        Assert.False(rec.GetProperty("calibrated").GetBoolean());
        Assert.Equal("window-reset", rec.GetProperty("reason").GetString());
        Assert.False(Pairs(p).TryGetProperty("opus|high", out _));
    }

    [Fact]
    public void Overlapping_session_without_token_data_is_recorded_but_not_calibrated()
    {
        using var p = new TempProject();
        Cycle(p, "s", "claude-opus-4-7", "high", delta: 8, otherSessionAtSubmit: "s-other");
        var rec = p.Events().Single(e => e.Type == "ManaStepRecorded").Payload;
        Assert.False(rec.GetProperty("calibrated").GetBoolean());
        Assert.Equal("overlap-without-token-data", rec.GetProperty("reason").GetString());
    }

    [Fact]
    public void Stale_snapshot_is_recorded_but_not_calibrated()
    {
        using var p = new TempProject();
        Seed.Quest(p, "Q-0003");
        var v = Vars("s", "high");
        WriteMana(p, 20, 10, "s", "claude-opus-4-7");
        p.Run(["quest", "accept", "Q-0003"], vars: v);
        p.Run(["quest", "start", "Q-0003"], vars: v);
        p.Clock.UtcNow = p.Clock.UtcNow.AddMinutes(30); // snapshot older than 10 minutes
        p.Run(["quest", "submit", "Q-0003"], vars: v);
        var rec = p.Events().Single(e => e.Type == "ManaStepRecorded").Payload;
        Assert.Equal("stale-snapshot", rec.GetProperty("reason").GetString());
    }

    [Fact]
    public void Resolve_limits_are_identical_with_and_without_a_calibration_file()
    {
        using var p = new TempProject();
        for (var i = 0; i < 3; i++)
        {
            p.Run(["recap"]);
        }

        var config = SystemConfigReader.Load(p.Root);
        var without = ManaSupport.Resolve(p.Root, config);
        Cycle(p, "s", "claude-opus-4-7", "xhigh", delta: 40); // factor moves to 1.9 (n=1) and a calibration file exists
        File.WriteAllText(p.LocalPath("mana-calibration.json"), """{"schema":1,"pairs":{"opus|xhigh":{"factor":4,"n":9}},"pending":{}}""");
        var with = ManaSupport.Resolve(p.Root, config);
        Assert.Equal(without.Limits, with.Limits);
        Assert.Equal(without.Source, with.Source);
        Assert.Equal(without.Recaps, with.Recaps);
    }

    [Fact]
    public void Output_at_the_limit_contains_no_recommendation_to_change_model_or_effort()
    {
        using var p = new TempProject();
        Seed.Quest(p, "Q-0003");
        var v = Vars("s", "xhigh");
        File.WriteAllText(p.LocalPath("mana-calibration.json"), """{"schema":1,"pairs":{"opus|xhigh":{"factor":3.2,"n":5}},"pending":{}}""");
        WriteMana(p, 97, 98, "s", "claude-opus-4-7");
        var deny = new Regex(Deny, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var outputs = new List<string>();
        foreach (var args in new[] { new[] { "mana" }, ["mana", "--estimate", "30"], ["quest", "accept", "Q-0003"], ["rest"] })
        {
            var r = p.Run(args, vars: v);
            outputs.Add(r.Out + r.Err);
        }

        var env = new CliEnv(p.Root, p.Home, new StringReader(string.Empty), new StringWriter(), new StringWriter(), p.Clock, null, new GitHubTokenProvider(_ => "t", () => null), false, v);
        outputs.Add(ManaCommands.Forecast(env, ProjectPaths.Locate(p.Root, p.Home)!, 30, 3));

        Assert.Contains("Paar (opus, xhigh): Faktor 3.20, kalibriert (n=5)", outputs[0], StringComparison.Ordinal);
        Assert.Contains("Mana does not fit", outputs[2], StringComparison.Ordinal);
        Assert.Contains("Paar (opus, xhigh)", outputs[2], StringComparison.Ordinal);
        Assert.Contains("\"display\": \"Paar (opus, xhigh)", outputs[4], StringComparison.Ordinal);
        foreach (var o in outputs)
        {
            Assert.DoesNotMatch(deny, o);
        }
    }

    [Fact]
    public void A_hostile_model_or_effort_value_cannot_smuggle_text_into_the_display()
    {
        using var p = new TempProject();
        WriteMana(p, 97, 98, "s", "claude-lower-the-effort-model");
        var r = p.Run(["mana"], vars: Vars("s", "switch to sonnet"));
        Assert.Contains("Paar (unknown, unknown)", r.Out, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(Deny, RegexOptions.IgnoreCase), r.Out);
    }

    [Fact]
    public void Skill_texts_contain_no_recommendation_to_change_model_or_effort()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "plugin", "skills")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        var skills = Directory.GetFiles(Path.Combine(dir, "plugin", "skills"), "SKILL.md", SearchOption.AllDirectories);
        Assert.NotEmpty(skills);
        var deny = new Regex(Deny, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var f in skills)
        {
            Assert.DoesNotMatch(deny, File.ReadAllText(f));
        }
    }

    [Fact]
    public void Statusline_records_the_model_per_session()
    {
        using var p = new TempProject();
        p.Run(["statusline"], """{"session_id":"abc","model":{"id":"claude-opus-4-7"},"context_window":{"used_percentage":12}}""");
        var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(p.Home, "mana.json"))).RootElement;
        Assert.Equal("claude-opus-4-7", m.GetProperty("sessions").GetProperty("abc").GetProperty("model").GetString());
    }
}
