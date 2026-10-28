using Monarch.Domain;

namespace Monarch.Domain.Tests;

public class DomainSmokeTests
{
    [Fact]
    public void Domain_has_no_package_dependencies()
    {
        var refs = typeof(DomainAssembly).Assembly.GetReferencedAssemblies();
        Assert.All(refs, r => Assert.True(
            r.Name is "System.Runtime" or "netstandard" || r.Name!.StartsWith("System.", StringComparison.Ordinal),
            $"Unexpected reference {r.Name}"));
    }
}
