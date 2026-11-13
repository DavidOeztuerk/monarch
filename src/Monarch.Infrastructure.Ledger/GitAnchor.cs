using System.ComponentModel;
using System.Diagnostics;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// Git as secondary anchor: a ledger file that is tracked and unmodified against <c>HEAD</c> is protected by the
/// content-addressed, reviewed git history even when the local head anchor does not know it (fresh clone, merge).
/// Used on the hook path only when the ledger file exists, has no local anchor and the hook writes (deny, ask or signal);
/// falls back to "not anchored" when git is unavailable. git is resolved by <c>ToolLocator</c> from absolute <c>PATH</c> entries only.
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
        var result = Invoke(root, args, null, TimeSpan.FromMilliseconds(TimeoutMs), isolated: false);
        output = result.Output;
        return result.Status == GitStatus.Ok;
    }

    /// <summary>How an isolated git call ended; the history check (ADR-0018) must tell "git failed" from "git said no".</summary>
    internal enum GitStatus
    {
        /// <summary>Exit code 0.</summary>
        Ok,

        /// <summary>git ran and exited non-zero.</summary>
        Failed,

        /// <summary>git did not finish in time and was killed.</summary>
        TimedOut,

        /// <summary>git was not found or could not be started.</summary>
        Unavailable,
    }

    /// <summary>Result of an isolated git call.</summary>
    /// <param name="Status">How the call ended.</param>
    /// <param name="Output">Standard output (empty unless <see cref="GitStatus.Ok"/> or <see cref="GitStatus.Failed"/>).</param>
    internal readonly record struct GitResult(GitStatus Status, byte[] Output)
    {
        /// <summary>Gets a value indicating whether git exited with 0.</summary>
        public bool Ok => Status == GitStatus.Ok;

        /// <summary>Gets the output as UTF-8 text.</summary>
        public string Text => System.Text.Encoding.UTF8.GetString(Output);
    }

    /// <summary>The commit id of <c>HEAD</c> (40 or 64 lowercase hex characters), or null when git gives no unambiguous commit.</summary>
    public static string? HeadCommit(string root)
    {
        var r = Isolated(root, ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"], null, TimeSpan.FromMilliseconds(TimeoutMs));
        return r.Ok && LedgerHistory.IsCommitId(r.Text.Trim()) ? r.Text.Trim() : null;
    }

    /// <summary>
    /// Runs git for the history check: without inherited <c>GIT_*</c> variables, with replace objects off and grafts pointed at a file that does not exist,
    /// so that <c>git replace</c> or a graft file cannot bend the history with one command. <paramref name="stdin"/> is written and closed.
    /// </summary>
    internal static GitResult Isolated(string root, string[] args, byte[]? stdin, TimeSpan timeout) => Invoke(root, args, stdin, timeout, isolated: true);

    private static GitResult Invoke(string root, string[] args, byte[]? stdin, TimeSpan timeout, bool isolated)
    {
        try
        {
            if (Monarch.Application.ToolLocator.Find("git") is not { } git)
            {
                return new GitResult(GitStatus.Unavailable, []);
            }

            var psi = new ProcessStartInfo(git)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            };
            if (isolated)
            {
                foreach (var key in psi.Environment.Keys.Where(k => k.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    psi.Environment.Remove(key);
                }

                psi.Environment["GIT_NO_REPLACE_OBJECTS"] = "1";
                psi.Environment["GIT_GRAFT_FILE"] = Path.Combine(Path.GetTempPath(), "monarch-no-graft-" + Guid.NewGuid().ToString("N"));
            }

            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return new GitResult(GitStatus.Unavailable, []);
            }

            var drainOut = ReadAllAsync(p.StandardOutput.BaseStream);
            var drainErr = p.StandardError.ReadToEndAsync();
            Task feed = Task.CompletedTask;
            if (stdin is null)
            {
                p.StandardInput.Close();
            }
            else
            {
                feed = Task.Run(() =>
                {
                    try
                    {
                        p.StandardInput.BaseStream.Write(stdin);
                        p.StandardInput.Close();
                    }
                    catch (IOException)
                    {
                        // git exited before it read everything; its exit code tells.
                    }
                });
            }
            if (!p.WaitForExit(timeout))
            {
                p.Kill(true);
                return new GitResult(GitStatus.TimedOut, []);
            }

            Task.WaitAll(drainOut, drainErr, feed);
            return new GitResult(p.ExitCode == 0 ? GitStatus.Ok : GitStatus.Failed, drainOut.Result);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return new GitResult(GitStatus.Unavailable, []);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
