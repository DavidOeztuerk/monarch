using System.Globalization;
using Monarch.Domain;
using Monarch.Infrastructure.Boards.Frontmatter;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>Project configuration from <c>.monarch/system.yaml</c>.</summary>
internal sealed record SystemConfig(
    string Theme, PlanKind Plan, ManaLimits Limits, PenaltyMode PenaltyZone, bool ScopeLock, bool RefuseHighRankInBypassMode,
    IReadOnlyList<RiskTagRule> RiskTags, IReadOnlyList<string> DenyBash, IReadOnlyList<string> ProtectPaths,
    IReadOnlyList<string> ProtectSecretsExtra, IReadOnlyList<string> ReadOnlyShadows,
    IReadOnlyList<string> ProtectedBranches, string IntegrationBranch)
{
    /// <summary>Opt-in of the out-of-session wake call (ADR-0008): <c>launchd</c>, <c>systemd</c> or <c>off</c> (default). Owner decision in the protected system.yaml.</summary>
    public string Wake { get; init; } = "off";

    /// <summary><c>git.merge_method</c> (<c>squash</c> or <c>merge</c>, ADR-0007 Nachtrag Q-0140); <see langword="null"/> when the key is missing: <c>gate merge</c> then derives it from the history.</summary>
    public string? MergeMethod { get; init; }

    /// <summary><c>work_until_pct</c> per window (valid values only); it replaces the table for all models (ADR-0012 Nachtrag Q-0189).</summary>
    public LimitOverrides WorkUntil { get; init; } = new(null, null);

    /// <summary>Entries per model (<c>limits.five_hour.opus</c> and so on); they go before <see cref="WorkUntil"/>.</summary>
    public IReadOnlyDictionary<ModelClass, LimitOverrides> PerModel { get; init; } = new Dictionary<ModelClass, LimitOverrides>();

    /// <summary>The set limits for a model class: entry per model, else <c>work_until_pct</c>, else the table for plan and model.</summary>
    public ManaLimits LimitsFor(ModelClass model)
    {
        var table = ManaLimits.For(Plan, model);
        var entry = PerModel.TryGetValue(model, out var e) ? e : new LimitOverrides(null, null);
        return new ManaLimits(entry.FiveHour ?? WorkUntil.FiveHour ?? table.FiveHourPct, entry.SevenDay ?? WorkUntil.SevenDay ?? table.SevenDayPct);
    }

    /// <summary>Built-in risk tags (from the concept).</summary>
    public static IReadOnlyList<RiskTagRule> DefaultRiskTags { get; } =
    [
        new("auth", Rank.A, ["src/**/Auth/**", "src/**/Identity/**", "src/**/Jwt*"]),
        new("payment", Rank.S, ["src/**/Payments/**"]),
        new("migration", Rank.B, ["**/Migrations/**"]),
        new("infra", Rank.A, ["infra/**", ".github/workflows/**"]),
    ];

    /// <summary>Defaults used for a missing file or missing keys.</summary>
    public static SystemConfig Default { get; } = Create(PlanKind.Pro);

    /// <summary>Defaults for a plan.</summary>
    public static SystemConfig Create(PlanKind plan)
    {
        var p = new PolicyOptions();
        return new SystemConfig("monarch", plan, ManaLimits.ForPlan(plan), PenaltyMode.Block, true, true, DefaultRiskTags,
            p.DenyBash, p.ProtectPaths, [], p.ReadOnlyShadowTypes, p.ProtectedBranches, p.IntegrationBranch);
    }

    /// <summary>Policy options; built-in protections and bans are always kept, the file can only add.</summary>
    public PolicyOptions ToPolicyOptions()
    {
        var d = new PolicyOptions();
        return new PolicyOptions
        {
            ProtectPaths = [.. d.ProtectPaths.Concat(ProtectPaths).Distinct(StringComparer.Ordinal)],
            DenyBash = [.. d.DenyBash.Concat(DenyBash).Distinct(StringComparer.Ordinal)],
            ExtraSecretGlobs = ProtectSecretsExtra,
            ReadOnlyShadowTypes = [.. d.ReadOnlyShadowTypes.Concat(ReadOnlyShadows).Distinct(StringComparer.Ordinal)],
            ProtectedBranches = [.. d.ProtectedBranches.Concat(ProtectedBranches).Distinct(StringComparer.Ordinal)],
            IntegrationBranch = IntegrationBranch,
            ScopeLock = ScopeLock,
            RefuseHighRankInBypassMode = RefuseHighRankInBypassMode,
        };
    }
}

/// <summary>Optional set limits per window (<see langword="null"/> = not set or invalid).</summary>
/// <param name="FiveHour">5h limit.</param>
/// <param name="SevenDay">7d limit.</param>
internal sealed record LimitOverrides(double? FiveHour, double? SevenDay);

/// <summary>Reads <see cref="SystemConfig"/> from YAML.</summary>
internal static class SystemConfigReader
{
    /// <summary>Loads the project's config; a missing file yields defaults, a malformed one throws.</summary>
    public static SystemConfig Load(string root)
    {
        var path = new ProjectPaths(root).SystemYaml;
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : SystemConfig.Default;
    }

