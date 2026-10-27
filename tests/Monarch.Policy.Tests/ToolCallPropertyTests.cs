using FsCheck.Xunit;
using Monarch.Domain;
using Monarch.Policy;

namespace Monarch.Policy.Tests;

public class ToolCallPropertyTests
{
    private static int I(int n, int mod) => Math.Abs(n % mod == int.MinValue ? 0 : n % mod);
    private static string Dirs(int[] ns) => string.Join('/', ns.Take(4).Select(n => "d" + I(n, 20)));
    private static readonly string[] Tools = ["Edit", "Write", "MultiEdit", "NotebookEdit"];
    private static readonly string?[] Agents = [null, "knight", "warden", "scout", "scribe"];
    private static readonly string?[] Modes = [null, "default", "plan", "acceptEdits", "auto", "dontAsk", "bypassPermissions", "xyz"];

    [Property]
    public bool Protected_glob_paths_always_denied(int[] dirs, int tool, int agent, int mode, int rank, int shape)
    {
        var d = Dirs(dirs);
        var path = I(shape, 2) == 0 ? $".monarch/ledger/{d}/f.jsonl" : $".monarch/shadows/s{I(shape, 9)}/shadow.yaml";
        var quest = rank % 3 == 0 ? null : new ActiveQuest(new QuestId("Q"), (Rank)I(rank, 6), new QuestScope(["**"], []));
        var ctx = new PolicyContext(ToolCallPolicyTests.Root, quest, false, new PolicyOptions());
        var call = new ToolCall(Tools[I(tool, 4)], FilePath: path, AgentType: Agents[I(agent, 5)], PermissionMode: Modes[I(mode, 8)]);
        return ToolCallPolicy.Evaluate(call, ctx).Kind == PolicyDecisionKind.Deny;
    }

    private static readonly string[] DenyCmds =
        ["git push --force", "git push -f origin main", "git push origin +main", "git push --force-with-lease", "git reset --hard HEAD~2",
         "dotnet ef database drop --force", "monarch permit x", "monarch promote y"];
    private static readonly string[] Safe = ["echo hi", "ls -la", "cd src", "git status", "true"];
    private static readonly string[] Seps = [" && ", " || ", "; ", " | ", "\n"];
    private static readonly string[] Wrap = ["", "sudo ", "env ", "command ", "exec ", "sudo env "];

    [Property]
    public bool Deny_bash_survives_obfuscation(int cmd, int assigns, int spaces, int wrap, int before, int after, int sepB, int sepA)
    {
        var core = DenyCmds[I(cmd, DenyCmds.Length)].Replace(" ", new string(' ', 1 + I(spaces, 4)), StringComparison.Ordinal);
        var assign = string.Concat(Enumerable.Range(0, I(assigns, 4)).Select(i => $"V{i}=x{i}  "));
        var full = assign + Wrap[I(wrap, Wrap.Length)] + core;
        if (I(before, 3) != 0) full = Safe[I(before, Safe.Length)] + Seps[I(sepB, Seps.Length)] + full;
        if (I(after, 3) != 0) full += Seps[I(sepA, Seps.Length)] + Safe[I(after, Safe.Length)];
        var d = ToolCallPolicy.Evaluate(new ToolCall("Bash", Command: full), new PolicyContext(ToolCallPolicyTests.Root));
        return d.Kind == PolicyDecisionKind.Deny;
    }

    [Property]
    public bool Scope_lock_inside_allowed_outside_denied(int[] dirs, int name, int top, int inside)
    {
        var d = Dirs(dirs);
        var ctx = ToolCallPolicyTests.Ctx(Rank.C, scope: ToolCallPolicyTests.Scope);
        if (I(inside, 2) == 0)
        {
            var path = $"{(I(top, 2) == 0 ? "src" : "tests")}/{d}/F{I(name, 99)}.cs";
            return ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(path), ctx).Kind == PolicyDecisionKind.Allow;
        }

        string[] outside = [$"docs/{d}/F{I(name, 99)}.md", $"other/{d}/a.cs", $"src/{d}/Migrations/{I(name, 9)}.Designer.cs", $"../x/{d}.cs"];
        var dec = ToolCallPolicy.Evaluate(ToolCallPolicyTests.Edit(outside[I(top, 4)]), ctx);
        return dec.Kind == PolicyDecisionKind.Deny && dec.Signals.Contains(PolicySignals.DoubleDungeon);
    }

    [Property]
    public bool Combine_deny_wins_then_ask(int[] kinds)
    {
        var ds = kinds.Select((k, i) => new PolicyDecision((PolicyDecisionKind)I(k, 3), "r" + i, "x")).ToList();
        var result = ToolCallPolicy.Combine(ds).Kind;
        var expected = ds.Any(d => d.Kind == PolicyDecisionKind.Deny) ? PolicyDecisionKind.Deny
            : ds.Any(d => d.Kind == PolicyDecisionKind.Ask) ? PolicyDecisionKind.Ask : PolicyDecisionKind.Allow;
        return result == expected;
    }

    [Property]
    public bool Any_denying_rule_makes_final_deny(int tool, int agent, int mode, int rank)
    {
        // protect_paths always denies this path; whatever the other rules say, the final decision is Deny.
        var ctx = new PolicyContext(ToolCallPolicyTests.Root, new ActiveQuest(new QuestId("Q"), (Rank)I(rank, 6), ToolCallPolicyTests.Scope), false, new PolicyOptions());
        var call = new ToolCall(Tools[I(tool, 4)], FilePath: ".monarch/ledger/x.jsonl", AgentType: Agents[I(agent, 5)], PermissionMode: Modes[I(mode, 8)]);
        return ToolCallPolicy.Evaluate(call, ctx).Kind == PolicyDecisionKind.Deny;
    }

    [Property]
    public bool Glob_double_star_matches_everything(int[] dirs) => GlobMatcher.IsMatch("**", Dirs(dirs));
}
