using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

public class ToolCallPolicyTests
{
    internal const string Root = "/work/proj";
    internal static readonly QuestScope Scope = new(["src/**", "tests/**"], ["**/Migrations/*.Designer.cs"]);
    internal static PolicyContext Ctx(Rank? rank = null, PolicyOptions? o = null, QuestScope? scope = null) =>
        new(Root, rank is null ? null : new ActiveQuest(new QuestId("Q-1"), rank.Value, scope ?? new QuestScope(["**"], [])), false, o ?? new PolicyOptions());
    internal static ToolCall Edit(string path, string tool = "Edit", string? agent = null, string? mode = null) =>
        new(tool, FilePath: path, AgentType: agent, PermissionMode: mode);
    internal static ToolCall Bash(string cmd, string? agent = null, string? mode = null) =>
        new("Bash", Command: cmd, AgentType: agent, PermissionMode: mode);
    private static PolicyDecision Run(ToolCall c, PolicyContext? ctx = null) => ToolCallPolicy.Evaluate(c, ctx ?? Ctx());

    private static void Denied(ToolCall c, string rule, PolicyContext? ctx = null)
    {
        var d = Run(c, ctx);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(rule, d.Rule);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }
    private static void Allowed(ToolCall c, PolicyContext? ctx = null) =>
        Assert.Equal(PolicyDecisionKind.Allow, Run(c, ctx).Kind);

    // ---- glob matcher
    [Theory]
    [InlineData(".monarch/ledger/**", ".monarch/ledger/2026-10/s.jsonl", true)]
    [InlineData(".monarch/ledger/**", ".monarch/ledger", true)]
    [InlineData(".monarch/ledger/**", ".monarch/ledgerx/a", false)]
    [InlineData(".monarch/shadows/*/shadow.yaml", ".monarch/shadows/knight/shadow.yaml", true)]
    [InlineData(".monarch/shadows/*/shadow.yaml", ".monarch/shadows/a/b/shadow.yaml", false)]
    [InlineData("**/.env", ".env", true)]
    [InlineData("**/.env", "a/b/.env", true)]
    [InlineData("**/.env", "a/b/.env.local", false)]
    [InlineData("**/.env.*", "a/.env.local", true)]
    [InlineData("**/*.key", "x/y/server.key", true)]
    [InlineData("src/**/Auth/**", "src/Api/Auth/Login.cs", true)]
    [InlineData("src/**/Auth/**", "src/Auth/Login.cs", true)]
    [InlineData("src/**/Auth/**", "lib/Auth/Login.cs", false)]
    [InlineData("a?c", "abc", true)]
    [InlineData("a?c", "a/c", false)]
    [InlineData("A", "a", false)]
    [InlineData("**", "anything/at/all", true)]
    [InlineData("src/*", "src/a/b", false)]
    public void Glob_matching(string pattern, string path, bool expected) =>
        Assert.Equal(expected, GlobMatcher.IsMatch(pattern, path));

    [Fact]
    public void Glob_ancestor_and_wildcard()
    {
        Assert.True(GlobMatcher.IsMatchOrAncestor(".monarch/shadows/*/shadow.yaml", ".monarch/shadows/knight"));
        Assert.True(GlobMatcher.IsMatchOrAncestor(".monarch/ledger/**", ".monarch"));
        Assert.False(GlobMatcher.IsMatchOrAncestor(".monarch/ledger/**", "src"));
        Assert.True(GlobMatcher.WildcardMatch("git push --force*", "git push --force-with-lease origin"));
        Assert.False(GlobMatcher.WildcardMatch("git push --force*", "git push origin"));
    }

