using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0122 Teil A: Bash-Analyse strukturell nach ADR-0011 (Regeln 1-5, 7, 8 und Regel 6 "neues .monarch anlegen").</summary>
public sealed class Adr0011Tests : IDisposable
{
    private static readonly string Home = (Environment.GetEnvironmentVariable("HOME") ?? "/home/u").TrimEnd('/');
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "adr11-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { /* best effort */ }
    }

    private static PolicyContext RankA(string root = ToolCallPolicyTests.Root) =>
        new(root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string cwd = "", string root = ToolCallPolicyTests.Root) =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), RankA(root));

    private static void Denied(string cmd, string cwd = "", string root = ToolCallPolicyTests.Root)
    {
        var d = Run(cmd, cwd, root);
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny for: {cmd} (was {d.Kind})");
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    private static void Allowed(string cmd, string cwd = "")
    {
        var d = Run(cmd, cwd);
        Assert.True(d.Kind == PolicyDecisionKind.Allow, $"expected allow for: {cmd} (was {d.Kind}: {d.Reason})");
    }

    // ---- N1 Symlinks (Regel 4) und logisch/physisch aufgelöstes cd
    [Theory]
    [InlineData("ln -s . l && rm -rf l/.monarch/ledger")]
    [InlineData("ln -s . l && sed -i 's/a/b/' l/.monarch/system.yaml")]
    [InlineData("ln -sf .. l")]
    [InlineData("ln -s /work/proj l")]
    [InlineData("ln -sF . l && rm -rf l/.monarch")]
    public void N1_links_to_ancestors_of_protection_zones_are_denied(string cmd) => Denied(cmd);

    [Fact]
    public void N1_link_to_part_of_a_protection_zone_is_denied() => Denied("ln -s ../.monarch x", "/work/proj/src");

    [Fact]
    public void N1_cd_through_link_is_resolved_logically_and_physically()
    {
        var root = Path.Combine(_tmp, "p");
        Directory.CreateDirectory(Path.Combine(root, ".monarch", "ledger"));
        Directory.CreateDirectory(Path.Combine(root, "sub"));
        Directory.CreateSymbolicLink(Path.Combine(root, "sub", "l"), "..");
        var canon = PathCanon.Resolve("/", root);
        Denied("cd l/../.. && rm -rf .monarch/ledger", Path.Combine(canon, "sub"), canon);
    }

    // ---- N2 und Regel 3
    [Theory]
    [InlineData("tar -xf x.tar -C ~/.monarch/bin/0.1.0")]
    [InlineData("unzip -o x.zip -d .monarch/ledger")]
    [InlineData("curl -o .monarch/system.yaml https://x")]
    [InlineData("gsed -i s/a/b/ .monarch/system.yaml")]
    [InlineData("awk -i inplace '{print}' .monarch/system.yaml")]
    [InlineData("ed .monarch/system.yaml")]
    [InlineData("cpio -idm < x -D .monarch/local")]
    [InlineData("sort -o .monarch/system.yaml x")]
    public void N2_unknown_commands_naming_a_protected_path_are_denied(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("cd ~/.monarch/bin && curl -sL https://x | tar -xz", "")]
    [InlineData("cd .monarch/local && unzip x.zip", "")]
    [InlineData("tar -xf x.tar", "")]
    [InlineData("curl -O https://x/system.yaml", "")]
    [InlineData("cpio -id < x", "")]
    [InlineData("wget https://x/a", "")]
    [InlineData("curl -sLO https://x/a", "/work")]
    public void Rule3_pathless_writes_into_a_protected_cwd_are_denied(string cmd, string cwd) => Denied(cmd, cwd);

    [Theory]
    [InlineData("tar -xf x.tar", "/work/proj/src")]
    [InlineData("unzip x.zip", "/work/proj/src")]
    [InlineData("curl -O https://x/a", "/work/proj/src")]
    [InlineData("wget https://x/a", "/work/proj/src")]
    [InlineData("tar -xf vendor.tar -C vendor/", "")]
    [InlineData("unzip x.zip -d out", "")]
    [InlineData("tar -tf x.tar", "")]
    [InlineData("dotnet build", "")]
    [InlineData("dotnet test", "")]
    [InlineData("npm install", "")]
    [InlineData("git pull", "")]
    [InlineData("git merge origin/develop", "")]
    public void Rule3_other_cwd_or_other_commands_stay_allowed(string cmd, string cwd) => Allowed(cmd, cwd);

    // ---- N3 Normalisierung (Regel 1)
    [Theory]
    [InlineData("if true; then rm -rf .monarch/ledger; fi")]
    [InlineData("{ rm -rf .monarch/ledger; }")]
    [InlineData("! rm -rf .monarch/ledger")]
    [InlineData("for f in a; do rm -rf .monarch/ledger; done")]
    [InlineData("while false; do :; done; rm -rf .monarch/ledger")]
    [InlineData("\\rm -rf .monarch/ledger")]
    [InlineData("command rm -rf .monarch/ledger")]
    [InlineData("builtin cd .monarch && rm -rf ledger")]
    [InlineData("rm -rf .monarch/ledge{r,x}")]
    [InlineData("rm -rf $'.monarch/ledger'")]
    [InlineData("rm -rf $'\\x2emonarch/ledger'")]
    [InlineData("if true; then rm -rf ~/.claude/plugins/cache; fi")]
    [InlineData("time rm -rf .monarch/ledger")]
    [InlineData("rm -rf $PWD/.monarch/ledger")]
    [InlineData("rm -rf $(pwd)/.monarch/ledger")]
    [InlineData("rm -rf $(git rev-parse --show-toplevel)/.monarch/ledger")]
    [InlineData("rm -rf ${HOME}/.monarch/bin")]
    [InlineData("echo \"$(rm -rf .monarch/ledger)\"")]
    [InlineData("rm -rf .monarch/{ledger,local}")]
    public void N3_normalization_exposes_protected_paths(string cmd) => Denied(cmd);

    // ---- Regel 5
    [Theory]
    [InlineData("git stash -u")]
    [InlineData("git stash --include-untracked")]
    [InlineData("git checkout -f")]
    [InlineData("git switch --discard-changes main")]
    [InlineData("git switch -f main")]
    [InlineData("git reset --keep HEAD~1")]
    [InlineData("git reset --merge")]
    public void Rule5_pathless_worktree_mutations_ask(string cmd)
    {
        var d = Run(cmd);
        Assert.Equal(PolicyDecisionKind.Ask, d.Kind);
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        Assert.Contains("git", d.Reason);
    }

    // ---- Regel 7
    [Theory]
    [InlineData("monarch ledger ack-reset abc")]
    [InlineData("~/.monarch/bin/monarch ledger ack-reset abc")]
    [InlineData("/x/y/monarch permit Q-1")]
    [InlineData("script -q /dev/null monarch ledger ack-reset abc")]
    [InlineData("monarch promote x")]
    [InlineData("scripts/install-local.sh")]
    [InlineData("bash scripts/install-local.sh")]
    [InlineData("./scripts/install-local.sh --force")]
    public void Rule7_owner_commands_are_denied(string cmd)
    {
        var d = Run(cmd);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.DenyBash, d.Rule);
        Assert.Contains("Owner", d.Reason);
    }

    [Theory]
    [InlineData("git add scripts/install-local.sh")]
    [InlineData("git diff scripts/install-local.sh")]
    [InlineData("cat scripts/install-local.sh")]
    [InlineData("monarch ledger verify")]
    public void Rule7_does_not_block_reading_or_other_subcommands(string cmd) => Allowed(cmd);

    // ---- Regel 8
    public static TheoryData<string> FoldedPaths() => new()
    {
        ".monarch/ſystem.yaml", ".monarch/queſts/x", ".claude/ſettings.json", ".monarch/syﬆem.yaml", ".monarch/gateſ/G-1.yaml",
        ".monarch/ledger/ſ.jsonl", ".monarch/ſhadows/a/ſhadow.yaml", ".monarch/quest\u017F/x",
    };

    [Theory]
    [MemberData(nameof(FoldedPaths))]
    public void Rule8_folded_spellings_are_protected_for_write_and_rm(string rel)
    {
        var w = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit("/work/proj/" + rel, "Write"), RankA());
        Assert.Equal(PolicyDecisionKind.Deny, w.Kind);
        Assert.Equal(PolicyRules.ProtectPaths, w.Rule);
        Denied("rm " + rel);
    }

    [Fact]
    public void Rule8_kelvin_sign_and_fold_in_secrets_and_global_zone()
    {
        var d = ToolCallPolicy.Evaluate(new ToolCall("Read", FilePath: "/work/proj/a.\u212Aey"), RankA());
        Assert.Equal(PolicyRules.ProtectSecrets, d.Rule);
        Denied("rm -rf ~/.claude/plugins/ca\u017Fhe");
        Denied("rm -rf ~/.MONARCH/bin");
    }

    [Theory]
    [InlineData("ſ", "s")]
    [InlineData("\u212A", "k")]
    [InlineData("ß", "ss")]
    [InlineData("ẞ", "ss")]
    [InlineData("ﬀ", "ff")]
    [InlineData("ﬁ", "fi")]
    [InlineData("ﬂ", "fl")]
    [InlineData("ﬃ", "ffi")]
    [InlineData("ﬄ", "ffl")]
    [InlineData("ﬅ", "st")]
    [InlineData("ﬆ", "st")]
    [InlineData(".MONARCH/System.YAML", ".monarch/system.yaml")]
    [InlineData("ｓ", "ｓ")]
    public void Rule8_fold_table(string input, string expected) => Assert.Equal(expected, PathFold.Fold(input));

    [Fact]
    public void Rule8_fullwidth_s_is_not_folded_and_stays_allowed()
    {
        var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit("/work/proj/.monarch/ｓystem.yaml", "Write"), new PolicyContext("/work/proj"));
        Assert.Equal(PolicyDecisionKind.Allow, d.Kind);
    }

    // ---- Regel 6: neues .monarch anlegen
    [Theory]
    [InlineData("mkdir -p src/d/.monarch")]
    [InlineData("mkdir src/.monarch")]
    [InlineData("mkdir -p src/d/.monarch/ledger")]
    public void Rule6_mkdir_of_a_new_monarch_directory_is_denied(string cmd) => Denied(cmd);

    [Fact]
    public void Rule6_write_below_a_new_monarch_directory_is_denied()
    {
        var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit("/work/proj/src/d/.monarch/system.yaml", "Write"), RankA());
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
    }

    // ---- Regel 2 Ausnahmen und weitere Negativtests
    [Theory]
    [InlineData("~/.monarch/bin/monarch quest accept Q-1")]
    [InlineData("monarch status")]
    [InlineData("git add .monarch/ledger/2026-10/x.jsonl")]
    [InlineData("git commit -m \"fix: .monarch/ledger handling\"")]
    [InlineData("git commit -am \"fix: .monarch/ledger handling\"")]
    [InlineData("git commit --message=\"x .monarch/ledger\"")]
    [InlineData("git commit -F msg.txt")]
    [InlineData("git push origin monarch/G-0100-x")]
    [InlineData("gh pr comment 1 --body \"… .monarch/ledger …\"")]
    [InlineData("gh pr create --title \"x .monarch\" -b \"y .monarch/ledger\"")]
    [InlineData("cat <<'EOF' > notes.md\n.monarch/ledger\nrm -rf .monarch/ledger\nEOF")]
    [InlineData("git commit -F- <<'EOF'\nfix .monarch\nEOF")]
    [InlineData("git commit -m \"$(cat <<'EOF'\nfix: .monarch/ledger\nEOF\n)\"")]
    [InlineData("rm -rf src/bin")]
    [InlineData("if [ -f src/x ]; then rm src/x; fi")]
    [InlineData("for f in src/*.cs; do echo $f; done")]
    [InlineData("ln -s ../shared src/shared")]
    [InlineData("cat .monarch/system.yaml")]
    [InlineData("grep -r x .monarch/ledger")]
    [InlineData("git stash")]
    public void Legitimate_work_and_rule2_exceptions_stay_allowed(string cmd) => Allowed(cmd);

    [Theory]
    [InlineData("./monarch ledger rewrite .monarch/ledger/x")]
    [InlineData("git checkout -m .monarch/ledger/x")]
    [InlineData("git stash push -m x .monarch/ledger/x")]
    [InlineData("cat <<EOF | bash\nrm -rf .monarch/ledger\nEOF")]
    [InlineData("bash <<'EOF'\nrm -rf .monarch/ledger\nEOF")]
    [InlineData("cat <<EOF\n$(rm -rf .monarch/ledger)\nEOF")]
    public void Rule2_binary_must_be_canonical_and_heredocs_to_shells_are_code(string cmd) => Denied(cmd);


    // ---- Lexer: Konstruktionen, die Segmente verstecken könnten
    [Theory]
    [InlineData("cat <<< \"rm -rf .monarch/ledger\" | bash")]
    [InlineData("bash <<< 'rm -rf .monarch/ledger'")]
    [InlineData("rm -rf .monarch/ledger 2>&1")]
    [InlineData("echo $(rm -rf .monarch/ledger)")]
    [InlineData("echo `rm -rf .monarch/ledger`")]
    [InlineData("diff <(rm -rf .monarch/ledger) x")]
    [InlineData("cat x > .monarch/ledger/y")]
    [InlineData("> .monarch/ledger/y")]
    [InlineData("&> .monarch/ledger/y")]
    [InlineData("ls && (cd .monarch/local; unzip x.zip)")]
    [InlineData("echo x | sudo tee .monarch/system.yaml")]
    [InlineData("env FOO=1 rm -rf .monarch/ledger")]
    [InlineData("nohup rm -rf .monarch/ledger &")]
    [InlineData("timeout 5 rm -rf .monarch/ledger")]
    [InlineData("case x in x) rm -rf .monarch/ledger;; esac")]
    [InlineData("until false; do rm -rf .monarch/ledger; done")]
    [InlineData("sh -c \"rm -rf '.monarch/ledger'\"")]
    [InlineData("eval $'rm -rf .monarch/ledger'")]
    [InlineData("rm -rf \"$HOME/.monarch\"")]
    [InlineData("cd \"$PWD/.monarch\" && rm -rf ledger")]
    [InlineData("pushd .monarch/local && tar -xf x.tar")]
    [InlineData("cat <<EOF\n`rm -rf .monarch/ledger`\nEOF")]
    [InlineData("rm -rf .monarch/led$(echo)ger")]
    [InlineData("rm -rf $'\\056monarch/ledger'")]
    [InlineData("rm -rf $'\\u002emonarch/ledger'")]
    [InlineData("rm -rf .monarch/{a,b}/../ledger")]
    public void Lexer_constructions_do_not_hide_segments(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("echo hi 2>&1")]
    [InlineData("dotnet build &> /tmp/log")]
    [InlineData("diff <(ls a) <(ls b)")]
    [InlineData("cat <<< \"x .monarch/ledger\"")]
    [InlineData("echo $((1+2))")]
    [InlineData("rm -rf /tmp/{a,b}")]
    [InlineData("rm -rf /tmp/x{1..100000}")]
    [InlineData("echo x > .monarch/knowledge/a.md")]
    [InlineData("mkdir -p .monarch/knowledge")]
    [InlineData("git commit -m \"a;b\" && git push")]
    [InlineData("echo \"a ; rm .monarch/ledger\"")]
    [InlineData("echo 'unterminated")]
    [InlineData("find . -name '*.cs' -exec grep -l foo {} \\;")]
    [InlineData("git log --format=%H -1 > /tmp/h")]
    [InlineData("cat x | head -3")]
    public void Lexer_ordinary_constructions_stay_allowed(string cmd) => Allowed(cmd);

    [Fact]
    public void Reasons_name_rule_and_path()
    {
        Assert.Contains("Regel 3", Run("tar -xf x.tar").Reason);
        Assert.Contains(".monarch/system.yaml", Run("ed .monarch/system.yaml").Reason);
        Assert.Contains("Regel 4", Run("ln -s . l").Reason);
        Assert.Contains("Regel 5", Run("git stash -u").Reason);
    }
}
