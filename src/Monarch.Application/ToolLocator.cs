namespace Monarch.Application;

/// <summary>
/// Finds helper programs (<c>git</c>, <c>gh</c>) by absolute path, only in the absolute entries of <c>PATH</c> (ADR-0016, #60).
/// The working directory and the directory of the executable never count; a bare name is never handed to the process start.
/// </summary>
public sealed class ToolLocator
{
    private static readonly Lazy<ToolLocator> Shared = new(() => new ToolLocator(Environment.GetEnvironmentVariable("PATH"), Console.Error));

    private readonly string? _path;
    private readonly TextWriter _err;
    private readonly HashSet<string> _warned = [];
    private readonly object _gate = new();

    /// <summary>Creates a locator over an explicit <c>PATH</c> value and error writer (test seam).</summary>
    /// <param name="path">The <c>PATH</c> value, entries separated by a colon.</param>
    /// <param name="err">Receives the single "not found" line per name.</param>
    public ToolLocator(string? path, TextWriter err)
    {
        _path = path;
        _err = err;
    }

    /// <summary>Absolute path of <paramref name="name"/> in the process <c>PATH</c>, or <see langword="null"/> (one stderr line per process and name).</summary>
    /// <param name="name">The program name, for example <c>git</c>.</param>
    public static string? Find(string name) => Shared.Value.Locate(name);

    /// <summary>Absolute path of <paramref name="name"/> in this locator's <c>PATH</c>, or <see langword="null"/> (one stderr line per name).</summary>
    /// <param name="name">The program name, for example <c>git</c>.</param>
    public string? Locate(string name)
    {
        if (!string.IsNullOrEmpty(_path))
        {
            foreach (var entry in _path.Split(':'))
            {
                if (entry.Length == 0 || !Path.IsPathRooted(entry))
                {
                    continue;
                }

                var candidate = Path.Combine(entry, name);
                if (IsExecutableFile(candidate))
                {
                    return candidate;
                }
            }
        }

        lock (_gate)
        {
            if (_warned.Add(name))
            {
                _err.WriteLine($"monarch: {name} nicht in PATH gefunden (nur absolute Einträge zählen)");
            }
        }

        return null;
    }

    private static bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (OperatingSystem.IsWindows())
            {
                return true;
            }

            return (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
