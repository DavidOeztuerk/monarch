using System.ComponentModel;
using System.Diagnostics;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Git as secondary anchor: a ledger file that is tracked and unmodified against <c>HEAD</c> is protected by the
/// content-addressed, reviewed git history even when the local head anchor does not know it (fresh clone, merge).
/// Never used on the hook hot path; falls back to "not anchored" when git is unavailable.
/// </summary>
internal static class GitAnchor
{
    private const int TimeoutMs = 10_000;

    /// <summary>Returns whether <paramref name="relative"/> is tracked by git and equals its committed content.</summary>
    public static bool IsAnchoredByGit(string root, string relative) =>
        Run(root, "ls-files", "--error-unmatch", "--", relative) && Run(root, "diff", "--quiet", "HEAD", "--", relative);

    /// <summary>Returns whether <paramref name="relative"/> is tracked by git, regardless of its content.</summary>
    public static bool IsTracked(string root, string relative) => Run(root, "ls-files", "--error-unmatch", "--", relative);

    private static bool Run(string root, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return false;
            }

            p.StandardInput.Close();
            var drainOut = p.StandardOutput.ReadToEndAsync();
            var drainErr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeoutMs))
            {
                p.Kill(true);
                return false;
            }

            Task.WaitAll(drainOut, drainErr);
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
