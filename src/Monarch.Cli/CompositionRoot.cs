using Microsoft.Extensions.DependencyInjection;
using Monarch.Hosting;
using Monarch.Infrastructure.Boards;

namespace Monarch.Cli;

/// <summary>Builds the dependency injection container for the CLI.</summary>
public static class CompositionRoot
{
    /// <summary>Creates the service provider with all Monarch components registered.</summary>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddMonarch(o => { o.ManaReservePct = 10; })
            .UseJsonlLedger()
            .UseSqliteReadModel()
            .AddBoard<LocalFileBoard>()
            .AddGitHubBoard(o => o.Owner = "DavidOeztuerk")
            .AddGitHubChecksEvidence()
            .AddClaudeCodeRuntime();
        return services.BuildServiceProvider();
    }
}
