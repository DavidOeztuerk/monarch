using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>Q-0122 / ADR-0011 rule 6: relative tokens find projects (N5) and every awakened ancestor project is evaluated in full (N6).</summary>
public class HookNestedProjectTests
{
    private static string Input(string tool, string toolInput, string cwd) =>
        $$"""{"session_id":"sess1","cwd":{{JsonSerializer.Serialize(cwd)}},"hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"{{tool}}","tool_input":{{toolInput}}}""";

    private static string BashInput(string command, string cwd) => Input("Bash", $$"""{"command":{{JsonSerializer.Serialize(command)}}}""", cwd);

    private static string FileInput(string tool, string file, string cwd) => Input(tool, $$"""{"file_path":{{JsonSerializer.Serialize(file)}}}""", cwd);

    private static string Decision(string output) =>
        output.Length == 0 ? "" : JsonDocument.Parse(output).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString()!;

    private static int Policy(TempProject p) => p.Events().Count(e => e.Type == "PolicyDecided");

    private static string Rel(string from, string to) => Path.GetRelativePath(from, to);

    [Fact]
    public void N5_rm_by_relative_path_into_an_awakened_project_from_a_plain_cwd_is_denied()
    {
        using var proj = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = proj.Run(["hook", "pre-tool-use"], BashInput($"rm -rf {Rel(plain.Root, proj.Root)}/.monarch/ledger", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(proj));
    }

    [Fact]
    public void N5_rm_by_relative_path_from_another_awakened_project_is_denied_and_logged_in_the_target()
    {
        using var other = new TempProject();
        using var work = new TempProject(activeRank: "B", include: "**");
        var r = work.Run(["hook", "pre-tool-use"], BashInput($"rm -rf {Rel(work.Root, other.Root)}/.monarch/ledger", work.Root), cwd: work.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(other));
    }

    [Fact]
    public void N5_write_with_a_relative_file_path_into_an_awakened_project_is_denied()
    {
        using var proj = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = proj.Run(["hook", "pre-tool-use"], FileInput("Write", Rel(plain.Root, proj.Root) + "/.monarch/system.yaml", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(proj));
    }

    [Fact]
    public void N5_relative_path_after_a_relative_cd_is_resolved_against_the_new_directory()
    {
        using var proj = new TempProject();
        using var plain = new TempProject(awakened: false);
        Directory.CreateDirectory(Path.Combine(plain.Root, "sub"));
        var r = proj.Run(["hook", "pre-tool-use"], BashInput($"cd sub && rm -rf ../{Rel(plain.Root, proj.Root)}/.monarch/ledger", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
    }

    private static (TempProject P, string Decoy) Decoyed()
    {
        var p = new TempProject(activeRank: "B", include: "src/**");
        var decoy = Path.Combine(p.Root, "src", "d");
        Directory.CreateDirectory(Path.Combine(decoy, ".monarch"));
        File.WriteAllText(Path.Combine(decoy, ".monarch", "system.yaml"), "# decoy\n");
        return (p, decoy);
    }

    [Theory]
    [InlineData("rm -rf ../../.monarch/local")]
    [InlineData("sed -i s/a/b/ ../../.monarch/local/active.json")]
    public void N6_decoy_project_does_not_hide_the_real_project_from_bash(string command)
    {
        var (p, decoy) = Decoyed();
        using (p)
        {
            var r = p.Run(["hook", "pre-tool-use"], BashInput(command, decoy), cwd: decoy);
            Assert.Equal("deny", Decision(r.Out));
            Assert.Equal(1, Policy(p));
        }
    }

    [Fact]
    public void N6_ancestor_project_scope_lock_applies_inside_a_decoy()
    {
        var (p, decoy) = Decoyed();
        using (p)
        {
            var r = p.Run(["hook", "pre-tool-use"], FileInput("Write", "../../docs/x.md", decoy), cwd: decoy);
            Assert.Equal("deny", Decision(r.Out));
            var ev = Assert.Single(p.Events(), e => e.Type == "PolicyDecided");
            Assert.Equal("scope_lock", ev.Payload.GetProperty("rule").GetString());
        }
    }

    [Fact]
    public void N6_a_write_inside_the_scope_of_the_ancestor_stays_allowed_in_a_nested_project()
    {
        var (p, decoy) = Decoyed();
        using (p)
        {
            var r = p.Run(["hook", "pre-tool-use"], FileInput("Write", "../e/x.cs", decoy), cwd: decoy);
            Assert.Equal(string.Empty, r.Out);
        }
    }

    [Fact]
    public void N6_each_decision_lands_in_the_ledger_of_the_project_whose_rule_fired()
    {
        using var outer = new TempProject(activeRank: "B", include: "src/**");
        var inner = Path.Combine(outer.Root, "src", "inner");
        Directory.CreateDirectory(Path.Combine(inner, ".monarch", "ledger"));
        Directory.CreateDirectory(Path.Combine(inner, ".monarch", "local"));
        var r = outer.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(outer.Root, "docs", "x.md"), inner), cwd: inner);
        Assert.Equal("deny", Decision(r.Out));
        Assert.Equal(1, Policy(outer));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(inner, ".monarch", "ledger"), "*.jsonl", SearchOption.AllDirectories));
    }
}
