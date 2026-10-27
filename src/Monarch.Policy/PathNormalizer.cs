namespace Monarch.Policy;

/// <summary>
/// Kanonisiert Pfade für die Policy: relative Pfade gegen das Arbeitsverzeichnis des Tool-Calls, <c>\</c> zu <c>/</c>, mehrfache <c>/</c>,
/// <c>.</c> und <c>..</c> und Symlinks (Komponente für Komponente; fehlende Komponenten bleiben lexikalisch). Wurzel und Ziel werden
/// gleich kanonisiert. Eine Instanz gehört zu einem Tool-Call und cacht die Syscalls (je Komponente höchstens ein <c>lstat</c>).
/// </summary>
internal sealed class PathResolver
{
    private const int MaxLinkHops = 40;
    private readonly Dictionary<string, string?> _links = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resolved = new(StringComparer.Ordinal);

    /// <summary>Erzeugt einen Resolver für Projektwurzel und Arbeitsverzeichnis (leer: die Wurzel).</summary>
    public PathResolver(string projectRoot, string cwd)
    {
        Root = Resolve(projectRoot, "/");
        Cwd = string.IsNullOrEmpty(cwd) ? Root : Resolve(cwd, Root);
    }

    /// <summary>Kanonische Projektwurzel.</summary>
    public string Root { get; }

    /// <summary>Kanonisches Arbeitsverzeichnis.</summary>
    public string Cwd { get; }

    /// <summary>Kanonischer absoluter Pfad; relative Pfade werden gegen <paramref name="baseDir"/> (kanonisch, absolut) aufgelöst.</summary>
    public string Resolve(string path, string baseDir)
    {
        var p = path.Replace('\\', '/');
        if (!p.StartsWith('/')) p = baseDir.TrimEnd('/') + "/" + p;
        if (_resolved.TryGetValue(p, out var hit)) return hit;
        var pending = new Stack<string>();
        PushSegments(pending, p);
        var cur = new List<string>();
        var hops = 0;
        var missing = false;
        while (pending.Count > 0)
        {
            var seg = pending.Pop();
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..")
            {
                if (cur.Count > 0) cur.RemoveAt(cur.Count - 1);
                continue;
            }

            if (!missing)
            {
                var full = "/" + string.Join('/', cur) + (cur.Count == 0 ? "" : "/") + seg;
                var target = LinkTarget(full);
                if (target is not null && hops++ < MaxLinkHops)
                {
                    if (target.StartsWith('/')) cur.Clear();
                    PushSegments(pending, target.Replace('\\', '/'));
                    continue;
                }

                missing = target is null && !Exists(full);
            }

            cur.Add(seg);
        }

        var result = "/" + string.Join('/', cur);
        _resolved[p] = result;
        return result;
    }

    /// <summary>
    /// Pfad relativ zur kanonischen Wurzel (<c>/</c>-getrennt, leer für die Wurzel selbst); außerhalb der Wurzel der absolute kanonische Pfad.
    /// Der Präfixvergleich ist case-insensitiv (konservativ).
    /// </summary>
    public string Relative(string path, string? baseDir = null)
    {
        var abs = Resolve(path, baseDir ?? Cwd);
        if (abs.Equals(Root, StringComparison.OrdinalIgnoreCase)) return "";
        var prefix = Root == "/" ? "/" : Root + "/";
        return abs.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? abs[prefix.Length..] : abs;
    }

    /// <summary>Ersetzt ein führendes <c>~</c> bzw. <c>~/</c> durch das Home-Verzeichnis (nur für Bash-Pfade).</summary>
    public static string ExpandTilde(string path)
    {
        if (path != "~" && !path.StartsWith("~/", StringComparison.Ordinal)) return path;
        var home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? path : home.TrimEnd('/') + path[1..];
    }

    private static void PushSegments(Stack<string> stack, string path)
    {
        var parts = path.Split('/');
        for (var i = parts.Length - 1; i >= 0; i--) stack.Push(parts[i]);
    }

    private string? LinkTarget(string full)
    {
        if (_links.TryGetValue(full, out var cached)) return cached;
        string? target = null;
        try
        {
            target = new FileInfo(full).LinkTarget;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not readable: treat as an ordinary component.
        }

        _links[full] = target;
        return target;
    }

    private static bool Exists(string full) => Directory.Exists(full) || File.Exists(full);
}

/// <summary>Öffentliche Kanonisierung für Aufrufer außerhalb der Policy (Projektauflösung im Hook).</summary>
public static class PathCanon
{
    /// <summary>Kanonischer absoluter Pfad (Symlinks aufgelöst, <c>.</c>/<c>..</c> normalisiert); relative Pfade gegen <paramref name="cwd"/>.</summary>
    public static string Resolve(string cwd, string path)
    {
        var r = new PathResolver("/", "/");
        var baseDir = string.IsNullOrEmpty(cwd) ? "/" : r.Resolve(cwd, "/");
        return r.Resolve(path, baseDir);
    }
}
