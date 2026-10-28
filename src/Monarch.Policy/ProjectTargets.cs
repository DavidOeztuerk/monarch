namespace Monarch.Policy;

/// <summary>Findet die Orte, an denen ein Tool-Call wirken kann; der Hook sucht dort nach erweckten Projekten.</summary>
public static class ProjectTargets
{
    /// <summary>
    /// Kanonische absolute Pfade, von denen aus ein Projekt gesucht werden soll: das cwd, bei Datei-Tools der Zielpfad, bei Bash die Ziele von
    /// <c>cd</c>/<c>pushd</c>/<c>git -C</c>/<c>--git-dir</c>/<c>--work-tree</c> sowie absolute Pfad-Tokens (auch <c>~</c>) des Kommandos.
    /// </summary>
    public static IReadOnlyList<string> Locations(ToolCall call)
    {
        var rs = new PathResolver("/", string.IsNullOrEmpty(call.Cwd) ? "/" : call.Cwd);
        var list = new List<string>();
        void Add(string p) { if (!list.Contains(p)) list.Add(p); }
        Add(rs.Cwd);
        if (call.FilePath is { Length: > 0 } fp) Add(rs.Resolve(PathResolver.ExpandTilde(fp), rs.Cwd));
        if (call.ToolName != "Bash" || call.Command is not { } cmd) return list;
        var segments = BashAnalysis.Segments(cmd);
        var cur = rs.Cwd;
        var bases = new List<string> { rs.Cwd };
        foreach (var (dir, changes) in BashAnalysis.DirectoryTargets(segments))
        {
            var d = rs.Resolve(PathResolver.ExpandTilde(dir), cur);
            Add(d);
            if (!bases.Contains(d)) bases.Add(d);
            if (changes) cur = d;
        }

        foreach (var token in BashAnalysis.PathTokens(cmd))
        {
            if (token.StartsWith('/') || token == "~" || token.StartsWith("~/", StringComparison.Ordinal))
            {
                Add(rs.Resolve(PathResolver.ExpandTilde(token), rs.Cwd));
            }
            else if (token is "." or ".." || token.Contains('/'))
            {
                // Relative tokens also search for projects (ADR-0011 rule 6): against the cwd and every cd target of the command.
                foreach (var b in bases) Add(rs.Resolve(token, b));
            }
        }

        return list;
    }
}
