using System.Diagnostics;
using Monarch.Application;

namespace Monarch.Cli.Tests;

/// <summary>Q-0145 / ADR-0016 (#60): helper programs only absolute from PATH.</summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
public class ToolLocatorTests
{
    private static string Tmp() => Directory.CreateTempSubdirectory("monarch-tl-").FullName;

    private static void Exe(string path, string body = "#!/bin/sh\nexit 0\n")
    {
        File.WriteAllText(path, body);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void Stub(string dir, string name, string marker) => Exe(Path.Combine(dir, name), $"#!/bin/sh\necho ran > '{marker}'\nexit 1\n");

    [Fact]
    public void Find_takes_first_absolute_executable_file_and_skips_relative_empty_dir_and_non_executable()
    {
        var a = Tmp(); var b = Tmp(); var c = Tmp();
        Directory.CreateDirectory(Path.Combine(a, "git"));
        File.WriteAllText(Path.Combine(b, "git"), "x");
        File.SetUnixFileMode(Path.Combine(b, "git"), UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Exe(Path.Combine(c, "git"));
        var err = new StringWriter();
        var l = new ToolLocator($":.:bin::{a}:{b}:{c}", err);
        Assert.Equal(Path.Combine(c, "git"), l.Locate("git"));
        Assert.Equal(string.Empty, err.ToString());
    }

    [Fact]
    public void Find_follows_symlinks()
    {
        var a = Tmp(); var b = Tmp();
        Exe(Path.Combine(b, "real"));
        File.CreateSymbolicLink(Path.Combine(a, "git"), Path.Combine(b, "real"));
        Assert.Equal(Path.Combine(a, "git"), new ToolLocator(a, new StringWriter()).Locate("git"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("bin:.::")]
    public void Find_without_hit_gives_null_and_one_stderr_line_per_name(string? path)
    {
        var err = new StringWriter();
        var l = new ToolLocator(path, err);
        Assert.Null(l.Locate("git"));
        Assert.Null(l.Locate("git"));
        Assert.Null(l.Locate("gh"));
        var lines = err.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(x => x.TrimEnd('\r')).ToArray();
        Assert.Equal(["monarch: git nicht in PATH gefunden (nur absolute Einträge zählen)", "monarch: gh nicht in PATH gefunden (nur absolute Einträge zählen)"], lines);
    }

    [Fact]
    public void Static_scan_src_has_no_bare_git_or_gh_program_name()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "src", "Monarch.Cli"))) dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        var sep = Path.DirectorySeparatorChar;
        var bad = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(dir!, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.Contains($"{sep}obj{sep}") || f.Contains($"{sep}bin{sep}")) continue;
            var lines = File.ReadAllLines(f);
            // only files that start processes can hand a program name to one (Policy merely compares words of analysed commands)
            if (!lines.Any(l => l.Contains("ProcessStartInfo", StringComparison.Ordinal) || l.Contains("Process.Start", StringComparison.Ordinal))) continue;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                foreach (var name in new[] { "\"git\"", "\"gh\"" })
                {
                    var idx = line.IndexOf(name, StringComparison.Ordinal);
                    while (idx >= 0)
                    {
                        if (!line[..idx].EndsWith("ToolLocator.Find(", StringComparison.Ordinal)) bad.Add($"{Path.GetRelativePath(dir!, f)}:{i + 1}: {line.Trim()}");
                        idx = line.IndexOf(name, idx + 1, StringComparison.Ordinal);
                    }
                }
            }
        }

        Assert.True(bad.Count == 0, string.Join('\n', bad));
    }

    private static (string Out, string Err) RunMonarch(string cwd, string path, string[] args, string? stdin, string home)
    {
        var psi = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "monarch"))
        {
            WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["PATH"] = path;
        psi.Environment["MONARCH_HOME"] = home;
        psi.Environment.Remove("MONARCH_GIT_BIN");
        using var p = Process.Start(psi)!;
        p.StandardInput.Write(stdin ?? string.Empty);
        p.StandardInput.Close();
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (o.Result, e.Result);
    }

    /// <summary>The stubs sit in the process cwd (and in the binary directory test below); PATH is hostile; they must not run.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("bin:.::")]
    public void Hook_pre_tool_use_deny_with_unanchored_ledger_does_not_run_a_git_stub_from_cwd(string path)
    {
        var root = Tmp();
        var marker = Path.Combine(Tmp(), "marker");
        Directory.CreateDirectory(Path.Combine(root, ".monarch", "local"));
        var month = DateTime.UtcNow.ToString("yyyy-MM");
        Directory.CreateDirectory(Path.Combine(root, ".monarch", "ledger", month));
        File.WriteAllText(Path.Combine(root, ".monarch", "local", "writer-id"), "abcdef01");
        File.WriteAllText(Path.Combine(root, ".monarch", "ledger", month, "sess1.abcdef01.jsonl"), string.Empty);
        Stub(root, "git", marker);
        Stub(root, "gh", marker);
        var json = $$$"""{"session_id":"sess1","cwd":"{{{root}}}","hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"Edit","tool_input":{"file_path":"{{{root}}}/.monarch/ledger/x.jsonl"}}""";
        var r = RunMonarch(root, path, ["hook", "pre-tool-use"], json, Tmp());
        Assert.Contains("deny", r.Out);
        Assert.False(File.Exists(marker), "the git stub in the cwd ran");
    }

    [Theory]
    [InlineData(".:bin::")]
    [InlineData("")]
    public void Awaken_does_not_run_a_git_stub_from_cwd(string path)
    {
        var root = Tmp();
        var marker = Path.Combine(Tmp(), "marker");
        Stub(root, "git", marker);
        var r = RunMonarch(root, path, ["awaken"], null, Tmp());
        Assert.False(File.Exists(marker), $"the git stub ran: {r.Err}");
    }

    [Fact]
    public void Derive_with_no_git_in_path_falls_back_to_squash_without_running_a_stub()
    {
        var root = Tmp();
        var marker = Path.Combine(Tmp(), "marker");
        Stub(root, "git", marker);
        var r = RunMonarch(root, ".", ["awaken"], null, Tmp());
        Assert.False(File.Exists(marker));
        Assert.Contains("nicht in PATH", r.Err);
    }
}
