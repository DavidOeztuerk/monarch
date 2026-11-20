using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>Well-known locations inside an awakened project.</summary>
/// <param name="Root">Project root (contains <c>.monarch/</c>).</param>
internal sealed record ProjectPaths(string Root)
{
    /// <summary>The <c>.monarch</c> directory.</summary>
    public string MonarchDir => Path.Combine(Root, ".monarch");

    /// <summary>Gitignored local state.</summary>
    public string Local => Path.Combine(MonarchDir, "local");

    /// <summary>Project configuration.</summary>
    public string SystemYaml => Path.Combine(MonarchDir, "system.yaml");

    /// <summary>The session's active quest.</summary>
    public string ActiveJson => Path.Combine(Local, "active.json");

    /// <summary>Id of the current agent session.</summary>
    public string CurrentSession => Path.Combine(Local, "current-session");

    /// <summary>Internal hook failures.</summary>
    public string HookErrors => Path.Combine(Local, "hook-errors.log");

    /// <summary>Pre-compact handoff notes.</summary>
    public string HandoffDir => Path.Combine(Local, "handoff");

    /// <summary>The recap written by the last session.</summary>
    public string RecapFile => Path.Combine(MonarchDir, "recap", "RECAP.md");

    /// <summary>Walks up from <paramref name="start"/> to the nearest directory containing <c>.monarch/</c>; no process is started.</summary>
    /// <param name="start">Directory to start at.</param>
    /// <param name="monarchHome">The account-global directory, which is never a project's <c>.monarch</c>.</param>
    /// <param name="userHome">The user's home; its <c>.monarch</c> is never a project either, whatever <c>MONARCH_HOME</c> says (Q-0125). Null resolves the real home.</param>
    /// <returns>The paths or null when the directory is not awakened.</returns>
    public static ProjectPaths? Locate(string start, string monarchHome, string? userHome = null) => LocateAll(start, monarchHome, userHome).FirstOrDefault();

