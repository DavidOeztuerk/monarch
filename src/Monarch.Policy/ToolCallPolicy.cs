using Monarch.Domain;

namespace Monarch.Policy;

/// <summary>Annahme-Regeln für Quests (Penalty Zone).</summary>
public static class QuestAcceptancePolicy
{
    /// <summary>
    /// Im Modus <see cref="PenaltyMode.Block"/> werden Main und Side bei aktiver Penalty Zone verweigert;
    /// Penalty, Emergency, Daily (und Hidden) sind immer erlaubt. Im Modus Warn wird nie verweigert.
    /// </summary>
    public static bool CanAccept(QuestKind kind, bool penaltyZoneActive, PenaltyMode mode) =>
        !(penaltyZoneActive && mode == PenaltyMode.Block && kind is QuestKind.Main or QuestKind.Side);
}

/// <summary>
/// Tool-Call-Policy für den PreToolUse-Hook. Alle Regeln werden ausgewertet und mit der Rangfolge
/// Deny vor Ask vor Allow kombiniert (wie in Claude Code); Signale aller Regeln bleiben erhalten.
/// Reihenfolge der Regeln (bestimmt, welches Deny gemeldet wird): protect_paths, protect_secrets, deny_bash,
/// shadow_read_only, refuse_high_rank_in_bypass_mode, scope_lock, rank_s_approval.
/// Pfade werden relativ zu <see cref="PolicyContext.ProjectRoot"/> aufgelöst (nicht relativ zu <see cref="ToolCall.Cwd"/>).
/// </summary>
public static class ToolCallPolicy
{
    private static readonly string[] FileMutators = ["Edit", "Write", "MultiEdit", "NotebookEdit"];
    private static readonly string[] AlwaysWritable = [".monarch/recap/**", ".monarch/knowledge/**"];
    private static readonly string[] KnownModes = ["default", "plan", "acceptEdits", "auto", "dontAsk", "bypassPermissions"];

    private static readonly string[] MutatingWords =
        ["rm", "mv", "cp", "tee", "truncate", "dd", "chmod", "chown", "ln", "touch", "install", "rsync", "shred", "unlink"];

    private static readonly string[] SecretReaders =
        ["cat", "head", "tail", "less", "more", "grep", "rg", "awk", "sed", "jq", "strings", "xxd", "base64", "od", "source", ".",
         "cp", "cut", "nl", "tac", "sort", "bat", "column"];

    /// <summary>
    /// Bash-Kommandowörter, die als eindeutig lesend gelten (Heuristik, bewusst klein): ls cat head tail less more grep rg wc stat pwd echo
    /// printf which whoami date diff file tree du df sort uniq cut jq test [ true false cd basename dirname realpath sed awk (ohne -i), find
    /// (ohne -delete/-exec) sowie die Git-Unterkommandos status log diff show rev-parse ls-files check-ignore blame describe ls-tree rev-list
    /// shortlog grep. Alles andere, jede Umleitung nach <c>&gt;</c> (außer /dev/null und Dateideskriptoren) und unbekannte Kommandos gelten als mutierend.
    /// </summary>
    private static readonly string[] ReadOnlyWords =
        ["ls", "cat", "head", "tail", "less", "more", "grep", "rg", "wc", "stat", "pwd", "echo", "printf", "which", "whoami", "date", "diff",
         "file", "tree", "du", "df", "sort", "uniq", "cut", "jq", "test", "[", "true", "false", "cd", "basename", "dirname", "realpath",
         "sed", "awk", "find"];

    private static readonly string[] ReadOnlyGit =
        ["status", "log", "diff", "show", "rev-parse", "ls-files", "check-ignore", "blame", "describe", "ls-tree", "rev-list", "shortlog", "grep"];

    /// <summary>Wertet alle Regeln aus und kombiniert sie.</summary>
    public static PolicyDecision Evaluate(ToolCall call, PolicyContext context)
    {
        var results = new List<PolicyDecision>(7);
        void Add(PolicyDecision? d) { if (d is not null) results.Add(d); }
        var segments = call.ToolName == "Bash" && call.Command is { } cmd ? BashAnalysis.Segments(cmd) : [];
        Add(ProtectPaths(call, context, segments));
        Add(ProtectSecrets(call, context, segments));
        Add(DenyBash(call, context, segments));
        Add(ShadowReadOnly(call, context, segments));
        Add(RefuseHighRank(call, context));
        Add(ScopeLock(call, context));
        Add(RankS(call, context));
        return Combine(results);
    }

    /// <summary>Kombiniert Regelergebnisse: das erste Deny gewinnt vor dem ersten Ask vor Allow; die Signale aller Ergebnisse werden vereinigt.</summary>
    public static PolicyDecision Combine(IEnumerable<PolicyDecision> decisions)
    {
        PolicyDecision? deny = null, ask = null;
        var signals = new List<string>();
        foreach (var d in decisions)
        {
            foreach (var s in d.Signals) if (!signals.Contains(s)) signals.Add(s);
            if (d.Kind == PolicyDecisionKind.Deny) deny ??= d;
            else if (d.Kind == PolicyDecisionKind.Ask) ask ??= d;
        }

        return (deny ?? ask ?? PolicyDecision.Allow) with { Signals = signals };
    }

