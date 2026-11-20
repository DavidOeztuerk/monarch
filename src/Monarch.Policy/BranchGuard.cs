namespace Monarch.Policy;

/// <summary>Wie ein Branch-Name zur Branch-Policy steht (ADR-0007).</summary>
internal enum BranchClass
{
    /// <summary>Weder geschützt noch Integrations-Branch.</summary>
    Other,

    /// <summary>Direkter Push fragt nach (<c>integration_branch</c>, T4).</summary>
    Integration,

    /// <summary>Geschützter Branch (<c>protected_branches</c>).</summary>
    Protected,
}

/// <summary>
/// Hilfen für die Regel <c>protect_branches</c>: Branch-Klassifikation, Refspec-Zerlegung und Dateizugriff auf Git-Verzeichnis, <c>HEAD</c> und Config
/// (ohne Prozess-Fork). Alles ist heuristisch und konservativ, Vergleiche sind exakt und case-insensitiv, nie Substring über den Kommandotext.
/// </summary>
internal static class BranchGuard
{
    /// <summary>Ein gelesenes Git-Verzeichnis.</summary>
    /// <param name="GitDir">Git-Verzeichnis (bei Worktrees <c>.git/worktrees/&lt;name&gt;</c>).</param>
    /// <param name="Branch">Aktueller Branch aus <c>HEAD</c>; null bei detached oder unlesbarem HEAD.</param>
    /// <param name="Config">Gelesene Config (null: unlesbar).</param>
    public sealed record Repo(string GitDir, string? Branch, GitConfig? Config);

    /// <summary>Mehrwertige Git-Config als Schlüssel <c>section.subsection.key</c> (Section und Key klein, Subsection wie geschrieben).</summary>
    public sealed class GitConfig
    {
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        /// <summary>Letzter Wert oder null.</summary>
        public string? Get(string key) => _values.TryGetValue(key, out var v) ? v[^1] : null;

        /// <summary>Alle Werte.</summary>
        public IReadOnlyList<string> GetAll(string key) => _values.TryGetValue(key, out var v) ? v : [];

        /// <summary>Liest Config-Text; spätere Einträge überschreiben nur in <see cref="Get"/>.</summary>
        public void Parse(string text)
        {
            var section = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] is '#' or ';') continue;
                if (line[0] == '[')
                {
                    var end = line.IndexOf(']', StringComparison.Ordinal);
                    if (end < 0) continue;
                    var header = line[1..end].Trim();
                    var q = header.IndexOf('"', StringComparison.Ordinal);
                    if (q >= 0)
                    {
                        var name = header[..q].Trim().ToLowerInvariant();
                        var sub = header[(q + 1)..];
                        var close = sub.LastIndexOf('"');
                        section = name + "." + (close >= 0 ? sub[..close] : sub).Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal) + ".";
                    }
                    else
                    {
                        var dot = header.IndexOf('.', StringComparison.Ordinal);
                        section = dot > 0 ? header[..dot].ToLowerInvariant() + "." + header[(dot + 1)..] + "." : header.ToLowerInvariant() + ".";
                    }

