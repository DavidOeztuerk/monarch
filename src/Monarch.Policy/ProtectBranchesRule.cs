namespace Monarch.Policy;

/// <summary>Regel <c>protect_branches</c> (ADR-0007): kein Push auf geschützte Branches, kein <c>gh</c>-Merge, Merge nur über <c>monarch gate merge</c>.</summary>
public static partial class ToolCallPolicy
{
    private static readonly string[] GraphQlMerges = ["mergePullRequest", "enablePullRequestAutoMerge", "enqueuePullRequest", "mergeBranch"];
    private static readonly string[] PushOptionsWithValue = ["--receive-pack", "--exec", "--push-option"];
    private const int MaxRepos = 16;

    private static PolicyDecision? ProtectBranches(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments, Canon paths)
    {
        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        PolicyDecision? ask = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            PolicyDecision? d = null;
            if (seg.Word == "git" && BashAnalysis.GitSubcommand(seg.Tokens, out var si) == "push") d = GitPush(ctx, paths, segments, i, si);
            else if (seg.Word == "gh") d = GhMerge(ctx, seg, cmd);
            if (d is null && call.AgentType is { Length: > 0 }) d = GateOnlyMain(seg);
            if (d?.Kind == PolicyDecisionKind.Deny) return d;
            ask ??= d;
        }

