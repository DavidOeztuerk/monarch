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
        if (Environment.GetEnvironmentVariable("HOME") is { Length: > 1 } home && home.StartsWith('/'))
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

    private static PolicyDecision? BashProtect(string cmd, PolicyContext ctx, List<BashAnalysis.Segment> segments, Canon paths)
    {
        // Interpreter mit Inline- oder stdin-Code, der .monarch/.claude erwähnt (auch zerlegt wie '.mon' + 'arch').
        var inline = segments.FirstOrDefault(BashAnalysis.IsInlineInterpreter);
        if (inline is not null)
        {
            var compact = BashAnalysis.Compress(cmd);
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
        for (var si = 0; si < segments.Count; si++)
        {
            var seg = segments[si];
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

            var (mutating, destructive) = Classify(word, tokens);
            if (!mutating && IsReading(word, tokens)) continue;
            if (IsPositive(seg, paths)) continue;
            tokens = DropDataValues(word, tokens);
            for (var i = 1; i < tokens.Length; i++)
            {
                var isDestructive = destructive.Contains(tokens[i]);
                foreach (var t in BashAnalysis.PathTokens(tokens[i]))
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
        if (word == "git") return BashAnalysis.GitSubcommand(tokens, out _) is { } sub && Array.IndexOf(ReadOnlyGit, sub) >= 0;
        var args = tokens.Skip(1);
        if (word is "awk" && args.Any(a => a == "-i" || a.StartsWith("--in-place", StringComparison.Ordinal) || a.StartsWith("-i", StringComparison.Ordinal))) return false;
        if (word is "sort" && args.Any(a => a == "-o" || a.StartsWith("--output", StringComparison.Ordinal) || a.StartsWith("-o", StringComparison.Ordinal)
                                            || (a.Length > 2 && a[0] == '-' && a[1] != '-' && a[^1] == 'o'))) return false;
        return Array.IndexOf(ReadOnlyWords, word) >= 0 || Array.IndexOf(ShellInert, word) >= 0;
    }

    private static bool IsPositive(BashAnalysis.Segment seg, Canon paths)
    {
        if (IsMonarchBinary(seg.Tokens[0], paths)) return true;
        if (seg.Word != "git" || seg.Canonical == "git push --force") return false;
        return BashAnalysis.GitSubcommand(seg.Tokens, out _) is "add" or "commit" or "push";
    }

    /// <summary>Das Binary gilt über den kanonischen Pfad <c>~/.monarch/bin/[&lt;version&gt;/]monarch</c> oder als <c>monarch</c> über PATH; ein lokales <c>./monarch</c> nicht.</summary>
    private static bool IsMonarchBinary(string token, Canon paths)
    {
        if (Path.GetFileName(token) != "monarch") return false;
        if (!token.Contains('/', StringComparison.Ordinal)) return true;
        if (paths.Home is not { } home) return false;
        var abs = PathFold.Fold(paths.Abs(token));
        var prefix = PathFold.Fold(home).TrimEnd('/') + "/.monarch/bin/";
        if (!abs.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var rest = abs[prefix.Length..].Split('/');
        return rest is ["monarch"] or [_, "monarch"];
    }

    /// <summary>Entfernt Werte, die Text statt Pfade sind: <c>-m/-F/--message/--file</c> bei git tag|merge|stash, <c>--body/--title/-b/-t</c> bei gh.</summary>
    private static string[] DropDataValues(string word, string[] tokens)
    {
        string[] shortFlags, longFlags;
        if (word == "git" && BashAnalysis.GitSubcommand(tokens, out _) is "commit" or "tag" or "merge" or "stash") (shortFlags, longFlags) = (["m", "F"], ["--message", "--file"]);
        else if (word == "gh") (shortFlags, longFlags) = (["b", "t"], ["--body", "--title"]);
        else return tokens;
        var keep = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (i > 0 && t.StartsWith("--", StringComparison.Ordinal))
            {
                var eq = t.IndexOf('=', StringComparison.Ordinal);
                var name = eq < 0 ? t : t[..eq];
                if (Array.IndexOf(longFlags, name) >= 0) { if (eq < 0) i++; continue; }
            }
            else if (i > 0 && t.Length > 1 && t[0] == '-')
            {
                var cluster = t.AsSpan(1);
                var pos = cluster.IndexOfAny(string.Concat(shortFlags));
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

    private static string? Find(List<Zone> zones, Canon paths, string token, bool destructive)
    {
        if (token.Length == 0 || token.StartsWith('-')) return null;
        if (BashAnalysis.IsGlob(token))
        {
            foreach (var (dir, name) in paths.GlobParts(token))
                foreach (var z in zones)
                    if (z.HitGlob(dir, name, destructive) is { } g) return g;
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
            return (args.Any(t => t == "--in-place" || t.StartsWith("-i", StringComparison.Ordinal)), destructive);
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
