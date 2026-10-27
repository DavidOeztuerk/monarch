using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Ledger;

namespace Monarch.Hosting;

/// <summary>Ledger registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class LedgerBuilderExtensions
{
    /// <summary>Registers the JSONL ledger.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Optional options callback, e.g. <c>o =&gt; o.Root = "/repo"</c>.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder UseJsonlLedger(this MonarchBuilder builder, Action<JsonlLedgerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var o = builder.Services.AddOptions<JsonlLedgerOptions>();
        if (configure is not null)
        {
            o.Configure(configure);
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JsonlLedgerOptions>, JsonlLedgerOptionsValidator>());
        builder.Services.AddSingleton<ILedgerStore>(sp =>
            new JsonlLedgerStore(sp.GetRequiredService<IOptions<JsonlLedgerOptions>>().Value, sp.GetRequiredService<IClock>()));
        return builder;
    }

    /// <summary>Registers the SQLite read model.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="path">Database file path.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder UseSqliteReadModel(this MonarchBuilder builder, string path = ".monarch/local/read.db")
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        builder.Services.AddSingleton(new SqliteReadModelOptions(path));
        builder.Services.AddSingleton<IReadModel>(sp => new SqliteReadModel(sp.GetRequiredService<SqliteReadModelOptions>()));
        return builder;
    }
}
