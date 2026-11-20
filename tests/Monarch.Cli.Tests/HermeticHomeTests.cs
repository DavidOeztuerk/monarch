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
        // Captured before HOME is redirected, so the guard test still knows the real home.
        RealHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".monarch");
        _oldHome = Environment.GetEnvironmentVariable("HOME");
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "home"));
        Environment.SetEnvironmentVariable("MONARCH_HOME", Root);
        Environment.SetEnvironmentVariable("HOME", Path.Combine(Root, "home"));
    }

    private readonly string? _oldHome;

    /// <summary>The real <c>~/.monarch</c>, captured before HOME is redirected to a temp directory.</summary>
    public static string RealHome { get; private set; } = string.Empty;

    /// <summary>Removes the temp home.</summary>
    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MONARCH_HOME", null);
        Environment.SetEnvironmentVariable("HOME", _oldHome);
        Directory.Delete(Root, true);
    }
}

/// <summary>Guards that the test process never resolves the real Monarch home.</summary>
public class HermeticHomeTests
{
    [Fact]
    public void Process_environment_never_resolves_to_the_real_monarch_home()
    {
        var real = HermeticHome.RealHome;
        var home = CliEnv.FromProcess().Home;
        Assert.NotEqual(Path.GetFullPath(real), home);
        Assert.NotEqual(Path.GetFullPath(real), Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".monarch")));
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), home, StringComparison.Ordinal);
    }
}
