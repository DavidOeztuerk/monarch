namespace Monarch.Policy;

/// <summary>
/// Regel <c>protect_paths</c> (Q-0118). Zwei Zonen: die Projektzone (<see cref="PolicyOptions.ProtectPaths"/>, relativ zur Projektwurzel) und die globalen
/// Schutzpfade (<c>~/.monarch/**</c>, <c>~/.claude/settings*.json</c>, <c>~/.claude/plugins/**</c>, dazu <c>$MONARCH_HOME</c>), die unabhängig vom Projekt gelten,
/// sobald ein erwecktes Projekt im Spiel ist (die Policy läuft nur für solche Projekte). Gesperrt wird nur das Mutieren, Lesen bleibt erlaubt.
/// <para>
/// Bash ist fail-closed: Pfad-Tokens entstehen auf dem quote-bereinigten Text; Glob-Tokens treffen, wenn ihr literales Präfix bis zum ersten Wildcard
/// Präfix eines geschützten Pfads ist oder umgekehrt (ein leeres Präfix trifft keine Dotfiles, wie in der Shell ohne dotglob); ein zerstörerisches
/// Kommando (rm, mv-Quellen, git rm/clean/checkout/restore/..., find -delete/-exec, rsync --delete, chmod -R) trifft auch, wenn sein Ziel ein
/// Vorfahr eines geschützten Pfads ist (Wurzel, <c>.monarch</c>, <c>..</c>); Interpreter mit Inline-Code oder stdin-Code, der <c>.monarch</c> erwähnt, sind verboten.
/// Seit Q-0122 (ADR-0011): Das Kommando wird strukturell zerlegt (<see cref="ShellLexer"/>: Schlüsselwörter, Escapes, <c>$'…'</c>, Braces, Heredocs); jedes
/// nicht als lesend bekannte Segment, das einen Schutzpfad oder eine fremde <c>.monarch</c>-Komponente nennt, ist deny (Regel 2, Ausnahmen: Monarch-Binary,
/// <c>git add/commit/push</c>, Text-Werte von <c>-m</c>/<c>--body</c>); Schreiben ins cwd ohne Zielpfad (Regel 3), Links auf Schutzzonen (Regel 4),
/// pfadlose Arbeitsbaum-Mutationen (Regel 5, ask) und ein neues <c>.monarch</c> (Regel 6) sind geprüft; alle Vergleiche laufen gefaltet (<see cref="PathFold"/>, Regel 8).
/// Dokumentierte Grenzen: Interpreter-Skriptdateien, zur Laufzeit zusammengesetzte Pfade (Variablen, Befehlsersetzung) und Netzwerk-APIs.
/// </para>
/// <para>
/// Monarchs eigene Schreibvorgänge (Engine, Statusline, Wrapper-Download) laufen im Binary bzw. im Hook-Wrapper und sind keine Tool-Calls des Agenten;
/// der Aufruf des Binaries (<c>monarch quest accept</c>) enthält kein mutierendes Kommandowort und bleibt erlaubt.
/// </para>
/// </summary>
public static partial class ToolCallPolicy
{
    private static readonly string[] GlobalProtectGlobs = [".monarch/**", ".claude/settings*.json", ".claude/plugins/**"];
    private static readonly string[] DeleteWords = ["rm", "rmdir", "unlink", "shred"];
    private static readonly string[] MutatingGitSubcommands = ["rm", "clean", "checkout", "restore", "stash", "reset", "mv", "apply", "am", "update-index"];
    private static readonly string[] ExtraMutatingWords = ["rmdir", "chgrp", "patch", "ditto"];
    private static readonly string[] ShellInert =
        [":", "export", "unset", "local", "declare", "typeset", "readonly", "alias", "type", "read", "wait", "shift", "set", "exit", "return", "break", "continue", "sleep", "pushd", "popd"];
    private static readonly string[] InterpreterMentions = [".monarch", ".claude"];
    private static readonly string[] FindExecFlags = ["-exec", "-execdir", "-ok", "-okdir"];
    private static readonly string[] XargsValueFlags = ["-I", "-n", "-P", "-L", "-s", "-d", "-E", "-J", "-a"];

    /// <summary>Eine Schutzzone: Basisverzeichnis (kanonisch) und Globs relativ dazu. Alle Vergleiche laufen auf gefalteten Pfaden (<see cref="PathFold"/>).</summary>
    private sealed class Zone
    {
        private readonly string _base;
        private readonly string[] _globs;
        private readonly string[] _literals;
        private readonly string[] _shown;

        public Zone(string baseDir, IReadOnlyList<string> globs, string display)
        {
            Base = baseDir;
            _base = PathFold.Fold(baseDir);
            _globs = [.. globs.Select(PathFold.Fold)];
            _shown = [.. globs.Select(g => display + g)];
            _literals = [.. _globs.Select(g => { var i = g.AsSpan().IndexOfAny('*', '?', '['); return i < 0 ? g : g[..i]; })];
        }

        public string Base { get; }

        /// <summary>Gefalteter Pfad relativ zur Zone, "" für die Zone selbst, null außerhalb.</summary>
        public string? Rel(string abs) => RelFolded(PathFold.Fold(abs));

        private string? RelFolded(string f)
        {
            if (f == _base) return "";
            var prefix = _base == "/" ? "/" : _base + "/";
            return f.StartsWith(prefix, StringComparison.Ordinal) ? f[prefix.Length..] : null;
        }

        /// <summary>Anzeige: relativ zur Zone, sonst absolut.</summary>
        public string Show(string abs) => Rel(abs) is { Length: > 0 } r ? r : abs;

        /// <summary>Treffer-Muster, wenn der Pfad geschützt ist (<paramref name="exact"/>: nur Pfade, die ein Muster erfüllen; sonst auch Vorfahren der Muster).</summary>
        public string? Hit(string abs, bool destructive, bool exact)
        {
            var f = PathFold.Fold(abs);
            var rel = RelFolded(f);
            if (rel is null) return destructive && _base.StartsWith(f == "/" ? "/" : f + "/", StringComparison.Ordinal) ? _shown[0] : null;
            if (rel.Length == 0) return destructive ? _shown[0] : null;
            for (var i = 0; i < _globs.Length; i++)
                if (exact ? GlobMatcher.IsMatch(_globs[i], rel) : GlobMatcher.IsMatchOrAncestor(_globs[i], rel)) return _shown[i];
            return null;
        }