    /// <summary>True für Edit, Write, MultiEdit, NotebookEdit und für Bash, sofern das Kommando nicht eindeutig lesend ist (siehe Heuristik). Unbekannte Tools gelten als nicht mutierend.</summary>
    public static bool IsMutating(ToolCall call)
    {
        if (Array.IndexOf(FileMutators, call.ToolName) >= 0) return true;
        if (call.ToolName != "Bash") return false;
        if (string.IsNullOrWhiteSpace(call.Command)) return true;
        if (HasOutputRedirect(call.Command)) return true;
        var segments = BashAnalysis.Segments(call.Command);
        foreach (var seg in segments)
        {
            if (seg.Word == "git")
            {
                var sub = BashAnalysis.GitSubcommand(seg.Tokens, out _);
                if (sub is null || Array.IndexOf(ReadOnlyGit, sub) < 0) return true;
                continue;
            }

            if (Array.IndexOf(ReadOnlyWords, seg.Word) < 0) return true;
            if (seg.Word == "find" && seg.Tokens.Any(t => t is "-delete" or "-exec" or "-execdir" or "-ok" or "-fprint")) return true;
            if (seg.Word == "sed" && seg.Tokens.Any(t => t == "--in-place" || (t.StartsWith("-i", StringComparison.Ordinal)))) return true;
            if (seg.Word == "sort" && seg.Tokens.Any(t => t.StartsWith("-o", StringComparison.Ordinal))) return true;
        }

        return false;
    }

    private static bool HasOutputRedirect(string command)
    {
        foreach (var t in BashAnalysis.RedirectTargets(command, '>'))
            if (t != "/dev/null") return true;
        return false;
    }

    // ---- 1. protect_paths
    private static PolicyDecision? ProtectPaths(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
    {
        var globs = ctx.Options.ProtectPaths;
        if (globs.Count == 0) return null;
        if (Array.IndexOf(FileMutators, call.ToolName) >= 0 && call.FilePath is { } fp)
        {
            var rel = PathNormalizer.Normalize(ctx.ProjectRoot, fp);
            if (MatchesAny(globs, rel))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths, $"'{rel}' ist geschützt (Ledger/Shadow-Definition); nur die Engine schreibt dort.");
            return null;
        }

        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        var mentioned = BashAnalysis.PathTokens(cmd).Any(t => MentionsProtected(globs, ctx.ProjectRoot, t));
        if (!mentioned) return null;
        var mutating = segments.Any(IsMutatingSegment) || HasOutputRedirect(cmd);
        return mutating
            ? PolicyDecision.Deny(PolicyRules.ProtectPaths, "Das Kommando verändert einen geschützten Pfad (Ledger/Shadow-Definition).")
            : null;
    }

    private static bool MentionsProtected(IReadOnlyList<string> globs, string root, string token)
    {
        if (token.StartsWith('-')) return false;
        var rel = PathNormalizer.Normalize(root, token);
        if (rel.Length == 0) return false;
        foreach (var g in globs) if (GlobMatcher.IsMatchOrAncestor(g, rel)) return true;
        return false;
    }

    private static bool IsMutatingSegment(BashAnalysis.Segment s)
    {
        if (Array.IndexOf(MutatingWords, s.Word) >= 0) return true;
        if (s.Word == "sed") return s.Tokens.Any(t => t == "--in-place" || t.StartsWith("-i", StringComparison.Ordinal));
        if (s.Word == "git")
        {
            var sub = BashAnalysis.GitSubcommand(s.Tokens, out _);
            return sub == "restore" || (sub == "checkout" && s.Tokens.Contains("--"));
        }

        return false;
    }