    /// <summary>Parses config text.</summary>
    public static SystemConfig Parse(string yaml)
    {
        var root = YamlDocument.Parse(yaml);
        var mana = root.Get("mana") as YamlMap;
        var policy = root.Get("policy") as YamlMap;
        var git = root.Get("git") as YamlMap;
        var plan = ParsePlan(mana?.GetString("plan"));
        var d = SystemConfig.Create(plan);
        var limits = mana?.Get("limits") as YamlMap;
        var fiveMap = limits?.Get("five_hour") as YamlMap;
        var sevenMap = limits?.Get("seven_day") as YamlMap;
        var workUntil = new LimitOverrides(Num(fiveMap?.GetString("work_until_pct")), Num(sevenMap?.GetString("work_until_pct")));
        var perModel = new Dictionary<ModelClass, LimitOverrides>();
        foreach (var (name, cls) in new[] { ("opus", ModelClass.Opus), ("sonnet", ModelClass.Sonnet), ("haiku", ModelClass.Haiku) })
        {
            var entry = new LimitOverrides(Num(fiveMap?.GetString(name)), Num(sevenMap?.GetString(name)));
            if (entry.FiveHour is not null || entry.SevenDay is not null)
            {
                perModel[cls] = entry;
            }
        }

        var config = d with
        {
            Theme = root.GetString("theme") is { Length: > 0 } t ? t : d.Theme,
            Limits = new ManaLimits(workUntil.FiveHour ?? d.Limits.FiveHourPct, workUntil.SevenDay ?? d.Limits.SevenDayPct),
            WorkUntil = workUntil,
            PerModel = perModel,
            Wake = ParseWake(mana?.GetString("wake")),
            PenaltyZone = string.Equals(policy?.GetString("penalty_zone"), "warn", StringComparison.OrdinalIgnoreCase) ? PenaltyMode.Warn : PenaltyMode.Block,
            ScopeLock = Bool(policy?.GetString("scope_lock"), d.ScopeLock),
            RefuseHighRankInBypassMode = Bool(policy?.GetString("refuse_high_rank_in_bypass_mode"), d.RefuseHighRankInBypassMode),
            RiskTags = policy?.Get("risk_tags") is YamlMap tags ? ParseRiskTags(tags) : d.RiskTags,
            DenyBash = Strings(policy, "deny_bash", d.DenyBash),
            ProtectPaths = Strings(policy, "protect_paths", d.ProtectPaths),
            ProtectSecretsExtra = Strings(policy, "protect_secrets_extra", d.ProtectSecretsExtra),
            ReadOnlyShadows = Strings(policy, "read_only_shadows", d.ReadOnlyShadows),
            ProtectedBranches = Strings(git, "protected_branches", d.ProtectedBranches),
            IntegrationBranch = git?.GetString("integration_branch") is { Length: > 0 } ib ? ib : d.IntegrationBranch,
            MergeMethod = git?.GetString("merge_method") is { Length: > 0 } mm ? mm : null,
        };

        if (git?.Get("merge_method") is not (null or YamlScalar))
        {
            throw new FormatException("git.merge_method must be a single value, squash or merge (ADR-0007, no rebase).");
        }

        if (config.MergeMethod is not null &&!MergeMethodResolver.IsValid(config.MergeMethod))
        {
            throw new FormatException($"git.merge_method '{config.MergeMethod}' is not allowed; use squash or merge (ADR-0007, no rebase).");
        }

        // ADR-0007: the integration branch is never a protected branch (built-ins or file, case-insensitive).
        if (IsProtectedBranch(config, config.IntegrationBranch))
        {
            throw new FormatException($"git.integration_branch '{config.IntegrationBranch}' is a protected branch; main/master merges only the owner (ADR-0007).");
        }

        return config;
    }

    /// <summary>True if <paramref name="branch"/> is protected by the built-in defaults or the file (case-insensitive).</summary>
    public static bool IsProtectedBranch(SystemConfig config, string branch) =>
        SystemConfig.Default.ProtectedBranches.Concat(config.ProtectedBranches)
            .Any(p => string.Equals(p, branch, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses <c>name: { min_rank, paths }</c> entries.</summary>
    public static IReadOnlyList<RiskTagRule> ParseRiskTags(YamlMap tags) =>
    [
        .. tags.Entries.Where(e => e.Value is YamlMap).Select(e =>
        {
            var m = (YamlMap)e.Value;
            return new RiskTagRule(e.Key, Enum.TryParse<Rank>(m.GetString("min_rank"), true, out var r) ? r : Rank.A, m.GetStrings("paths"));
        }),
    ];

    /// <summary>Parses a plan name as written in the config and on the command line.</summary>
    public static PlanKind ParsePlan(string? text) => text?.ToLowerInvariant() switch
    {
        "max" or "max5" => PlanKind.Max,
        "ultra" or "max20" => PlanKind.Ultra,
        "api-key" or "apikey" => PlanKind.ApiKey,
        _ => PlanKind.Pro,
    };

    /// <summary>Parses <c>mana.wake</c>; anything but <c>launchd</c> or <c>systemd</c> is <c>off</c>.</summary>
    public static string ParseWake(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "launchd" => "launchd",
        "systemd" => "systemd",
        _ => "off",
    };

    /// <summary>A limit counts only when it is finite and 0 &lt;= value &lt;= 100 (ADR-0012 Nachtrag Q-0189); otherwise it counts as missing.</summary>
    private static double? Num(string? t) =>
        double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) && v >= 0 && v <= 100 ? v : null;

    private static bool Bool(string? t, bool fallback) => bool.TryParse(t, out var b) ? b : fallback;

    private static IReadOnlyList<string> Strings(YamlMap? map, string key, IReadOnlyList<string> fallback) =>
        map?.Get(key) is null ? fallback : map.GetStrings(key);
}
