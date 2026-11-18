using System.Text.RegularExpressions;
using Monarch.Policy;

namespace Monarch.Cli.Tests;

/// <summary>
/// Issue 117: every file in src/Monarch.Cli that declares a type the hook path (HookRunner.cs) calls or creates must sit in the risk tag
/// <c>policy</c>, and the risk tag <c>mana</c> must cover Statusline.cs. The file list is derived from the source, not written down here.
/// </summary>
public class HookPathRiskTagTests
{
    private const string CliDir = "src/Monarch.Cli";
    private const string HookRunnerFile = "HookRunner.cs";

    private static readonly Regex Declaration = new(
        @"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*(?:record\s+struct|record|class|struct|enum|interface)\s+(?<name>[A-Za-z_]\w*)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

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

    /// <summary>Maps each file (repo-relative, forward slashes) to the names of the types it declares.</summary>
    private static Dictionary<string, List<string>> DeclaredTypes(string root)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "src", "Monarch.Cli"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            var rel = CliDir + "/" + Path.GetFileName(file);
            result[rel] = [.. Declaration.Matches(File.ReadAllText(file)).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal)];
        }

        return result;
    }

    /// <summary>Files (other than HookRunner.cs) that declare a type HookRunner.cs calls (Name.), creates (new Name) or takes as parameter (CliEnv).</summary>
    private static SortedSet<string> HookPathFiles(string root)
    {
        var hook = File.ReadAllText(Path.Combine(root, "src", "Monarch.Cli", HookRunnerFile));
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (file, names) in DeclaredTypes(root))
        {
            if (file.EndsWith("/" + HookRunnerFile, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var name in names)
            {
                var n = Regex.Escape(name);
                var called = Regex.IsMatch(hook, $@"(?<![\w.]){n}\.");
                var created = Regex.IsMatch(hook, $@"\bnew\s+{n}\b");
                var parameter = name == "CliEnv" && Regex.IsMatch(hook, @"\bCliEnv\s+\w+");
                if (called || created || parameter)
                {
                    files.Add(file);
                    break;
                }
            }
        }

        return files;
    }

    private static IReadOnlyList<RiskTagRule> Tags(string root) => SystemConfigReader.Load(root).RiskTags;

    private static bool InTag(IReadOnlyList<RiskTagRule> tags, string tag, string file) =>
        tags.Where(t => t.Tag == tag).Any(t => t.Paths.Any(g => GlobMatcher.IsMatch(g, file)));

    [Fact]
    public void Detection_finds_known_hook_path_calls_so_the_check_is_not_empty()
    {
        var files = HookPathFiles(RepoRoot());
        Assert.Contains("src/Monarch.Cli/ProjectPaths.cs", files);
        Assert.Contains("src/Monarch.Cli/BackgroundShells.cs", files);
        Assert.Contains("src/Monarch.Cli/RuleTexts.cs", files);
        Assert.Contains("src/Monarch.Cli/State.cs", files);
        Assert.Contains("src/Monarch.Cli/CliEnv.cs", files);
    }

    [Fact]
    public void Every_file_the_hook_path_uses_is_in_the_risk_tag_policy()
    {
        var root = RepoRoot();
        var tags = Tags(root);
        var missing = HookPathFiles(root).Where(f => !InTag(tags, "policy", f)).ToList();
        Assert.True(missing.Count == 0, "Not in risk tag 'policy': " + string.Join(", ", missing));
    }

    [Fact]
    public void Risk_tag_mana_exists_and_covers_Statusline()
    {
        var tags = Tags(RepoRoot());
        Assert.Contains(tags, t => t.Tag == "mana");
        Assert.True(InTag(tags, "mana", "src/Monarch.Cli/Statusline.cs"), "Risk tag 'mana' does not cover src/Monarch.Cli/Statusline.cs");
    }
}
