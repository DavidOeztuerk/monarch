using Microsoft.Extensions.DependencyInjection;
using Monarch.Application.Ports;
using Monarch.Hosting;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Infrastructure.Tests;

public class InfrastructureSmokeTests
{
    [Fact]
    public void Placeholders_throw_not_implemented()
    {
        Assert.Throws<NotImplementedException>(() => new JsonlLedgerStore());
    }

    [Fact]
    public void Ledger_registration_adds_descriptors()
    {
        var services = new ServiceCollection();
        services.AddMonarch(_ => { }).UseJsonlLedger().UseSqliteReadModel();
        Assert.Contains(services, d => d.ServiceType == typeof(ILedgerStore));
        Assert.Contains(services, d => d.ServiceType == typeof(IReadModel));
    }
}
