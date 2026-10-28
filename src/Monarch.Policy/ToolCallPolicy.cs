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
/// Pfade werden kanonisiert (relativ zu <see cref="ToolCall.Cwd"/>, <c>.</c>/<c>..</c>/<c>//</c> normalisiert, Symlinks aufgelöst, Wurzel und Ziel gleich);
/// der Vergleich gegen geschützte Pfade und Secret-Globs ist case-insensitiv.
/// </summary>
public static partial class ToolCallPolicy
{
    /// <summary>Home-Verzeichnis des Kontexts (<see cref="PolicyContext.HomeDir"/>) oder <c>$HOME</c> des Prozesses.</summary>
    private static string? HomeOf(PolicyContext ctx) => ctx.HomeDir ?? Environment.GetEnvironmentVariable("HOME");

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
        var paths = new Canon(call, context, segments);
        Add(ProtectPaths(call, context, segments, paths));
        Add(ProtectSecrets(call, context, segments, paths));
        Add(DenyBash(call, context, segments));
        Add(ShadowReadOnly(call, context, segments));
        Add(RefuseHighRank(call, context));
        Add(ScopeLock(call, context, paths));
        Add(RankS(call, context));
        return Combine(results);
    }

    /// <summary>
    /// Nur protect_paths und protect_secrets, kombiniert (Deny vor Ask). Für Projekte, die nur als Ziel eines Tool-Calls vorkommen
    /// (das cwd-Projekt wertet <see cref="Evaluate"/> vollständig aus).
    /// </summary>
    public static PolicyDecision EvaluateProtection(ToolCall call, PolicyContext context)
    {
        var segments = call.ToolName == "Bash" && call.Command is { } cmd ? BashAnalysis.Segments(cmd) : [];
        var paths = new Canon(call, context, segments);
        var results = new List<PolicyDecision>(2);
        if (ProtectPaths(call, context, segments, paths) is { } a) results.Add(a);
        if (ProtectSecrets(call, context, segments, paths) is { } b) results.Add(b);
        return Combine(results);
    }

    /// <summary>
    /// Kanonisierung eines Tool-Calls: Resolver (Wurzel, cwd) und die Verzeichnisse, gegen die relative Bash-Pfade gelten (cwd und <c>cd</c>/<c>pushd</c>/<c>git -C</c>-Ziele).
    /// <c>cd</c>-Ziele werden logisch und physisch aufgelöst (ADR-0011 Regel 3); beide gelten als Basis.
    /// </summary>
    private sealed class Canon
    {
        private const int MaxCandidates = 16;
        private readonly PathResolver _rs;
        private readonly List<string> _bases;
        private readonly string[][] _cwdAt;
        private readonly PolicyContext _ctx;
        private bool? _bareMonarch;

        public Canon(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
        {
            _ctx = ctx;
            _rs = new PathResolver(ctx.ProjectRoot, call.Cwd);
            var start = new List<string> { _rs.Cwd };
            if (call.Cwd.StartsWith('/') && PathResolver.Lexical(call.Cwd, "/") is var lex && !start.Contains(lex)) start.Add(lex);
            _bases = [.. start];
            _cwdAt = new string[segments.Count][];

            // cwd-Modell: aktuelle Kandidaten, OLDPWD (cd -), Verzeichnisstapel (pushd/popd) und die Zustände vor offenen Subshells.
            // Wo das Modell unsicher ist, gilt die Vereinigung aller möglichen Verzeichnisse.
            var cur = start;
            var prev = start;
            var stack = new List<List<string>>();
            var saved = new Stack<(List<string> Cur, List<string> Prev, List<List<string>> Stack)>();
            for (var i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                foreach (var ev in seg.Events)
                {
                    if (ev == '(') saved.Push((cur, prev, [.. stack]));
                    else if (saved.Count > 0) (cur, prev, stack) = saved.Pop();
                }

                _cwdAt[i] = [.. cur];
                switch (seg.Word)
                {
                    case "cd":
                    {
                        var target = BashAnalysis.CdTarget(seg);
                        var next = target == "-" ? prev : Resolve(target, cur);
                        (prev, cur) = (cur, next);
                        break;
                    }

                    case "pushd":
                    {
                        var operands = seg.Tokens.Skip(1).Count(t => !t.StartsWith('-') || t.Length == 1);
                        List<string> next;
                        if (operands == 0) next = Union(cur, stack.Count > 0 ? stack[^1] : cur); // vertauscht die obersten Einträge
                        else next = Resolve(BashAnalysis.CdTarget(seg), cur);
                        stack.Add(cur);
                        (prev, cur) = (cur, next);
                        break;
                    }

                    case "popd":
                    {
                        if (stack.Count == 0) break;
                        List<string> next;
                        if (seg.Tokens.Length > 1)
                        {
                            next = cur; // popd +N/-N: unklar, welcher Eintrag zählt
                            foreach (var e in stack) next = Union(next, e);
                        }
                        else
                        {
                            next = stack[^1];
                            stack.RemoveAt(stack.Count - 1);
                        }

                        (prev, cur) = (cur, next);
                        break;
                    }

                    default:
                        foreach (var (dir, changes) in BashAnalysis.DirectoryTargets([seg]))
                            if (!changes)
                                foreach (var c in cur)
                                    foreach (var v in AbsFrom(dir, c))
                                        if (!_bases.Contains(v)) _bases.Add(v);
                        break;
                }

                foreach (var v in cur) if (!_bases.Contains(v)) _bases.Add(v);
            }
        }

        private List<string> Resolve(string target, List<string> from)
        {
            var next = new List<string>();
            foreach (var c in from)
                foreach (var v in AbsFrom(target, c))
                    if (!next.Contains(v) && next.Count < MaxCandidates) next.Add(v);
            return next.Count > 0 ? next : from;
        }

        private static List<string> Union(List<string> a, List<string> b)
        {
            var r = new List<string>(a);
            foreach (var v in b) if (!r.Contains(v) && r.Count < MaxCandidates) r.Add(v);
            return r;
        }

        /// <summary>
        /// True, wenn ein nacktes <c>monarch</c> über PATH (<see cref="PolicyContext.PathEnv"/> oder <c>$PATH</c>, ohne Fork) auf das installierte Binary
        /// <c>~/.monarch/bin/monarch</c> bzw. <c>~/.monarch/bin/&lt;version&gt;/monarch</c> auflöst. Der erste Treffer zählt (wie in der Shell); ohne PATH,
        /// ohne Treffer oder bei einem anderen Treffer ist es nicht das Binary (fail-closed).
        /// </summary>
        public bool BareMonarchIsBinary()
        {
            if (_bareMonarch is { } known) return known;
            var result = false;
            var path = _ctx.PathEnv ?? Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path) && Home is not null)
            {
                foreach (var entry in path.Split(':'))
                {
                    var candidate = _rs.Resolve((entry.Length == 0 ? "." : entry) + "/monarch", _rs.Cwd);
                    if (!File.Exists(candidate)) continue;
                    result = IsInstalledBinary(candidate);
                    break;
                }
            }

            _bareMonarch = result;
            return result;
        }

        /// <summary>True, wenn der (kanonische) absolute Pfad <c>~/.monarch/bin/[&lt;version&gt;/]monarch</c> ist.</summary>
        public bool IsInstalledBinary(string abs)
        {
            if (Home is not { } home) return false;
            var prefix = PathFold.Fold(home).TrimEnd('/') + "/.monarch/bin/";
            var folded = PathFold.Fold(abs);
            if (!folded.StartsWith(prefix, StringComparison.Ordinal)) return false;
            var rest = folded[prefix.Length..].Split('/');
            return rest is ["monarch"] or [_, "monarch"];
        }

        /// <summary>Kanonische Projektwurzel.</summary>
        public string Root => _rs.Root;

        /// <summary>Kanonisches Home-Verzeichnis oder null.</summary>
        public string? Home =>
            HomeOf(_ctx) is { Length: > 1 } home && home.StartsWith('/') ? Canonical(home) : null;

        /// <summary>Wirksame Arbeitsverzeichnisse (logisch und physisch) vor dem Segment <paramref name="index"/>.</summary>
        public string[] CwdAt(int index) => _cwdAt[index];

        /// <summary>Kanonischer absoluter Pfad eines Datei-Tool-Ziels (ohne Tilde-Expansion), aufgelöst gegen das cwd.</summary>
        public string AbsFile(string path) => _rs.Resolve(path, _rs.Cwd);

        /// <summary>Kanonischer absoluter Pfad (Tilde und <c>$HOME</c> expandiert), aufgelöst gegen das cwd.</summary>
        public string Abs(string path) => _rs.Resolve(PathResolver.ExpandVars(path, _rs.Cwd, _rs.Root), _rs.Cwd);

        /// <summary>
        /// Absolute Pfade eines Tokens relativ zu <paramref name="baseDir"/>: physisch aufgelöst, dazu logisch (lexikalisch) und dieser logische Pfad physisch aufgelöst.
        /// Tilde, <c>$HOME</c>, <c>$PWD</c> und das Git-Toplevel sind expandiert.
        /// </summary>
        public IEnumerable<string> AbsFrom(string token, string baseDir)
        {
            var t = PathResolver.ExpandVars(token, baseDir, _rs.Root);
            var phys = _rs.Resolve(t, baseDir);
            yield return phys;
            var lex = PathResolver.Lexical(t, baseDir);
            if (lex == phys) yield break;
            yield return lex;
            var both = _rs.Resolve(lex, "/");
            if (both != phys && both != lex) yield return both;
        }

        /// <summary>Kanonischer absoluter Pfad, aufgelöst gegen jedes Basisverzeichnis (konservativ: jedes zählt).</summary>
        public IEnumerable<string> AbsAll(string token)
        {
            foreach (var b in _bases)
                foreach (var a in AbsFrom(token, b)) yield return a;
        }

        /// <summary>Kanonisches Verzeichnis (absolut, Symlinks aufgelöst), z. B. das Home-Verzeichnis.</summary>
        public string Canonical(string absolute) => _rs.Resolve(absolute, "/");

        /// <summary>
        /// Zerlegt ein Glob-Token am ersten Wildcard in das kanonische Verzeichnis des literalen Präfixes (je Basisverzeichnis) und den
        /// unvollständigen letzten Namensteil davor (gefaltet).
        /// </summary>
        public IEnumerable<(string Dir, string Name)> GlobParts(string token)
        {
            var prefix = token[..token.AsSpan().IndexOfAny('*', '?', '[')];
            var slash = prefix.LastIndexOf('/');
            var dir = slash < 0 ? "" : slash == 0 ? "/" : prefix[..slash];
            var name = PathFold.Fold(prefix[(slash + 1)..]);
            foreach (var b in _bases)
                foreach (var d in AbsFrom(dir.Length == 0 ? "." : dir, b)) yield return (d, name);
        }

        /// <summary>Pfad relativ zur Wurzel, aufgelöst gegen das cwd.</summary>
        public string Rel(string path) => _rs.Relative(path);

        /// <summary>Pfad relativ zur Wurzel, aufgelöst gegen jedes Basisverzeichnis (konservativ: jedes zählt).</summary>
        public IEnumerable<string> RelAll(string token)
        {
            var t = PathResolver.ExpandTilde(token);
            foreach (var b in _bases) yield return _rs.Relative(t, b);
        }
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
            if (seg.Tokens.Length == 0) continue; // reine Zuweisung oder Umleitung (Umleitungen prüft HasOutputRedirect)
            if (HiddenWriter(seg.Word, seg.Tokens)) return true;
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

    // ---- 2. protect_secrets
    private static PolicyDecision? ProtectSecrets(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments, Canon paths)
    {
        if (call.ToolName == "Read" && call.FilePath is { } fp)
            return IsSecret(ctx, paths.Rel(fp))
                ? PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Credential-Dateien liest Monarch nie. Nutze Existenz-Prüfungen (ls, test) oder frage den Guild Master.")
                : null;
        if (call.ToolName == "Grep")
        {
            var deniedPath = call.FilePath is { Length: > 0 } gp && IsSecret(ctx, paths.Rel(gp));
            if (deniedPath || (call.Glob is { Length: > 0 } glob && GlobTargetsSecret(ctx, glob)))
                return PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Grep liest Dateiinhalte: Credential-Dateien (Pfad oder Glob) sind tabu. Nutze Glob für Namen oder frage den Guild Master.");
            return null;
        }

        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        foreach (var seg in segments)
        {
            if (Array.IndexOf(SecretReaders, seg.Word) < 0) continue;
            for (var i = 1; i < seg.Tokens.Length; i++)
                foreach (var t in BashAnalysis.PathTokens(seg.Tokens[i]))
                    if (IsSecretToken(ctx, paths, t))
                        return PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Das Kommando liest eine Credential-Datei. Metadaten (ls, stat, test) sind erlaubt, Inhalte nicht.");
        }

        foreach (var t in BashAnalysis.RedirectTargets(cmd, '<'))
            if (IsSecretToken(ctx, paths, t))
                return PolicyDecision.Deny(PolicyRules.ProtectSecrets, "Das Kommando liest eine Credential-Datei per Umleitung.");
        return null;
    }

    /// <summary>
    /// True, wenn ein Grep-Glob Dateinamen einer Secret-Glob-Familie treffen kann. Je Secret-Glob wird dessen Dateiname instanziiert
    /// (<c>*</c> durch <c>x</c>, plus die Variante ohne Wildcard-Teil) und gegen den Grep-Glob als Muster geprüft.
    /// Reine Wildcard-Globs (<c>*</c>, <c>**</c>, <c>**/*</c>) sind ausgenommen: ripgrep überspringt versteckte Dateien und sie wären sonst jede Suche.
    /// </summary>
    private static bool GlobTargetsSecret(PolicyContext ctx, string glob)
    {
        var g = glob.Replace('\\', '/').Trim();
        if (g.Length == 0 || g.All(c => c is '*' or '/' or '?')) return false;
        var name = g[(g.LastIndexOf('/') + 1)..];
        if (name.Length == 0) return false;
        foreach (var secret in PolicyOptions.DefaultSecretGlobs.Concat(ctx.Options.ExtraSecretGlobs))
        {
            var sName = secret[(secret.LastIndexOf('/') + 1)..];
            if (GlobMatcher.WildcardMatch(name, sName.Replace("*", "x", StringComparison.Ordinal))
                || GlobMatcher.WildcardMatch(name, sName.Replace(".*", "", StringComparison.Ordinal).Replace("*", "", StringComparison.Ordinal))
                || (sName.Contains('*', StringComparison.Ordinal) && GlobMatcher.WildcardMatch(sName, name.Replace("*", "x", StringComparison.Ordinal))))
                return !MatchesAny(ctx.Options.SecretExceptions, name);
        }

        return false;
    }

    private static bool IsSecretToken(PolicyContext ctx, Canon paths, string token) =>
        !token.StartsWith('-') && paths.RelAll(token).Any(rel => rel.Length > 0 && IsSecret(ctx, rel));

    private static bool IsSecret(PolicyContext ctx, string rel)
    {
        if (MatchesAnyCI(ctx.Options.SecretExceptions, rel)) return false;
        return MatchesAnyCI(PolicyOptions.DefaultSecretGlobs, rel) || MatchesAnyCI(ctx.Options.ExtraSecretGlobs, rel);
    }

    // ---- 3. deny_bash
    private static PolicyDecision? DenyBash(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments)
    {
        foreach (var seg in segments)
            if (OwnerCommand(seg) is { } owner)
                return PolicyDecision.Deny(PolicyRules.DenyBash,
                    $"Regel 7 (ADR-0011): '{owner}' ist dem Owner vorbehalten (deny_bash: {owner}); der Agent führt es nie aus. Melde dem Guild Master, was nötig ist.");
        foreach (var seg in segments)
            foreach (var pattern in ctx.Options.DenyBash)
                if (GlobMatcher.WildcardMatch(pattern, seg.Canonical) || GlobMatcher.WildcardMatch(pattern, seg.Text))
                    return PolicyDecision.Deny(PolicyRules.DenyBash, $"Das Kommando '{seg.Canonical}' ist verboten (deny_bash: {pattern}).");
        return null;
    }

    private static readonly string[] ReadingGit = ["add", "commit", "diff", "log", "show", "blame", "status", "ls-files", "grep", "mv", "rm"];

    /// <summary>
    /// Owner-Befehle (ADR-0011 Regel 7): ein Token mit Basename <c>monarch</c> irgendwo im Segment, gefolgt von <c>ledger ack-reset</c>, <c>permit</c> oder
    /// <c>promote</c>, bzw. ein Token mit Basename <c>install-local.sh</c>. Lesende Kommandos (cat, grep, git add/diff/…) nennen das Skript nur und gelten nicht.
    /// </summary>
    private static string? OwnerCommand(BashAnalysis.Segment seg)
    {
        var t = seg.Tokens;
        for (var i = 0; i < t.Length; i++)
        {
            if (Path.GetFileName(t[i]) == "monarch")
            {
                var sub = new List<string>(2);
                for (var j = i + 1; j < t.Length && sub.Count < 2; j++) if (!t[j].StartsWith('-')) sub.Add(t[j]);
                if (sub.Count > 0 && sub[0] is "permit" or "promote") return "monarch " + sub[0];
                if (sub.Count > 1 && sub[0] == "ledger" && sub[1] == "ack-reset") return "monarch ledger ack-reset";
            }
        }

        if (!t.Any(x => Path.GetFileName(x) == "install-local.sh")) return null;
        if (Array.IndexOf(ReadOnlyWords, seg.Word) >= 0) return null;
        if (seg.Word == "git" && BashAnalysis.GitSubcommand(t, out _) is { } g && Array.IndexOf(ReadingGit, g) >= 0) return null;
        return "install-local.sh";
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
    private static PolicyDecision? ScopeLock(ToolCall call, PolicyContext ctx, Canon paths)
    {
        if (!ctx.Options.ScopeLock || ctx.ActiveQuest is not { } quest || Array.IndexOf(FileMutators, call.ToolName) < 0) return null;
        var rel = call.FilePath is { Length: > 0 } fp ? paths.Rel(fp) : "";
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

    private static bool MatchesAnyCI(IReadOnlyList<string> globs, string path)
    {
        if (globs.Count == 0) return false;
        var lower = PathFold.Fold(path);
        for (var i = 0; i < globs.Count; i++) if (GlobMatcher.IsMatch(PathFold.Fold(globs[i]), lower)) return true;
        return false;
    }

    private static bool MatchesAny(IReadOnlyList<string> globs, string path)
    {
        for (var i = 0; i < globs.Count; i++) if (GlobMatcher.IsMatch(globs[i], path)) return true;
        return false;
    }
}
