namespace Monarch.Policy;

/// <summary>Normalisiert Pfade relativ zur Projektwurzel: <c>\</c> zu <c>/</c>, Wurzelpräfix entfernen, <c>.</c> und <c>..</c> auflösen.</summary>
internal static class PathNormalizer
{
    /// <summary>
    /// Liefert den Pfad relativ zur Wurzel. Pfade außerhalb der Wurzel bleiben absolut (führendes <c>/</c>) bzw. beginnen mit <c>..</c>.
    /// Leere Ergebnisse (die Wurzel selbst) sind <see cref="string.Empty"/>.
    /// </summary>
    public static string Normalize(string projectRoot, string path)
    {
        var p = path.Replace('\\', '/');
        var root = projectRoot.Replace('\\', '/').TrimEnd('/');
        if (root.Length > 0 && (p == root || p.StartsWith(root + "/", StringComparison.Ordinal)))
            p = p[root.Length..];
        var absolute = p.StartsWith('/');
        var stack = new List<string>();
        foreach (var seg in p.Split('/'))
        {
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (stack.Count > 0 && stack[^1] != "..") stack.RemoveAt(stack.Count - 1);
                else if (!absolute) stack.Add("..");
                continue;
            }

            stack.Add(seg);
        }

        var joined = string.Join('/', stack);
        // Pfad war nur wegen des Wurzelpräfixes absolut: relativ zur Wurzel behandeln.
        return absolute && p.Length > 0 && projectRoot.Length > 0 && IsUnderRoot(path, projectRoot) ? joined : (absolute ? "/" + joined : joined);
    }

    private static bool IsUnderRoot(string path, string projectRoot)
    {
        var p = path.Replace('\\', '/');
        var root = projectRoot.Replace('\\', '/').TrimEnd('/');
        return root.Length > 0 && (p == root || p.StartsWith(root + "/", StringComparison.Ordinal));
    }
}
