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
    private static readonly string[] InterpreterMentions = [".monarch", ".claude"];
    private static readonly string[] FindExecFlags = ["-exec", "-execdir", "-ok", "-okdir"];
    private static readonly string[] XargsValueFlags = ["-I", "-n", "-P", "-L", "-s", "-d", "-E", "-J", "-a"];

    /// <summary>Eine Schutzzone: Basisverzeichnis (kanonisch) und Globs relativ dazu.</summary>
    private sealed class Zone
    {
        private readonly string[] _globs;
        private readonly string[] _literals;
        private readonly string[] _shown;

        public Zone(string baseDir, IReadOnlyList<string> globs, string display)
        {
            Base = baseDir;
            _globs = [.. globs.Select(g => g.ToLowerInvariant())];
            _shown = [.. globs.Select(g => display + g)];
            _literals = [.. _globs.Select(g => { var i = g.AsSpan().IndexOfAny('*', '?', '['); return i < 0 ? g : g[..i]; })];
        }

        public string Base { get; }

        public string? Rel(string abs)
        {
            if (abs.Equals(Base, StringComparison.OrdinalIgnoreCase)) return "";
            var prefix = Base == "/" ? "/" : Base + "/";
            return abs.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? abs[prefix.Length..] : null;
        }

        /// <summary>Anzeige: relativ zur Zone, sonst absolut.</summary>
        public string Show(string abs) => Rel(abs) is { Length: > 0 } r ? r : abs;

        /// <summary>Treffer-Muster, wenn der Pfad geschützt ist (<paramref name="exact"/>: nur Pfade, die ein Muster erfüllen; sonst auch Vorfahren der Muster).</summary>
        public string? Hit(string abs, bool destructive, bool exact)
        {
            var rel = Rel(abs);
            if (rel is null) return destructive && Base.StartsWith(abs == "/" ? "/" : abs + "/", StringComparison.OrdinalIgnoreCase) ? _shown[0] : null;
            if (rel.Length == 0) return destructive ? _shown[0] : null;
            var lower = rel.ToLowerInvariant();
            for (var i = 0; i < _globs.Length; i++)
                if (exact ? GlobMatcher.IsMatch(_globs[i], lower) : GlobMatcher.IsMatchOrAncestor(_globs[i], lower)) return _shown[i];
            return null;
        }

        /// <summary>Treffer-Muster für ein Glob-Token: <paramref name="dir"/> (kanonisch) plus unvollständiger Namensteil <paramref name="name"/> (klein).</summary>
        public string? HitGlob(string dir, string name, bool destructive)
        {
            var rel = Rel(dir);
            if (rel is null)
            {
                var full = (dir == "/" ? "/" : dir + "/") + name;
                return destructive && (Base + "/").StartsWith(full, StringComparison.OrdinalIgnoreCase) ? _shown[0] : null;
            }

            var prefix = (rel.Length == 0 ? "" : rel.ToLowerInvariant() + "/") + name;
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
            return null;
        }

        if (call.ToolName != "Bash" || call.Command is not { } cmd) return null;
        return BashProtect(cmd, ctx, segments, paths);
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
            if (sub == "stash" && flags.Any(f => f == "--all" || (!f.StartsWith("--", StringComparison.Ordinal) && f.AsSpan(1).Contains('a'))))
                return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                    "'git stash -a' erfasst ignorierte Dateien, darunter das Anker-Verzeichnis .monarch/local (protect_paths: .monarch/local/**).");
        }

        var redirects = BashAnalysis.RedirectTargets(BashAnalysis.StripQuotes(cmd), '>').Where(t => t != "/dev/null").ToList();
        var plans = new List<(string Word, string[] Tokens, bool Mutating, List<string> Destructive)>();
        foreach (var seg in segments)
        {
            var (word, tokens) = Effective(seg, segments);
            var (mutating, destructive) = Classify(word, tokens);
            if (mutating) plans.Add((word, tokens, true, destructive));
        }

        if (redirects.Count == 0 && plans.Count == 0) return null;
        var zones = Zones(ctx, paths);
        foreach (var t in redirects)
            if (Find(zones, paths, t, destructive: false) is { } pattern)
                return PolicyDecision.Deny(PolicyRules.ProtectPaths, $"Die Umleitung nach '{t}' verändert einen geschützten Pfad (protect_paths: {pattern}).");
        foreach (var (word, tokens, _, destructive) in plans)
        {
            for (var i = 1; i < tokens.Length; i++)
            {
                var isDestructive = destructive.Contains(tokens[i]);
                foreach (var t in BashAnalysis.PathTokens(tokens[i]))
                    if (Find(zones, paths, t, isDestructive) is { } pattern)
                        return PolicyDecision.Deny(PolicyRules.ProtectPaths,
                            $"Das Kommando '{word}' verändert oder enthält den geschützten Pfad '{t}' (protect_paths: {pattern}).");
            }
        }

        return null;
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
