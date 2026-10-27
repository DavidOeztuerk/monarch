using Microsoft.Extensions.DependencyInjection;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Hosting;

/// <summary>Ledger registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class LedgerBuilderExtensions
{
    /// <summary>Registers the JSONL ledger.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Optional options callback.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder UseJsonlLedger(this MonarchBuilder builder, Action<JsonlLedgerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = new JsonlLedgerOptions();
        configure?.Invoke(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<ILedgerStore, JsonlLedgerStore>();
        return builder;
    }

    /// <summary>Registers the SQLite read model.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="path">Database file path.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder UseSqliteReadModel(this MonarchBuilder builder, string path = ".monarch/local/read.db")
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton(new SqliteReadModelOptions(path));
        builder.Services.AddSingleton<IReadModel, SqliteReadModel>();
        return builder;
    }
}
