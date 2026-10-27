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
    /// <returns>The paths or null when the directory is not awakened.</returns>
    public static ProjectPaths? Locate(string start, string monarchHome)
    {
        if (string.IsNullOrEmpty(start))
        {
            return null;
        }

        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(monarchHome));
        var dir = new DirectoryInfo(Path.GetFullPath(start));
        for (; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".monarch");
            if (Directory.Exists(candidate) && !string.Equals(Path.TrimEndingDirectorySeparator(candidate), home, StringComparison.Ordinal))
            {
                return new ProjectPaths(dir.FullName);
            }
        }

        return null;
    }
}
