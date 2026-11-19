using System.Text.RegularExpressions;

namespace Monarch.Cli.Tests;

/// <summary>ADR-0019 Entscheidung 4 (Q-0186): der Plugin-Name steht im Code an genau einer Stelle.</summary>
public class PluginNameConstantTests
{
    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, ".monarch", "system.yaml")))
        {
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(dir);
        return dir;
    }

    [Fact]
    public void Exactly_one_line_in_src_names_umbrarch()
    {
        var src = Path.Combine(RepoRoot(), "src");
        var hits = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(f => File.ReadLines(f).Select((l, i) => (File: Path.GetRelativePath(src, f), Line: i + 1, Text: l)))
            .Where(x => Regex.IsMatch(x.Text, "umbrarch", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToList();
        Assert.True(hits.Count == 1, "expected exactly one line naming umbrarch in src/**/*.cs, found: " + string.Join("; ", hits.Select(h => $"{h.File}:{h.Line}")));
        Assert.Equal("RuleTexts.cs", Path.GetFileName(hits[0].File));
        Assert.Contains("PluginName", hits[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_constant_is_umbrarch_and_the_warden_types_derive_from_it() =>
        Assert.Equal("umbrarch", RuleTexts.PluginName);
}
