using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0133: Fehlalarme #28, #14, #47 ohne Lockerung (ADR-0014). Allow-Tests der Fehlalarme sind vorher rot, "Regression" markiert schon grüne Fälle.</summary>
public sealed class Adr0014Tests
{
    private static PolicyContext Ctx() =>
        new(ToolCallPolicyTests.Root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd) => ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: ""), Ctx());

    private static void Denied(string cmd)
    {
        var d = Run(cmd);
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny for: {cmd} (was {d.Kind})");
    }

    private static void Allowed(string cmd)
    {
        var d = Run(cmd);
        Assert.True(d.Kind == PolicyDecisionKind.Allow, $"expected allow for: {cmd} (was {d.Kind}: {d.Reason})");
    }

    // ---- #28 (rot vorher)
    [Theory]
    [InlineData("gh api repos/o/r/commits --jq '.[0].message'")]
    [InlineData("gh api repos/o/r/commits --jq='.[0].message'")]
    [InlineData("gh api repos/o/r/commits -q '.[0].message'")]
    [InlineData("gh api repos/o/r/commits -q'.[0].message'")]
    [InlineData("gh api repos/o/r/commits -q='.[0].message'")]
    [InlineData("gh api repos/o/r/commits --template '{{.[0]}}'")]
    [InlineData("sed -i -E 's/^(.*)x/\\1/' f.txt")]
    [InlineData("sed -i 's/,.*//' f.txt")]
    public void Issue28_dot_wildcard_data_is_allowed(string cmd) => Allowed(cmd);

    // ---- Regression: schon vorher grün
    [Theory]
    [InlineData("gh pr checks 29")]
    [InlineData("gh pr view 29 --json statusCheckRollup --jq '.statusCheckRollup[].conclusion'")]
    [InlineData("gh run list --branch develop --limit 5")]
    [InlineData("gh run view 41038797796 --log-failed")]
    [InlineData("git fetch origin")]
    [InlineData("git rev-parse HEAD")]
    [InlineData("git log --oneline -5")]
    [InlineData("git diff origin/develop...HEAD")]
    [InlineData("jq '.[] | .name' file.json")]
    [InlineData("jq '.[0]' datei")]
    [InlineData("jq '.[]' f.json")]
    [InlineData("sed 's/.*x//' f.txt")]
    [InlineData("perl -pe 's/.*?foo//g' f.txt")]
    public void Regression_read_only_and_dot_star_commands_allowed(string cmd) => Allowed(cmd);

    [Theory]
    [InlineData("rm -rf .*")]
    [InlineData("rm -rf .m*")]
    [InlineData("mv .mon[a]rch x")]
    [InlineData("rm .monarch/ledger/*")]
    [InlineData("git rm '.monarch/**'")]
    [InlineData("rm -rf .[a-z]*")]
    [InlineData("rm -rf .[!M]onarch")]
    [InlineData("rm -rf .[]m]onarch")]
    [InlineData("rm -rf .[![:upper:]]onarch")]
    [InlineData("rm -rf .[[=m=]]onarch")]
    [InlineData("git rm '.monarch?ledger?x'")]
    [InlineData("git rm '.monarch[/]ledger'")]
    [InlineData("rm -rf ./s*/../.monarch/ledger")]
    [InlineData("rm -rf .claude/sett*")]
    [InlineData("cp -R x/ledger .monarc?")]
    [InlineData("cp -R x/ledger .mon[a]rch")]
    [InlineData("chmod 000 .monarc?")]
    [InlineData("gh api repos/o/r/commits --jq '.[0]' > .monarch/ledger/x")]
    [InlineData("gh api --jq '.x' .monarch/ledger/x")]
    [InlineData("sed -i 's/x/y/w .monarch/ledger/x' f.txt")]
    [InlineData("sed -i 's/,.*//' .monarch/ledger/x")]
    [InlineData("sed -i -f s.sed -E 's/(.*)//' f")]
    [InlineData("perl -pi -e 's/(.*?)foo//g' f")]
    [InlineData("perl -e 'system(\"rm -rf .m*\")'")]
    [InlineData("sed -i'.monarch/ledger/*' 's/a/b/' f")]
    [InlineData("sed --in-place='.monarch/ledger/*' 's/a/b/' f")]
    public void Glob_gh_sed_protection_stays_denied(string cmd) => Denied(cmd);

    // ---- #14 (rot vorher)
    [Theory]
    [InlineData("cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF'\nprint(1)\nEOF")]
    [InlineData("cd /work/proj/.claude/worktrees/G-0104 && python3 - <<EOF\nprint(1)\nEOF")]
    [InlineData("cd .claude/worktrees/G-0104 && python3 -c \"print(1)\"")]
    public void Issue14_cd_before_interpreter_is_not_code(string cmd) => Allowed(cmd);

    [Theory]
    [InlineData("cd .monarch/local && python3 -c \"print(1)\"")]
    [InlineData("cd .MONARCH/local && python3 -c \"print(1)\"")]
    [InlineData("cd .claude && python3 -c \"print(1)\"")]
    [InlineData("cd .claude/worktrees/../ && python3 -c \"print(1)\"")]
    [InlineData("cd .claude/worktrees/G/.monarch && python3 -c \"print(1)\"")]
    [InlineData("python3 -c \"os.remove('.mon' + 'arch/local/x')\"")]
    [InlineData("cd ~/Projects/.monarch-worktrees/x && python3 -c \"os.remove('.mon' + 'arch/local/x')\"")]
    [InlineData("echo \"os.remove('.monarch/local/x')\" | python3 -")]
    [InlineData("python3 -c \"$(echo \"import os; os.remove('.monarch/local/x')\")\"")]
    [InlineData("python3 -c \"$(echo \"import os; os.remove('.mon'+'arch/local/x')\")\"")]
    [InlineData("python3 <(echo \"os.remove('.mon'+'arch/local/x')\")")]
    [InlineData("{ echo \"os.remove('.mon'+'arch/local/x')\"; } | python3 -")]
    [InlineData("( echo \"os.remove('.mon'+'arch/local/x')\" ) | python3 -")]
    [InlineData("C=\"os.remove('.mon'+'arch/local/x')\" python3 -c 'exec(os.environ[\"C\"])'")]
    [InlineData("cd .monarch-worktrees/x && python3 - <<'EOF'\nos.remove('.monarch/local/x')\nEOF")]
    [InlineData("python3 -c 'print(1)'; python3 -c \"os.remove('.monarch/x')\"")]
    [InlineData("cd() { python3 -c \"import os,sys; os.remove(sys.argv[1]+sys.argv[2])\" \"$@\"; }; cd .mon arch/local/x")]
    public void Interpreter_code_mentions_and_real_components_stay_denied(string cmd) => Denied(cmd);

    // ---- #47 (rot vorher)
    [Theory]
    [InlineData("git restore --staged .monarch")]
    [InlineData("git restore -S -q -- .monarch/ledger")]
    [InlineData("git reset -q HEAD -- .monarch/ledger/x")]
    [InlineData("git reset -- .monarch/ledger/x")]
    public void Issue47_index_only_forms_allowed(string cmd) => Allowed(cmd);

    [Theory]
    [InlineData("git restore .monarch")]
    [InlineData("git restore --staged --worktree .monarch")]
    [InlineData("git restore -SW .monarch")]
    [InlineData("git restore -qS .monarch")]
    [InlineData("git restore --source=HEAD~1 --staged .monarch")]
    [InlineData("git restore -s HEAD --staged .monarch")]
    [InlineData("git restore -sHEAD --staged .monarch")]
    [InlineData("git restore -p .monarch")]
    [InlineData("git restore --staged --pathspec-from-file=x .monarch")]
    [InlineData("git reset HEAD .monarch/ledger/x")]
    [InlineData("git reset HEAD~1 -- .monarch/ledger/x")]
    [InlineData("git reset --hard -- .monarch")]
    [InlineData("git rm --cached .monarch/ledger/x")]
    [InlineData("git checkout -- .monarch")]
    [InlineData("GIT_INDEX_FILE=.monarch/ledger/new git restore --staged src/a")]
    [InlineData("GIT_INDEX_FILE=.monarch/ledger/new git reset -q -- src/a")]
    [InlineData("export GIT_DIR=x; git reset -- .monarch/ledger/x")]
    [InlineData("git -C .monarch restore --staged x")]
    [InlineData("git -c core.x=y restore --staged .monarch")]
    [InlineData("git --git-dir=d restore --staged .monarch")]
    [InlineData("W=--worktree; git restore --staged .monarch $W")]
    [InlineData("git restore --staged .monarch $(cat f)")]
    [InlineData("printf -- --worktree | xargs git restore --staged .monarch")]
    [InlineData("git restore --staged .monarch *")]
    [InlineData("./git restore --staged .monarch/ledger")]
    [InlineData("env git restore --staged --worktree .monarch")]
    public void Git_non_index_or_non_literal_forms_stay_denied(string cmd) => Denied(cmd);
}
