using System.Diagnostics;

namespace Monarch.Cli;

/// <summary>Runs git for the repository identity check (never used on the hook path).</summary>
internal static class Git
{
    /// <summary>Runs <c>git</c> with the arguments; returns trimmed stdout or null on any failure.</summary>
    public static string? Run(string cwd, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git") { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args)
            {
                psi.ArgumentList.Add(a);
            }

            using var p = Process.Start(psi);
            if (p is null)
            {
                return null;
            }

            var output = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