        return ask;
    }

    private static PolicyDecision BranchDeny(string reason) => PolicyDecision.Deny(PolicyRules.ProtectBranches, reason);

    private static PolicyDecision? ClassDecision(BranchClass c, string what) => c switch
    {
        BranchClass.Protected => BranchDeny($"{what} trifft einen geschützten Branch (protect_branches, ADR-0007). main/master merged nur der Owner; in den Integrations-Branch wird über 'monarch gate merge' gemerged."),
        BranchClass.Integration => PolicyDecision.Ask(PolicyRules.ProtectBranches, $"{what} trifft den Integrations-Branch. Gemerged wird über 'monarch gate merge', nicht per Push (ADR-0007)."),
        _ => null,
    };

    /// <summary><c>monarch gate merge|verdict</c> führt nur der Main aus, nie ein Shadow.</summary>
    private static PolicyDecision? GateOnlyMain(BashAnalysis.Segment seg)
    {
        var t = seg.Tokens;
        for (var i = 0; i < t.Length; i++)
        {
            if (Path.GetFileName(t[i]) != "monarch") continue;
            var sub = new List<string>(2);
            for (var j = i + 1; j < t.Length && sub.Count < 2; j++) if (!t[j].StartsWith('-')) sub.Add(t[j]);
            if (sub.Count > 1 && sub[0] == "gate" && sub[1] is "merge" or "verdict")
                return BranchDeny($"'monarch gate {sub[1]}' führt nur der Main aus, kein Shadow (ADR-0007).");
        }

        return null;
    }

    private static PolicyDecision? GitPush(PolicyContext ctx, Canon paths, List<BashAnalysis.Segment> all, int index, int subIndex)
    {
        var o = ctx.Options;
        var t = all[index].Tokens;
        var cDirs = new List<string>();
        string? gitDirOpt = null;
        for (var i = 1; i < subIndex; i++)
        {
            if (t[i] == "-C" && i + 1 < t.Length) cDirs.Add(t[++i]);
            else if (t[i] == "--git-dir" && i + 1 < t.Length) gitDirOpt = t[++i];
            else if (t[i].StartsWith("--git-dir=", StringComparison.Ordinal)) gitDirOpt = t[i]["--git-dir=".Length..];
            else if (t[i] is "-c" or "--work-tree") i++;
        }

        bool deleteMode = false, everything = false;
        string? repoOption = null;
        var positional = new List<string>();
        for (var i = subIndex + 1; i < t.Length; i++)
        {
            var a = t[i];
            if (a == "--") { positional.AddRange(t.Skip(i + 1)); break; }
            if (a.StartsWith("--", StringComparison.Ordinal))
            {
                if (a is "--all" or "--branches" or "--mirror") everything = true;
                else if (a == "--delete") deleteMode = true;
                else if (a == "--repo" && i + 1 < t.Length) repoOption = t[++i];
                else if (a.StartsWith("--repo=", StringComparison.Ordinal)) repoOption = a["--repo=".Length..];
                else if (Array.IndexOf(PushOptionsWithValue, a) >= 0) i++;
            }
            else if (a.Length > 1 && a[0] == '-')
            {
                if (a == "-o") i++;
                else if (a.AsSpan(1).Contains('d')) deleteMode = true;
            }
            else positional.Add(a);
        }

        _ = deleteMode; // Löschen nutzt dieselben Namen wie ein Push; --delete/-d ändert die Zielprüfung nicht
        if (everything)
            return BranchDeny("git push --all/--branches/--mirror trifft auch geschützte Branches (protect_branches, ADR-0007). Pushe den Branch ausdrücklich: 'git push origin <branch>'.");

        var remote = repoOption ?? (positional.Count > 0 ? positional[0] : null);
        var specs = new List<string>();
        for (var i = repoOption is null ? 1 : 0; i < positional.Count; i++)
        {
            if (positional[i] == "tag" && i + 1 < positional.Count) { i++; continue; } // 'tag <name>' ist refs/tags/<name>
            specs.Add(positional[i]);
        }

        List<BranchGuard.Repo>? repos = null;
        List<BranchGuard.Repo> Repos() => repos ??= FindRepos(ctx, paths, all, index, cDirs, gitDirOpt);

        PolicyDecision? ask = null;
        PolicyDecision? Note(PolicyDecision? d, ref PolicyDecision? acc)
        {
            if (d?.Kind == PolicyDecisionKind.Deny) return d;
            acc ??= d;
            return null;
        }

        PolicyDecision? Refspec(string spec)
        {
            var (src, dst) = BranchGuard.SplitRefspec(spec);
            if (BranchGuard.IsHead(dst) || (spec.IndexOf(':', StringComparison.Ordinal) < 0 && BranchGuard.IsHead(src)))
                return Current(Repos(), o, $"Ziel '{spec}' (aktueller Branch)");
            return ClassDecision(BranchGuard.Classify(o, dst), $"Das Ziel '{spec}'");
        }

        if (specs.Count > 0 || positional.Count > (repoOption is null ? 1 : 0))
        {
            foreach (var s in specs)
            {
                var d = Refspec(s);
                if (Note(d, ref ask) is { } deny) return deny;
            }

            return ask;
        }

        // Ohne Refspec: aktueller Branch, Upstream, remote.<r>.push/mirror und push.default aus der Config (Dateizugriff, kein Fork).
        foreach (var repo in Repos())
        {
            if (repo.Config is not { } cfg)
            {
                ask ??= PolicyDecision.Ask(PolicyRules.ProtectBranches, "git push ohne Refspec: die Git-Config ist nicht lesbar, das Ziel bleibt unbekannt (protect_branches, ADR-0007). Nenne den Branch ausdrücklich.");
                continue;
            }

            var cur = repo.Branch;
            var remoteName = remote ?? (cur is null ? null : cfg.Get($"branch.{cur}.pushremote")) ?? cfg.Get("remote.pushdefault") ?? (cur is null ? null : cfg.Get($"branch.{cur}.remote")) ?? "origin";
            if (string.Equals(cfg.Get($"remote.{remoteName}.mirror"), "true", StringComparison.OrdinalIgnoreCase))
                return BranchDeny($"remote.{remoteName}.mirror ist gesetzt: git push spiegelt alle Branches inklusive geschützter (protect_branches, ADR-0007).");
            var configured = cfg.GetAll($"remote.{remoteName}.push");
            if (configured.Count > 0)
            {
                foreach (var s in configured)
                {
                    var (src, dst) = BranchGuard.SplitRefspec(s);
                    var d = BranchGuard.IsHead(dst) || BranchGuard.IsHead(src) && s.IndexOf(':', StringComparison.Ordinal) < 0
                        ? Current([repo], o, $"remote.{remoteName}.push '{s}'")
                        : ClassDecision(BranchGuard.Classify(o, dst), $"remote.{remoteName}.push '{s}'");
                    if (Note(d, ref ask) is { } deny) return deny;
                }

                continue;
            }

            var mode = cfg.Get("push.default")?.ToLowerInvariant() ?? "simple";
            PolicyDecision? one;
            switch (mode)
            {
                case "nothing":
                    one = null;
                    break;
                case "matching":
                    return BranchDeny("push.default=matching pusht alle gleichnamigen Branches, auch geschützte (protect_branches, ADR-0007).");
                case "upstream" or "tracking":
                    one = cur is null ? Unknown() : cfg.Get($"branch.{cur}.merge") is { } merge ? ClassDecision(BranchGuard.Classify(o, merge), $"Der Upstream '{merge}' von '{cur}'") : null;
                    break;
                default:
                    one = Current([repo], o, "git push ohne Refspec (aktueller Branch)");
                    break;
            }

            if (Note(one, ref ask) is { } deny2) return deny2;
        }

        return ask;
    }

    private static PolicyDecision Unknown() =>
        PolicyDecision.Ask(PolicyRules.ProtectBranches, "git push: der aktuelle Branch ist nicht lesbar (detached HEAD?), das Ziel bleibt unbekannt (protect_branches, ADR-0007). Nenne den Branch ausdrücklich.");

    /// <summary>Wertet den aktuellen Branch aller Kandidaten-Repos aus; Deny vor Ask. Ohne Repo (git scheitert selbst) greift die Regel nicht.</summary>
    private static PolicyDecision? Current(List<BranchGuard.Repo> repos, PolicyOptions o, string what)
    {
        PolicyDecision? ask = null;
        foreach (var r in repos)
        {
            var d = r.Branch is null ? Unknown() : ClassDecision(BranchGuard.Classify(o, r.Branch), $"{what} '{r.Branch}'");
            if (d?.Kind == PolicyDecisionKind.Deny) return d;
            ask ??= d;
        }

        return ask;
    }

    private static List<BranchGuard.Repo> FindRepos(PolicyContext ctx, Canon paths, List<BashAnalysis.Segment> all, int index, List<string> cDirs, string? gitDirOpt)
    {
        var home = HomeOf(ctx);
        var result = new List<BranchGuard.Repo>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? gitDir)
        {
            if (gitDir is null || result.Count >= MaxRepos || !seen.Add(gitDir)) return;
            result.Add(BranchGuard.Load(gitDir, home));
        }

        var bases = new List<string>(paths.CwdAt(index));
        foreach (var c in cDirs)
        {
            var next = new List<string>();
            foreach (var b in bases)
                foreach (var v in paths.AbsFrom(c, b))
                    if (!next.Contains(v) && next.Count < MaxRepos) next.Add(v);
            if (next.Count > 0) bases = next;
        }

        // --git-dir und GIT_DIR (in diesem oder einem anderen Segment, z. B. 'export GIT_DIR=…; git push') bestimmen das Git-Verzeichnis ausdrücklich.
        var explicitDirs = new List<string>();
        if (gitDirOpt is not null) explicitDirs.Add(gitDirOpt);
        else
            foreach (var seg in all)
                for (var k = 0; k < seg.AssignmentNames.Count && k < seg.Assignments.Count; k++)
                    if (seg.AssignmentNames[k] == "GIT_DIR" && seg.Assignments[k].Length > 0) explicitDirs.Add(seg.Assignments[k]);

        if (explicitDirs.Count > 0)
        {
            foreach (var dir in explicitDirs)
                foreach (var b in bases)
                    foreach (var abs in paths.AbsFrom(dir, b))
                        if (Directory.Exists(abs)) Add(abs);
            return result;
        }

        foreach (var b in bases) Add(BranchGuard.FindGitDir(b));
        return result;
    }

    private static PolicyDecision? GhMerge(PolicyContext ctx, BashAnalysis.Segment seg, string command)
    {
        var t = seg.Tokens;
        var pr = Array.IndexOf(t, "pr");
        if (pr > 0 && Array.IndexOf(t, "merge", pr + 1) > pr)
            return BranchDeny("'gh pr merge' ist in jeder Form verboten. Gemerged wird nur über 'monarch gate merge' in den Integrations-Branch; main/master merged der Owner (protect_branches, ADR-0007).");
        var api = Array.IndexOf(t, "api");
        if (api <= 0) return null;

        foreach (var name in GraphQlMerges)
            if (command.Contains(name, StringComparison.OrdinalIgnoreCase))
                return BranchDeny($"GraphQL '{name}' über 'gh api' ist verboten. Gemerged wird nur über 'monarch gate merge' (protect_branches, ADR-0007).");

        string? method = null;
        var hasBody = false;
        for (var i = api + 1; i < t.Length; i++)
        {
            var a = t[i];
            if (a is "-X" or "--method") { if (i + 1 < t.Length) method = t[++i]; }
            else if (a.StartsWith("--method=", StringComparison.Ordinal)) method = a["--method=".Length..];
            else if (a.StartsWith("-X", StringComparison.Ordinal) && a.Length > 2) method = a[2..];
            else if (a is "-f" or "-F" or "--field" or "--raw-field" or "--input" || a.StartsWith("--field=", StringComparison.Ordinal)
                     || a.StartsWith("--raw-field=", StringComparison.Ordinal) || a.StartsWith("--input=", StringComparison.Ordinal)) hasBody = true;
        }

        var effective = (method ?? (hasBody ? "POST" : "GET")).ToUpperInvariant();
        PolicyDecision? ask = null;
        for (var i = api + 1; i < t.Length; i++)
        {
            if (t[i].StartsWith('-')) continue;
            var d = ApiEndpoint(ctx.Options, t[i], effective);
            if (d?.Kind == PolicyDecisionKind.Deny) return d;
            ask ??= d;
        }

        return ask;
    }

    private static PolicyDecision? ApiEndpoint(PolicyOptions o, string token, string method)
    {
        var s = token;
        var scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0)
        {
            var slash = s.IndexOf('/', scheme + 3);
            s = slash < 0 ? "" : s[slash..];
        }

        var cut = s.IndexOfAny(['?', '#']);
        if (cut >= 0) s = s[..cut];
        try { s = Uri.UnescapeDataString(s); }
        catch (UriFormatException) { /* unveränderter Text */ }
        var segs = s.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var k = 0; k < segs.Length; k++)
        {
            if (segs[k].Equals("pulls", StringComparison.OrdinalIgnoreCase) && k + 2 < segs.Length && segs[k + 2].Equals("merge", StringComparison.OrdinalIgnoreCase))
                return BranchDeny("'gh api …/pulls/<n>/merge' ist verboten. Gemerged wird nur über 'monarch gate merge' (protect_branches, ADR-0007).");
            if (k == segs.Length - 1 && segs[k].Equals("merges", StringComparison.OrdinalIgnoreCase) && k > 0)
                return BranchDeny("'gh api …/merges' ist verboten. Gemerged wird nur über 'monarch gate merge' (protect_branches, ADR-0007).");
            if (k + 3 < segs.Length && segs[k].Equals("git", StringComparison.OrdinalIgnoreCase) && segs[k + 1].Equals("refs", StringComparison.OrdinalIgnoreCase)
                && segs[k + 2].Equals("heads", StringComparison.OrdinalIgnoreCase) && method != "GET")
            {
                var branch = string.Join('/', segs[(k + 3)..]);
                if (ClassDecision(BranchGuard.Classify(o, branch), $"'gh api … git/refs/heads/{branch}' ({method})") is { } d) return d;
            }
        }

        return null;
    }
}
