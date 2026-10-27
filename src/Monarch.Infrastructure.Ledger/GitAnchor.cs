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

    /// <summary>Lists the ledger files in the git index (second, independent source); null when git is unavailable or this is no repository.</summary>
    public static List<string>? TrackedLedgerFiles(string root) =>
        Capture(root, "ls-files", "-z", "--", LedgerFiles.LedgerDir) is { } o ? Split(o) : null;

    /// <summary>Lists ledger files whose working tree differs from <c>HEAD</c> (including deleted ones); null when unavailable.</summary>
    public static HashSet<string>? ChangedAgainstHead(string root) =>
        Capture(root, "diff", "-z", "--name-only", "--relative", "HEAD", "--", LedgerFiles.LedgerDir) is { } o ? [.. Split(o)] : null;

    /// <summary>Returns the content of <paramref name="relative"/> at <c>HEAD</c>, or null when it is not part of HEAD.</summary>
    public static byte[]? HeadBlob(string root, string relative) => Capture(root, "show", "HEAD:./" + relative);

    private static List<string> Split(byte[] output) =>
        [.. System.Text.Encoding.UTF8.GetString(output).Split('\0', StringSplitOptions.RemoveEmptyEntries)];

    private static bool Run(string root, params string[] args) => Execute(root, args, out _);

    private static byte[]? Capture(string root, params string[] args) => Execute(root, args, out var output) ? output : null;

    private static bool Execute(string root, string[] args, out byte[] output)
    {
        output = [];
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
            var drainOut = ReadAllAsync(p.StandardOutput.BaseStream);
            var drainErr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(TimeoutMs))
            {
                p.Kill(true);
                return false;
            }

            Task.WaitAll(drainOut, drainErr);
            output = drainOut.Result;
            return p.ExitCode == 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
