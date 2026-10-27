using System.Text;
using Monarch.Application.Ports;

namespace Monarch.Cli;

/// <summary>Everything a command needs from its surroundings; injectable so the CLI runs in-process in tests.</summary>
/// <param name="Cwd">Working directory.</param>
/// <param name="Home">Account-global Monarch directory (<c>~/.monarch</c>, overridable via <c>MONARCH_HOME</c>).</param>
/// <param name="In">Standard input.</param>
/// <param name="Out">Standard output.</param>
/// <param name="Err">Standard error.</param>
/// <param name="Clock">Time source.</param>
internal sealed record CliEnv(string Cwd, string Home, TextReader In, TextWriter Out, TextWriter Err, IClock Clock)
{
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
            new SystemClock());
    }
}