    // ---- 2. protect_secrets
    private static PolicyDecision? ProtectSecrets(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
    {
        if (call.ToolName == "Read" && call.FilePath is { } fp)
            return IsSecret(ctx, PathNormalizer.Normalize(ctx.ProjectRoot, fp))
                ? PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Credential-Dateien liest Monarch nie. Nutze Existenz-Prüfungen (ls, test) oder frage den Guild Master.")
                : null;
        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        foreach (var seg in segments)
        {
            if (Array.IndexOf(SecretReaders, seg.Word) < 0) continue;
            for (var i = 1; i < seg.Tokens.Length; i++)
                foreach (var t in BashAnalysis.PathTokens(seg.Tokens[i]))
                    if (IsSecretToken(ctx, t))
                        return PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Das Kommando liest eine Credential-Datei. Metadaten (ls, stat, test) sind erlaubt, Inhalte nicht.");
        }

        foreach (var t in BashAnalysis.RedirectTargets(cmd, '<'))
            if (IsSecretToken(ctx, t))
                return PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Das Kommando liest eine Credential-Datei per Umleitung.");
        return null;
    }

    private static bool IsSecretToken(PolicyContext ctx, string token) =>
        !token.StartsWith('-') && PathNormalizer.Normalize(ctx.ProjectRoot, token) is { Length: > 0 } rel && IsSecret(ctx, rel);

    private static bool IsSecret(PolicyContext ctx, string rel)
    {
        if (MatchesAny(ctx.Options.SecretExceptions, rel)) return false;
        return MatchesAny(PolicyOptions.DefaultSecretGlobs, rel) || MatchesAny(ctx.Options.ExtraSecretGlobs, rel);
    }

    // ---- 3. deny_bash
    private static PolicyDecision? DenyBash(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
    {
        foreach (var seg in segments)
            foreach (var pattern in ctx.Options.DenyBash)
                if (GlobMatcher.WildcardMatch(pattern, seg.Canonical) || GlobMatcher.WildcardMatch(pattern, seg.Text))
                    return PolicyDecision.Deny(PolicyRules.DenyBash, $"Das Kommando '{seg.Canonical}' ist verboten (deny_bash: {pattern}).");
        return null;
    }

    // ---- 4. Shadow read-only classes (ADR-0005)
    private static PolicyDecision? ShadowReadOnly(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
    {
        if (call.AgentType is not { Length: > 0 } agent || !ctx.Options.ReadOnlyShadowTypes.Contains(agent)) return null;
        if (Array.IndexOf(FileMutators, call.ToolName) >= 0)
            return PolicyDecision.Deny(PolicyRules.ShadowReadOnly, $"Shadow '{agent}' ist read-only und darf keine Dateien ändern.");
        foreach (var seg in segments)
            if (BashAnalysis.GitSubcommand(seg.Tokens, out _) is "commit" or "push" or "merge")
                return PolicyDecision.Deny(PolicyRules.ShadowReadOnly, $"Shadow '{agent}' ist read-only und darf nicht committen, pushen oder mergen.");
        return null;
    }

    // ---- 5. refuse_high_rank_in_bypass_mode (ADR-0002)
    private static PolicyDecision? RefuseHighRank(ToolCall call, PolicyContext ctx)
    {
        if (!ctx.Options.RefuseHighRankInBypassMode || ctx.ActiveQuest is not { } quest) return null;
        var mode = call.PermissionMode ?? "";
        var known = Array.IndexOf(KnownModes, mode) >= 0;
        var signals = known ? [] : new[] { PolicySignals.UnknownPermissionMode };
        var refuse = mode switch
        {
            "bypassPermissions" or "dontAsk" => quest.Rank >= Rank.A,
            "auto" or "acceptEdits" => quest.Rank >= Rank.S,
            _ => false,
        };
        if (refuse && IsMutating(call))
            return PolicyDecision.Deny(PolicyRules.RefuseHighRankInBypassMode,
                $"Rank {quest.Rank}: mutierende Tools sind im Permission-Mode '{mode}' verboten. Wechsle in den Modus default.") with { Signals = signals };
        return signals.Length == 0 ? null : PolicyDecision.Allow with { Signals = signals };
    }

    // ---- 6. Scope lock
    private static PolicyDecision? ScopeLock(ToolCall call, PolicyContext ctx)
    {
        if (!ctx.Options.ScopeLock || ctx.ActiveQuest is not { } quest || Array.IndexOf(FileMutators, call.ToolName) < 0) return null;
        var rel = call.FilePath is { Length: > 0 } fp ? PathNormalizer.Normalize(ctx.ProjectRoot, fp) : "";
        if (rel.Length > 0 && MatchesAny(AlwaysWritable, rel)) return null;
        if (rel.Length > 0 && MatchesAny(quest.Scope.Include, rel) && !MatchesAny(quest.Scope.Exclude, rel)) return null;
        return PolicyDecision.Deny(PolicyRules.ScopeLock,
            $"'{(rel.Length == 0 ? call.FilePath : rel)}' liegt außerhalb des Scopes von {quest.Id.Value}. Double-Dungeon-Signal: Replan nötig.")
            with { Signals = [PolicySignals.DoubleDungeon] };
    }

    // ---- 7. Rank S (ADR-0003)
    private static PolicyDecision? RankS(ToolCall call, PolicyContext ctx) =>
        ctx.ActiveQuest is { Rank: Rank.S } && IsMutating(call)
            ? PolicyDecision.Ask(PolicyRules.RankSApproval, "Rank S: Freigabe nötig. Stoppe diesen Schritt und melde ihn dem Guild Master.")
            : null;

    private static bool MatchesAny(IReadOnlyList<string> globs, string path)
    {
        for (var i = 0; i < globs.Count; i++) if (GlobMatcher.IsMatch(globs[i], path)) return true;
        return false;
    }
}
