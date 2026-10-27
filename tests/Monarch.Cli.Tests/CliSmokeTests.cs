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
    public void Composition_root_resolves_ledger_read_model_board_and_evidence_with_a_temp_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "monarch-cli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var provider = CompositionRoot.Build(root);
            var ledger = provider.GetRequiredService<ILedgerStore>();
            var entry = ledger.Append("s1", "knight", "GateOpened", System.Text.Json.JsonDocument.Parse("""{"gate":"G-1","rank":"A","title":"T"}""").RootElement.Clone());
            Assert.StartsWith(".monarch/ledger/", entry.File, StringComparison.Ordinal);
            Assert.True(File.Exists(Path.Combine(root, entry.File)));
            var model = provider.GetRequiredService<IReadModel>();
            model.Rebuild(root);
            Assert.Single(model.GetGates());
            Assert.True(File.Exists(Path.Combine(root, ".monarch", "local", "read.db")));
            Assert.IsType<Monarch.Infrastructure.Boards.LocalFileBoard>(provider.GetRequiredService<IQuestBoard>());
            Assert.NotNull(provider.GetRequiredService<IEvidenceSource>());
            Assert.NotNull(provider.GetRequiredService<Monarch.Infrastructure.Boards.GitHubIssuesBoard>());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Invalid_ledger_options_fail_validation()
    {
        var services = new ServiceCollection();
        services.AddMonarch(_ => { }).UseJsonlLedger(o => o.Root = string.Empty);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ILedgerStore>());
    }

    [Fact]
    public void Invalid_github_board_options_fail_validation()
    {
        var services = new ServiceCollection();
        services.AddMonarch(_ => { }).AddGitHubBoard(o => o.Owner = "x");
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<Monarch.Infrastructure.Boards.GitHubIssuesBoard>());
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