                    // "[remote \"origin\"] push = x" auf einer Zeile
                    var rest = line[(end + 1)..].Trim();
                    if (rest.Length > 0) Add(section, rest);
                    continue;
                }

                if (section.Length > 0) Add(section, line);
            }
        }

        private void Add(string section, string line)
        {
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            var key = (eq < 0 ? line : line[..eq]).Trim().ToLowerInvariant();
            var value = eq < 0 ? "true" : Unquote(line[(eq + 1)..].Trim());
            if (key.Length == 0) return;
            var full = section + key;
            if (!_values.TryGetValue(full, out var list)) _values[full] = list = [];
            list.Add(value);
        }

        private static string Unquote(string v)
        {
            var sb = new System.Text.StringBuilder();
            var inQuote = false;
            foreach (var c in v)
            {
                if (c == '"') { inQuote = !inQuote; continue; }
                if (!inQuote && c is '#' or ';') break;
                sb.Append(c);
            }

            return sb.ToString().Trim();
        }
    }

    /// <summary>Klassifiziert einen Branch-Namen oder ein Ziel-Muster mit <c>*</c> (Vergleich exakt und case-insensitiv).</summary>
    public static BranchClass Classify(PolicyOptions o, string target)
    {
        var name = target.Trim();
        if (name.Contains('*', StringComparison.Ordinal))
        {
            // Wildcard-Ziel: trifft das Muster einen Branch (als Kurzname, heads/ oder refs/heads/)?
            var best = BranchClass.Other;
            foreach (var p in o.ProtectedBranches)
                if (Hits(name, p)) return BranchClass.Protected;
            if (Hits(name, o.IntegrationBranch)) best = BranchClass.Integration;
            return best;
        }

        var branch = BranchName(name);
        if (branch is null) return BranchClass.Other;
        foreach (var p in o.ProtectedBranches)
            if (string.Equals(branch, p, StringComparison.OrdinalIgnoreCase)) return BranchClass.Protected;
        return string.Equals(branch, o.IntegrationBranch, StringComparison.OrdinalIgnoreCase) ? BranchClass.Integration : BranchClass.Other;
    }

    private static bool Hits(string pattern, string branch)
    {
        var lower = pattern.ToLowerInvariant();
        var b = branch.ToLowerInvariant();
        return GlobMatcher.WildcardMatch(lower, b) || GlobMatcher.WildcardMatch(lower, "heads/" + b) || GlobMatcher.WildcardMatch(lower, "refs/heads/" + b);
    }

    /// <summary>Branch-Kurzname eines Ref-Namens: <c>refs/heads/x</c> und <c>heads/x</c> werden zu <c>x</c>, andere <c>refs/…</c> sind keine Branches (null).</summary>
    public static string? BranchName(string name)
    {
        if (name.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)) return name["refs/heads/".Length..];
        if (name.StartsWith("heads/", StringComparison.OrdinalIgnoreCase)) return name["heads/".Length..];
        return name.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ? null : name;
    }

    /// <summary>
    /// Zerlegt einen Refspec: führendes <c>+</c> entfällt, ohne <c>:</c> gilt <c>&lt;src&gt;:&lt;src&gt;</c>, ein leeres Ziel (<c>x:</c>) gilt ebenso als Quelle.
    /// <c>:main</c> hat eine leere Quelle (Löschen).
    /// </summary>
    public static (string Src, string Dst) SplitRefspec(string spec)
    {
        var s = spec.StartsWith('+') ? spec[1..] : spec;
        var c = s.IndexOf(':', StringComparison.Ordinal);
        if (c < 0) return (s, s);
        var src = s[..c];
        var dst = s[(c + 1)..];
        return (src, dst.Length == 0 ? src : dst);
    }

    /// <summary>True für <c>HEAD</c> und <c>@</c> (der aktuelle Branch).</summary>
    public static bool IsHead(string name) => name == "@" || string.Equals(name, "HEAD", StringComparison.OrdinalIgnoreCase);

    /// <summary>Sucht das Git-Verzeichnis ab <paramref name="dir"/> aufwärts (<c>.git</c>-Verzeichnis, <c>.git</c>-Datei eines Worktrees, bare). Null: keines.</summary>
    public static string? FindGitDir(string dir)
    {
        var d = dir;
        for (var hops = 0; hops < 64; hops++)
        {
            try
            {
                var dotGit = Path.Combine(d, ".git");
                if (Directory.Exists(dotGit)) return dotGit;
                if (File.Exists(dotGit))
                {
                    foreach (var line in File.ReadLines(dotGit))
                    {
                        if (!line.StartsWith("gitdir:", StringComparison.Ordinal)) continue;
                        var target = line["gitdir:".Length..].Trim();
                        return Path.GetFullPath(target, d);
                    }

                    return dotGit; // kaputte .git-Datei: Load() meldet unlesbar
                }

                if (File.Exists(Path.Combine(d, "HEAD")) && Directory.Exists(Path.Combine(d, "objects"))) return d;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return d;
            }

            var parent = Path.GetDirectoryName(d.TrimEnd('/'));
            if (string.IsNullOrEmpty(parent) || parent == d) return null;
            d = parent;
        }

        return null;
    }

    /// <summary>Liest <c>HEAD</c> und die Config eines Git-Verzeichnisses (Worktree: <c>commondir</c>); <paramref name="home"/> liefert die globale <c>~/.gitconfig</c>.</summary>
    public static Repo Load(string gitDir, string? home)
    {
        string? branch = null;
        try
        {
            var head = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
            if (head.StartsWith("ref:", StringComparison.Ordinal))
            {
                var r = head["ref:".Length..].Trim();
                if (r.StartsWith("refs/heads/", StringComparison.Ordinal)) branch = r["refs/heads/".Length..];
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            branch = null;
        }

        GitConfig? config = new();
        try
        {
            if (home is not null)
            {
                var global = Path.Combine(home, ".gitconfig");
                if (File.Exists(global))
                {
                    try { config.Parse(File.ReadAllText(global)); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* globale Config ist optional */ }
                }
            }

            var common = gitDir;
            var commondir = Path.Combine(gitDir, "commondir");
            if (File.Exists(commondir)) common = Path.GetFullPath(File.ReadAllText(commondir).Trim(), gitDir);
            config.Parse(File.ReadAllText(Path.Combine(common, "config")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            config = null;
        }

        return new Repo(gitDir, branch, config);
    }
}
