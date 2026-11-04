using System.Text;
using System.Text.Json;
using Monarch.Domain;
using Monarch.Infrastructure.Boards.Frontmatter;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary><c>monarch awaken</c>.</summary>
internal static class AwakenCommand
{
    private static readonly string[] Dirs = ["ledger", "quests", "gates", "recap", "knowledge", "local"];

    /// <summary>Runs the command.</summary>
    public static int Run(CliEnv env, string? plan, bool force, string? riskTagsFile)
    {
        var root = Path.GetFullPath(env.Cwd);
        var paths = new ProjectPaths(root);
        if (plan is not null && plan.ToLowerInvariant() is not ("pro" or "max5" or "max20" or "api-key"))
        {
            env.Err.WriteLine($"Unknown plan '{plan}'. Use pro, max5, max20 or api-key.");
            return 2;
        }

        if (File.Exists(paths.SystemYaml) && !force)
        {
            env.Out.WriteLine("Monarch is already awakened here (.monarch/system.yaml exists); nothing changed. Use --force to rewrite system.yaml.");
            return 0;
        }

        IReadOnlyList<RiskTagRule> tags = SystemConfig.DefaultRiskTags;
        if (riskTagsFile is not null)
        {
            var doc = YamlDocument.Parse(File.ReadAllText(riskTagsFile));
            var extra = SystemConfigReader.ParseRiskTags(doc.Get("risk_tags") as YamlMap ?? doc);
            tags = [.. tags.Where(t => extra.All(e => e.Tag != t.Tag)), .. extra];
        }

        var planKind = SystemConfigReader.ParsePlan(plan);
        foreach (var d in Dirs)
        {
            Directory.CreateDirectory(Path.Combine(paths.MonarchDir, d));
        }

        Files.WriteAtomic(paths.SystemYaml, Render(Path.GetFileName(root), planKind, plan ?? "pro", tags));
        var ignored = EnsureGitignore(root);

        var top = Git.Run(root, "rev-parse", "--show-toplevel");
        var origin = top is null ? null : Git.Run(root, "remote", "get-url", "origin");
        var head = top is null ? null : Git.Run(root, "rev-parse", "--short", "HEAD");
        var dirty = top is null ? null : (Git.Run(root, "status", "--porcelain") is { Length: > 0 } ? "yes" : "no");
        env.Out.WriteLine($"Awakened: {root}");
        env.Out.WriteLine($"  created .monarch/ ({string.Join(", ", Dirs)}), system.yaml (plan {plan ?? "pro"}){(ignored ? ", .gitignore updated" : "")}");
        if (top is null)
        {
            env.Out.WriteLine("  WARNING: not inside a git repository (or git unavailable); repo identity could not be verified.");
        }
        else
        {
            env.Out.WriteLine($"  git top-level: {top}");
            env.Out.WriteLine($"  origin: {origin ?? "(none)"}  HEAD: {head ?? "(none)"}  dirty: {dirty}");
            if (!SamePath(top, root))
            {
                env.Out.WriteLine("  WARNING: the current directory is not the git top-level; is this the intended project root?");
            }
        }

        env.Out.WriteLine($"  Secret-Scan-Hook: {SecretScanHook.Detect(root).Text}");
        var ledger = new ProjectLedger(paths, env.Clock);
        ledger.Append(ledger.SessionId(), "cli", "Awakened", ("plan", plan ?? "pro"), ("root", root), ("git_top_level", top), ("origin", origin), ("head", head), ("dirty", dirty));
        return 0;
    }

    // macOS reports /private/var for /var; compare without that prefix.
    private static bool SamePath(string a, string b) =>
        string.Equals(Norm(a), Norm(b), StringComparison.Ordinal);

    private static string Norm(string p)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(p));
        return full.StartsWith("/private/", StringComparison.Ordinal) ? full[8..] : full;
    }

    private static bool EnsureGitignore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        var text = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (text.Split('\n').Any(l => l.Trim() is ".monarch/local/" or ".monarch/local" or "/.monarch/local/"))
        {
            return false;
        }

        var prefix = text.Length == 0 || text.EndsWith('\n') ? "" : "\n";
        File.WriteAllText(path, text + prefix + ".monarch/local/\n", new UTF8Encoding(false));
        return true;
    }

    private static string Q(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string List(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(Q)) + "]";

    private static string Render(string name, PlanKind plan, string planName, IReadOnlyList<RiskTagRule> tags)
    {
        var limits = ManaLimits.ForPlan(plan);
        var p = new PolicyOptions();
        var sb = new StringBuilder();
        sb.AppendLine("version: 1");
        sb.AppendLine("theme: monarch                      # monarch | neutral");
        sb.AppendLine("project:");
        sb.AppendLine($"  name: {Q(name)}");
        sb.AppendLine("mana:");
        sb.AppendLine($"  plan: {planName.ToLowerInvariant()}                         # pro | max5 | max20 | api-key");
        sb.AppendLine("  limits:");
        sb.AppendLine(FormattableString.Invariant($"    five_hour: {{ work_until_pct: {limits.FiveHourPct:0}, clamp: [{limits.FiveHourPct:0}, 97] }}"));
        sb.AppendLine(FormattableString.Invariant($"    seven_day: {{ work_until_pct: {limits.SevenDayPct:0}, clamp: [{limits.SevenDayPct:0}, 99] }}"));
        sb.AppendLine("  skip_recap_if_reset_within_min: 10");
        sb.AppendLine("  pacing_tolerance_pts: 15");
        sb.AppendLine("policy:");
        sb.AppendLine("  penalty_zone: block               # warn | block");
        sb.AppendLine("  scope_lock: true");
        sb.AppendLine("  refuse_high_rank_in_bypass_mode: true");
        sb.AppendLine("  risk_tags:");
        foreach (var t in tags)
        {
            sb.AppendLine($"    {t.Tag}: {{ min_rank: {t.MinRank}, paths: {List(t.Paths)} }}");
        }

        sb.AppendLine($"  deny_bash: {List(p.DenyBash)}");
        sb.AppendLine($"  protect_paths: {List(p.ProtectPaths)}");
        sb.AppendLine("  protect_secrets_extra: []");
        sb.AppendLine($"  read_only_shadows: {List(p.ReadOnlyShadowTypes)}");
        sb.AppendLine("recap:");
        sb.AppendLine("  path: .monarch/recap/");
        return sb.ToString();
    }
}
