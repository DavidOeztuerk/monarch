using FsCheck.Xunit;

namespace Monarch.Policy.Tests;

public class PolicySmokeTests
{
    [Property]
    public bool Property_wiring_runs(int x) => (long)x + 0 == x;

    [Fact]
    public void Policy_assembly_is_loadable() => Assert.Equal("Monarch.Policy", PolicyAssembly.Name);
}
