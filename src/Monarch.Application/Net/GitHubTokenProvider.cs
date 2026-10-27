using System.ComponentModel;
using System.Diagnostics;

namespace Monarch.Application.Net;

/// <summary>
/// Resolves a GitHub token from <c>GH_TOKEN</c>, then <c>GITHUB_TOKEN</c>, then <c>gh auth token</c> (child process,
/// stdout kept in memory only). The token is never logged or persisted.
/// </summary>
public sealed class GitHubTokenProvider
{
    private readonly Func<string, string?> _getEnv;
    private readonly Func<string?> _gh;
    private readonly Lock _gate = new();
    private bool _resolved;
    private string? _token;

    /// <summary>Initializes a provider using the process environment and the <c>gh</c> CLI.</summary>
    public GitHubTokenProvider()
        : this(Environment.GetEnvironmentVariable, RunGhAuthToken)
    {
    }

    /// <summary>Initializes a provider with custom sources (for tests).</summary>
    /// <param name="getEnv">Environment variable lookup.</param>
    /// <param name="ghAuthToken">Fallback that returns the output of <c>gh auth token</c>, or <see langword="null"/>.</param>
    public GitHubTokenProvider(Func<string, string?> getEnv, Func<string?> ghAuthToken)
    {
        _getEnv = getEnv;
        _gh = ghAuthToken;
    }

    /// <summary>Gets the token, resolving it once.</summary>
    /// <returns>The token, or <see langword="null"/> when none is available.</returns>
    public string? GetToken()
    {
        lock (_gate)
        {
            if (!_resolved)
            {
                _token = Clean(_getEnv("GH_TOKEN")) ?? Clean(_getEnv("GITHUB_TOKEN")) ?? Clean(_gh());
                _resolved = true;
            }

            return _token;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? RunGhAuthToken()
    {
        try
        {
            var psi = new ProcessStartInfo("gh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add("auth");
            psi.ArgumentList.Add("token");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(5000))
            {
                process.Kill(true);
                return null;
            }

            return process.ExitCode == 0 ? stdout.GetAwaiter().GetResult() : null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
