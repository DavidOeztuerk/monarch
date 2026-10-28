using FsCheck.Xunit;
using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

public class RankTests
{
    private static readonly IReadOnlyList<RiskTagRule> NoTags = [];
    private static readonly IReadOnlyList<string> NoPaths = [];
    private static Rank R(ChangeFacts f) => RankPolicy.Assess(f, NoTags, NoPaths).Rank;

    [Fact] public void E_single_file_no_logic() => Assert.Equal(Rank.E, R(new ChangeFacts(1, LogicChange: false)));
    [Fact] public void E_zero_files() => Assert.Equal(Rank.E, R(new ChangeFacts(0, LogicChange: false)));
    [Fact] public void D_small_module_with_tests() => Assert.Equal(Rank.D, R(new ChangeFacts(5, 1, true, true)));
    [Fact] public void D_single_file_logic_with_tests() => Assert.Equal(Rank.D, R(new ChangeFacts(1, 1, true, true)));
    [Fact] public void C_multiple_modules() => Assert.Equal(Rank.C, R(new ChangeFacts(3, 2, true, true)));
    [Fact] public void C_new_dependency() => Assert.Equal(Rank.C, R(new ChangeFacts(1, 1, true, true, NewDependency: true)));
    [Fact] public void C_fallback_when_many_files() => Assert.Equal(Rank.C, R(new ChangeFacts(6, 1, true, true)));
    [Fact] public void C_fallback_without_tests() => Assert.Equal(Rank.C, R(new ChangeFacts(2, 1, true, false)));
    [Fact] public void B_cross_service() => Assert.Equal(Rank.B, R(new ChangeFacts(1, CrossService: true)));
    [Fact] public void B_public_api() => Assert.Equal(Rank.B, R(new ChangeFacts(1, PublicApiChange: true)));
    [Fact] public void B_additive_schema() => Assert.Equal(Rank.B, R(new ChangeFacts(1, AdditiveSchemaChange: true)));
    [Fact] public void A_breaking() => Assert.Equal(Rank.A, R(new ChangeFacts(1, BreakingChange: true)));
    [Fact] public void A_data_migration() => Assert.Equal(Rank.A, R(new ChangeFacts(1, DataMigration: true)));
    [Fact] public void A_auth() => Assert.Equal(Rank.A, R(new ChangeFacts(1, AuthSecurityCode: true)));
    [Fact] public void A_infra() => Assert.Equal(Rank.A, R(new ChangeFacts(1, Infrastructure: true)));
    [Fact] public void S_destructive() => Assert.Equal(Rank.S, R(new ChangeFacts(1, DestructiveMigration: true)));
    [Fact] public void S_secrets() => Assert.Equal(Rank.S, R(new ChangeFacts(1, Secrets: true)));
    [Fact] public void S_payment() => Assert.Equal(Rank.S, R(new ChangeFacts(1, Payment: true)));
    [Fact] public void S_prod_deploy() => Assert.Equal(Rank.S, R(new ChangeFacts(1, ProdDeploy: true)));
    [Fact] public void Highest_wins() => Assert.Equal(Rank.S, R(new ChangeFacts(1, CrossService: true, BreakingChange: true, Payment: true)));

    [Fact]
    public void Reasons_are_never_empty()
    {
        Assert.NotEmpty(RankPolicy.Assess(new ChangeFacts(1, LogicChange: false), NoTags, NoPaths).Reasons);
        Assert.NotEmpty(RankPolicy.Assess(new ChangeFacts(1, BreakingChange: true), NoTags, NoPaths).Reasons);
    }

    [Fact]
    public void Risk_tag_raises_minimum()
    {
        RiskTagRule[] tags = [new("auth", Rank.A, ["src/**/Auth/**"]), new("payment", Rank.S, ["src/**/Payments/**"])];
        var d = RankPolicy.Assess(new ChangeFacts(1, 1, true, true), tags, ["src/Api/Auth/Login.cs"]);
        Assert.Equal(Rank.A, d.Rank);
        Assert.Contains(d.Reasons, r => r.Contains("auth", StringComparison.Ordinal));
        Assert.Equal(Rank.S, RankPolicy.Assess(new ChangeFacts(1, 1, true, true), tags, ["src/Api/Auth/Login.cs", "src/X/Payments/Pay.cs"]).Rank);
    }

    [Fact]
    public void Risk_tag_never_lowers_and_ignores_non_matching()
    {
        RiskTagRule[] tags = [new("migration", Rank.B, ["**/Migrations/**"])];
        Assert.Equal(Rank.A, RankPolicy.Assess(new ChangeFacts(1, BreakingChange: true), tags, ["a/Migrations/x.cs"]).Rank);
        Assert.Equal(Rank.D, RankPolicy.Assess(new ChangeFacts(1, 1, true, true), tags, ["a/b.cs"]).Rank);
    }

    [Fact]
    public void Override_needs_reason_and_records_it()
    {
        var d = new RankDecision(Rank.C, ["x"]);
        Assert.ThrowsAny<Exception>(() => RankPolicy.Override(d, Rank.B, ""));
        Assert.ThrowsAny<Exception>(() => RankPolicy.Override(d, Rank.B, "   "));
        var o = RankPolicy.Override(d, Rank.B, "Guild Master: public contract");
        Assert.Equal(Rank.B, o.Rank);
        Assert.Contains(o.Reasons, r => r.Contains("public contract", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Rank.E, MergeRule.AutoMergeWhenCiGreenAndWardenApproved, false)]
    [InlineData(Rank.D, MergeRule.AutoMergeWhenCiGreenAndWardenApproved, true)]
    [InlineData(Rank.C, MergeRule.AutoMergeWhenCiGreenAndWardenApproved, true)]
    [InlineData(Rank.B, MergeRule.AutoMergeWhenCiGreenAndWardenApproved, true)]
    [InlineData(Rank.A, MergeRule.AutoMergeWhenCiGreenAndWardenApproved, true)]
    [InlineData(Rank.S, MergeRule.EachMutationNeedsApproval, true)]
    public void Governance(Rank rank, MergeRule rule, bool warden)
    {
        Assert.Equal(rule, RankGovernance.For(rank));
        Assert.Equal(warden, RankGovernance.WardenRequired(rank));
    }

    [Property]
    public bool Tag_never_lowers_rank(bool breaking, bool cross, int tagRank)
    {
        var facts = new ChangeFacts(1, 1, true, true, BreakingChange: breaking, CrossService: cross);
        var baseRank = R(facts);
        var tag = new RiskTagRule("t", (Rank)(Math.Abs(tagRank % 6)), ["**"]);
        var with = RankPolicy.Assess(facts, [tag], ["a/b"]).Rank;
        return with >= baseRank && with >= tag.MinRank;
    }
}
