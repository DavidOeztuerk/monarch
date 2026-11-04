namespace Monarch.Cli;

/// <summary>State of the secret-scan pre-commit hook.</summary>
internal enum SecretScanState
{
    Unknown,
    Missing,
    NoGitleaks,
    NotExecutable,
    Active,
}

/// <summary>Result of the hook detection: state plus the hook path (null when unknown).</summary>
internal sealed record SecretScanResult(SecretScanState State, string? Path)
{
    public string Code => State switch
    {
        SecretScanState.Active => "active",
        SecretScanState.NoGitleaks => "no-gitleaks-call",
        SecretScanState.NotExecutable => "not-executable",
        SecretScanState.Missing => "missing",
        _ => "unknown",
    };

    public string Text => State switch
    {
        SecretScanState.Active => $"aktiv ({Path})",
        SecretScanState.NoGitleaks => $"pre-commit-Hook ohne erkennbaren gitleaks-Aufruf ({Path})",
        SecretScanState.NotExecutable => $"pre-commit-Hook nicht ausfuehrbar ({Path})",
        SecretScanState.Missing => $"kein pre-commit-Hook ({Path})",
        _ => "unbekannt",
    };
}

/// <summary>Read-only detection of the gitleaks pre-commit hook; never writes, never runs gitleaks.</summary>
internal static class SecretScanHook
{
    private const int MaxRead = 64 * 1024;

    /// <summary>Detects the hook for the repository containing <paramref name="cwd"/>; Unknown outside git or without git.</summary>
    public static SecretScanResult Detect(string cwd)
    {
        var top = Git.Run(cwd, "rev-parse", "--show-toplevel");
        if (top is not { Length: > 0 })
        {
            return new(SecretScanState.Unknown, null);
        }

        string hook;
        var configured = Git.Run(cwd, "config", "--type=path", "--get", "core.hooksPath");
        if (configured is { Length: > 0 })
        {
            hook = Path.Combine(Path.GetFullPath(configured, top), "pre-commit");
        }
        else
        {
            var common = Git.Run(cwd, "rev-parse", "--git-common-dir");
            if (common is not { Length: > 0 })
            {
                return new(SecretScanState.Unknown, null);
            }

            hook = Path.Combine(Path.GetFullPath(common, cwd), "hooks", "pre-commit");
        }

        if (!File.Exists(hook))
        {
            return new(SecretScanState.Missing, hook);
        }

        try
        {
            if (!OperatingSystem.IsWindows()
                && (File.GetUnixFileMode(hook) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            {
                return new(SecretScanState.NotExecutable, hook);
            }

            using var fs = new FileStream(hook, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buf = new byte[MaxRead];
            var n = fs.Read(buf, 0, buf.Length);
            var text = System.Text.Encoding.UTF8.GetString(buf, 0, n);
            return new(text.Contains("gitleaks", StringComparison.OrdinalIgnoreCase) ? SecretScanState.Active : SecretScanState.NoGitleaks, hook);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(SecretScanState.Unknown, hook);
        }
    }
}
