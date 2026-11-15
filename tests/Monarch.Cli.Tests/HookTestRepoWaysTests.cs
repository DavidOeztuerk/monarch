using System.Text.Json;

namespace Monarch.Cli.Tests;

/// <summary>Q-0163: a test repository with a .monarch folder outside the cwd project, seen through the pre-tool-use hook.</summary>
public class HookTestRepoWaysTests
{
    private static string Input(string tool, string toolInput, string cwd) =>
        $$"""{"session_id":"sess1","cwd":{{JsonSerializer.Serialize(cwd)}},"hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"{{tool}}","tool_input":{{toolInput}}}""";

    private static string FileInput(string tool, string file, string cwd) => Input(tool, $$"""{"file_path":{{JsonSerializer.Serialize(file)}}}""", cwd);

    private static string BashInput(string command, string cwd) => Input("Bash", $$"""{"command":{{JsonSerializer.Serialize(command)}}}""", cwd);

    private static string Decision(string output) =>
        output.Length == 0 ? "" : JsonDocument.Parse(output).RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString()!;

    [Fact]
    public void Clone_of_the_project_into_a_new_absolute_folder_is_allowed()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput($"git clone {plain.Root} {repo.Root}-clone1", plain.Root), cwd: plain.Root);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void Checkout_force_with_a_concrete_path_in_the_test_repo_is_allowed()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput($"git -C {repo.Root} checkout -f -- src/a.cs", plain.Root), cwd: plain.Root);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void Writing_an_unprotected_file_in_the_test_repo_by_absolute_path_is_allowed()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], FileInput("Write", Path.Combine(repo.Root, "src", "a.cs"), plain.Root), cwd: plain.Root);
        Assert.Equal(string.Empty, r.Out);
    }

    [Fact]
    public void Rm_rf_of_the_test_repo_folder_is_denied()
    {
        using var repo = new TempProject();
        using var plain = new TempProject(awakened: false);
        var r = repo.Run(["hook", "pre-tool-use"], BashInput($"rm -rf {repo.Root}", plain.Root), cwd: plain.Root);
        Assert.Equal("deny", Decision(r.Out));
    }
}
