using System.Diagnostics;
using System.Globalization;

namespace Monarch.Cli;

/// <summary>The merge method of a repository (ADR-0007 Nachtrag Q-0140): <c>squash</c> or <c>merge</c>, never rebase.</summary>
/// <param name="Method"><c>squash</c> or <c>merge</c>.</param>
/// <param name="Source">Where the value comes from, in words for the output and the ledger.</param>
/// <param name="Fallback"><see langword="true"/> when the history could not be read and the default <c>squash</c> applies.</param>
internal sealed record MergeMethodChoice(string Method, string Source, bool Fallback = false);

/// <summary>Resolves the merge method: the configured value, else derived from the first-parent history of the integration branch.</summary>
internal static class MergeMethodResolver
{
    internal const string Squash = "squash";
    internal const string Merge = "merge";
    internal const int HistoryLimit = 50;
    internal const int DefaultGitSeconds = 10;

    /// <summary>True for the two allowed values.</summary>
    public static bool IsValid(string? value) => value is Squash or Merge;

    /// <summary>The configured method, or the one derived from the history when the key is missing. Test seams: <c>MONARCH_GIT_BIN</c> (git executable) and <c>MONARCH_MERGE_GIT_SECONDS</c> (deadline per git call).</summary>
    public static MergeMethodChoice Resolve(CliEnv env, SystemConfig config, string root, CancellationToken ct = default)
    {
        if (config.MergeMethod is { } configured)
        {
            return new MergeMethodChoice(configured, "Konfiguration (git.merge_method)");
        }

        var seconds = int.TryParse(env.GetVar("MONARCH_MERGE_GIT_SECONDS"), NumberStyles.None, CultureInfo.InvariantCulture, out var s) && s > 0 ? s : DefaultGitSeconds;
        return Derive(root, config.IntegrationBranch, TimeSpan.FromSeconds(seconds), env.GetVar("MONARCH_GIT_BIN") is { Length: > 0 } bin ? bin : "git", ct);
    }

    /// <summary>Derives the method: more than half of the last 50 first-parent commits are merge commits -> merge, otherwise squash. A missing branch or an unreadable history gives squash.</summary>
    public static MergeMethodChoice Derive(string root, string integrationBranch, TimeSpan? timeout = null, string git = "git", CancellationToken ct = default)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(DefaultGitSeconds);
        foreach (var reference in new[] { $"refs/remotes/origin/{integrationBranch}", $"refs/heads/{integrationBranch}" })
        {
            if (RunGit(git, root, limit, ct, "rev-list", "--first-parent", "--parents", "-n", HistoryLimit.ToString(CultureInfo.InvariantCulture), reference) is not { } output)
            {
                continue;
            }

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var label = reference.StartsWith("refs/remotes/", StringComparison.Ordinal) ? $"origin/{integrationBranch}" : integrationBranch;
            var merges = lines.Count(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3);
            var method = lines.Length > 0 && merges * 2 > lines.Length ? Merge : Squash;
            return new MergeMethodChoice(method, $"aus der Historie von {label} abgeleitet ({merges} von {lines.Length} Commits der first-parent-Linie sind Merge-Commits)");
        }

        return new MergeMethodChoice(Squash, $"Standard (Historie von {integrationBranch} nicht lesbar, Rückfall auf squash)", true);
    }

    /// <summary>Runs git under a fixed deadline; null on any failure or when the deadline passes (the process tree is killed). A cancelled <paramref name="ct"/> (the caller's deadline) throws.</summary>
    private static string? RunGit(string git, string cwd, TimeSpan limit, CancellationToken ct, params string[] args)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(limit);
        Process? p = null;
        try
        {
            var psi = new ProcessStartInfo(git) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr = p.StandardError.ReadToEndAsync(linked.Token);
            p.WaitForExitAsync(linked.Token).GetAwaiter().GetResult();
            _ = stderr.GetAwaiter().GetResult();
            var text = stdout.GetAwaiter().GetResult();
            return p.ExitCode == 0 ? text : null;
        }
        catch (OperationCanceledException)
        {
            Kill(p);
            ct.ThrowIfCancellationRequested();
            return null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Kill(p);
            return null;
        }
        finally
        {
            p?.Dispose();
        }
    }

    private static void Kill(Process? p)
    {
        try
        {
            p?.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // already gone
        }
    }
}
