using Xunit;

[assembly: AssemblyFixture(typeof(Monarch.Cli.Tests.HermeticHome))]

namespace Monarch.Cli.Tests;

/// <summary>Assembly fixture pointing <c>MONARCH_HOME</c> at a per-run temp directory so no test can touch the real <c>~/.monarch</c>.</summary>
public sealed class HermeticHome : IDisposable
{
    /// <summary>Root of the per-run temp Monarch home.</summary>
    public static string Root { get; } = Path.Combine(Path.GetTempPath(), "monarch-run-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temp home and exports it as <c>MONARCH_HOME</c>.</summary>
    public HermeticHome()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("MONARCH_HOME", Root);
    }

    /// <summary>Removes the temp home.</summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MONARCH_HOME", null);
        Directory.Delete(Root, true);
    }
}

/// <summary>Guards that the test process never resolves the real Monarch home.</summary>
public class HermeticHomeTests
{
    [Fact]
    public void Process_environment_never_resolves_to_the_real_monarch_home()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".monarch");
        var home = CliEnv.FromProcess().Home;
        Assert.NotEqual(Path.GetFullPath(real), home);
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), home, StringComparison.Ordinal);
    }
}
