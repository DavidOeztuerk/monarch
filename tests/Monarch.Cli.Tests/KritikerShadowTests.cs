namespace Monarch.Cli.Tests;

/// <summary>Q-0171: Kritiker-Shadow, nur lesend (Agent und Skill-Regel 11).</summary>
public class KritikerShadowTests
{
    private static string PluginFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "plugin", "skills")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return Path.Combine([dir, "plugin", .. parts]);
    }

    private static string AgentText()
    {
        var path = PluginFile("agents", "kritiker.md");
        Assert.True(File.Exists(path), "plugin/agents/kritiker.md fehlt");
        return File.ReadAllText(path);
    }

    private static string[] Tools(string text)
    {
        var line = text.Split('\n').FirstOrDefault(l => l.StartsWith("tools:", StringComparison.Ordinal));
        Assert.NotNull(line);
        return line["tools:".Length..].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Kritiker_agent_exists_with_name()
    {
        Assert.Contains("name: kritiker", AgentText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Kritiker_tools_are_read_only_and_have_no_bash()
    {
        var tools = Tools(AgentText());
        Assert.Equal(["Read", "Grep", "Glob"], tools);
        foreach (var banned in new[] { "Bash", "Edit", "Write", "MultiEdit", "NotebookEdit" })
        {
            Assert.DoesNotContain(banned, tools);
        }
    }

    [Fact]
    public void Kritiker_text_has_the_canary_rule()
    {
        var text = AgentText();
        Assert.Contains("Canary-Regel", text, StringComparison.Ordinal);
        Assert.Contains("INVALID_RUN (canary confirmed)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Regeln_skill_has_rule_11_with_the_kritiker()
    {
        var text = File.ReadAllText(PluginFile("skills", "regeln", "SKILL.md"));
        var idx = text.IndexOf("\n11. ", StringComparison.Ordinal);
        Assert.True(idx >= 0, "Regel 11 fehlt");
        var end = text.IndexOf('\n', idx + 1);
        var rule = end < 0 ? text[idx..] : text[idx..end];
        Assert.Contains("`monarch:kritiker`", rule, StringComparison.Ordinal);
    }

    [Fact]
    public void Readme_lists_the_kritiker_agent()
    {
        var text = File.ReadAllText(PluginFile("README.md"));
        Assert.Contains("warden, kritiker", text, StringComparison.Ordinal);
    }
}
