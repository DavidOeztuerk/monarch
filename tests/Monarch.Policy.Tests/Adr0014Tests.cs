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
    [InlineData("env git restore --staged .monarch")]
    public void Git_non_index_or_non_literal_forms_stay_denied(string cmd) => Denied(cmd);

    // ---- Warden Code-Runde: Platzhalter-Schutz mit Rest nach dem Wildcard (Slash, ., //, .. durch Wildcard)
    [Theory]
    [InlineData("rsync -a x/ .monarc?/")]
    [InlineData("cp -R x/ledger .monarc?/")]
    [InlineData("rm .monarc?/./ledger/x")]
    [InlineData("rm .monarc?//ledger/x")]
    [InlineData("rm -rf .?/*")]
    [InlineData("rm -rf .[.]/proj")]
    [InlineData("rm -rf ~/.monarc?/")]
    [InlineData("rm -rf ~/.claud?/settings.json")]
    [InlineData("cp x ~/.monarc?/")]
    public void Wildcard_rest_normalisation_stays_denied(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("sed -i 's/foo.*/bar/' f.txt")]
    [InlineData("rm -rf src/.cache*/x")]
    public void Wildcard_rest_normalisation_regression_allowed(string cmd) => Allowed(cmd);

    // ---- Heredoc/Here-String an Gruppe oder Subshell gehört zum Code
    [Theory]
    [InlineData("cd /tmp && ( python3 - ) <<'EOF'\nimport os; os.remove('.monarch/local/x')\nEOF")]
    [InlineData("cd /tmp && { python3 -; } <<'EOF'\nimport os; os.remove('.monarch/local/x')\nEOF")]
    [InlineData("cd /tmp && ( python3 - ) <<< \"import os; os.remove('.monarch/local/x')\"")]
    [InlineData("python3 -c \"open('.mon'+'arch/ledger/x','w')\"")]
    public void Heredoc_on_group_and_decomposed_inline_code_stay_denied(string cmd) => Denied(cmd);

    // ---- gh: nur der erste Buchstabe eines Short-Clusters ist ein Daten-Flag
    [Theory]
    [InlineData("gh run download 123 -Dq/../.monarch/ledger")]
    [InlineData("gh release download v1 -Oq/../.monarch/ledger/x")]
    public void Gh_cluster_with_write_path_value_stays_denied(string cmd) => Denied(cmd);

    // ---- FlatPattern (Einheitentests)
    [Theory]
    [InlineData(".monarc?", ".monarch", true)]
    [InlineData(".mon[a]rch", ".monarch", true)]
    [InlineData(".[]m]onarch", ".monarch", true)]
    [InlineData(".[!M]onarch", ".monarch", true)]
    [InlineData(".[a-z]onarch", ".monarch", true)]
    [InlineData(".[[=m=]]onarch", ".monarch", true)]
    [InlineData(".[0]x", ".monarch", false)]
    [InlineData(".[", ".[", true)]
    [InlineData(".[]", ".monarch", false)]
    [InlineData(".[M]onarch", ".monarch", true)]
    [InlineData(".m*", ".monarch", true)]
    [InlineData(".monarch?ledger", ".monarch/ledger", true)]
    [InlineData(".monarch[/]ledger", ".monarch/ledger", true)]
    [InlineData(".[ß]x", ".qx", true)]
    public void FlatPattern_matches(string pattern, string text, bool expected) =>
        Assert.Equal(expected, FlatPattern.Parse(pattern).Matches(text));

    [Theory]
    [InlineData(".mon*/x", ".monarch/", true)]
    [InlineData(".[0].message", ".monarch/", false)]
    [InlineData("s/.*x", ".monarch/", false)]
    public void FlatPattern_can_start_with(string pattern, string text, bool expected) =>
        Assert.Equal(expected, FlatPattern.Parse(pattern).CanStartWith(text));

    // ---- Q-0138 / Issue 55: genau eine sed-Form wieder frei (ADR-0014 Nachtrag, Owner-Entscheid STRENG)
    [Theory]
    [InlineData("sed 's/.*/new/' f")]
    [InlineData("sed -e 's/.*/e/' f")]
    [InlineData("sed 's/.*/new/' src/a.txt")]
    public void Issue55_sed_substitute_dot_star_single_operand_is_allowed(string cmd) => Allowed(cmd);

    [Theory]
    [InlineData("awk '/.*/ {print | \"sort\"}' f")]
    [InlineData("rm -rf dist/.*")]
    [InlineData("rm -rf .?/*")]
    [InlineData("rm -rf dist/.?/.monarch/ledger")]
    [InlineData("rm -rf dist/.*/.monarch")]
    [InlineData("cp -R x/ledger dist/.?/.monarc?")]
    [InlineData("chmod -R 000 dist/.*")]
    [InlineData("sed 's/x/y/w s/.?/.monarch/ledger/x' f")]
    [InlineData("sed -i 's/a/b/' dist/.?/.monarch/ledger/x")]
    public void Issue55_known_false_alarms_and_protection_stay_denied(string cmd) => Denied(cmd);

    [Theory]
    // Operand
    [InlineData("sed 's/.*/new/' .monarch/ledger/x")]
    [InlineData("sed 's/.*/new/' sub/.monarch/x")]
    [InlineData("sed 's/.*/new/' .monarch")]
    [InlineData("sed 's/.*/new/' .claude")]
    [InlineData("sed 's/.*/new/'")]
    // Skript
    [InlineData("sed 's/.*/new/w .monarch/ledger/x' f")]
    [InlineData("sed 's/.*/new/e' f")]
    [InlineData("sed 's/.*/new/;w .monarch/ledger/x' f")]
    [InlineData("sed -e 's/.*/new/' -e 'w .monarch/ledger/x' f")]
    [InlineData("sed -i 's/.*/new/' f")]
    [InlineData("sed -n 's/.*/new/p' f")]
    [InlineData("sed -E 's/.*/new/' f")]
    [InlineData("sed 's/.*/new/' a b")]
    [InlineData("sed -n '/private static .*Foo/,/^}/p' f")]
    // Optionen hinter dem Operanden
    [InlineData("sed 's/.*/new/' f -i")]
    [InlineData("sed 's/.*/new/' f --in-place")]
    [InlineData("sed 's/.*/new/' f -n")]
    [InlineData("sed 's/.*/new/' f -e 'w .monarch/ledger/x'")]
    [InlineData("sed 's/.*/new/' -- f")]
    [InlineData("sed 's/.*/new/' -")]
    [InlineData("sed 's/.*/new/' {f,-i}")]
    // Warden Code-Runde: Vorfahren und Root als Operand, fd-Umleitungen, Subshell, Here-String
    [InlineData("sed 's/.*/new/' .")]
    [InlineData("sed 's/.*/new/' ..")]
    [InlineData("sed 's/.*/new/' /")]
    [InlineData("sed 's/.*/new/' f 2>&1")]
    [InlineData("sed 's/.*/new/' f >&2")]
    [InlineData("( sed 's/.*/new/' f )")]
    [InlineData("sed 's/.*/new/' f <<< x")]
    // Umleitung, Pipe, Subshell, Gruppe, Verkettung
    [InlineData("sed 's/.*/new/' f > out")]
    [InlineData("sed 's/.*/new/' f > $T")]
    [InlineData("sed 's/.*/new/' f | sh")]
    [InlineData("sed 's/.*/new/' f | sort")]
    [InlineData("sed 's/.*/new/' f < g")]
    [InlineData("cat g | sed 's/.*/new/' f")]
    [InlineData("( sed 's/.*/new/' f ) > out")]
    [InlineData("( sed 's/.*/new/' f ) | sh")]
    [InlineData("{ true; sed 's/.*/new/' f; } > out")]
    [InlineData("cd x && sed 's/.*/new/' f")]
    // Nicht wörtlich
    [InlineData("sed 's/.*/$NEW/' f")]
    [InlineData("sed \"s/.*/$(cat g)new/\" f")]
    [InlineData("sed 's/.*/new/' $F")]
    [InlineData("sed 's/.*/new/' *")]
    // Wrapper
    [InlineData("env sed 's/.*/new/' f")]
    [InlineData("./sed 's/.*/new/' f")]
    [InlineData("printf f | xargs sed 's/.*/new/'")]
    public void Issue55_sed_exception_does_not_reach_further(string cmd) => Denied(cmd);
}
