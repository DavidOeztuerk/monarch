using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Cli.Tests;

public class ConfigTests
{
    private const string Sample = """
        version: 1
        theme: neutral                      # monarch | neutral
        mana:
          plan: max5                        # pro | max5 | max20 | api-key
          limits:
            five_hour: { work_until_pct: 91, clamp: [90, 97] }
            seven_day: { work_until_pct: 95, clamp: [96, 99] }
        policy:
          penalty_zone: warn
          scope_lock: false
          refuse_high_rank_in_bypass_mode: false
          risk_tags:
            auth:      { min_rank: A, paths: ["src/**/Auth/**", "src/**/Jwt*"] }  # trailing comment
            payment:   { min_rank: S, paths: ["src/**/Payments/**"] }
          deny_bash: ["git push --force*", "echo '# not a comment'"]
          protect_paths: [".monarch/ledger/**"]
          protect_secrets_extra: ["vault/**"]
          read_only_shadows: [warden]
        """;

    [Fact]
    public void Parses_the_concept_sample_with_comments_and_flow_values()
    {
        var c = SystemConfigReader.Parse(Sample);
        Assert.Equal("neutral", c.Theme);
        Assert.Equal(PlanKind.Max5, c.Plan);
        Assert.Equal(new ManaLimits(91, 95), c.Limits);
        Assert.Equal(PenaltyMode.Warn, c.PenaltyZone);
        Assert.False(c.ScopeLock);
        Assert.False(c.RefuseHighRankInBypassMode);
        Assert.Equal(2, c.RiskTags.Count);
        var auth = c.RiskTags.Single(t => t.Tag == "auth");
        Assert.Equal(Rank.A, auth.MinRank);
        Assert.Equal(["src/**/Auth/**", "src/**/Jwt*"], auth.Paths);
        Assert.Contains("echo '# not a comment'", c.DenyBash);
        Assert.Contains("vault/**", c.ProtectSecretsExtra);
        Assert.Equal(["warden"], c.ReadOnlyShadows);
    }

    [Fact]
    public void Empty_or_partial_config_falls_back_to_defaults()
    {
        var c = SystemConfigReader.Parse("version: 1\n");
        Assert.Equal(SystemConfig.Default.Plan, c.Plan);
        Assert.Equal(PenaltyMode.Block, c.PenaltyZone);
        Assert.True(c.ScopeLock);
        Assert.Equal(ManaLimits.ForPlan(PlanKind.Pro), c.Limits);
    }

    [Fact]
    public void Policy_options_never_drop_the_built_in_protections()
    {
        var o = SystemConfigReader.Parse(Sample).ToPolicyOptions();
        Assert.Contains(".monarch/local/**", o.ProtectPaths);
        Assert.Contains(".monarch/ledger/**", o.ProtectPaths);
        Assert.Contains("git reset --hard*", o.DenyBash);
        Assert.Contains("vault/**", o.ExtraSecretGlobs);
        Assert.False(o.ScopeLock);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var p = new TempProject();
        Assert.Equal(SystemConfig.Default.Plan, SystemConfigReader.Load(p.Root).Plan);
    }

    [Fact]
    public void Read_only_shadows_in_the_file_only_add_to_the_built_ins()
    {
        Assert.Equal(["warden", "scout"], SystemConfigReader.Parse("policy:\n  read_only_shadows: []\n").ToPolicyOptions().ReadOnlyShadowTypes);
        var added = SystemConfigReader.Parse("policy:\n  read_only_shadows: [scribe]\n").ToPolicyOptions().ReadOnlyShadowTypes;
        Assert.Contains("scribe", added);
        Assert.Contains("warden", added);
        Assert.Contains("scout", added);
    }
}
