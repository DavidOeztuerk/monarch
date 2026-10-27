using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Monarch.Application.Ports;
using Monarch.Hosting;

namespace Monarch.Cli.Tests;

public class CliSmokeTests
{
    [Fact]
    public void Composition_root_builds_a_provider()
    {
        using var provider = CompositionRoot.Build();
        Assert.NotNull(provider.GetRequiredService<IClock>());
        Assert.Equal(10, provider.GetRequiredService<IOptions<MonarchOptions>>().Value.ManaReservePct);
    }

    [Fact]
    public void Invalid_options_fail_validation()
    {
        var services = new ServiceCollection();
        services.AddMonarch(o => o.ManaReservePct = 500);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MonarchOptions>>().Value);
    }
}
