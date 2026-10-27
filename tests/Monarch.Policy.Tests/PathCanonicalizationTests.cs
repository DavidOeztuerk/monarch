using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

/// <summary>Q-0117: path canonicalization (cwd, symlinks, case, dot segments) for protect_paths, protect_secrets and the AlwaysWritable exception.</summary>
public sealed class PathCanonicalizationTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), "monarch-canon-" + Guid.NewGuid().ToString("N"));
    private readonly string _root;

    public PathCanonicalizationTests()
    {
        _root = Path.Combine(_base, "proj");
        Directory.CreateDirectory(Path.Combine(_root, ".monarch", "ledger", "2026-10"));
        Directory.CreateDirectory(Path.Combine(_root, ".monarch", "knowledge"));
        Directory.CreateDirectory(Path.Combine(_root, ".monarch", "recap"));
        Directory.CreateDirectory(Path.Combine(_root, "src"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_base, true); } catch (IOException) { }
    }

    private PolicyContext Ctx(Rank? rank = null) =>
        new(_root, rank is null ? null : new ActiveQuest(new QuestId("Q-1"), rank.Value, new QuestScope(["**"], [])), false, new PolicyOptions());

    private static PolicyDecision Run(ToolCall c, PolicyContext ctx) => ToolCallPolicy.Evaluate(c, ctx);

    private void AssertDenied(ToolCall c, string rule, PolicyContext? ctx = null)
    {
        var d = Run(c, ctx ?? Ctx());
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(rule, d.Rule);
    }

    // ---- Umgehung 1: Gross-/Kleinschreibung
    [Fact]
    public void Bypass1_uppercase_monarch_dir_is_still_protected()
    {
        AssertDenied(new ToolCall("Write", FilePath: Path.Combine(_root, ".MONARCH", "ledger", "x.jsonl"), Cwd: _root), PolicyRules.ProtectPaths);
        AssertDenied(new ToolCall("Write", FilePath: ".Monarch/Ledger/x.jsonl", Cwd: _root), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Bypass1_secret_globs_match_case_insensitively()
    {
        AssertDenied(new ToolCall("Read", FilePath: Path.Combine(_root, "src", ".ENV"), Cwd: _root), PolicyRules.ProtectSecrets);
        AssertDenied(new ToolCall("Bash", Command: "cat src/SERVER.KEY", Cwd: _root), PolicyRules.ProtectSecrets);
    }

    // ---- Umgehung 2: doppelte Schraegstriche
    [Fact]
    public void Bypass2_double_leading_slash_is_normalized()
    {
        AssertDenied(new ToolCall("Write", FilePath: "/" + Path.Combine(_root, ".monarch", "ledger", "x"), Cwd: _root), PolicyRules.ProtectPaths);
        AssertDenied(new ToolCall("Write", FilePath: _root + "//.monarch///ledger//x", Cwd: _root), PolicyRules.ProtectPaths);
    }

    // ---- Umgehung 3: Rueckweg ueber den Projektnamen
    [Fact]
    public void Bypass3_dotdot_back_through_the_project_name_is_normalized()
    {
        AssertDenied(new ToolCall("Write", FilePath: Path.Combine(_root, "..", "proj", ".monarch", "ledger", "x"), Cwd: _root), PolicyRules.ProtectPaths);
    }

    // ---- Umgehung 4: Alias-Pfad der Wurzel (macOS /tmp <-> /private/tmp, allgemein ein Symlink auf die Wurzel)
    [Fact]
    public void Bypass4_symlinked_alias_of_the_root_is_the_same_root()
    {
        var alias = Path.Combine(_base, "alias");
        Directory.CreateSymbolicLink(alias, _root);
        AssertDenied(new ToolCall("Write", FilePath: Path.Combine(alias, ".monarch", "ledger", "x"), Cwd: alias), PolicyRules.ProtectPaths);
        // and the other direction: root given as the alias, target as the real path
        var ctx = new PolicyContext(alias, null, false, new PolicyOptions());
        AssertDenied(new ToolCall("Write", FilePath: Path.Combine(_root, ".monarch", "ledger", "x"), Cwd: _root), PolicyRules.ProtectPaths, ctx);
    }

    [Fact]
    public void Bypass4_macos_tmp_and_private_tmp_are_the_same()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var dir = Path.Combine("/tmp", "monarch-canon-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, ".monarch", "ledger"));
            var priv = "/private" + dir;
            AssertDenied(new ToolCall("Write", FilePath: Path.Combine(dir, ".monarch", "ledger", "x"), Cwd: priv), PolicyRules.ProtectPaths, new PolicyContext(priv, null, false, new PolicyOptions()));
            AssertDenied(new ToolCall("Write", FilePath: Path.Combine(priv, ".monarch", "ledger", "x"), Cwd: dir), PolicyRules.ProtectPaths, new PolicyContext(dir, null, false, new PolicyOptions()));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // ---- Umgehung 5: Symlink aus der AlwaysWritable-Zone ins Ledger
    [Fact]
    public void Bypass5_symlink_from_knowledge_into_the_ledger_is_denied()
    {
        Directory.CreateSymbolicLink(Path.Combine(_root, ".monarch", "knowledge", "l"), "../ledger");
        var target = Path.Combine(_root, ".monarch", "knowledge", "l", "2026-10", "x.jsonl");
        AssertDenied(new ToolCall("Write", FilePath: target, Cwd: _root), PolicyRules.ProtectPaths);
        AssertDenied(new ToolCall("Write", FilePath: target, Cwd: _root), PolicyRules.ProtectPaths, Ctx(Rank.B));
    }

    [Fact]
    public void Bypass5_symlinked_knowledge_dir_pointing_outside_loses_the_exception()
    {
        var outside = Path.Combine(_base, "outside");
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(_root, ".monarch", "knowledge", "o"), outside);
        var d = Run(new ToolCall("Write", FilePath: ".monarch/knowledge/o/x.md", Cwd: _root), new PolicyContext(_root, new ActiveQuest(new QuestId("Q-1"), Rank.B, new QuestScope(["src/**"], [])), false, new PolicyOptions()));
        Assert.Equal(PolicyDecisionKind.Deny, d.Kind);
        Assert.Equal(PolicyRules.ScopeLock, d.Rule);
    }

    [Fact]
    public void Plain_knowledge_and_recap_stay_writable()
    {
        var ctx = new PolicyContext(_root, new ActiveQuest(new QuestId("Q-1"), Rank.B, new QuestScope(["src/**"], [])), false, new PolicyOptions());
        Assert.Equal(PolicyDecisionKind.Allow, Run(new ToolCall("Write", FilePath: ".monarch/knowledge/a.md", Cwd: _root), ctx).Kind);
        Assert.Equal(PolicyDecisionKind.Allow, Run(new ToolCall("Write", FilePath: Path.Combine(_root, ".monarch", "recap", "RECAP.md"), Cwd: _root), ctx).Kind);
    }

    // ---- Umgehung 6: relative Pfade gegen das cwd des Tool-Calls
    [Fact]
    public void Bypass6_relative_path_in_bash_resolves_against_the_cwd()
    {
        AssertDenied(new ToolCall("Bash", Command: "rm ../.monarch/ledger/2026-10/x.jsonl", Cwd: Path.Combine(_root, "src")), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Bypass6_relative_file_tool_path_resolves_against_the_cwd()
    {
        AssertDenied(new ToolCall("Write", FilePath: "../.monarch/ledger/x", Cwd: Path.Combine(_root, "src")), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Bypass6_same_relative_name_in_a_subdirectory_is_not_the_ledger()
    {
        Assert.Equal(PolicyDecisionKind.Allow, Run(new ToolCall("Write", FilePath: ".monarch/ledger/x", Cwd: Path.Combine(_root, "src")), Ctx()).Kind);
    }

    [Fact]
    public void Bash_cd_into_the_ledger_then_relative_rm_is_denied()
    {
        AssertDenied(new ToolCall("Bash", Command: "cd .monarch/ledger && rm 2026-10/x.jsonl", Cwd: _root), PolicyRules.ProtectPaths);
        AssertDenied(new ToolCall("Bash", Command: "cd .. && rm .monarch/ledger/x", Cwd: Path.Combine(_root, "src")), PolicyRules.ProtectPaths);
    }

    [Fact]
    public void Bash_symlink_to_a_secret_is_denied()
    {
        File.WriteAllText(Path.Combine(_root, "src", "plain.txt"), "");
        Directory.CreateSymbolicLink(Path.Combine(_root, "src", "innocent"), Path.Combine(_root, "src", ".env"));
        AssertDenied(new ToolCall("Bash", Command: "cat src/innocent", Cwd: _root), PolicyRules.ProtectSecrets);
        AssertDenied(new ToolCall("Read", FilePath: "src/innocent", Cwd: _root), PolicyRules.ProtectSecrets);
    }
}
