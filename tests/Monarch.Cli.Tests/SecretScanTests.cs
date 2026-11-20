using System.Text.Json;

namespace Monarch.Cli.Tests;

public sealed class SecretScanTests
{
    private static string Repo(TempProject p)
    {
        Assert.NotNull(Git.Run(p.Root, "init", "-q"));
        Assert.NotNull(Git.Run(p.Root, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "-q", "--allow-empty", "-m", "i"));
        return p.Root;
    }

    private static string WriteHook(string dir, string content, bool exec = true)
    {
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "pre-commit");
        File.WriteAllText(f, content);
        if (OperatingSystem.IsWindows())
        {
            return f;
        }

        File.SetUnixFileMode(f, exec ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return f;
    }

    private static string Status(TempProject p) => p.Run(["status"]).Out;

    private static JsonElement StatusJson(TempProject p) => JsonDocument.Parse(p.Run(["status", "--json"]).Out).RootElement.GetProperty("secretScanHook").Clone();

    private static string Snapshot(string dir) =>
        string.Join("|", Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories).Order()
            .Select(f => f + (File.Exists(f) ? File.ReadAllText(f) + File.GetLastWriteTimeUtc(f).Ticks : string.Empty)));

    [Fact]
    public void Hook_missing_is_reported_in_text_and_json()
    {
        using var p = new TempProject();
        Repo(p);
        Assert.Contains("kein pre-commit-Hook", Status(p), StringComparison.Ordinal);
        var j = StatusJson(p);
        Assert.Equal("missing", j.GetProperty("state").GetString());
        Assert.EndsWith("hooks/pre-commit", j.GetProperty("path").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Hook_without_gitleaks_is_reported()
    {
        using var p = new TempProject();
        WriteHook(Path.Combine(Repo(p), ".git", "hooks"), "#!/bin/sh\nexit 0\n");
        Assert.Contains("ohne erkennbaren gitleaks-Aufruf", Status(p), StringComparison.Ordinal);
        Assert.Equal("no-gitleaks-call", StatusJson(p).GetProperty("state").GetString());
    }

    [Fact]
    public void Hook_not_executable_is_not_active()
    {
        using var p = new TempProject();
        WriteHook(Path.Combine(Repo(p), ".git", "hooks"), "#!/bin/sh\ngitleaks git --staged\n", exec: false);
        var s = Status(p);
        Assert.DoesNotContain("aktiv", s, StringComparison.Ordinal);
        Assert.Contains("nicht ausfuehrbar", s, StringComparison.Ordinal);
        Assert.Equal("not-executable", StatusJson(p).GetProperty("state").GetString());
    }

    [Fact]
    public void Executable_gitleaks_hook_is_active_with_path()
    {
        using var p = new TempProject();
        WriteHook(Path.Combine(Repo(p), ".git", "hooks"), "#!/bin/sh\ngitleaks git --staged\n");
        Assert.Contains("aktiv", Status(p), StringComparison.Ordinal);
        var j = StatusJson(p);
        Assert.Equal("active", j.GetProperty("state").GetString());
        Assert.EndsWith(".git/hooks/pre-commit", j.GetProperty("path").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Core_hooksPath_absolute_and_relative_are_honoured()
    {
        using var p = new TempProject();
        Repo(p);
        WriteHook(Path.Combine(p.Root, ".githooks"), "#!/bin/sh\nexec gitleaks protect --staged\n");
        Assert.NotNull(Git.Run(p.Root, "config", "core.hooksPath", ".githooks"));
        var j = StatusJson(p);
        Assert.Equal("active", j.GetProperty("state").GetString());
        Assert.EndsWith(".githooks/pre-commit", j.GetProperty("path").GetString(), StringComparison.Ordinal);

        // relative path is resolved against the top-level even when cwd is a subdirectory
        var sub = Path.Combine(p.Root, "src", "x");
        Directory.CreateDirectory(sub);
        Assert.Equal("active", SecretScanHook.Detect(sub).Code);

        var abs = Path.Combine(p.Root, "abs-hooks");
        WriteHook(abs, "#!/bin/sh\nexit 0\n");
        Assert.NotNull(Git.Run(p.Root, "config", "core.hooksPath", abs));
        Assert.Equal("no-gitleaks-call", StatusJson(p).GetProperty("state").GetString());
    }

    [Fact]
    public void Worktree_uses_hook_in_git_common_dir()
    {
        using var p = new TempProject(awakened: false);
        Repo(p);
        WriteHook(Path.Combine(p.Root, ".git", "hooks"), "#!/bin/sh\ngitleaks git --staged\n");
        var wt = p.Root + "-wt";
        try
        {
            Assert.NotNull(Git.Run(p.Root, "worktree", "add", "-q", "-b", "wt", wt));
            var r = SecretScanHook.Detect(wt);
            Assert.Equal("active", r.Code);
            Assert.EndsWith(".git/hooks/pre-commit", r.Path, StringComparison.Ordinal);
            Assert.DoesNotContain("-wt", r.Path, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(wt))
            {
                Directory.Delete(wt, true);
            }
        }
    }

    [Fact]
    public void Boss_worktree_without_hook_reports_missing_then_active_and_hook_dir_stays_unchanged()
    {
        using var p = new TempProject(awakened: false);
        Repo(p);
        var common = Path.Combine(p.Root, ".git", "hooks");
        Directory.CreateDirectory(common);
        var wt = p.Root + "-boss";
        try
        {
            Assert.NotNull(Git.Run(p.Root, "worktree", "add", "-q", "-b", "boss", wt));
            foreach (var d in new[] { "local", "ledger", "quests" })
            {
                Directory.CreateDirectory(Path.Combine(wt, ".monarch", d));
            }

            var before = Snapshot(common);
            var text = p.Run(["status"], cwd: wt).Out;
            var json = JsonDocument.Parse(p.Run(["status", "--json"], cwd: wt).Out).RootElement.GetProperty("secretScanHook");
            Assert.Contains("kein pre-commit-Hook", text, StringComparison.Ordinal);
            Assert.Equal("missing", json.GetProperty("state").GetString());
            Assert.Equal(before, Snapshot(common));

            WriteHook(common, "#!/bin/sh\ngitleaks git --staged\n");
            before = Snapshot(common);
            text = p.Run(["status"], cwd: wt).Out;
            json = JsonDocument.Parse(p.Run(["status", "--json"], cwd: wt).Out).RootElement.GetProperty("secretScanHook");
            Assert.Contains("aktiv", text, StringComparison.Ordinal);
            Assert.Equal("active", json.GetProperty("state").GetString());
            Assert.EndsWith(".git/hooks/pre-commit", json.GetProperty("path").GetString(), StringComparison.Ordinal);
            Assert.Equal(before, Snapshot(common));
        }
        finally
        {
            if (Directory.Exists(wt))
            {
                Directory.Delete(wt, true);
            }
        }
    }

    [Fact]
    public void Git_common_dir_is_found_from_subdirectories()
    {
        using var p = new TempProject();
        WriteHook(Path.Combine(Repo(p), ".git", "hooks"), "#!/bin/sh\ngitleaks git --staged\n");
        var sub = Path.Combine(p.Root, "a", "b");
        Directory.CreateDirectory(sub);
        var r = SecretScanHook.Detect(sub);
        Assert.Equal("active", r.Code);
        Assert.EndsWith(".git/hooks/pre-commit", r.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void Symlinked_hook_is_followed()
    {
        using var p = new TempProject();
        var hooks = Path.Combine(Repo(p), ".git", "hooks");
        var real = WriteHook(Path.Combine(p.Root, "real"), "#!/bin/sh\ngitleaks git --staged\n");
        Directory.CreateDirectory(hooks);
        File.CreateSymbolicLink(Path.Combine(hooks, "pre-commit"), real);
        Assert.Equal("active", SecretScanHook.Detect(p.Root).Code);
    }

    [Fact]
    public void Dangling_symlink_hook_is_missing()
    {
        using var p = new TempProject();
        var hooks = Path.Combine(Repo(p), ".git", "hooks");
        Directory.CreateDirectory(hooks);
        File.CreateSymbolicLink(Path.Combine(hooks, "pre-commit"), Path.Combine(p.Root, "nowhere"));
        Assert.Equal("missing", SecretScanHook.Detect(p.Root).Code);
        Assert.Contains("kein pre-commit-Hook", Status(p), StringComparison.Ordinal);
    }

    [Fact]
    public void Directory_as_hook_is_not_a_gitleaks_hook()
    {
        using var p = new TempProject();
        Directory.CreateDirectory(Path.Combine(Repo(p), ".git", "hooks", "pre-commit"));
        var r = SecretScanHook.Detect(p.Root);
        Assert.Equal("no-gitleaks-call", r.Code);
        Assert.Equal("no-gitleaks-call", StatusJson(p).GetProperty("state").GetString());
    }

    [Fact(Timeout = 60000)]
    public async Task Fifo_as_hook_does_not_hang()
    {
        using var p = new TempProject();
        var hooks = Path.Combine(Repo(p), ".git", "hooks");
        Directory.CreateDirectory(hooks);
        using (var mk = System.Diagnostics.Process.Start("mkfifo", Path.Combine(hooks, "pre-commit")))
        {
            mk.WaitForExit();
            Assert.Equal(0, mk.ExitCode);
        }

        var root = p.Root;
        var t = Task.Run(() => SecretScanHook.Detect(root));
        var done = await Task.WhenAny(t, Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));
        Assert.True(ReferenceEquals(done, t), "Detect hangs on a FIFO hook");
        Assert.Equal("no-gitleaks-call", (await t).Code);
    }

    [Fact]
    public void Outside_git_is_unknown_and_status_does_not_fail()
    {
        using var p = new TempProject();
        var r = p.Run(["status"]);
        Assert.Equal(0, r.Code);
        Assert.Contains("unbekannt", r.Out, StringComparison.Ordinal);
        Assert.Equal("unknown", StatusJson(p).GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, StatusJson(p).GetProperty("path").ValueKind);
    }

    [Fact]
    public void Status_and_awaken_leave_hook_directory_unchanged_and_status_stays_short()
    {
        using var p = new TempProject(awakened: false);
        var hooks = Path.Combine(Repo(p), ".git", "hooks");
        WriteHook(hooks, "#!/bin/sh\ngitleaks git --staged\n");
        var before = Snapshot(hooks);
        var a = p.Run(["awaken"]);
        Assert.Contains("Secret-Scan", a.Out, StringComparison.Ordinal);
        Assert.Contains("aktiv", a.Out, StringComparison.Ordinal);
        var s = p.Run(["status"]);
        Assert.True(s.Out.TrimEnd().Split('\n').Length <= 15);
        _ = p.Run(["status", "--json"]);
        Assert.Equal(before, Snapshot(hooks));
    }

    [Fact]
    public void Awaken_reports_missing_hook()
    {
        using var p = new TempProject(awakened: false);
        Repo(p);
        Assert.Contains("kein pre-commit-Hook", p.Run(["awaken"]).Out, StringComparison.Ordinal);
    }

    [Fact]
    public void Oversized_hook_is_read_bounded()
    {
        using var p = new TempProject();
        // gitleaks only beyond the 64 KiB read limit is not recognised
        WriteHook(Path.Combine(Repo(p), ".git", "hooks"), "#!/bin/sh\n" + new string('#', 70_000) + "\ngitleaks\n");
        Assert.Equal("no-gitleaks-call", StatusJson(p).GetProperty("state").GetString());
    }
}
