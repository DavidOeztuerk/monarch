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
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".monarch");
            var trimmed = Path.TrimEndingDirectorySeparator(candidate);
            if (Directory.Exists(candidate)
                && !string.Equals(trimmed, home, StringComparison.Ordinal)
                && !string.Equals(trimmed, userMonarch, StringComparison.Ordinal))
            {
                found.Add(new ProjectPaths(dir.FullName));
            }
        }

        return found;
    }
}
