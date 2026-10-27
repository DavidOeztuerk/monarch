using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>Q-0117 / F1: the project is resolved from the target path and from Bash directory targets, not only from the cwd.</summary>
public class HookProjectResolutionTests
{
    private static string Input(string tool, string toolInput, string cwd) =>
        $$"""{"session_id":"sess1","cwd":{{JsonSerializer.Serialize(cwd)}},"hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"{{tool}}","tool_input":{{toolInput}}}""";

    private static string FileInput(string tool, string file, string cwd) => Input(tool, $$"""{"file_path":{{JsonSerializer.Serialize(file)}}}""", cwd);

    private static string BashInput(string command, string cwd) => Input("Bash", $$"""{"command":{{JsonSerializer.Serialize(command)}}}""", cwd);

    private static string Decision(string output) =>
        output.Length == 0 ? "" : JsonDocument.Parse(output).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString()!;

    private static int Policy(TempProject p) => p.Events().Count(e => e.Type == "PolicyDecided");

    [Fact]
    public void Bypass7_write_by_absolute_path_into_an_awakened_repo_from_a_plain_cwd_is_denied()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(repo.Root, ".monarch", "ledger", "x.jsonl"), plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
        var ev = Assert.Single(repo.Events(), e => e.Type == "PolicyDecided");
        Assert.Equal("protect_paths", ev.Payload.GetProperty("rule").GetString());
    }

    [Fact]
    public void Bypass7_bash_cd_into_an_awakened_repo_then_rm_is_denied()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput($"cd {repo.Root} && rm .monarch/ledger/x", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(repo));
    }

    [Theory]
    [InlineData("git -C {0} restore .monarch/ledger/x")]
    [InlineData("pushd {0} && rm .monarch/ledger/x")]
    [InlineData("git --git-dir={0}/.git --work-tree={0} checkout -- .monarch/ledger/x")]
    [InlineData("rm {0}/.monarch/ledger/x")]
    public void Bypass7_other_bash_directory_targets_are_resolved(string template)
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput(string.Format(template, repo.Root), plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
    }

    [Fact]
    public void Bypass7_protect_paths_of_the_target_repo_apply_even_with_an_active_quest_in_the_cwd_project()
    {
        using var cwdProject = new TempProject(activeRank: "B", include: "**");
        using var target = new TempProject();
        var r = cwdProject.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(target.Root, ".monarch", "ledger", "x.jsonl"), cwdProject.Root), cwd: cwdProject.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(target));
    }

    [Fact]
    public void Bypass7_secrets_of_the_target_repo_apply_to_bash_from_a_plain_cwd()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput($"cd {repo.Root} && cat .env", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal("protect_secrets", Assert.Single(repo.Events(), e => e.Type == "PolicyDecided").Payload.GetProperty("rule").GetString());
    }

    [Fact]
    public void Both_projects_log_their_own_decision_and_the_strictest_wins()
    {
        using var a = new TempProject(activeRank: "S", include: "**");
        using var b = new TempProject();
        // Rank S asks in A for any mutation; B's protect_paths denies.
        var r = a.Run(["hook", "pre-tool-use"], BashInput($"rm {b.Root}/.monarch/ledger/x", a.Root), cwd: a.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(b));
        Assert.Equal("rank_s_approval", Assert.Single(a.Events(), e => e.Type == "PolicyDecided").Payload.GetProperty("rule").GetString());
    }

    [Fact]
    public void Writes_into_a_non_awakened_target_stay_a_noop()
    {
        using var plain = new TempProject(awakened: false);
        using var other = new TempProject(awakened: false);
        var r = plain.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(other.Root, ".monarch", "ledger", "x"), plain.Root), cwd: plain.Root);
        Assert.Equal(string.Empty, r.Out);
        Assert.False(Directory.Exists(Path.Combine(plain.Root, ".monarch")));
    }

    [Fact]
    public void Harmless_target_in_an_awakened_repo_prints_nothing()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(repo.Root, "src", "a.cs"), plain.Root), cwd: plain.Root);
        Assert.Equal(string.Empty, r.Out);
        Assert.Equal(0, Policy(repo));
    }

    [Fact]
    public void Symlinked_directory_into_a_repo_is_resolved_to_that_repo()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var link = Path.Combine(plain.Root, "ln");
        Directory.CreateSymbolicLink(link, Path.Combine(repo.Root, ".monarch", "ledger"));
        var r = repo.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(link, "x.jsonl"), plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
    }
}