        /// <summary>
        /// Beweis zu <see cref="HitGlob"/> (ADR-0014 1c): True, wenn das Muster einen String s erzeugen kann, für den <see cref="Hit"/> trifft
        /// (Zone oder Vorfahren der Zone). Außerhalb der Projekt-Root bleibt es beim Vorfilter.
        /// </summary>
        internal bool CanReach(string dir, FlatPattern pat, bool destructive)
        {
            var rel = RelFolded(PathFold.Fold(dir));
            if (rel is null) return true;
            var dp = rel.Length == 0 ? "" : rel + "/";
            if (destructive && dp.Length == 0 && pat.Matches("")) return true;
            for (var i = 0; i < _literals.Length; i++)
            {
                var lit = _literals[i];
                if (lit.Length == 0) { if (_globs[i] == "**") return true; continue; }
                if (lit.StartsWith(dp, StringComparison.Ordinal))
                {
                    if (pat.CanStartWith(lit[dp.Length..])) return true;
                    for (var k = lit.IndexOf('/', StringComparison.Ordinal); k > 0; k = lit.IndexOf('/', k + 1))
                        if (lit[..k].StartsWith(dp, StringComparison.Ordinal) && pat.Matches(lit[dp.Length..k])) return true;
                }
                else if (dp.StartsWith(lit, StringComparison.Ordinal)) return true;
            }

            return false;
        }

        public string AnyShown => _shown[0];

        /// <summary>Treffer-Muster für ein Glob-Token: <paramref name="dir"/> (kanonisch) plus unvollständiger Namensteil <paramref name="name"/> (gefaltet).</summary>
        public string? HitGlob(string dir, string name, bool destructive)
        {
            var f = PathFold.Fold(dir);
            var rel = RelFolded(f);
            if (rel is null)
            {
                var full = (f == "/" ? "/" : f + "/") + name;
                return destructive && (_base + "/").StartsWith(full, StringComparison.Ordinal) ? _shown[0] : null;
            }

            var prefix = (rel.Length == 0 ? "" : rel + "/") + name;
            for (var i = 0; i < _literals.Length; i++)
            {
                var lit = _literals[i];
                if (lit.Length == 0)
                {
                    if (_globs[i] == "**") return _shown[i];
                    continue;
                }

                if (lit.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (name.Length == 0 && lit.Length > prefix.Length && lit[prefix.Length] == '.') continue;
                    return _shown[i];
                }

                if (prefix.StartsWith(lit, StringComparison.Ordinal)) return _shown[i];
            }

            return null;
        }
    }

    private static List<Zone> Zones(PolicyContext ctx, Canon paths)
    {
        var zones = new List<Zone>(3);
        if (ctx.Options.ProtectPaths.Count > 0) zones.Add(new Zone(paths.Root, ctx.Options.ProtectPaths, ""));
        if (HomeOf(ctx) is { Length: > 1 } home && home.StartsWith('/'))
            zones.Add(new Zone(paths.Canonical(home), GlobalProtectGlobs, "~/"));
        if (Environment.GetEnvironmentVariable("MONARCH_HOME") is { Length: > 1 } mh && mh.StartsWith('/'))
            zones.Add(new Zone(paths.Canonical(mh), ["**"], "$MONARCH_HOME/"));
        return zones;
    }