    /// <summary>
    /// Every awakened project from <paramref name="start"/> upwards, nearest first (ADR-0011 rule 6): a nested <c>.monarch</c> directory never hides
    /// the project around it. No process is started.
    /// </summary>
    /// <param name="start">Directory to start at.</param>
    /// <param name="monarchHome">The account-global directory, which is never a project's <c>.monarch</c>.</param>
    /// <param name="userHome">The user's home; its <c>.monarch</c> is never a project either, whatever <c>MONARCH_HOME</c> says (Q-0125). Null resolves the real home.</param>
    /// <returns>The projects, possibly empty.</returns>
    public static List<ProjectPaths> LocateAll(string start, string monarchHome, string? userHome = null)
    {
        var found = new List<ProjectPaths>();
        if (string.IsNullOrEmpty(start))
        {
            return found;
        }

        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(monarchHome));
        userHome = string.IsNullOrEmpty(userHome) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : userHome;
        var userMonarch = string.IsNullOrEmpty(userHome) ? null : Path.TrimEndingDirectorySeparator(Path.Combine(Path.GetFullPath(userHome), ".monarch"));
        var globals = GlobalDirs(monarchHome, userHome);
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".monarch");
            var trimmed = Path.TrimEndingDirectorySeparator(candidate);
            if (Directory.Exists(candidate)
                && !string.Equals(trimmed, home, StringComparison.Ordinal)
                && !string.Equals(trimmed, userMonarch, StringComparison.Ordinal)
                && !globals.Any(g => SamePath(Physical(candidate), g)))
            {
                found.Add(new ProjectPaths(dir.FullName));
            }
        }

        return found;
    }

    /// <summary>Physical (symlink-free) absolute path; components that do not exist stay lexical.</summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The physical path without trailing separator.</returns>
    public static string Physical(string path)
    {
        var p = PathCanon.Resolve("/", path);
        return p.Length > 1 ? p.TrimEnd('/') : p;
    }

    /// <summary>Path equality; case- and fold-insensitive on platforms whose default file systems fold (ADR-0011 rule 8), ordinal elsewhere.</summary>
    /// <param name="a">Physical path.</param>
    /// <param name="b">Physical path.</param>
    /// <returns>True when both name the same location.</returns>
    public static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.Ordinal) || (FoldsCase && string.Equals(PathFold.Fold(a), PathFold.Fold(b), StringComparison.Ordinal));

    /// <summary>True when <paramref name="path"/> equals <paramref name="root"/> or lies below it; whole components only (never a string prefix).</summary>
    /// <param name="path">Physical path.</param>
    /// <param name="root">Physical path.</param>
    /// <returns>True when inside.</returns>
    public static bool IsSameOrBelow(string path, string root)
    {
        if (SamePath(path, root))
        {
            return true;
        }

        var prefix = root == "/" ? "/" : root + "/";
        return FoldsCase
            ? PathFold.Fold(path).StartsWith(PathFold.Fold(prefix), StringComparison.Ordinal)
            : path.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static bool FoldsCase => OperatingSystem.IsMacOS() || OperatingSystem.IsWindows();

    /// <summary>The physical global directories that are never a project's <c>.monarch</c>: <c>MONARCH_HOME</c> and <c>~/.monarch</c>.</summary>
    /// <param name="monarchHome">The account-global directory.</param>
    /// <param name="userHome">The user's home; null resolves the real one.</param>
    /// <returns>Zero to two physical paths.</returns>
    public static List<string> GlobalDirs(string monarchHome, string? userHome)
    {
        var list = new List<string> { Physical(Path.GetFullPath(monarchHome)) };
        userHome = string.IsNullOrEmpty(userHome) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : userHome;
        if (!string.IsNullOrEmpty(userHome))
        {
            list.Add(Physical(Path.Combine(Path.GetFullPath(userHome), ".monarch")));
        }

        return list;
    }

    /// <summary>
    /// True when the project root's <c>.monarch</c> is physically one of the global directories, or the project lies inside one of them
    /// (identity of the resolved directory, not of the path string; ADR-0013 rule 3.5).
    /// </summary>
    /// <param name="projectRoot">Candidate project root.</param>
    /// <param name="globals">Result of <see cref="GlobalDirs"/>.</param>
    /// <returns>True when the candidate must not be a project.</returns>
    public static bool IsGlobal(string projectRoot, IReadOnlyList<string> globals)
    {
        var physicalRoot = Physical(projectRoot);
        var monarch = Physical(Path.Combine(physicalRoot, ".monarch"));
        return globals.Any(g => SamePath(monarch, g) || IsSameOrBelow(physicalRoot, g));
    }

    /// <summary>True when <c>.monarch/system.yaml</c> is a regular file directly below <paramref name="root"/>.</summary>
    /// <param name="root">Candidate project root.</param>
    /// <returns>True for an awakened project.</returns>
    public static bool HasSystemYaml(string root) => File.Exists(Path.Combine(root, ".monarch", "system.yaml"));
}

/// <summary>Which stage of ADR-0013 chose the project.</summary>
internal enum ProjectFailure
{
    /// <summary>Resolved.</summary>
    None,

    /// <summary>The <c>project</c> parameter was invalid or outside the roots.</summary>
    Parameter,

    /// <summary>No parameter and no unique project.</summary>
    NotFound,
}

/// <summary>Outcome of <see cref="ProjectResolver.Resolve"/>.</summary>
/// <param name="Paths">The resolved project or null.</param>
/// <param name="Source"><c>parameter</c>, <c>roots</c> or <c>cwd</c>.</param>
/// <param name="Failure">Why there is none.</param>
/// <param name="Error">Message with the way forward.</param>
/// <param name="RootsCount">Number of accepted client roots.</param>
/// <param name="CwdProject">Project of the process cwd or empty.</param>
internal sealed record ProjectResolution(ProjectPaths? Paths, string Source, ProjectFailure Failure, string Error, int RootsCount, string CwdProject);

/// <summary>Resolves the project of one MCP call: explicit parameter, then client roots, then the process cwd (ADR-0013).</summary>
internal static class ProjectResolver
{
    private const int ScanDepth = 2;
    private const int MaxCandidates = 10;
    private static readonly string[] SkippedNames = [".git", ".monarch", "node_modules"];

