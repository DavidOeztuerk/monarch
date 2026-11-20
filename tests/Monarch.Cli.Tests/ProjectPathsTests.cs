namespace Monarch.Cli.Tests;

/// <summary>Q-0125: the user's own <c>~/.monarch</c> is never a project, even when MONARCH_HOME points elsewhere.</summary>
public class ProjectPathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "monarch-pp-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void Real_home_dot_monarch_is_never_a_project_when_MONARCH_HOME_points_elsewhere()
    {
        var monarchHome = Path.Combine(_root, "mh");
        var fakeHome = Path.Combine(_root, "home");
        var cwd = Path.Combine(fakeHome, "work", "sub");
        Directory.CreateDirectory(monarchHome);
        Directory.CreateDirectory(Path.Combine(fakeHome, ".monarch"));
        Directory.CreateDirectory(cwd);

        Assert.Empty(ProjectPaths.LocateAll(cwd, monarchHome, fakeHome));
        Assert.Null(ProjectPaths.Locate(cwd, monarchHome, fakeHome));
    }

    [Fact]
    public void A_project_below_the_home_is_still_found()
    {
        var monarchHome = Path.Combine(_root, "mh");
        var fakeHome = Path.Combine(_root, "home");
        var proj = Path.Combine(fakeHome, "proj");
        Directory.CreateDirectory(Path.Combine(fakeHome, ".monarch"));
        Directory.CreateDirectory(Path.Combine(proj, ".monarch"));

        Assert.Equal(proj, ProjectPaths.Locate(Path.Combine(proj, "x"), monarchHome, fakeHome)?.Root);
    }
}