    // ---- 1. protect_paths
    private static PolicyDecision? ProtectPaths(ToolCall call, PolicyContext ctx, List<BashAnalysis.Segment> segments, Canon paths)
    {
        if (Array.IndexOf(FileMutators, call.ToolName) >= 0)
        {
            if (call.FilePath is not { Length: > 0 } fp) return null;
            var abs = paths.AbsFile(fp);
            foreach (var zone in Zones(ctx, paths))
                if (zone.Hit(abs, destructive: false, exact: true) is { } pattern)
                    return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                        $"'{zone.Show(abs)}' ist geschützt (protect_paths: {pattern}); nur die Engine schreibt dort.");
            if (IsNewMonarchDirectory(paths, abs))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    $"Regel 6 (ADR-0011): '{abs}' läge in einem neuen .monarch-Verzeichnis (protect_paths: .monarch); ein neues .monarch legt nur 'monarch awaken' an.");
            return null;
        }

        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        return BashProtect(cmd, ctx, segments, paths) ?? WorktreeAsk(segments);
    }

    private static readonly string[] NonCodeWords = ["cd", "pushd", "popd", "ls", "pwd", "true", "false", "test", "["];

    /// <summary>
    /// ADR-0014 Entscheidung 2: Nachweislich kein Code ist nur ein Segment der obersten Ebene mit harmlosem Kommandowort, ohne Pipe-Ausgang, Zuweisung,
    /// Umleitung, Heredoc, und nur in einem Kommando ohne Substitution und ohne Funktions- oder Alias-Definition.
    /// </summary>
    private static bool IsNonCodeSegment(BashAnalysis.Segment s, bool commandAllows)
    {
        if (!commandAllows) return false;
        if (s.Tokens.Length == 0 || Array.IndexOf(NonCodeWords, s.Tokens[0]) < 0) return false;
        if (s.PipesToNext || s.Assignments.Count > 0 || s.OutRedirects.Count > 0 || s.InRedirects.Count > 0 || s.Heredoc is not null || s.Events.Length > 0) return false;
        if (s.RawWords.Length != s.Tokens.Length) return false;
        return true;
    }

    /// <summary>True, wenn das Kommando Substitution enthaelt.</summary>
    private static bool HasSubstitution(string cmd) =>
        cmd.Contains("$(", StringComparison.Ordinal) || cmd.Contains('`') || cmd.Contains("<(", StringComparison.Ordinal) || cmd.Contains(">(", StringComparison.Ordinal);

    /// <summary>
    /// Einmal je Kommando: Nicht-Code-Segmente gibt es nur ohne Substitution, ohne Funktions-/Alias-Definition und wenn kein Heredoc/Here-String an einem
    /// verworfenen Segment (Gruppe, Subshell) haengt (mehr Heredoc-Operatoren im Text als Segmente mit Heredoc).
    /// </summary>
    private static bool NonCodeAllowed(string cmd, List<BashAnalysis.Segment> segments)
    {
        if (HasSubstitution(cmd) || HasDefinition(cmd)) return false;
        var raw = 0;
        for (var i = cmd.IndexOf("<<", StringComparison.Ordinal); i >= 0; i = cmd.IndexOf("<<", i + 2, StringComparison.Ordinal)) raw++;
        return raw <= segments.Count(s => s.Heredoc is not null);
    }

    private static bool HasDefinition(string cmd)
    {
        for (var i = cmd.IndexOf('('); i >= 0; i = cmd.IndexOf('(', i + 1))
        {
            var j = i + 1;
            while (j < cmd.Length && cmd[j] is ' ' or '\t') j++;
            if (j < cmd.Length && cmd[j] == ')') return true;
        }

        return BashAnalysis.PathTokens(cmd).Any(t => t is "function" or "alias");
    }

    /// <summary>Echte Komponente <c>.monarch</c>/<c>.claude</c> (gefaltet, lexikalisch normalisiert); <c>.claude/worktrees</c> ist Projektcode und zählt nicht.</summary>
    private static bool HasProtectedComponent(string token)
    {
        var parts = new List<string>();
        foreach (var c in PathFold.Fold(token).Split('/'))
        {
            if (c.Length == 0 || c == ".") continue;
            if (c == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
            else parts.Add(c);
        }

        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] == ".monarch") return true;
            if (parts[i] == ".claude" && !(i + 1 < parts.Count && parts[i + 1] == "worktrees")) return true;
        }

        return false;
    }

    /// <summary>
    /// ADR-0014 Entscheidung 3: <c>git restore --staged|-S [-q|--quiet] [--] pfade</c> und <c>git reset [-q|--quiet] [HEAD] -- pfade</c> setzen nur den
    /// Index zurück. Gilt nur für wörtliche Argumente (keine Variablen, Globs, Substitutionen, Wrapper, Zuweisungen, Globaloptionen).
    /// </summary>
    private static bool IsIndexOnlyGit(BashAnalysis.Segment seg, List<BashAnalysis.Segment> all, bool hasSubstitution)
    {
        var raw = seg.RawWords;
        if (raw.Length < 3 || raw[0] != "git" || !raw.SequenceEqual(seg.Tokens) || seg.Assignments.Count > 0) return false;
        if (seg.OutRedirects.Count > 0 || seg.InRedirects.Count > 0 || seg.Heredoc is not null || seg.PipesToNext) return false;
        if (hasSubstitution) return false;
        if (all.Any(s => s.AssignmentNames.Any(n => n.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)))) return false;
        if (raw.Any(t => t.Contains('$', StringComparison.Ordinal) || t.AsSpan().IndexOfAny("*?[]{}") >= 0)) return false;
        var sub = raw[1];
        var i = 2;
        var paths = 0;
        if (sub == "restore")
        {
            var staged = false;
            var dd = false;
            for (; i < raw.Length; i++)
            {
                var t = raw[i];
                if (dd) { paths++; continue; }
                if (t == "--") { dd = true; continue; }
                if (t is "--staged" or "-S") { staged = true; continue; }
                if (t is "-q" or "--quiet") continue;
                if (t.StartsWith('-')) return false;
                paths++;
            }

            return staged && paths > 0;
        }

        if (sub == "reset")
        {
            for (; i < raw.Length && raw[i] is "-q" or "--quiet"; i++) { }
            if (i < raw.Length && raw[i] == "HEAD") i++;
            if (i >= raw.Length || raw[i] != "--") return false;
            return raw.Length - i - 1 > 0;
        }

        return false;
    }

    /// <summary>
    /// ADR-0014 Nachtrag Q-0138 (Issue 55, Owner-Entscheid STRENG): genau <c>sed s/.*/R/ datei</c> und <c>sed -e s/.*/R/ datei</c> (R alphanumerisch) ist kein
    /// Schreiber, obwohl R ein <c>e</c> oder <c>w</c> enthalten kann. Wörtlich wie <see cref="IsIndexOnlyGit"/>: geprüft auf dem Segment, nicht auf
    /// <c>Effective()</c>; einziges Segment ohne Subshell/Gruppe, ohne Umleitung, Pipe, Heredoc, Zuweisung, Substitution; der Operand beginnt nicht mit
    /// <c>-</c>, hat kein <c>$</c>/Glob/Brace und trifft weder eine Zone noch ein weiteres .monarch.
    /// </summary>
    private static bool IsPlainSedSubstitute(BashAnalysis.Segment seg, List<BashAnalysis.Segment> all, bool hasSubstitution, List<Zone> zones, Canon paths)
    {
        var raw = seg.RawWords;
        if (all.Count != 1 || seg.Events.Length != 0 || hasSubstitution) return false;
        if (raw.Length is not (3 or 4) || raw[0] != "sed" || !raw.SequenceEqual(seg.Tokens) || seg.Assignments.Count > 0) return false;
        if (seg.OutRedirects.Count > 0 || seg.InRedirects.Count > 0 || seg.Heredoc is not null || seg.PipesToNext) return false;
        if (raw.Length == 4 && raw[1] != "-e") return false;
        var script = raw[^2];
        var operand = raw[^1];
        if (operand.Length == 0 || operand[0] == '-' || operand.AsSpan().IndexOfAny("$*?[]{}") >= 0) return false;
        if (!script.StartsWith("s/.*/", StringComparison.Ordinal) || script.Length < 7 || script[^1] != '/') return false;
        foreach (var c in script.AsSpan(5, script.Length - 6))
            if (!char.IsAsciiLetterOrDigit(c)) return false;
        foreach (var t in PathCandidates(operand))
            if (Find(zones, paths, t, destructive: false) is not null || IsDecoyMonarch(paths, t)) return false;
        return true;
    }

    private static PolicyDecision? BashProtect(string cmd, PolicyContext ctx, List<BashAnalysis.Segment> segments, Canon paths)
    {
        // Interpreter mit Inline- oder stdin-Code, der .monarch/.claude erwähnt (auch zerlegt wie '.mon' + 'arch').
        var hasSubstitution = HasSubstitution(cmd);
        var inline = segments.FirstOrDefault(BashAnalysis.IsInlineInterpreter);
        var nonCodeAllowed = inline is not null && NonCodeAllowed(cmd, segments);
        if (inline is not null)
        {
            // ADR-0014 Entscheidung 2: Default ist "Code" (Teilstring-Suche wie bisher); nur nachweislich harmlose cd/ls-Segmente sind es nicht,
            // dort zählt nur eine echte Pfadkomponente.
            var nonCode = segments.Where(s => IsNonCodeSegment(s, nonCodeAllowed)).ToList();
            string compact;
            if (nonCode.Count == 0) compact = BashAnalysis.Compress(cmd);
            else
            {
                var sb = new System.Text.StringBuilder();
                foreach (var s in segments.Where(s => !nonCode.Contains(s)))
                {
                    sb.Append(s.Text).Append(' ').Append(s.Heredoc).Append(' ');
                    foreach (var a in s.Assignments) sb.Append(a).Append(' ');
                    foreach (var r in s.OutRedirects.Concat(s.InRedirects)) sb.Append(r).Append(' ');
                }

                compact = BashAnalysis.Compress(sb.ToString());
                foreach (var s in nonCode)
                    foreach (var arg in s.Tokens.Skip(1))
                        foreach (var t in BashAnalysis.PathTokens(arg))
                            if (HasProtectedComponent(t))
                                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                                    $"Der Interpreter '{inline.Word}' läuft nach '{s.Word} {t}', das eine Komponente .monarch/.claude nennt (protect_paths); geschützte Pfade ändert nur das Monarch-Binary.");
            }

            foreach (var m in InterpreterMentions)
                if (compact.Contains(m, StringComparison.Ordinal))
                    return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                        $"Der Interpreter '{inline.Word}' führt Inline-Code aus, der '{m}' erwähnt (protect_paths); geschützte Pfade ändert nur das Monarch-Binary. Skriptdateien sind erlaubt.");
        }

        // git clean -x/-X und git stash -a erfassen ignorierte Dateien, darunter .monarch/local.
        foreach (var seg in segments)
        {
            if (BashAnalysis.GitSubcommand(seg.Tokens, out var gi) is not { } sub) continue;
            var flags = seg.Tokens.Skip(gi + 1).Where(t => t.StartsWith('-')).ToArray();
            if (sub == "clean" && flags.Any(f => !f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).IndexOfAny('x', 'X') >= 0))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    "'git clean -x/-X' löscht ignorierte Dateien, darunter das Anker-Verzeichnis .monarch/local (protect_paths: .monarch/local/**).");
            if (sub == "clean" && IsForcedClean(flags) && !HasPathspec(seg.Tokens, gi + 1))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    "'git clean' ohne Pfadangabe räumt die ganze Root, darunter noch nicht committete Ledger-Dateien (protect_paths: .monarch/ledger/**); Pfad angeben, z. B. 'git clean -fd src/'.");
            if (sub == "stash" && flags.Any(f => f == "--all" || (!f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).Contains('a'))))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    "'git stash -a' erfasst ignorierte Dateien, darunter das Anker-Verzeichnis .monarch/local (protect_paths: .monarch/local/**).");
        }

        var zones = Zones(ctx, paths);
        var assignment = FirstAssignmentHit(zones, paths, segments);
        for (var si = 0; si < segments.Count; si++)
        {
            var seg = segments[si];
            if (assignment is { } a && a.Index <= si && SegmentMutates(seg, segments, paths))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    $"Regel 2 (ADR-0011): Die Zuweisung '{a.Value}' nennt den geschützten Pfad '{a.Path}' (protect_paths: {a.Pattern}) und ein Kommando im selben Aufruf mutiert; Variablen löst Monarch nicht auf. Nenne den Pfad nicht oder trenne Zuweisung und Mutation.");
            foreach (var t in seg.OutRedirects)
            {
                if (t == "/dev/null") continue;
                if (Find(zones, paths, t, destructive: false) is { } pattern)
                    return PolicyDecision.Deny(PolicyRules.ProtectPaths, $"Die Umleitung nach '{t}' verändert einen geschützten Pfad (protect_paths: {pattern}).");
                if (IsDecoyMonarch(paths, t))
                    return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                        $"Regel 6 (ADR-0011): Die Umleitung nach '{t}' schreibt in ein weiteres .monarch-Verzeichnis (protect_paths: .monarch); ein neues .monarch legt nur 'monarch awaken' an.");
            }

            if (seg.Tokens.Length == 0) continue;
            var (word, tokens) = Effective(seg, segments);
            if (word == "ln" && LinkHit(zones, paths, tokens, si) is { } link) return link;
            if (CwdWriter(word, tokens) is { } writer && CwdInZone(zones, paths, si) is { } cwdHit)
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    $"Regel 3 (ADR-0011): '{writer}' schreibt ohne Zielpfad in das Arbeitsverzeichnis '{cwdHit.Cwd}', das in einer Schutzzone liegt oder sie enthält (protect_paths: {cwdHit.Pattern}); gib ein Zielverzeichnis an.");

            if (TargetDirectoryHit(zones, paths, word, tokens, si) is { } target)
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    $"Regel 3 (ADR-0011): '{word}' schreibt in das Zielverzeichnis '{target.Dir}' ('{target.Abs}'), das in einer Schutzzone liegt oder sie enthält (protect_paths: {target.Pattern}); wähle ein Verzeichnis außerhalb.");
            if (!IsPlainSedSubstitute(seg, segments, hasSubstitution, zones, paths) && ScriptWriterHit(zones, paths, word, tokens) is { } script)
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    $"Regel 2 (ADR-0011): Das Skript von '{word}' schreibt in den geschützten Pfad '{script.Path}' (protect_paths: {script.Pattern}).");

            if (IsIndexOnlyGit(seg, segments, hasSubstitution)) continue; // Segment bleibt mutierend (Zuweisungsregel oben), nur die Operanden sind ausgenommen
            var (mutating, destructive) = Classify(word, tokens);
            if (!mutating && IsReading(word, tokens)) continue;
            if (IsPositive(seg, paths)) continue;
            tokens = DropDataValues(word, tokens);
            for (var i = 1; i < tokens.Length; i++)
            {
                var isDestructive = destructive.Contains(tokens[i]);
                foreach (var t in PathCandidates(tokens[i]))
                {
                    if (Find(zones, paths, t, isDestructive) is { } pattern)
                        return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                            $"Regel 2 (ADR-0011): Das Kommando '{word}' ist nicht als lesend bekannt und nennt den geschützten Pfad '{t}' (protect_paths: {pattern}).");
                    if (IsDecoyMonarch(paths, t))
                        return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                            $"Regel 2 (ADR-0011): Das Kommando '{word}' ist nicht als lesend bekannt und nennt '{t}' mit einer .monarch-Komponente (protect_paths: .monarch); ein weiteres .monarch legt nur 'monarch awaken' an.");
                }
            }
        }

        return null;
    }

    /// <summary>Pfad-Tokens eines Arguments; bei <c>-oWERT</c> (Kurzoption mit angehängtem Wert) zusätzlich der Wert.</summary>
    private static IEnumerable<string> PathCandidates(string arg)
    {
        foreach (var t in BashAnalysis.PathTokens(arg)) yield return t;
        // Zeichenklassen koennen Trenner (= , Leerzeichen) enthalten; das ganze Argument wird zusaetzlich als Muster geprueft (ADR-0014 1c).
        if (arg.Contains('[', StringComparison.Ordinal)) yield return BashAnalysis.StripQuotes(arg);
        if (arg.Length > 2 && arg[0] == '-' && arg[1] != '-')
        {
            foreach (var t in BashAnalysis.PathTokens(arg[2..])) yield return t;
            var dot = arg.AsSpan().IndexOfAny('.', '/', '~');
            if (dot > 2) foreach (var t in BashAnalysis.PathTokens(arg[dot..])) yield return t;
        }
    }

    // ---- Zuweisungen (Regel 2): ein Schutzpfad in einem Zuweisungswert plus eine spätere Mutation im selben Aufruf
    private static (int Index, string Value, string Path, string Pattern)? FirstAssignmentHit(List<Zone> zones, Canon paths, List<BashAnalysis.Segment> segments)
    {
        for (var i = 0; i < segments.Count; i++)
            foreach (var value in segments[i].Assignments)
                foreach (var part in value.Split(':'))
                    foreach (var t in BashAnalysis.PathTokens(part))
                    {
                        if (Find(zones, paths, t, destructive: false) is { } pattern) return (i, value, t, pattern);
                        if (IsDecoyMonarch(paths, t)) return (i, value, t, ".monarch");
                    }
        return null;
    }

    /// <summary>True, wenn das Segment nicht als lesend bekannt ist (Umleitung in eine Datei, mutierendes oder unbekanntes Kommando); Monarch-Binary und git add/commit/push zählen nicht.</summary>
    private static bool SegmentMutates(BashAnalysis.Segment seg, List<BashAnalysis.Segment> all, Canon paths)
    {
        if (seg.OutRedirects.Any(t => t != "/dev/null")) return true;
        if (seg.Tokens.Length == 0) return false;
        var (word, tokens) = Effective(seg, all);
        var (mutating, _) = Classify(word, tokens);
        if (!mutating && IsReading(word, tokens)) return false;
        return !IsPositive(seg, paths);
    }

    // ---- Regel 5: pfadlose Arbeitsbaum-Mutationen -> ask
    private static PolicyDecision? WorktreeAsk(List<BashAnalysis.Segment> segments)
    {
        foreach (var seg in segments)
        {
            if (BashAnalysis.GitSubcommand(seg.Tokens, out var gi) is not { } sub) continue;
            var rest = seg.Tokens.Skip(gi + 1).ToArray();
            var dd = Array.IndexOf(rest, "--");
            if (dd >= 0 && dd + 1 < rest.Length) continue; // Pfadangabe: nur diese Dateien
            var flags = rest.Where(t => t.StartsWith('-') && t.Length > 1).ToArray();
            bool Short(char c) => flags.Any(f => !f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).Contains(c));
            bool Long(string name) => flags.Contains(name);
            var what = sub switch
            {
                "stash" when !rest.Any(r => r is "pop" or "apply" or "list" or "show" or "drop" or "clear" or "branch" or "create" or "store")
                    && (Short('u') || Long("--include-untracked")) => "git stash -u/--include-untracked",
                "checkout" when Short('f') || Long("--force") => "git checkout -f",
                "switch" when Long("--discard-changes") || Short('f') || Long("--force") => "git switch --discard-changes/-f",
                "reset" when Long("--keep") || Long("--merge") => "git reset --keep/--merge",
                _ => null,
            };
            if (what is not null)
                return PolicyDecision.Ask(PolicyRules.ProtectPaths,
                    $"Regel 5 (ADR-0011): '{what}' verwirft oder verschiebt pfadlos Arbeitsbaum-Änderungen, darunter noch nicht committete Ledger-Dateien (protect_paths: .monarch/ledger/**). Freigabe nötig; mit Pfadangabe (-- <pfad>) ist es gezielt.");
        }

        return null;
    }

    // ---- Regel 3: Schreiben ins cwd ohne Zielpfad
    private static string? CwdWriter(string word, string[] tokens)
    {
        var args = tokens.Skip(1).ToArray();
        bool Cluster(char c) => args.Any(a => a.Length > 1 && a[0] == '-' && a[1] != '-' && a.AsSpan(1).Contains(c));
        bool Long(string name) => args.Any(a => a == name || a.StartsWith(name + "=", StringComparison.Ordinal));
        switch (word)
        {
            case "tar" or "bsdtar" or "gtar":
                var old = args.Length > 0 && args[0].Length > 0 && args[0][0] != '-' ? args[0] : "";
                var extract = Cluster('x') || Long("--extract") || Long("--get") || (old.Length > 0 && old.Contains('x', StringComparison.Ordinal));
                var dir = Cluster('C') || Long("--directory") || (old.Length > 0 && old.Contains('C', StringComparison.Ordinal));
                return extract && !dir ? "tar -x ohne -C" : null;
            case "unzip":
                return !Cluster('d') && !Cluster('l') && !Cluster('t') && !Cluster('p') && !Cluster('v') && !Cluster('Z') ? "unzip ohne -d" : null;
            case "cpio":
                return (Cluster('i') || Long("--extract")) && !Cluster('D') && !Long("--directory") ? "cpio -i" : null;
            case "curl":
                return (Cluster('O') || Cluster('J') || Long("--remote-name") || Long("--remote-header-name") || Long("--remote-name-all")) && !Long("--output-dir")
                    ? "curl -O/-J" : null;
            case "wget":
                return !Cluster('O') && !Cluster('P') && !Long("--output-document") && !Long("--directory-prefix") ? "wget ohne -O/-P" : null;
            default:
                return null;
        }
    }

    private const string TarValueLetters = "fCTXIbLNHMFVgKRSZ";
    private const string WgetValueLetters = "OoaeiBtTwUQlARDIXPb";

    /// <summary>Werte der Kurzoption <paramref name="target"/> (gebündelt, angehängt oder als nächstes Wort); ein anderer wertnehmender Buchstabe vorher beendet das Bündel.</summary>
    private static IEnumerable<string> ShortValues(string[] args, char target, string valueLetters)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.Length < 2 || a[0] != '-' || a[1] == '-') continue;
            for (var k = 1; k < a.Length; k++)
            {
                if (a[k] == target)
                {
                    if (k + 1 < a.Length) yield return a[(k + 1)..];
                    else if (i + 1 < args.Length) yield return args[i + 1];
                    break;
                }

                if (valueLetters.Contains(a[k], StringComparison.Ordinal)) break;
            }
        }
    }

    /// <summary>Werte der Langoption <paramref name="name"/> (<c>--name=wert</c> oder <c>--name wert</c>).</summary>
    private static IEnumerable<string> LongValues(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == name) { if (i + 1 < args.Length) yield return args[i + 1]; }
            else if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) yield return args[i][(name.Length + 1)..];
        }
    }

    /// <summary>Zielverzeichnisse, in die ein Kommando schreibt: <c>tar -x -C</c>/<c>--directory</c>, <c>unzip -d</c>, <c>wget -P</c>/<c>--directory-prefix</c>, <c>curl --output-dir</c>.</summary>
    private static List<string> TargetDirectories(string word, string[] tokens)
    {
        var args = tokens.Skip(1).ToArray();
        var dirs = new List<string>();
        bool Cluster(char c) => args.Any(a => a.Length > 1 && a[0] == '-' && a[1] != '-' && a.AsSpan(1).Contains(c));
        switch (word)
        {
            case "tar" or "bsdtar" or "gtar":
                var old = args.Length > 0 && args[0].Length > 0 && args[0][0] != '-' ? args[0] : "";
                var extract = Cluster('x') || args.Contains("--extract") || args.Contains("--get") || old.Contains('x', StringComparison.Ordinal);
                if (!extract) break;
                dirs.AddRange(ShortValues(args, 'C', TarValueLetters));
                dirs.AddRange(LongValues(args, "--directory"));
                var next = 1; // Altform: die Werte folgen in der Reihenfolge der wertnehmenden Buchstaben
                foreach (var ch in old)
                    if (TarValueLetters.Contains(ch, StringComparison.Ordinal))
                    {
                        if (ch == 'C' && next < args.Length) dirs.Add(args[next]);
                        next++;
                    }

                break;
            case "unzip":
                if (Cluster('l') || Cluster('t') || Cluster('p') || Cluster('v') || Cluster('Z')) break;
                dirs.AddRange(ShortValues(args, 'd', "dx"));
                break;
            case "wget":
                dirs.AddRange(ShortValues(args, 'P', WgetValueLetters));
                dirs.AddRange(LongValues(args, "--directory-prefix"));
                break;
            case "curl":
                dirs.AddRange(LongValues(args, "--output-dir"));
                break;
        }

        return dirs;
    }

    private static (string Dir, string Abs, string Pattern)? TargetDirectoryHit(List<Zone> zones, Canon paths, string word, string[] tokens, int segmentIndex)
    {
        foreach (var dir in TargetDirectories(word, tokens))
        {
            if (dir.Length == 0) continue;
            foreach (var cwd in paths.CwdAt(segmentIndex))
                foreach (var abs in paths.AbsFrom(dir, cwd))
                    foreach (var z in zones)
                        if (z.Hit(abs, destructive: true, exact: false) is { } pattern) return (dir, abs, pattern);
        }

        return null;
    }

    /// <summary>
    /// Schreibende Programme hinter Kommandos, die sonst als lesend gelten: <c>awk</c>-Programme mit Umleitung oder <c>system()</c> und <c>sed</c>-Skripte mit
    /// <c>w</c>/<c>W</c>/<c>e</c>, die einen Schutzpfad nennen.
    /// </summary>
    private static (string Path, string Pattern)? ScriptWriterHit(List<Zone> zones, Canon paths, string word, string[] tokens)
    {
        var scripts = new List<string>();
        var args = tokens.Skip(1).ToArray();
        if (word == "awk")
        {
            var hasFile = false;
            string? program = null;
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a is "-f" or "--file") { hasFile = true; i++; }
                else if (a is "-v" or "-F" or "--assign" or "--field-separator") i++;
                else if (a is "-e" or "--source") { if (i + 1 < args.Length) scripts.Add(args[++i]); }
                else if (a.StartsWith('-') && a.Length > 1) continue;
                else { program = a; break; }
            }

            if (program is not null && !hasFile && scripts.Count == 0) scripts.Add(program);
            scripts.RemoveAll(sc => !(sc.Contains('>', StringComparison.Ordinal) || sc.Contains('|', StringComparison.Ordinal) || sc.Contains("system", StringComparison.Ordinal)));
        }
        else if (word == "sed")
        {
            var expressions = new List<string>();
            var hasFile = false;
            string? first = null;
            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (a is "-e" or "--expression") { if (i + 1 < args.Length) expressions.Add(args[++i]); }
                else if (a.StartsWith("--expression=", StringComparison.Ordinal)) expressions.Add(a["--expression=".Length..]);
                else if (a is "-f" or "--file") { hasFile = true; i++; }
                else if (a.StartsWith('-') && a.Length > 1) continue;
                else if (first is null) first = a;
            }

            if (expressions.Count == 0 && !hasFile && first is not null) expressions.Add(first);
            scripts.AddRange(expressions.Where(sc => sc.AsSpan().IndexOfAny('w', 'W', 'e') >= 0));
        }

        foreach (var script in scripts)
            foreach (var t in BashAnalysis.PathTokens(script))
            {
                if (Find(zones, paths, t, destructive: false) is { } pattern) return (t, pattern);
                if (IsDecoyMonarch(paths, t)) return (t, ".monarch");
            }

        return null;
    }

    /// <summary>Schreibende Aufrufe von Kommandos, die sonst als lesend gelten: <c>sort -o</c>, <c>uniq</c> mit Ausgabedatei (zweites Argument), <c>git diff/log/show --output</c>.</summary>
    private static bool HiddenWriter(string word, string[] tokens)
    {
        var args = tokens.Skip(1).ToArray();
        switch (word)
        {
            case "sort":
                return args.Any(a => a == "-o" || a.StartsWith("--output", StringComparison.Ordinal) || a.StartsWith("-o", StringComparison.Ordinal)
                                     || (a.Length > 2 && a[0] == '-' && a[1] != '-' && a[^1] == 'o')
                                     || (a.Length > 2 && a[0] == '-' && a[1] != '-' && a.AsSpan(1).Contains('o')));
            case "uniq":
                var positional = 0;
                for (var i = 0; i < args.Length; i++)
                {
                    var a = args[i];
                    if (a is "-f" or "-s" or "-w" or "--skip-fields" or "--skip-chars" or "--check-chars") i++;
                    else if (a == "--") positional += args.Length - i - 1;
                    else if (a.Length > 1 && a[0] == '-') continue;
                    else positional++;
                    if (a == "--") break;
                }

                return positional >= 2;
            case "git":
                if (BashAnalysis.GitSubcommand(tokens, out var gi) is not { } sub || Array.IndexOf(ReadOnlyGit, sub) < 0) return false;
                return tokens.Skip(gi + 1).Any(t => t.StartsWith("--out", StringComparison.Ordinal));
            default:
                return false;
        }
    }

    private static (string Cwd, string Pattern)? CwdInZone(List<Zone> zones, Canon paths, int segmentIndex)
    {
        foreach (var cwd in paths.CwdAt(segmentIndex))
            foreach (var z in zones)
                if (z.Hit(cwd, destructive: true, exact: false) is { } pattern) return (cwd, pattern);
        return null;
    }

    // ---- Regel 4: Links auf Vorfahren oder Teile einer Schutzzone
    private static PolicyDecision? LinkHit(List<Zone> zones, Canon paths, string[] tokens, int segmentIndex)
    {
        var operands = new List<string>();
        string? dirOption = null;
        for (var i = 1; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t is "-t" or "--target-directory") { if (i + 1 < tokens.Length) dirOption = tokens[++i]; continue; }
            if (t.StartsWith("--target-directory=", StringComparison.Ordinal)) { dirOption = t["--target-directory=".Length..]; continue; }
            if (t.StartsWith('-') && t.Length > 1) continue;
            operands.Add(t);
        }

        List<string> targets, linkDirs;
        if (dirOption is not null) { targets = operands; linkDirs = [dirOption]; }
        else if (operands.Count == 0) return null;
        else if (operands.Count == 1) { targets = operands; linkDirs = ["."]; }
        else
        {
            var link = operands[^1];
            targets = operands.GetRange(0, operands.Count - 1);
            linkDirs = [link, Path.GetDirectoryName(link) is { Length: > 0 } d ? d : "."];
        }

        foreach (var target in targets)
        {
            if (target.Length == 0 || target.StartsWith('-')) continue;
            foreach (var cwd in paths.CwdAt(segmentIndex))
            {
                // Das Ziel eines relativen Links gilt relativ zum Verzeichnis des Links; konservativ zählt auch das cwd.
                var bases = new List<string> { cwd };
                foreach (var ld in linkDirs) bases.AddRange(paths.AbsFrom(ld, cwd));
                foreach (var b in bases)
                    foreach (var abs in paths.AbsFrom(target, b))
                        foreach (var z in zones)
                            if (z.Hit(abs, destructive: true, exact: false) is { } pattern)
                                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                                    $"Regel 4 (ADR-0011): Ein Link auf '{target}' ('{abs}') zeigt auf einen Vorfahren oder Teil einer Schutzzone (protect_paths: {pattern}); darüber ließen sich geschützte Pfade ändern. Links auf andere Ziele sind erlaubt.");
            }
        }

        return null;
    }

    // ---- Regel 2: bekannte lesende Kommandos, Positivliste, Daten-Werte
    private static bool IsReading(string word, string[] tokens)
    {
        if (HiddenWriter(word, tokens)) return false;
        if (word == "git") return BashAnalysis.GitSubcommand(tokens, out _) is { } sub && Array.IndexOf(ReadOnlyGit, sub) >= 0;
        var args = tokens.Skip(1);
        if (word is "awk" && args.Any(a => a == "-i" || a.StartsWith("--in-place", StringComparison.Ordinal) || a.StartsWith("-i", StringComparison.Ordinal))) return false;
        return Array.IndexOf(ReadOnlyWords, word) >= 0 || Array.IndexOf(ShellInert, word) >= 0;
    }

    private static bool IsPositive(BashAnalysis.Segment seg, Canon paths)
    {
        if (IsMonarchBinary(seg.Tokens[0], paths)) return true;
        if (seg.Word != "git" || seg.Canonical == "git push --force") return false;
        return BashAnalysis.GitSubcommand(seg.Tokens, out _) is "add" or "commit" or "push";
    }

    /// <summary>Das Binary gilt über den kanonischen Pfad <c>~/.monarch/bin/[&lt;version&gt;/]monarch</c> oder als nacktes <c>monarch</c>, wenn PATH darauf auflöst (<see cref="Canon.BareMonarchIsBinary"/>); ein lokales <c>./monarch</c> nicht.</summary>
    private static bool IsMonarchBinary(string token, Canon paths)
    {
        if (Path.GetFileName(token) != "monarch") return false;
        if (!token.Contains('/', StringComparison.Ordinal)) return paths.BareMonarchIsBinary();
        return paths.IsInstalledBinary(paths.Abs(token));
    }

    /// <summary>Entfernt Werte, die Text statt Pfade sind: <c>-m/-F/--message/--file</c> bei git tag|merge|stash, <c>--body/--title/-b/-t</c> bei gh.</summary>
    private static string[] DropDataValues(string word, string[] tokens)
    {
        string[] shortFlags, longFlags;
        if (word == "git" && BashAnalysis.GitSubcommand(tokens, out _) is "commit" or "tag" or "merge" or "stash") (shortFlags, longFlags) = (["m", "F"], ["--message", "--file"]);
        else if (word == "gh")
        {
            // ADR-0014 1a: jq-Ausdruck und Go-Template sind Daten (--template nur bei gh api; sonst ein Dateiname).
            (shortFlags, longFlags) = (["b", "t", "q"], tokens.Length > 1 && tokens[1] == "api" ? ["--body", "--title", "--jq", "--template"] : ["--body", "--title", "--jq"]);
        }
        else if (word == "sed") return DropSedScript(tokens);
        else return tokens;
        var keep = new List<string>(tokens.Length);
        var afterDashDash = false;
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t == "--") afterDashDash = true;
            if (afterDashDash) { keep.Add(t); continue; }
            if (i > 0 && t.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = t.IndexOf('=', StringComparison.Ordinal);
                var name = eq < 0 ? t : t[..eq];
                if (Array.IndexOf(longFlags, name) >= 0) { if (eq < 0) i++; continue; }
            }
            else if (i > 0 && t.Length > 1 && t[0] == '-')
            {
                var cluster = t.AsSpan(1);
                var letters = 0;
                while (letters < cluster.Length && char.IsAsciiLetter(cluster[letters])) letters++;
                var pos = cluster[..letters].IndexOfAny(string.Concat(shortFlags));
                if (word == "gh" && pos > 0) pos = -1; // gh: nur der erste Buchstabe ist das Daten-Flag (-Dq/..: -D ist ein Schreibpfad)
                if (pos >= 0)
                {
                    if (pos == cluster.Length - 1) i++; // Wert ist das nächste Wort
                    continue; // sonst hängt der Wert am Flag (-mText)
                }
            }

            keep.Add(t);
        }

        return [.. keep];
    }

    private static readonly string[] SedPlainOptions =
        ["-n", "-E", "-r", "-s", "-u", "-z", "--quiet", "--silent", "--regexp-extended", "--separate", "--unbuffered", "--null-data", "--posix", "--debug"];

    /// <summary>
    /// ADR-0014 1b: Das sed-Skript ist Daten, solange es kein w, W oder e enthält und alle Optionen eindeutig sind. Optionstokens (auch -i mit Suffix,
    /// --in-place=...) und Dateioperanden bleiben Pfad-Kandidaten. Sonst gilt die heutige Prüfung.
    /// </summary>
    private static string[] DropSedScript(string[] tokens)
    {
        var drop = new HashSet<int>();
        var expressions = new List<string>();
        var firstPositional = -1;
        for (var i = 1; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (Array.IndexOf(SedPlainOptions, t) >= 0 || t == "-i" || t == "--in-place") continue;
            if (t.StartsWith("--in-place=", StringComparison.Ordinal) || (t.StartsWith("-i", StringComparison.Ordinal) && t.Length > 2 && t[2] != '-')) continue;
            if (t is "-e" or "--expression") { if (i + 1 >= tokens.Length) return tokens; expressions.Add(tokens[i + 1]); drop.Add(i + 1); i++; continue; }
            if (t.StartsWith("--expression=", StringComparison.Ordinal)) { expressions.Add(t["--expression=".Length..]); drop.Add(i); continue; }
            if (t.Length > 1 && t[0] == '-') return tokens; // unbekannte Option (-f, -l N, Bündel, --): Skript nicht eindeutig
            if (firstPositional < 0) firstPositional = i;
        }

        if (expressions.Count == 0)
        {
            if (firstPositional < 0) return tokens;
            expressions.Add(tokens[firstPositional]);
            drop.Add(firstPositional);
        }

        if (expressions.Any(e => e.AsSpan().IndexOfAny('w', 'W', 'e') >= 0)) return tokens;
        return [.. tokens.Where((_, i) => !drop.Contains(i))];
    }

    // ---- Regel 6: weiteres .monarch
    private static bool IsDecoyMonarch(Canon paths, string token)
    {
        if (token.Length == 0 || token.StartsWith('-') || BashAnalysis.IsGlob(token)) return false;
        foreach (var abs in paths.AbsAll(token))
            if (MonarchComponentDir(paths, abs) is not null) return true;
        return false;
    }

    private static bool IsNewMonarchDirectory(Canon paths, string abs) =>
        MonarchComponentDir(paths, abs) is { } dir && !Directory.Exists(dir);

    /// <summary>
    /// Pfad bis einschließlich der ersten Komponente <c>.monarch</c> unterhalb der Projektwurzel, die nicht das <c>.monarch</c> der Wurzel selbst ist; sonst null.
    /// Außerhalb der Wurzel liegende Pfade (andere Projekte, <c>~/.monarch</c>) regeln deren eigene Projekte bzw. die globale Zone.
    /// </summary>
    private static string? MonarchComponentDir(Canon paths, string abs)
    {
        var parts = abs.Split('/');
        var root = PathFold.Fold(paths.Root);
        var rootPrefix = root == "/" ? "/" : root + "/";
        if (!PathFold.Fold(abs).StartsWith(rootPrefix, StringComparison.Ordinal)) return null;
        for (var k = 1; k < parts.Length; k++)
        {
            if (PathFold.Fold(parts[k]) != ".monarch") continue;
            var parent = PathFold.Fold("/" + string.Join('/', parts[1..k]));
            if (parent == root) return null;
            return string.Join('/', parts[..(k + 1)]);
        }

        return null;
    }

    // git clean löscht nur mit -f/--force und ohne -n/--dry-run.
    private static bool IsForcedClean(string[] flags) =>
        flags.Any(f => f == "--force" || (!f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).Contains('f')))
        && !flags.Any(f => f == "--dry-run" || (!f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).Contains('n')));

    // Pfadangaben nach dem Unterkommando; Werte von -e/--exclude zählen nicht.
    private static bool HasPathspec(string[] tokens, int start)
    {
        for (var i = start; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t == "--") return i + 1 < tokens.Length;
            if (t is "-e" or "--exclude" || (t.Length > 1 && t[0] == '-' && t[1] != '-' && t[^1] == 'e')) { i++; continue; }
            if (t.StartsWith('-')) continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Rest eines Glob-Tokens ab dem Wildcard-Segment (ADR-0014 1c): leere und <c>.</c>-Segmente und ein Slash am Ende entfallen. <paramref name="dotHit"/>:
    /// ein Segment <c>..</c> oder ein Wildcard-Segment, das <c>.</c> oder <c>..</c> erzeugen kann (bash 3.2: <c>.?/</c>), zaehlt als Treffer (fail-closed).
    /// </summary>
    private static string NormalizeTail(string tail, out bool dotHit)
    {
        dotHit = false;
        var keep = new List<string>();
        foreach (var seg in tail.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == ".." || (seg[0] == '.' && BashAnalysis.IsGlob(seg) && FlatPattern.Parse(seg) is var p && (p.Matches(".") || p.Matches("..")))) dotHit = true;
            keep.Add(seg);
        }

        return string.Join('/', keep);
    }

    private static string? Find(List<Zone> zones, Canon paths, string token, bool destructive)
    {
        if (token.Length == 0 || token.StartsWith('-')) return null;
        if (BashAnalysis.IsGlob(token))
        {
            foreach (var (dir, name, rawTail) in paths.GlobParts(token))
            {
                var tail = NormalizeTail(rawTail, out var dotHit);
                if (dotHit && zones.Count > 0) return zones[0].AnyShown;
                var pat = FlatPattern.Parse(tail);
                foreach (var z in zones)
                    if (z.HitGlob(dir, name, destructive) is { } g && z.CanReach(dir, pat, destructive)) return g;
            }

            return null;
        }

        foreach (var abs in paths.AbsAll(token))
            foreach (var z in zones)
                if (z.Hit(abs, destructive, exact: false) is { } p) return p;
        return null;
    }

    /// <summary>Bei <c>xargs</c> zählt das aufgerufene Kommando, und es erhält die Pfade der übrigen Segmente (Pipe-Eingabe) als Argumente.</summary>
    private static (string Word, string[] Tokens) Effective(BashAnalysis.Segment seg, List<BashAnalysis.Segment> all)
    {
        if (seg.Word != "xargs") return (seg.Word, seg.Tokens);
        var j = 1;
        while (j < seg.Tokens.Length && seg.Tokens[j].StartsWith('-')) j += Array.IndexOf(XargsValueFlags, seg.Tokens[j]) >= 0 ? 2 : 1;
        if (j >= seg.Tokens.Length) return ("xargs", seg.Tokens);
        var inner = new List<string>(seg.Tokens[j..]);
        foreach (var other in all)
            if (!ReferenceEquals(other, seg)) inner.AddRange(other.Tokens);
        return (Path.GetFileName(seg.Tokens[j]), [.. inner]);
    }

    /// <summary>Ob das Kommando mutiert und welche seiner Argumente zerstörerisch behandelt werden (Vorfahren geschützter Pfade treffen).</summary>
    private static (bool Mutating, List<string> Destructive) Classify(string word, string[] tokens)
    {
        var destructive = new List<string>();
        var args = tokens.Skip(1).ToArray();
        var positional = args.Where(a => !a.StartsWith('-')).ToList();
        if (word == "git")
        {
            var sub = BashAnalysis.GitSubcommand(tokens, out var gi);
            if (sub is null || Array.IndexOf(MutatingGitSubcommands, sub) < 0) return (false, destructive);
            destructive.AddRange(tokens.Skip(gi + 1).Where(a => !a.StartsWith('-')));
            return (true, destructive);
        }

        if (word == "find") return ClassifyFind(tokens);
        if (word == "sed")
            return (args.Any(t => t.StartsWith("--in-place", StringComparison.Ordinal) || t.StartsWith("-i", StringComparison.Ordinal)), destructive);
        if (Array.IndexOf(MutatingWords, word) < 0 && Array.IndexOf(ExtraMutatingWords, word) < 0) return (false, destructive);
        if (Array.IndexOf(DeleteWords, word) >= 0) destructive.AddRange(positional);
        else if (word == "mv" && positional.Count >= 2) destructive.AddRange(positional.Take(positional.Count - 1));
        else if (word == "rsync" && positional.Count > 0 && args.Any(a => a.StartsWith("--delete", StringComparison.Ordinal) || a == "--remove-source-files"))
            destructive.Add(positional[^1]);
        else if (word is "chmod" or "chown" or "chgrp"
                 && args.Any(a => a == "--recursive" || (a.StartsWith('-') && !a.StartsWith("--", StringComparison.Ordinal) && a.AsSpan(1).IndexOfAny('R', 'r') >= 0)))
            destructive.AddRange(positional);
        return (true, destructive);
    }

    private static (bool Mutating, List<string> Destructive) ClassifyFind(string[] tokens)
    {
        var destructive = new List<string>();
        var delete = tokens.Any(t => t == "-delete");
        var writes = tokens.Any(t => t.StartsWith("-fprint", StringComparison.Ordinal) || t == "-fls");
        var badExec = false;
        for (var i = 1; i < tokens.Length; i++)
        {
            if (Array.IndexOf(FindExecFlags, tokens[i]) < 0) continue;
            var w = i + 1 < tokens.Length ? Path.GetFileName(tokens[i + 1]) : "";
            if (Array.IndexOf(ReadOnlyWords, w) < 0 || w is "sed" or "awk" or "find") badExec = true;
        }

        if (!delete && !writes && !badExec) return (false, destructive);
        if (delete || badExec)
        {
            var i = 1;
            while (i < tokens.Length && tokens[i] is "-H" or "-L" or "-P" or "-E" or "-X" or "-x") i++;
            for (; i < tokens.Length && !tokens[i].StartsWith('-') && tokens[i] is not ("(" or "!"); i++) destructive.Add(tokens[i]);
        }

        return (true, destructive);
    }
}
