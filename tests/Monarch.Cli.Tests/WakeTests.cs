using System.Globalization;
using System.Xml.Linq;
using Monarch.Domain;

namespace Monarch.Cli.Tests;

/// <summary>Fake launchctl, systemd-run, systemctl and claude on a temp PATH, plus a temp HOME: no test touches the real LaunchAgents or a real session.</summary>
internal sealed class WakeRig : IDisposable
{
    public const string Session = "da7d131d-0be1-43aa-9a10-991e823808ea";
    public static readonly DateTimeOffset Reset = new(2026, 10, 28, 19, 30, 0, TimeSpan.Zero);
    public static readonly DateTimeOffset At = new(2026, 10, 28, 19, 33, 0, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "monarch-wake-" + Guid.NewGuid().ToString("N"));

    public TempProject Project { get; }
    public string Bin => Path.Combine(_dir, "bin");
    public string FakeHome => Path.Combine(_dir, "home");
    public string AgentsFile => Path.Combine(_dir, "agents.json");
    public string Log(string tool) => Path.Combine(_dir, tool + ".log");
    public Dictionary<string, string?> Vars { get; }
    public string PlistPath => Path.Combine(FakeHome, "Library", "LaunchAgents", $"dev.monarch.wake.{Session}.plist");
    public string MonarchBin => Path.Combine(Project.Home, "bin", "monarch");

    public WakeRig(string? wake = "launchd", int agentsExit = 0, string? agentsJson = null)
    {
        Project = new TempProject(systemYaml: wake is null ? null : $"mana:\n  wake: {wake}\n");
        Directory.CreateDirectory(Bin);
        Directory.CreateDirectory(FakeHome);
        Directory.CreateDirectory(Path.Combine(Project.Home, "bin"));
        File.WriteAllText(MonarchBin, "#!/bin/sh\n");
        File.WriteAllText(AgentsFile, agentsJson ?? Agents("background", "idle"));
        foreach (var tool in new[] { "launchctl", "systemd-run", "systemctl" })
        {
            Script(tool, $"printf '[%s]' \"$@\" >> '{Log(tool)}'; printf '\\n' >> '{Log(tool)}'; exit 0");
        }

        Script("claude", $"printf '[%s]' \"$@\" >> '{Log("claude")}'; printf '\\n' >> '{Log("claude")}'\nif [ \"$1\" = agents ]; then cat '{AgentsFile}'; exit {agentsExit}; fi\nexit 0");
        Vars = new() { ["PATH"] = Bin, ["HOME"] = FakeHome };
        Project.Clock.UtcNow = At.AddMinutes(5);
    }

    public static string Agents(string kind, string status, string id = "df2cad7c") =>
        $$"""[{"id":"{{id}}","kind":"{{kind}}","sessionId":"{{Session}}","status":"{{status}}"},{"id":"zz","kind":"interactive","sessionId":"11111111-2222-3333-4444-555555555555","status":"busy"}]""";