    /// <summary>Resolves one call.</summary>
    /// <param name="env">The environment; <c>Cwd</c> is the unchangeable start cwd of the server process.</param>
    /// <param name="parameter">The <c>project</c> argument, or null when absent.</param>
    /// <param name="clientRoots">The accepted <c>file://</c> roots of the client.</param>
    /// <returns>The resolution.</returns>
    public static ProjectResolution Resolve(CliEnv env, string? parameter, IReadOnlyList<string> clientRoots)
    {
        var cwd = ProjectPaths.Physical(Path.GetFullPath(env.Cwd));
        var roots = new List<string>();
        foreach (var r in clientRoots.Where(Path.IsPathFullyQualified).Select(ProjectPaths.Physical))
        {
            if (!roots.Any(x => ProjectPaths.SamePath(x, r)))
            {
                roots.Add(r);
            }
        }

        var effective = roots.Count > 0 ? roots : [cwd];
        var globals = ProjectPaths.GlobalDirs(env.Home, env.UserHome);
        var cwdProject = ProjectPaths.Locate(cwd, env.Home, env.UserHome)?.Root ?? string.Empty;
        ProjectResolution Fail(ProjectFailure f, string error) => new(null, string.Empty, f, error, roots.Count, cwdProject);
        ProjectResolution Ok(string root, string source) => new(new ProjectPaths(root), source, ProjectFailure.None, string.Empty, roots.Count, cwdProject);

        if (parameter is not null)
        {
            string Reject(string stage, string why) => $"project parameter rejected ({stage}): {why}";
            if (string.IsNullOrWhiteSpace(parameter) || parameter.Contains('\0', StringComparison.Ordinal) || !Path.IsPathFullyQualified(parameter))
            {
                return Fail(ProjectFailure.Parameter, Reject("absolute", "the value must be an absolute path (no '~', no variables, nothing relative to the working directory)."));
            }

            var physical = ProjectPaths.Physical(Path.GetFullPath(parameter));
            if (!Directory.Exists(physical))
            {
                return Fail(ProjectFailure.Parameter, Reject("exists", $"'{physical}' is not an existing directory."));
            }

            if (ProjectPaths.IsGlobal(physical, globals))
            {
                return Fail(ProjectFailure.Parameter, Reject("global", $"'{physical}' is, or lies inside, the global Monarch directory (MONARCH_HOME or ~/.monarch), which is never a project."));
            }

            if (!ProjectPaths.HasSystemYaml(physical))
            {
                return Fail(ProjectFailure.Parameter, Reject("awakened", $"'{physical}' has no .monarch/system.yaml. The parameter names the project root itself, there is no upward search."));
            }

            if (!effective.Any(r => ProjectPaths.IsSameOrBelow(physical, r)))
            {
                return Fail(ProjectFailure.Parameter, Reject("roots", $"'{physical}' is not at or below one of the roots ({string.Join(", ", effective)})."));
            }

            return Ok(physical, "parameter");
        }

        if (roots.Count > 0)
        {
            var hits = new List<string>();
            foreach (var r in roots)
            {
                for (var dir = new DirectoryInfo(r); dir is not null; dir = dir.Parent)
                {
                    if (ProjectPaths.HasSystemYaml(dir.FullName) && !ProjectPaths.IsGlobal(dir.FullName, globals))
                    {
                        if (!hits.Any(h => ProjectPaths.SamePath(h, dir.FullName)))
                        {
                            hits.Add(dir.FullName);
                        }

                        break;
                    }
                }
            }

            if (hits.Count == 1)
            {
                return Ok(ProjectPaths.Physical(hits[0]), "roots");
            }
        }

        if (ProjectPaths.Locate(cwd, env.Home, env.UserHome) is { } byCwd)
        {
            return Ok(ProjectPaths.Physical(byCwd.Root), "cwd");
        }

        var candidates = new List<string>();
        foreach (var r in effective)
        {
            Scan(r, 0, globals, candidates);
        }

        var listed = candidates.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxCandidates).ToList();
        var list = listed.Count == 0
            ? $"none found at or below the roots: {string.Join(", ", effective)}"
            : "at or below the roots:\n" + string.Join("\n", listed.Select(c => "  - " + c));
        return Fail(ProjectFailure.NotFound, $"no awakened project determined. Pass parameter `project` (absolute path of an awakened project; {list}).");
    }

    private static void Scan(string dir, int depth, IReadOnlyList<string> globals, List<string> found)
    {
        if (ProjectPaths.HasSystemYaml(dir) && !ProjectPaths.IsGlobal(dir, globals))
        {
            found.Add(dir);
        }

        if (depth >= ScanDepth)
        {
            return;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(dir).Order(StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var child in children)
        {
            if (SkippedNames.Contains(Path.GetFileName(child), StringComparer.Ordinal))
            {
                continue;
            }

            try
            {
                if (new DirectoryInfo(child).LinkTarget is not null)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            Scan(child, depth + 1, globals, found);
        }
    }
}