    // ---- protect_paths
    [Theory]
    [InlineData("Edit"), InlineData("Write"), InlineData("MultiEdit"), InlineData("NotebookEdit")]
    public void Protected_paths_block_all_edit_tools(string tool)
    {
        Denied(Edit(".monarch/ledger/2026-10/s.jsonl", tool), PolicyRules.ProtectPaths);
        Denied(Edit(".monarch/shadows/knight/shadow.yaml", tool), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Protected_paths_resolve_absolute_dotdot_and_backslash()
    {
        Denied(Edit(Root + "/.monarch/ledger/a.jsonl"), PolicyRules.ProtectPaths);
        Denied(Edit("src/../.monarch/ledger/a.jsonl"), PolicyRules.ProtectPaths);
        Denied(Edit("./.monarch/ledger/a.jsonl"), PolicyRules.ProtectPaths);
        Denied(Edit(".monarch\\ledger\\a.jsonl"), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Other_monarch_files_are_not_protected() =>
        Allowed(Edit(".monarch/shadows/knight/notes.md"));

    [Theory]
    [InlineData("echo x >> .monarch/ledger/a.jsonl")]
    [InlineData("echo x>.monarch/ledger/a.jsonl")]
    [InlineData("rm -rf .monarch/ledger")]
    [InlineData("mv .monarch/ledger/a.jsonl /tmp/")]
    [InlineData("cp /tmp/x .monarch/ledger/a.jsonl")]
    [InlineData("sed -i s/a/b/ .monarch/ledger/a.jsonl")]
    [InlineData("echo x | tee .monarch/ledger/a.jsonl")]
    [InlineData("truncate -s 0 .monarch/ledger/a.jsonl")]
    [InlineData("dd if=/dev/zero of=.monarch/ledger/a.jsonl")]
    [InlineData("chmod 777 .monarch/shadows/k/shadow.yaml")]
    [InlineData("ln -sf /tmp/x .monarch/shadows/k/shadow.yaml")]
    [InlineData("git checkout -- .monarch/ledger/a.jsonl")]
    [InlineData("git restore .monarch/ledger/a.jsonl")]
    [InlineData("rm /work/proj/.monarch/ledger/a.jsonl")]
    public void Bash_mutation_of_protected_paths_is_denied(string cmd) =>
        Denied(Bash(cmd), PolicyRules.ProtectPaths);

    [Theory]
    [InlineData("cat .monarch/ledger/a.jsonl")]
    [InlineData("ls .monarch/ledger 2>&1")]
    [InlineData("grep foo .monarch/ledger/a.jsonl | head")]
    [InlineData("rm -rf obj bin")]
    public void Bash_reading_protected_paths_is_allowed(string cmd) => Allowed(Bash(cmd));

    // ---- protect_secrets
    [Theory]
    [InlineData(".env"), InlineData("config/.env.local"), InlineData("/home/u/app/.env"), InlineData("certs/server.key")]
    [InlineData("AuthKey_X.p8"), InlineData("cert.pfx"), InlineData("infra/keycloak.env"), InlineData("/home/u/.netrc")]
    [InlineData("/home/u/.ssh/id_rsa"), InlineData("/home/u/.ssh/id_rsa.pub")]
    public void Read_of_secrets_is_denied(string path) =>
        Denied(new ToolCall("Read", FilePath: path), PolicyRules.ProtectSecrets);

    [Theory]
    [InlineData(".env.example"), InlineData("app/.env.example"), InlineData("src/Program.cs"), InlineData("README.md")]
    public void Read_of_non_secrets_is_allowed(string path) => Allowed(new ToolCall("Read", FilePath: path));

    [Fact]
    public void Extra_secret_globs_apply() =>
        Denied(new ToolCall("Read", FilePath: "vault/token.txt"), PolicyRules.ProtectSecrets,
            Ctx(o: new PolicyOptions { ExtraSecretGlobs = ["vault/**"] }));

    [Theory]
    [InlineData("cat .env"), InlineData("head -n1 config/.env.local"), InlineData("grep PASSWORD .env"), InlineData("rg token keycloak.env")]
    [InlineData("awk '{print}' .env"), InlineData("sed -n 1p .env"), InlineData("jq . a.p8"), InlineData("strings x.pfx")]
    [InlineData("xxd server.key"), InlineData("base64 .env"), InlineData("od -c .env"), InlineData("source .env"), InlineData(". .env")]
    [InlineData("less .env"), InlineData("tail -f .env"), InlineData("more .env"), InlineData("FOO=1 cat .env")]
    [InlineData("while read l; do echo $l; done < .env"), InlineData("echo a && cat /home/u/.netrc")]
    public void Bash_content_readers_on_secrets_are_denied(string cmd) =>
        Denied(Bash(cmd), PolicyRules.ProtectSecrets);

    [Theory]
    [InlineData("ls -la .env"), InlineData("stat .env"), InlineData("test -f .env"), InlineData("git check-ignore .env")]
    [InlineData("wc -c .env"), InlineData("cat .env.example"), InlineData("echo hello")]
    public void Bash_metadata_on_secrets_is_allowed(string cmd) => Allowed(Bash(cmd));

    // ---- deny_bash
    [Theory]
    [InlineData("git push --force"), InlineData("git push --force origin main"), InlineData("git push -f origin main")]
    [InlineData("git push origin main --force-with-lease"), InlineData("git push origin +main"), InlineData("git push -uf origin main")]
    [InlineData("git reset --hard HEAD~1"), InlineData("dotnet ef database drop -f"), InlineData("monarch permit abc")]
    [InlineData("monarch promote x"), InlineData("  git   reset    --hard  "), InlineData("FOO=1 BAR=2 git reset --hard")]
    [InlineData("sudo git push --force"), InlineData("env GIT_X=1 git reset --hard"), InlineData("command monarch permit x")]
    [InlineData("exec monarch promote x"), InlineData("echo a && git reset --hard"), InlineData("ls; monarch permit x")]
    [InlineData("true || git push -f"), InlineData("cat x | monarch promote"), InlineData("echo $(git reset --hard)")]
    [InlineData("echo a\ngit reset --hard"), InlineData("bash -c \"git push --force\""), InlineData("git -C sub push -f")]
    public void Deny_bash_commands_are_denied(string cmd) => Denied(Bash(cmd), PolicyRules.DenyBash);

    [Theory]
    [InlineData("git push origin main"), InlineData("git push -u origin feature"), InlineData("git reset --soft HEAD~1")]
    [InlineData("monarch status"), InlineData("dotnet ef migrations add X"), InlineData("echo \"git\" push")]
    public void Ordinary_commands_are_not_denied_by_deny_bash(string cmd) =>
        Assert.NotEqual(PolicyRules.DenyBash, Run(Bash(cmd)).Rule);

    [Fact]
    public void Deny_bash_is_configurable() =>
        Denied(Bash("terraform destroy -auto-approve"), PolicyRules.DenyBash, Ctx(o: new PolicyOptions { DenyBash = ["terraform destroy*"] }));

    // ---- shadow read-only
    [Theory]
    [InlineData("warden"), InlineData("scout")]
    public void Read_only_shadows_cannot_write(string agent)
    {
        foreach (var tool in new[] { "Edit", "Write", "MultiEdit", "NotebookEdit" })
            Denied(Edit("src/a.cs", tool, agent), PolicyRules.ShadowReadOnly);
        Denied(Bash("git commit -m x", agent), PolicyRules.ShadowReadOnly);
        Denied(Bash("git push origin main", agent), PolicyRules.ShadowReadOnly);
        Denied(Bash("git -C . merge dev", agent), PolicyRules.ShadowReadOnly);
        Allowed(Bash("git status", agent));
        Allowed(new ToolCall("Read", FilePath: "src/a.cs", AgentType: agent));
    }

    [Fact]
    public void Knight_may_write_and_config_extends_read_only_set()
    {
        Allowed(Edit("src/a.cs", agent: "knight"));
        Denied(Edit("src/a.cs", agent: "scribe"), PolicyRules.ShadowReadOnly, Ctx(o: new PolicyOptions { ReadOnlyShadowTypes = ["scribe"] }));
    }

    // ---- refuse_high_rank_in_bypass_mode
    [Theory]
    [InlineData(Rank.A, "bypassPermissions", true), InlineData(Rank.A, "dontAsk", true), InlineData(Rank.A, "acceptEdits", false)]
    [InlineData(Rank.A, "auto", false), InlineData(Rank.A, "default", false), InlineData(Rank.B, "bypassPermissions", false)]
    [InlineData(Rank.S, "bypassPermissions", true), InlineData(Rank.S, "dontAsk", true), InlineData(Rank.S, "auto", true)]
    [InlineData(Rank.S, "acceptEdits", true), InlineData(Rank.S, "plan", false)]
    public void High_rank_in_permissive_modes(Rank rank, string mode, bool denied)
    {
        var d = Run(Edit("src/a.cs", mode: mode), Ctx(rank));
        Assert.Equal(denied, d.Kind == PolicyDecisionKind.Deny && d.Rule == PolicyRules.RefuseHighRankInBypassMode);
    }

    [Fact]
    public void Unknown_or_missing_mode_is_default_and_flagged()
    {
        var d = Run(Edit("src/a.cs", mode: "weird"), Ctx(Rank.A));
        Assert.Equal(PolicyDecisionKind.Allow, d.Kind);
        Assert.Contains(PolicySignals.UnknownPermissionMode, d.Signals);
        Assert.Equal(PolicyDecisionKind.Allow, Run(Edit("src/a.cs"), Ctx(Rank.A)).Kind);
    }

    [Fact]
    public void Bypass_rule_ignores_reads_and_can_be_disabled()
    {
        Allowed(new ToolCall("Read", FilePath: "src/a.cs", PermissionMode: "bypassPermissions"), Ctx(Rank.A));
        Allowed(Edit("src/a.cs", mode: "bypassPermissions"), Ctx(Rank.A, new PolicyOptions { RefuseHighRankInBypassMode = false }));
        Allowed(Bash("ls", mode: "bypassPermissions"), Ctx(Rank.A));
        Denied(Bash("dotnet build", mode: "bypassPermissions"), PolicyRules.RefuseHighRankInBypassMode, Ctx(Rank.A));
    }

    // ---- scope lock
    [Fact]
    public void Scope_lock_allows_inside_and_denies_outside_with_signal()
    {
        var ctx = Ctx(Rank.C, scope: Scope);
        Allowed(Edit("src/Matching/A.cs"), ctx);
        Allowed(Edit(Root + "/tests/X/T.cs", "Write"), ctx);
        var d = Run(Edit("docs/readme.md"), ctx);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ScopeLock, d.Rule);
        Assert.Contains(PolicySignals.DoubleDungeon, d.Signals);
        Assert.Contains(PolicySignals.DoubleDungeon, Run(Edit("src/Db/Migrations/1.Designer.cs"), ctx).Signals);
        Assert.Contains(PolicySignals.DoubleDungeon, Run(Edit("../other/src/a.cs"), ctx).Signals);
        Assert.Contains(PolicySignals.DoubleDungeon, Run(Edit("/etc/hosts"), ctx).Signals);
        Assert.Contains(PolicySignals.DoubleDungeon, Run(Edit("src\\..\\docs\\a.md"), ctx).Signals);
    }

    [Fact]
    public void Scope_lock_exemptions_options_and_non_applicability()
    {
        var ctx = Ctx(Rank.C, scope: Scope);
        Allowed(Edit(".monarch/recap/q-1.md"), ctx);
        Allowed(Edit(".monarch/knowledge/n.md", "Write"), ctx);
        Allowed(Edit("docs/readme.md"), Ctx());
        Allowed(Edit("docs/readme.md"), Ctx(Rank.C, new PolicyOptions { ScopeLock = false }, Scope));
        Allowed(new ToolCall("Read", FilePath: "docs/readme.md"), ctx);
        Denied(Edit("a.cs"), PolicyRules.ScopeLock, Ctx(Rank.C, scope: QuestScope.Empty));
    }

    // ---- rank S
    [Fact]
    public void Rank_S_asks_on_mutation_only()
    {
        var d = Run(Edit("src/a.cs"), Ctx(Rank.S));
        Assert.Equal(PolicyDecisionKind.Ask, d.Kind);
        Assert.Equal(PolicyRules.RankSApproval, d.Rule);
        Assert.Equal("Rank S: Freigabe nötig. Stoppe diesen Schritt und melde ihn dem Guild Master.", d.Reason);
        Assert.Equal(PolicyDecisionKind.Ask, Run(Bash("dotnet build"), Ctx(Rank.S)).Kind);
        Allowed(Bash("ls -la"), Ctx(Rank.S));
        Allowed(new ToolCall("Read", FilePath: "src/a.cs"), Ctx(Rank.S));
        Allowed(Edit("src/a.cs"), Ctx(Rank.A));
    }

    // ---- combining
    [Fact]
    public void Deny_beats_ask_beats_allow()
    {
        var d = Run(Edit("src/a.cs", mode: "bypassPermissions"), Ctx(Rank.S));
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.RefuseHighRankInBypassMode, d.Rule);
        var protectedS = Run(Edit(".monarch/ledger/a"), Ctx(Rank.S, scope: Scope));
        Assert.Equal(PolicyDecisionKind.Deny, protectedS.Kind);
        Assert.Contains(PolicySignals.DoubleDungeon, protectedS.Signals); // signals of all rules are kept
        Assert.Equal(PolicyDecisionKind.Allow, ToolCallPolicy.Combine([]).Kind);
    }

    // ---- mutating heuristic
    [Theory]
    [InlineData("Edit", null, true), InlineData("Write", null, true), InlineData("MultiEdit", null, true), InlineData("NotebookEdit", null, true)]
    [InlineData("Read", null, false), InlineData("Grep", null, false), InlineData("Glob", null, false)]
    [InlineData("Bash", "ls -la", false), InlineData("Bash", "cat a | grep b", false), InlineData("Bash", "git status", false)]
    [InlineData("Bash", "git log --oneline", false), InlineData("Bash", "echo hi 2>&1", false), InlineData("Bash", "find . -name '*.cs'", false)]
    [InlineData("Bash", "echo hi > a.txt", true), InlineData("Bash", "dotnet build", true), InlineData("Bash", "rm a", true)]
    [InlineData("Bash", "find . -delete", true), InlineData("Bash", "sed -i s/a/b/ f", true), InlineData("Bash", "ls && touch x", true)]
    [InlineData("Bash", "git commit -m x", true), InlineData("Bash", null, true), InlineData("Bash", "", true)]
    public void Mutating_heuristic(string tool, string? cmd, bool expected) =>
        Assert.Equal(expected, ToolCallPolicy.IsMutating(new ToolCall(tool, Command: cmd)));

    // ---- penalty zone
    [Theory]
    [InlineData(QuestKind.Main, false, PenaltyMode.Block, true), InlineData(QuestKind.Main, true, PenaltyMode.Block, false)]
    [InlineData(QuestKind.Side, true, PenaltyMode.Block, false), InlineData(QuestKind.Main, true, PenaltyMode.Warn, true)]
    [InlineData(QuestKind.Side, true, PenaltyMode.Warn, true), InlineData(QuestKind.Penalty, true, PenaltyMode.Block, true)]
    [InlineData(QuestKind.Emergency, true, PenaltyMode.Block, true), InlineData(QuestKind.Daily, true, PenaltyMode.Block, true)]
    [InlineData(QuestKind.Hidden, true, PenaltyMode.Block, true)]
    public void Quest_acceptance(QuestKind kind, bool zone, PenaltyMode mode, bool expected) =>
        Assert.Equal(expected, QuestAcceptancePolicy.CanAccept(kind, zone, mode));
}
