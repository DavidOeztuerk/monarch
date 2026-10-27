using Microsoft.Extensions.DependencyInjection;
using Monarch.Hosting;
using Monarch.Infrastructure.Boards;

namespace Monarch.Cli;

/// <summary>Builds the dependency injection container for the CLI.</summary>
public static class CompositionRoot
{
    /// <summary>Creates the service provider with all Monarch components registered.</summary>
    /// <param name="root">Project root; defaults to the current directory.</param>
    /// <returns>The built service provider.</returns>
    public static ServiceProvider Build(string? root = null)
    {
        root = Path.GetFullPath(root ?? Directory.GetCurrentDirectory());
        var services = new ServiceCollection();
        services.AddMonarch(o => { o.ManaReservePct = 10; o.Root = root; })
            .UseJsonlLedger(o => o.Root = root)
            .UseSqliteReadModel(Path.Combine(root, ".monarch", "local", "read.db"))
            .AddBoard<LocalFileBoard>()
            .AddGitHubBoard(o => { o.Owner = "DavidOeztuerk"; o.Repo = "monarch"; })
            .AddGitHubChecksEvidence()
            .AddClaudeCodeRuntime();
        return services.BuildServiceProvider();
    }
}