    private void Script(string name, string body)
    {
        var path = Path.Combine(Bin, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    public string[] Calls(string tool) => File.Exists(Log(tool)) ? File.ReadAllLines(Log(tool)) : [];

    private static string Iso(DateTimeOffset t) => t.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public RunResult Schedule(string session = Session, DateTimeOffset? at = null, Dictionary<string, string?>? vars = null) =>
        Project.Run(["wake", "schedule", "--session", session, "--at", Iso(at ?? At)], vars: vars ?? Vars);

    public RunResult Fire(string session = Session, DateTimeOffset? at = null) =>
        Project.Run(["wake", "fire", session, "--at", Iso(at ?? At)], vars: Vars);

    /// <summary>Seeds a ledger event of the session at <paramref name="when"/>.</summary>
    public void Activity(DateTimeOffset when, string type = "ToolUsed")
    {
        var keep = Project.Clock.UtcNow;
        Project.Clock.UtcNow = when;
        new ProjectLedger(new ProjectPaths(Project.Root), Project.Clock).Append(Session, "hook", type);
        Project.Clock.UtcNow = keep;
    }

    public List<string> EventTypes() => [.. Project.Events().Select(e => e.Type)];

    public void Dispose()
    {
        Project.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

public class WakeScheduleTests
{
    [Fact]
    public void Schedule_is_refused_without_opt_in()
    {
        using var rig = new WakeRig(wake: null);
        var r = rig.Schedule();
        Assert.Equal(1, r.Code);
        Assert.Contains("mana.wake", r.Err, StringComparison.Ordinal);
        Assert.False(File.Exists(rig.PlistPath));
        Assert.Empty(rig.Calls("launchctl"));
        Assert.DoesNotContain("WakeScheduled", rig.EventTypes());
    }

    [Fact]
    public void Schedule_is_refused_when_wake_is_off()
    {
        using var rig = new WakeRig(wake: "off");
        Assert.Equal(1, rig.Schedule().Code);
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Theory]
    [InlineData("df2cad7c")]
    [InlineData("../../etc/cron.d/x")]
    [InlineData("da7d131d-0be1-43aa-9a10-991e823808ea; touch x")]
    public void Schedule_is_refused_for_a_non_uuid_session(string session)
    {
        using var rig = new WakeRig();
        var r = rig.Schedule(session);
        Assert.Equal(1, r.Code);
        Assert.Contains("UUID", r.Err, StringComparison.Ordinal);
        Assert.Empty(rig.Calls("launchctl"));
    }

    [Fact]
    public void Schedule_is_refused_for_a_shadow()
    {
        using var rig = new WakeRig();
        var vars = new Dictionary<string, string?>(rig.Vars) { ["MONARCH_AGENT_TYPE"] = "monarch:knight" };
        var r = rig.Schedule(vars: vars);
        Assert.Equal(1, r.Code);
        Assert.Contains("shadow", r.Err, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Fact]
    public void Schedule_allows_only_one_job_per_session()
    {
        using var rig = new WakeRig();
        Assert.Equal(0, rig.Schedule().Code);
        var second = rig.Schedule(at: WakeRig.At.AddHours(1));
        Assert.Equal(1, second.Code);
        Assert.Contains("already", second.Err, StringComparison.OrdinalIgnoreCase);
        Assert.Single(rig.Calls("launchctl"));
        Assert.Single(rig.EventTypes(), t => t == "WakeScheduled");
    }

    [Fact]
    public void Schedule_refuses_an_at_that_is_already_stale()
    {
        using var rig = new WakeRig();
        var r = rig.Schedule(at: WakeRig.At.AddDays(-2));
        Assert.Equal(1, r.Code);
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Fact]
    public void Schedule_refuses_when_claude_is_not_on_the_path()
    {
        using var rig = new WakeRig();
        File.Delete(Path.Combine(rig.Bin, "claude"));
        Assert.Equal(1, rig.Schedule().Code);
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Fact]
    public void Launchd_plist_has_no_shell_absolute_paths_project_working_directory_and_run_at_load()
    {
        using var rig = new WakeRig();
        Assert.Equal(0, rig.Schedule().Code);
        Assert.True(File.Exists(rig.PlistPath), "plist must live under the temp HOME, never the real ~/Library/LaunchAgents");
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), Path.GetFullPath(rig.PlistPath), StringComparison.Ordinal);

        var dict = XDocument.Load(rig.PlistPath).Root!.Element("dict")!;
        XElement ValueOf(XElement d, string key) => d.Elements("key").First(k => k.Value == key).ElementsAfterSelf().First();
        var args = ValueOf(dict, "ProgramArguments").Elements("string").Select(s => s.Value).ToList();

        Assert.True(Path.IsPathRooted(args[0]));
        Assert.Equal(rig.MonarchBin, args[0]);
        Assert.Equal(["wake", "fire", WakeRig.Session, "--at", "2026-10-28T19:33:00Z"], args.Skip(1));
        Assert.DoesNotContain(args, a => a is "sh" or "bash" or "zsh" or "-c" || a.EndsWith("/sh", StringComparison.Ordinal) || a.EndsWith("/bash", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.Contains("Mana-Reset", StringComparison.Ordinal));
        Assert.Equal(rig.Project.Root, ValueOf(dict, "WorkingDirectory").Value);
        Assert.Equal($"dev.monarch.wake.{WakeRig.Session}", ValueOf(dict, "Label").Value);
        Assert.Equal("true", ValueOf(dict, "RunAtLoad").Name.LocalName);

        // Minimal PATH plus the absolute claude binary the fire step needs.
        var env = ValueOf(dict, "EnvironmentVariables");
        Assert.Equal(Path.Combine(rig.Bin, "claude"), ValueOf(env, "MONARCH_CLAUDE_BIN").Value);
        Assert.DoesNotContain(rig.Bin, ValueOf(env, "PATH").Value, StringComparison.Ordinal);

        // StartCalendarInterval = wake time + grace, local.
        var cal = ValueOf(dict, "StartCalendarInterval");
        var local = TimeZoneInfo.ConvertTime(WakeRig.At + WakePlan.Grace, TimeZoneInfo.Local);
        Assert.Equal(local.Minute.ToString(CultureInfo.InvariantCulture), ValueOf(cal, "Minute").Value);
        Assert.Equal(local.Hour.ToString(CultureInfo.InvariantCulture), ValueOf(cal, "Hour").Value);
        Assert.Equal(local.Day.ToString(CultureInfo.InvariantCulture), ValueOf(cal, "Day").Value);
        Assert.Equal(local.Month.ToString(CultureInfo.InvariantCulture), ValueOf(cal, "Month").Value);

        Assert.Contains(rig.Calls("launchctl"), c => c.StartsWith("[bootstrap]", StringComparison.Ordinal) && c.Contains(rig.PlistPath, StringComparison.Ordinal));
        Assert.Contains("WakeScheduled", rig.EventTypes());
    }

    [Fact]
    public void Systemd_run_gets_a_user_timer_without_shell_and_with_absolute_paths()
    {
        using var rig = new WakeRig(wake: "systemd");
        Assert.Equal(0, rig.Schedule().Code);
        var call = Assert.Single(rig.Calls("systemd-run"));
        Assert.Contains("[--user]", call, StringComparison.Ordinal);
        Assert.Contains("[--on-calendar=2026-10-28 19:36:00 UTC]", call, StringComparison.Ordinal);
        Assert.Contains($"[--working-directory={rig.Project.Root}]", call, StringComparison.Ordinal);
        Assert.Contains($"[{rig.MonarchBin}][wake][fire][{WakeRig.Session}][--at][2026-10-28T19:33:00Z]", call, StringComparison.Ordinal);
        Assert.DoesNotContain("[sh]", call, StringComparison.Ordinal);
        Assert.DoesNotContain("[-c]", call, StringComparison.Ordinal);
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Fact]
    public void Cancel_removes_the_job_and_allows_a_new_one()
    {
        using var rig = new WakeRig();
        rig.Schedule();
        var r = rig.Project.Run(["wake", "cancel", WakeRig.Session], vars: rig.Vars);
        Assert.Equal(0, r.Code);
        Assert.False(File.Exists(rig.PlistPath));
        Assert.Contains(rig.Calls("launchctl"), c => c.StartsWith("[bootout]", StringComparison.Ordinal));
        Assert.Equal(0, rig.Schedule().Code);
    }
}

public class WakeFireTests
{
    [Fact]
    public void Boss_idle_background_session_without_activity_since_reset_is_stopped_and_resumed_with_the_fixed_prompt()
    {
        using var rig = new WakeRig();
        rig.Schedule();
        rig.Activity(WakeRig.Reset.AddMinutes(-20)); // last work before the limit
        var r = rig.Fire();
        Assert.Equal(0, r.Code);
        var prompt = WakePlan.Prompt(Path.Combine(rig.Project.Root, ".monarch", "recap", "RECAP.md"));
        Assert.Equal(["[agents][--json]", "[stop][df2cad7c]", $"[--bg][--resume][{WakeRig.Session}][{prompt}]"], rig.Calls("claude"));
        Assert.Contains("WakeFired", rig.EventTypes());
        Assert.False(File.Exists(rig.PlistPath)); // the job removes itself
        Assert.Contains(rig.Calls("launchctl"), c => c.StartsWith("[bootout]", StringComparison.Ordinal));
    }

    [Fact]
    public void Session_with_activity_since_reset_gets_no_copy()
    {
        using var rig = new WakeRig();
        rig.Schedule();
        rig.Activity(WakeRig.Reset.AddMinutes(1)); // the in-session cron already woke it
        Assert.Equal(0, rig.Fire().Code);
        Assert.Equal(["[agents][--json]"], rig.Calls("claude"));
        Assert.Contains("WakeSkipped", rig.EventTypes());
        Assert.DoesNotContain("WakeFired", rig.EventTypes());
    }

    [Fact]
    public void Busy_session_gets_no_copy()
    {
        using var rig = new WakeRig(agentsJson: WakeRig.Agents("background", "busy"));
        rig.Schedule();
        Assert.Equal(0, rig.Fire().Code);
        Assert.Equal(["[agents][--json]"], rig.Calls("claude"));
        Assert.Contains("WakeSkipped", rig.EventTypes());
    }

    [Fact]
    public void Interactive_idle_session_is_skipped()
    {
        using var rig = new WakeRig(agentsJson: WakeRig.Agents("interactive", "idle"));
        rig.Schedule();
        Assert.Equal(0, rig.Fire().Code);
        Assert.Equal(["[agents][--json]"], rig.Calls("claude"));
        var skipped = Assert.Single(rig.Project.Events(), e => e.Type == "WakeSkipped");
        Assert.Equal("interactive-session", skipped.Payload.GetProperty("reason").GetString());
    }

    [Fact]
    public void Failing_claude_agents_means_no_resume()
    {
        using var rig = new WakeRig(agentsExit: 1);
        rig.Schedule();
        Assert.Equal(0, rig.Fire().Code);
        Assert.Equal(["[agents][--json]"], rig.Calls("claude"));
        var skipped = Assert.Single(rig.Project.Events(), e => e.Type == "WakeSkipped");
        Assert.Equal("agents-failed", skipped.Payload.GetProperty("reason").GetString());
    }

    [Fact]
    public void Unlisted_session_is_skipped_not_resumed()
    {
        using var rig = new WakeRig(agentsJson: "[]");
        rig.Schedule();
        Assert.Equal(0, rig.Fire().Code);
        Assert.Equal(["[agents][--json]"], rig.Calls("claude"));
        Assert.Contains("WakeSkipped", rig.EventTypes());
    }

    [Fact]
    public void Stale_at_is_discarded_without_calling_claude()
    {
        using var rig = new WakeRig();
        rig.Schedule();
        rig.Project.Clock.UtcNow = WakeRig.At.AddHours(30);
        Assert.Equal(0, rig.Fire().Code);
        Assert.Empty(rig.Calls("claude"));
        var skipped = Assert.Single(rig.Project.Events(), e => e.Type == "WakeSkipped");
        Assert.Equal("stale", skipped.Payload.GetProperty("reason").GetString());
        Assert.False(File.Exists(rig.PlistPath));
    }

    [Fact]
    public void Run_at_load_before_the_wake_time_does_nothing_and_keeps_the_job()
    {
        using var rig = new WakeRig();
        rig.Schedule();
        rig.Project.Clock.UtcNow = WakeRig.At.AddHours(-3);
        Assert.Equal(0, rig.Fire().Code);
        Assert.Empty(rig.Calls("claude"));
        Assert.True(File.Exists(rig.PlistPath));
        Assert.DoesNotContain("WakeFired", rig.EventTypes());
        Assert.DoesNotContain("WakeSkipped", rig.EventTypes());
    }

    [Fact]
    public void Fire_without_a_scheduled_job_is_refused()
    {
        using var rig = new WakeRig();
        var r = rig.Fire();
        Assert.Equal(1, r.Code);
        Assert.Empty(rig.Calls("claude"));
    }

    [Fact]
    public void Systemd_job_stops_its_timer_after_fire()
    {
        using var rig = new WakeRig(wake: "systemd");
        rig.Schedule();
        Assert.Equal(0, rig.Fire().Code);
        Assert.Contains(rig.Calls("systemctl"), c => c.Contains("[--user][stop]", StringComparison.Ordinal));
        Assert.Contains("WakeFired", rig.EventTypes());
        Assert.Equal(1, rig.Fire().Code); // job record gone
    }
}

public class RestWakeTimeTests
{
    [Fact]
    public void Rest_prints_wake_time_local_and_utc_cron_and_fixed_prompt()
    {
        using var p = new TempProject();
        Seed.Mana(p, 92, 30, new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero));
        var r = p.Run(["rest"]);
        Assert.Equal(0, r.Code);
        var wake = new DateTimeOffset(2026, 10, 27, 14, 3, 0, TimeSpan.Zero);
        var local = TimeZoneInfo.ConvertTime(wake, TimeZoneInfo.Local);
        Assert.Contains("Wake at 2026-10-27T14:03:00Z", r.Out, StringComparison.Ordinal);
        Assert.Contains("local " + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.Out, StringComparison.Ordinal);
        Assert.Contains($"Cron: {WakePlan.Cron(wake, TimeZoneInfo.Local)}", r.Out, StringComparison.Ordinal);
        Assert.Contains("Prompt: " + WakePlan.Prompt(Path.Combine(p.Root, ".monarch", "recap", "RECAP.md")), r.Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Rest_wakes_at_the_later_reset_when_both_windows_are_exceeded()
    {
        using var p = new TempProject();
        Seed.Mana(p, 92, 97, new DateTimeOffset(2026, 10, 27, 14, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.Zero));
        Assert.Contains("Wake at 2026-10-30T09:03:00Z", p.Run(["rest"]).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Rest_wake_minute_is_never_the_hour_or_half_hour_in_local_time()
    {
        using var p = new TempProject();
        var reset = new DateTimeOffset(2026, 10, 27, 14, 57, 0, TimeSpan.Zero);
        Seed.Mana(p, 92, 30, reset);
        var r = p.Run(["rest"]);
        var expected = WakePlan.WakeTime(reset, TimeZoneInfo.Local);
        Assert.Contains($"Wake at {expected.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss'Z'}", r.Out, StringComparison.Ordinal);
        Assert.NotEqual(0, TimeZoneInfo.ConvertTime(expected, TimeZoneInfo.Local).Minute);
        Assert.NotEqual(30, TimeZoneInfo.ConvertTime(expected, TimeZoneInfo.Local).Minute);
    }
}
