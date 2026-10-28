using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0124 Teil A: Funde des dritten Wardens in Lexer und Policy (Kommentare, Zuweisungen, Zielverzeichnisse, cwd-Modell, Schreiber hinter lesenden Kommandos, nacktes monarch).</summary>
public sealed class Q0124ATests : IDisposable
{
    private readonly string _tmp = PathCanon.Resolve("/", Path.Combine(Path.GetTempPath(), "q124a-" + Guid.NewGuid().ToString("N")));

    public Q0124ATests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch (IOException) { /* best effort */ }
    }

    private static PolicyContext Ctx(string root = ToolCallPolicyTests.Root) =>
        new(root, new ActiveQuest(new QuestId("Q-1"), Rank.A, new QuestScope(["src/**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(string cmd, string cwd = "", PolicyContext? ctx = null) =>
        ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: cmd, Cwd: cwd), ctx ?? Ctx());

    private static void Denied(string cmd, string cwd = "", PolicyContext? ctx = null)
    {
        var d = Run(cmd, cwd, ctx);
        Assert.True(d.Kind == PolicyDecisionKind.Deny, $"expected deny for: {cmd} (was {d.Kind})");
        Assert.Equal(PolicyRules.ProtectPaths, d.Rule);
    }

    private static void Allowed(string cmd, string cwd = "", PolicyContext? ctx = null)
    {
        var d = Run(cmd, cwd, ctx);
        Assert.True(d.Kind == PolicyDecisionKind.Allow, $"expected allow for: {cmd} (was {d.Kind}: {d.Reason})");
    }

    // ---- B1 Kommentare
    [Theory]
    [InlineData("ls # don't panic\nrm -rf .monarch/ledger")]
    [InlineData("git status # what's changed\nsed -i '' 's/scope_lock: true/scope_lock: false/' .monarch/system.yaml")]
    [InlineData("echo ok # don't\necho x > .monarch/system.yaml")]
    [InlineData("# it's\nrm -rf .monarch/ledger")]
    [InlineData("echo a#b; rm -rf .monarch/ledger")]
    [InlineData("echo ${x#y}; rm -rf .monarch/ledger")]
    [InlineData("echo $#; rm -rf .monarch/ledger")]
    [InlineData("git log --format='%h #%s'; rm -rf .monarch/ledger")]
    [InlineData("echo \"a#b\"; rm -rf .monarch/ledger")]
    public void B1_comments_end_at_newline_and_do_not_hide_following_lines(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("echo \"a#b\"", "echo|a#b")]
    [InlineData("echo a#b", "echo|a#b")]
    [InlineData("echo ${x#y}", "echo|${x#y}")]
    [InlineData("echo $#", "echo|$#")]
    [InlineData("git log --format='%h #%s'", "git|log|--format=%h #%s")]
    [InlineData("echo a #b c", "echo|a")]
    [InlineData("echo a\t#b c", "echo|a")]
    public void B1_hash_is_a_comment_only_at_word_start_outside_quotes(string cmd, string tokens) =>
        Assert.Equal(tokens, string.Join('|', BashAnalysis.Segments(cmd).Single().Tokens));

    [Theory]
    [InlineData("# don't forget\ndotnet test")]
    [InlineData("dotnet build # it's fine\ndotnet test # what's up")]
    public void B1_comments_with_apostrophes_stay_allowed(string cmd) => Allowed(cmd);

    // ---- B2 Zuweisungen
    [Theory]
    [InlineData("F=.monarch/system.yaml; echo \"scope_lock: false\" > $F")]
    [InlineData("L=.monarch/ledger; rm -rf \"$L\"")]
    [InlineData("export L=.monarch/ledger; rm -rf $L")]
    [InlineData("declare D=~/.monarch/bin; rm -rf $D")]
    [InlineData("local L=.monarch/ledger; rm -rf $L")]
    [InlineData("readonly L=.monarch/ledger; rm -rf $L")]
    [InlineData("typeset -x L=.monarch/ledger; rm -rf $L")]
    [InlineData("declare -r D=~/.monarch/bin && rm -rf $D")]
    [InlineData("L=.monarch/ledger rm -rf $L")]
    [InlineData("env L=.monarch/ledger rm -rf $L")]
    [InlineData("(F=.monarch/system.yaml; echo x > $F)")]
    [InlineData("L=.monarch/ledger\nrm -rf $L")]
    [InlineData("L=.monarch/ledger; echo $(rm -rf $L)")]
    public void B2_assignments_naming_a_protected_path_before_a_mutation_are_denied(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("F=.monarch/system.yaml; cat $F")]
    [InlineData("CFG=src/app.json; cat $CFG")]
    [InlineData("CFG=src/app.json; rm $CFG")]
    [InlineData("export CFG=src/app.json; sed -i s/a/b/ $CFG")]
    [InlineData("OUT=.; mkdir -p $OUT/build")]
    [InlineData("rm -rf build; F=.monarch/system.yaml")]
    public void B2_pure_assignments_and_unprotected_values_stay_allowed(string cmd) => Allowed(cmd);

    // ---- B3a Zielverzeichnis
    [Theory]
    [InlineData("tar -xf x.tar -C .")]
    [InlineData("tar -xf x.tar --directory=.")]
    [InlineData("tar -xf x.tar --directory .")]
    [InlineData("tar -xf x.tar -C \"$PWD\"")]
    [InlineData("tar -xf x.tar -C$PWD")]
    [InlineData("tar xf x.tar -C .")]
    [InlineData("tar -C . -xf x.tar")]
    [InlineData("tar xCf . x.tar")]
    [InlineData("unzip -o x.zip -d .")]
    [InlineData("unzip -o x.zip -d$PWD")]
    [InlineData("wget -P . https://x")]
    [InlineData("wget -P. https://x")]
    [InlineData("wget --directory-prefix=. https://x")]
    [InlineData("curl -O --output-dir . https://x/a")]
    [InlineData("cd src && tar -xf x.tar -C ..")]
    public void B3a_target_directory_is_checked_like_a_cwd(string cmd) => Denied(cmd);

    [Fact]
    public void B3a_relative_target_from_a_foreign_cwd_hits_an_awakened_project() =>
        Denied("tar -xf x.tar -C ../proj", "/work/other");

    [Theory]
    [InlineData("tar -xf vendor.tar -C vendor/")]
    [InlineData("tar -xzf a.tgz -C vendor")]
    [InlineData("unzip x.zip -d build/")]
    [InlineData("wget -P downloads https://x")]
    [InlineData("curl -O --output-dir out https://x/a")]
    [InlineData("tar -cf out.tar -C . src")]
    [InlineData("tar -tf x.tar -C .")]
    public void B3a_other_target_directories_stay_allowed(string cmd) => Allowed(cmd);

    // ---- B3b cwd-Modell
    [Theory]
    [InlineData("(cd /usr) && tar -xf x.tar")]
    [InlineData("(cd /usr); wget https://x")]
    [InlineData("cd /usr && cd - && tar -xf x.tar")]
    [InlineData("pushd /usr && popd && wget https://x")]
    [InlineData("pushd /usr >/dev/null; popd >/dev/null; unzip x.zip")]
    [InlineData("echo $(cd /usr) && tar -xf x.tar")]
    [InlineData("bash -c 'cd /usr' && tar -xf x.tar")]
    [InlineData("(cd /usr; (cd /opt); tar -xf x.tar; ) ; tar -xf x.tar")]
    public void B3b_cwd_model_restores_the_directory(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("cd /usr && tar -xf x.tar")]
    [InlineData("(cd /usr && tar -xf x.tar)")]
    [InlineData("cd /usr && (tar -xf x.tar)")]
    [InlineData("cd /usr && cd /opt && tar -xf x.tar")]
    [InlineData("pushd /usr && tar -xf x.tar && popd")]
    [InlineData("cd /usr && cd /opt && cd - && tar -xf x.tar")]
    [InlineData("(cd /usr && cd /opt) && cd /usr && tar -xf x.tar")]
    public void B3b_cwd_model_keeps_foreign_directories_allowed(string cmd) => Allowed(cmd);

    // ---- Schreiber hinter lesenden Kommandos
    [Theory]
    [InlineData("exec 3<>.monarch/system.yaml")]
    [InlineData("cat 3<>.monarch/system.yaml")]
    [InlineData("exec 3>|.monarch/system.yaml")]
    [InlineData("echo x >| .monarch/system.yaml")]
    [InlineData("awk '{print > \".monarch/local/x\"}' f")]
    [InlineData("awk '{print >> \".monarch/local/x\"}' f")]
    [InlineData("awk '{print | \"tee .monarch/local/x\"}' f")]
    [InlineData("sed -n 'w .monarch/ledger/x' f")]
    [InlineData("sed -e 's/a/b/w .monarch/ledger/x' f")]
    [InlineData("git diff --output=.monarch/system.yaml")]
    [InlineData("git log --output .monarch/system.yaml")]
    [InlineData("uniq a .monarch/system.yaml")]
    [InlineData("uniq -c a .monarch/system.yaml")]
    [InlineData("sort -o .monarch/system.yaml a")]
    [InlineData("sort -o.monarch/system.yaml a")]
    [InlineData("sort --output=.monarch/system.yaml a")]
    [InlineData("sort -ro .monarch/system.yaml a")]
    public void Hidden_writers_behind_reading_commands_are_denied(string cmd) => Denied(cmd);

    [Theory]
    [InlineData("awk '{print}' .monarch/system.yaml")]
    [InlineData("awk '$1 > 5 {print}' .monarch/ledger/x")]
    [InlineData("awk '{print > \"out.txt\"}' f")]
    [InlineData("sed -n 's/a/b/p' .monarch/system.yaml")]
    [InlineData("sed -n 'w out.txt' f")]
    [InlineData("git diff --stat .monarch/system.yaml")]
    [InlineData("uniq .monarch/system.yaml")]
    [InlineData("uniq -c .monarch/system.yaml")]
    [InlineData("uniq -f 1 .monarch/system.yaml")]
    [InlineData("sort .monarch/system.yaml")]
    [InlineData("sort -u a b")]
    public void Hidden_writer_checks_leave_plain_reads_allowed(string cmd) => Allowed(cmd);

    // ---- Nacktes monarch
    private (string Home, string Bin) FakeHome()
    {
        var home = Path.Combine(_tmp, "home");
        var ver = Path.Combine(home, ".monarch", "bin", "0.1.0");
        Directory.CreateDirectory(ver);
        File.WriteAllText(Path.Combine(ver, "monarch"), "");
        File.CreateSymbolicLink(Path.Combine(home, ".monarch", "bin", "monarch"), Path.Combine(ver, "monarch"));
        return (home, Path.Combine(home, ".monarch", "bin"));
    }

    private static PolicyContext With(PolicyContext c, string? home, string? path) => c with { HomeDir = home, PathEnv = path };

    private const string Writing = "monarch ledger rewrite .monarch/ledger/x";

    [Fact]
    public void Bare_monarch_resolving_to_the_installed_binary_stays_allowed()
    {
        var (home, bin) = FakeHome();
        Allowed(Writing, ctx: With(Ctx(), home, bin + ":/usr/bin"));
        Allowed("monarch status", ctx: With(Ctx(), home, bin + ":/usr/bin"));
        Allowed("monarch status");
    }

    [Fact]
    public void Bare_monarch_without_a_matching_path_is_failing_closed_when_it_names_a_protected_path()
    {
        var (home, bin) = FakeHome();
        Denied(Writing, ctx: With(Ctx(), home, "/usr/bin:/bin"));
        Denied(Writing, ctx: With(Ctx(), home, ""));
        var other = Directory.CreateDirectory(Path.Combine(_tmp, "other")).FullName;
        File.WriteAllText(Path.Combine(other, "monarch"), "#!/bin/sh\n");
        Denied(Writing, ctx: With(Ctx(), home, other + ":" + bin));
    }

    [Fact]
    public void Bare_monarch_found_in_a_dir_not_under_the_install_prefix_is_denied()
    {
        var (home, _) = FakeHome();
        var other = Directory.CreateDirectory(Path.Combine(_tmp, "bin2")).FullName;
        File.WriteAllText(Path.Combine(other, "monarch"), "");
        Denied(Writing, ctx: With(Ctx(), home, other));
    }

    [Theory]
    [InlineData("./monarch .monarch/system.yaml")]
    [InlineData("/tmp/x/monarch .monarch/system.yaml")]
    [InlineData("./monarch ledger rewrite .monarch/ledger/x")]
    public void Local_monarch_scripts_naming_a_protected_path_are_denied(string cmd)
    {
        var (home, bin) = FakeHome();
        Denied(cmd, ctx: With(Ctx(), home, bin));
    }
}
