using System.Diagnostics;

namespace Monarch.Cli;

/// <summary>The merge method of a repository (ADR-0007 Nachtrag Q-0140): <c>squash</c> or <c>merge</c>, never rebase.</summary>
/// <param name="Method"><c>squash</c> or <c>merge</c>.</param>
/// <param name="Source">Where the value comes from, in words for the output.</param>
internal sealed record MergeMethodChoice(string Method, string Source);

/// <summary>Resolves the merge method: the configured value, else derived from the first-parent history of the integration branch.</summary>
internal static class MergeMethodResolver
{
    internal const string Squash = "squash";
    internal const string Merge = "merge";
    internal const int HistoryLimit = 50;
    internal static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(10);

    /// <summary>True for the two allowed values.</summary>
    public static bool IsValid(string? value) => value is Squash or Merge;

    /// <summary>The configured method, or the one derived from the history when the key is missing.</summary>
    public static MergeMethodChoice Resolve(SystemConfig config, string root) =>
        config.MergeMethod is { } configured
            ? new MergeMethodChoice(configured, "Konfiguration (git.merge_method)")
            : Derive(root, config.IntegrationBranch);

    /// <summary>Derives the method: more than half of the last 50 first-parent commits are merge commits -> merge, otherwise squash. A missing branch or an unreadable history gives squash.</summary>
    public static MergeMethodChoice Derive(string root, string integrationBranch)
    {
        foreach (var reference in new[] { $"refs/remotes/origin/{integrationBranch}", $"refs/heads/{integrationBranch}" })
        {
            if (ReadParents(root, reference) is not { } lines)
            {
                continue;
            }

            var label = reference.StartsWith("refs/remotes/", StringComparison.Ordinal) ? $"origin/{integrationBranch}" : integrationBranch;
            var merges = lines.Count(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3);
            var method = lines.Count > 0 && merges * 2 > lines.Count ? Merge : Squash;
            return new MergeMethodChoice(method, $"aus der Historie von {label} abgeleitet ({merges} von {lines.Count} Commits der first-parent-Linie sind Merge-Commits)");
        }

        return new MergeMethodChoice(Squash, $"Standard (Branch {integrationBranch} nicht lesbar, keine Historie)");
    }

    private static List<string>? ReadParents(string root, string reference)
    {
        var output = RunGit(root, "rev-list", "--first-parent", "--parents", "-n", HistoryLimit.ToString(System.Globalization.CultureInfo.InvariantCulture), reference);
        return output is null ? null : [.. output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>Runs git with a fixed deadline; null on any failure or when the deadline passes (the process is killed). Local git, outside the forge deadlines, but it must not hang.</summary>
    private static string? RunGit(string cwd, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            p.StandardInput.Close();
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(GitTimeout))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // already gone
                }

                return null;
            }

            p.WaitForExit();
            _ = stderr.GetAwaiter().GetResult();
            return p.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
