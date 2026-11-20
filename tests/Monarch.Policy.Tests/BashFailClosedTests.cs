using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0118: Bash fail-closed um geschützte Pfade und globale Schutzpfade (Selbstschutz). Jede Zeile 1-9 des Warden-Befunds hat Tests.</summary>
public class BashFailClosedTests
{
    private static readonly string Home = (Environment.GetEnvironmentVariable("HOME") ?? "/home/u").TrimEnd('/');

    private static PolicyContext RankA() =>
        new(ToolCallPolicyTests.Root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string cwd = "") =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), RankA());

    private static void Denied(string cmd, string cwd = "")
    {
        var d = Run(cmd, cwd);
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    private static void Allowed(string cmd, string cwd = "") => Assert.Equal(PolicyDecisionKind.Allow, Run(cmd, cwd).Kind);

    // ---- 1. Interpreter mit Inline-Code
    [Theory]
    [InlineData("python3 -c \"import os; os.remove('.monarch/local/ledger-heads.json')\"")]
    [InlineData("python -c \"open('.monarch/ledger/x','w')\"")]
    [InlineData("perl -i -pe 's/a/b/' .monarch/ledger/x.jsonl")]
    [InlineData("perl -e 'unlink q(.monarch/local/ledger-heads.json)'")]
    [InlineData("ruby -e \"File.delete('.monarch/local/ledger-heads.json')\"")]
    [InlineData("node -e \"require('fs').unlinkSync('.monarch/local/ledger-heads.json')\"")]
    [InlineData("python3 -c \"import os; os.remove('.mon' + 'arch/local/x')\"")]
    [InlineData("python3 -c \"import os; os.remove('.MONARCH/local/x')\"")]
    [InlineData("python3 -c \"import os; os.remove(os.path.join('.mon', 'arch', 'x'))\"")]
    [InlineData("python3 - <<'EOF'\nimport os\nos.remove('.monarch/local/x')\nEOF")]
    [InlineData("echo \"import os; os.remove('.monarch/local/x')\" | python3")]
    [InlineData("python3 -c \"open(os.path.expanduser('~/.claude/settings.json'),'w')\"")]
    public void Interpreter_inline_code_mentioning_monarch_is_denied(string cmd) => Denied(cmd);

    // ---- 2. find -delete / -exec
    [Theory]
    [InlineData("find .monarch -name '*.jsonl' -delete")]
    [InlineData("find .monarch/ledger -type f -exec rm {} \\;")]
    [InlineData("find . -name '*.jsonl' -delete")]
    [InlineData("find . -name x -exec rm -f {} +")]
    [InlineData("find .monarch -execdir rm {} \\;")]
    [InlineData("find ~/.monarch -delete")]
    public void Find_delete_or_exec_below_protected_paths_is_denied(string cmd) => Denied(cmd);

    // ---- 3. git-Mutationen
    [Theory]
    [InlineData("git rm .monarch/ledger/2026-10/s.jsonl")]
    [InlineData("git rm -r --cached .monarch/ledger")]
    [InlineData("git clean -fdx")]
    [InlineData("git clean -xdf")]
    [InlineData("git clean -fd -x")]
    [InlineData("git clean -fdX")]
    [InlineData("git clean -f -d --force -x")]
    [InlineData("git checkout HEAD -- .monarch/ledger/2026-10/s.jsonl")]
    [InlineData("git checkout HEAD .monarch/ledger/x")]
    [InlineData("git restore --source=HEAD~1 .monarch/ledger/x")]
    [InlineData("git restore .monarch/local/ledger-heads.json")]
    [InlineData("git stash push .monarch/ledger/x")]
    [InlineData("git stash -a")]
    [InlineData("git reset HEAD .monarch/ledger/x")]
    [InlineData("git mv .monarch/ledger/a .monarch/ledger/b")]
    [InlineData("git update-index --assume-unchanged .monarch/ledger/x")]
    [InlineData("git -C . rm .monarch/ledger/x")]
    public void Git_mutations_of_protected_paths_are_denied(string cmd) => Denied(cmd);

    // git clean ohne Pfadangabe räumt die ganze Root, also auch noch nicht committete Ledger-Dateien (untracked, nicht ignoriert).
    [Theory]
    [InlineData("git clean -fd")]
    [InlineData("git clean -f")]
    [InlineData("git clean --force -d")]
    [InlineData("git clean -fd -e '*.log'")]
    [InlineData("git clean -fd --exclude=bin")]
    [InlineData("git clean -fd .")]
    [InlineData("git clean -fd -- .monarch")]
    public void Git_clean_of_the_whole_tree_is_denied(string cmd) => Denied(cmd);

    // ---- 4. Quote-Splitting
    [Theory]
    [InlineData("rm .mon\"\"arch/ledger/x")]
    [InlineData("rm '.mon'arch/ledger/x")]
    [InlineData("rm \".mon\"\"arch\"/local/ledger-heads.json")]
    [InlineData("rm .mon\\arch/ledger/x")]
    [InlineData("echo x > .mon\"\"arch/ledger/x")]
    [InlineData("eval \"rm .mon\"\"arch/ledger/x\"")]
    public void Quote_splitting_does_not_hide_protected_paths(string cmd) => Denied(cmd);

    // ---- 5. Globs
    [Theory]
    [InlineData("rm .monarch/l*/*/*.jsonl")]
    [InlineData("rm -rf .m*")]
    [InlineData("rm -rf .monarch/*")]
    [InlineData("rm -rf .mon?rch/ledger")]
    [InlineData("rm .monarch/led[g]er/x")]
    [InlineData("rm .monarch/shadows/*/shadow.yaml")]
    [InlineData("rm .claude/settings*.json")]
    [InlineData("rm -rf .*")]
    [InlineData("mv .mon* /tmp/x")]
    public void Glob_tokens_with_protected_prefix_are_denied(string cmd) => Denied(cmd);

    // ---- 6. Ziele, die einen geschützten Pfad enthalten
    [Theory]
    [InlineData("rm -rf .monarch", "")]
    [InlineData("rm -rf .", "")]
    [InlineData("rm -rf ./", "")]
    [InlineData("rm -rf ..", "/work/proj/src")]
    [InlineData("rm -rf ../", "/work/proj/src")]
    [InlineData("rm -rf /work/proj", "")]
    [InlineData("rm -rf /work", "")]
    [InlineData("rm -rf /", "")]
    [InlineData("mv .monarch x", "")]
    [InlineData("mv . /tmp/x", "")]
    [InlineData("rmdir .monarch", "")]
    [InlineData("cd src && rm -rf ..", "")]
    [InlineData("rsync -a --delete /tmp/empty/ ./", "")]
    [InlineData("cd .monarch && rm -rf *", "")]
    [InlineData("cd .monarch && rm -rf .", "")]
    [InlineData("echo .monarch | xargs rm -rf", "")]
    [InlineData("git checkout .", "")]
    [InlineData("git rm -r .", "")]
    [InlineData("chmod -R 000 .", "")]
    public void Ancestor_targets_of_protected_paths_are_denied(string cmd, string cwd) => Denied(cmd, cwd);

    // ---- 7. Binary und globale Pfade
    [Theory]
    [InlineData("rm -f ~/.monarch/bin/0.1.0/monarch")]
    [InlineData("rm -rf ~/.monarch")]
    [InlineData("rm -rf $HOME/.monarch/bin")]
    [InlineData("mv ~/.monarch/bin/0.1.0/monarch /tmp/m")]
    [InlineData("cp /tmp/evil ~/.monarch/bin/0.1.0/monarch")]
    [InlineData("chmod 000 ~/.monarch/bin/0.1.0/monarch")]
    [InlineData("echo x > ~/.monarch/bin/0.1.0/SHA256SUMS")]
    [InlineData("find ~/.monarch -name monarch -delete")]
    [InlineData("rm -rf ~")]
    public void Global_monarch_home_is_protected_from_mutation(string cmd) => Denied(cmd);

    [Fact]
    public void Global_monarch_home_is_protected_for_file_tools()
    {
        foreach (var tool in new[] { "Edit", "Write", "MultiEdit" })
        {
            var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(Home + "/.monarch/bin/0.1.0/monarch", tool), RankA());
            Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
            Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        }
    }

    // ---- 8. Konfiguration im Projekt
    [Theory]
    [InlineData(".monarch/system.yaml")]
    [InlineData(".monarch/quests/Q-0001.yaml")]
    [InlineData(".monarch/gates/G-0100.yaml")]
    public void System_quest_and_gate_files_are_protected_for_file_tools(string path)
    {
        foreach (var tool in new[] { "Edit", "Write", "MultiEdit" })
        {
            var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(path, tool), RankA());
            Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
            Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        }
    }

    [Theory]
    [InlineData("sed -i 's/scope_lock: true/scope_lock: false/' .monarch/system.yaml")]
    [InlineData("sed -i '' 's/true/false/' .monarch/system.yaml")]
    [InlineData("sed -i 's/src/**/x/' .monarch/quests/Q-0001.yaml")]
    [InlineData("echo 'read_only_shadows: []' >> .monarch/system.yaml")]
    [InlineData("rm .monarch/gates/G-0100.yaml")]
    [InlineData("tee .monarch/quests/Q-1.yaml < /tmp/x")]
    public void System_quest_and_gate_files_are_protected_from_bash(string cmd) => Denied(cmd);

    [Fact]
    public void Default_protect_paths_are_extended()
    {
        var p = new PolicyOptions().ProtectPaths;
        Assert.Contains(".monarch/system.yaml", p);
        Assert.Contains(".monarch/quests/**", p);
        Assert.Contains(".monarch/gates/**", p);
        Assert.Contains(".claude/settings*.json", p);
    }

    // ---- 9. Hook-Konfiguration
    [Theory]
    [InlineData("/.claude/settings.json")]
    [InlineData("/.claude/settings.local.json")]
    [InlineData("/.claude/plugins/installed_plugins.json")]
    [InlineData("/.claude/plugins/cache/monarch/hooks/hooks.json")]
    public void Global_claude_config_is_protected_for_file_tools(string suffix)
    {
        foreach (var tool in new[] { "Edit", "Write", "MultiEdit" })
        {
            var d = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(Home + suffix, tool), RankA());
            Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
            Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
        }
    }

    [Theory]
    [InlineData("/.claude/settings.json")]
    [InlineData("/.monarch/bin/0.1.0/monarch")]
    public void Global_paths_can_still_be_read_with_file_tools(string suffix) =>
        Assert.Equal(PolicyDecisionKind.Allow, ToolCallPolicy.Evaluate(new ToolCall("Read", FilePath: Home + suffix), RankA()).Kind);

    [Theory]
    [InlineData("sed -i 's/true/false/' ~/.claude/settings.json")]
    [InlineData("echo '{\"disableAllHooks\":true}' > ~/.claude/settings.local.json")]
    [InlineData("echo '{\"disableAllHooks\":true}' > .claude/settings.json")]
    [InlineData("echo '{}' > .claude/settings.local.json")]
    [InlineData("rm -rf ~/.claude/plugins")]
    [InlineData("rm -rf ~/.claude/plugins/cache/monarch")]
    [InlineData("rm ~/.claude/plugins/cache/monarch/hooks/hooks.json")]
    [InlineData("jq '.enabledPlugins={}' ~/.claude/settings.json > /tmp/x && mv /tmp/x ~/.claude/settings.json")]
    [InlineData("cp /tmp/s.json ~/.claude/settings.json")]
    [InlineData("rm -rf ~/.claude")]
    public void Hook_configuration_is_protected_from_bash(string cmd) => Denied(cmd);

    [Fact]
    public void Deny_reason_names_rule_and_path_or_pattern()
    {
        var d = Run("rm -rf .monarch/ledger");
        Assert.Contains("protect_paths", d.Reason);
        Assert.Contains(".monarch/ledger", d.Reason);
        var g = Run("rm -f ~/.monarch/bin/0.1.0/monarch");
        Assert.Contains("protect_paths", g.Reason);
        Assert.Contains(".monarch/**", g.Reason);
        var x = Run("git clean -fdx");
        Assert.Contains("protect_paths", x.Reason);
        Assert.Contains("clean", x.Reason);
    }

    // ---- Negativtests: legitime Arbeit bleibt erlaubt
    [Theory]
    [InlineData("git status")]
    [InlineData("git log --oneline -5")]
    [InlineData("git diff HEAD~1")]
    [InlineData("git diff -- .monarch/ledger/x")]
    [InlineData("git add src/x.cs")]
    [InlineData("git add .")]
    [InlineData("git commit -m \"fix: handle .monarch paths\"")]
    [InlineData("git merge origin/develop")]
    [InlineData("git push origin monarch/G-0100-x")]
    [InlineData("git checkout monarch/G-0100-x")]
    [InlineData("git checkout -b monarch/G-0100-y")]
    [InlineData("git stash")]
    [InlineData("git clean -n")]
    [InlineData("git clean -nd")]
    [InlineData("git clean -fd src/")]
    [InlineData("git clean -fd -- src tests")]
    [InlineData("dotnet test")]
    [InlineData("dotnet build -c Release")]
    [InlineData("rm -rf src/bin")]
    [InlineData("rm -rf obj bin")]
    [InlineData("rm *.log")]
    [InlineData("rm -rf src/*/obj")]
    [InlineData("rm -rf build/*")]
    [InlineData("find src -name '*.cs'")]
    [InlineData("find . -name '*.cs'")]
    [InlineData("find . -name '*.cs' -exec grep -l foo {} \\;")]
    [InlineData("find src -name '*.tmp' -delete")]
    [InlineData("python3 -c \"print(1)\"")]
    [InlineData("python3 tools/gen.py")]
    [InlineData("node build.js")]
    [InlineData("perl -e 'print 1'")]
    [InlineData("cat .monarch/system.yaml")]
    [InlineData("cat ~/.claude/settings.json")]
    [InlineData("ls ~/.monarch/bin")]
    [InlineData("grep -r foo .monarch/ledger")]
    [InlineData("monarch quest accept Q-1")]
    [InlineData("~/.monarch/bin/monarch quest accept Q-1")]
    [InlineData("~/.monarch/bin/monarch statusline")]
    [InlineData("cp /tmp/x.txt .")]
    [InlineData("mv a.txt b.txt")]
    [InlineData("mv foo.txt .")]
    [InlineData("touch src/new.cs")]
    [InlineData("rm -rf ~/tmp-build")]
    [InlineData("rm -rf /tmp/q0118")]
    public void Legitimate_work_is_not_blocked(string cmd) => Allowed(cmd);

    [Fact]
    public void Legitimate_work_from_subfolder_is_not_blocked()
    {
        Allowed("rm -rf bin obj", "/work/proj/src");
        Allowed("mv a.cs ../src/b.cs", "/work/proj/src");
        Allowed("git add .", "/work/proj/src");
    }

    /// <summary>
    /// Monarchs eigene Schreibvorgänge (Engine, Statusline, Wrapper-Download) laufen über das Binary beziehungsweise den Hook-Wrapper und sind
    /// keine Tool-Calls; die Policy sieht nur Bash-Kommandos des Agenten. Der Aufruf des Binaries selbst bleibt erlaubt.
    /// </summary>
    [Fact]
    public void Monarch_binary_invocations_remain_allowed() =>
        Allowed("MONARCH_HOME=$HOME/.monarch ~/.monarch/bin/monarch quest start Q-0118 && ~/.monarch/bin/monarch recap");
}
