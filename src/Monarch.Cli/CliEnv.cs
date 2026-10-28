using System.Text;
using Monarch.Application.Net;
using Monarch.Application.Ports;

namespace Monarch.Cli;

/// <summary>Everything a command needs from its surroundings; injectable so the CLI runs in-process in tests.</summary>
/// <param name="Cwd">Working directory.</param>
/// <param name="Home">Account-global Monarch directory (<c>~/.monarch</c>, overridable via <c>MONARCH_HOME</c>).</param>
/// <param name="In">Standard input.</param>
/// <param name="Out">Standard output.</param>
/// <param name="Err">Standard error.</param>
/// <param name="Clock">Time source.</param>
/// <param name="Http">HTTP handler for GitHub calls; null uses the system handler (tests inject a fake).</param>
/// <param name="Tokens">GitHub token source; null resolves from the environment.</param>
/// <param name="StdinIsTty">True when stdin is an interactive terminal; owner-only commands (ADR-0011 rule 7) need it. Tests inject it.</param>
/// <param name="Vars">Environment variables of the calling process; null reads the real process environment (tests inject a fixed set).</param>
internal sealed record CliEnv(string Cwd, string Home, TextReader In, TextWriter Out, TextWriter Err, IClock Clock, HttpMessageHandler? Http = null, GitHubTokenProvider? Tokens = null, bool StdinIsTty = false, IReadOnlyDictionary<string, string?>? Vars = null)
{
    /// <summary>Value of an environment variable of the calling process.</summary>
    public string? GetVar(string name) => Vars is null ? Environment.GetEnvironmentVariable(name) : Vars.GetValueOrDefault(name);

    /// <summary>Builds the environment of the running process.</summary>
    public static CliEnv FromProcess()
    {
        var home = Environment.GetEnvironmentVariable("MONARCH_HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".monarch");
        }

        var utf8 = new UTF8Encoding(false);
        return new CliEnv(
            Directory.GetCurrentDirectory(),
            Path.GetFullPath(home),
            new StreamReader(Console.OpenStandardInput(), utf8),
            new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = false },
            new StreamWriter(Console.OpenStandardError(), utf8) { AutoFlush = true },
            new SystemClock(),
            StdinIsTty: !Console.IsInputRedirected);
    }
}
