using System.Text.Json;
using System.Text.RegularExpressions;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0019 (Q-0187): the visible layer (manifests, MCP server, agent tool lists, skills) carries the plugin name from <see cref="RuleTexts.PluginName"/>.</summary>
public class VisibleLayerNameTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, ".monarch", "system.yaml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }

    private static JsonElement Json(string rel)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), rel)));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Manifests_and_mcp_server_use_the_constant()
    {
        var name = RuleTexts.PluginName;
        Assert.Equal(name, Json("plugin/.claude-plugin/plugin.json").GetProperty("name").GetString());
        var market = Json(".claude-plugin/marketplace.json");
        Assert.Equal(name + "-local", market.GetProperty("name").GetString());
        Assert.Equal(name, market.GetProperty("plugins")[0].GetProperty("name").GetString());
        var servers = Json("plugin/.mcp.json").GetProperty("mcpServers");
        Assert.Equal([name], servers.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Plugin_version_matches_the_wrapper_and_is_0_1_2()
    {
        var version = Json("plugin/.claude-plugin/plugin.json").GetProperty("version").GetString();
        var wrapper = File.ReadAllLines(Path.Combine(Root(), "plugin", "scripts", "monarch")).First(l => l.StartsWith("MONARCH_VERSION=", StringComparison.Ordinal));
        Assert.Equal("MONARCH_VERSION=" + version, wrapper);
        Assert.Equal("0.1.2", version);
    }

    [Fact]
    public void Agent_tool_lists_name_the_server_of_the_constant()
    {
        var prefix = $"mcp__plugin_{RuleTexts.PluginName}_{RuleTexts.PluginName}__";
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "plugin", "agents"), "*.md"))
        {
            var tools = File.ReadLines(file).FirstOrDefault(l => l.StartsWith("tools:", StringComparison.Ordinal));
            if (tools is null || !tools.Contains("mcp__", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var t in tools["tools:".Length..].Split(',', StringSplitOptions.TrimEntries).Where(x => x.StartsWith("mcp__", StringComparison.Ordinal)))
            {
                Assert.StartsWith(prefix, t, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void No_old_plugin_reference_remains_in_skills_agents_and_readmes()
    {
        var root = Root();
        var files = Directory.GetFiles(Path.Combine(root, "plugin", "skills"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "plugin", "agents"), "*.md"))
            .Concat([Path.Combine(root, "plugin", "README.md"), Path.Combine(root, "README.md"), Path.Combine(root, "CLAUDE.md"), Path.Combine(root, "scripts", "install-local.sh")]);
        // Verweise "monarch:<agent|skill|befehl>" (nicht "monarch: <text>" und nicht ein Pfad), der alte MCP-Präfix, der alte Marketplace.
        var old = new Regex(@"(?<![\w.-])monarch:(?:regeln|awaken|status|gate|quest|recap|mana|rest|scout|strategist|knight|warden|kritiker)\b|mcp__plugin_monarch|monarch@monarch-local|monarch-local", RegexOptions.CultureInvariant);
        var hits = files.SelectMany(f => File.ReadLines(f).Select((l, i) => (f, i: i + 1, l))).Where(x => old.IsMatch(x.l)).Select(x => $"{Path.GetRelativePath(root, x.f)}:{x.i}").ToList();
        Assert.True(hits.Count == 0, "old plugin name in the visible layer: " + string.Join(", ", hits));
    }

    [Fact]
    public void Skill_descriptions_name_umbrarch_not_monarch()
    {
        foreach (var file in Directory.GetFiles(Path.Combine(Root(), "plugin", "skills"), "SKILL.md", SearchOption.AllDirectories))
        {
            var desc = File.ReadLines(file).First(l => l.StartsWith("description:", StringComparison.Ordinal));
            Assert.DoesNotContain("Monarch", desc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Plugin_description_in_the_plugin_directory_does_not_name_monarch()
    {
        var desc = Json("plugin/.claude-plugin/plugin.json").GetProperty("description").GetString();
        Assert.NotNull(desc);
        Assert.DoesNotContain("Monarch", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readmes_start_the_installation_with_a_clone_of_the_repo_url()
    {
        const string url = "https://github.com/DavidOeztuerk/umbrarch";
        foreach (var f in new[] { "README.md", Path.Combine("plugin", "README.md") })
        {
            var text = File.ReadAllText(Path.Combine(Root(), f));
            Assert.Contains(url, text, StringComparison.Ordinal);
            Assert.Contains("git clone " + url, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Readmes_use_the_repo_placeholder_not_a_personal_path()
    {
        var root = Root();
        foreach (var f in new[] { "README.md", Path.Combine("plugin", "README.md") })
        {
            Assert.DoesNotContain("/Users/davidozturk", File.ReadAllText(Path.Combine(root, f)), StringComparison.Ordinal);
        }
    }
}
