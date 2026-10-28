using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monarch.Application.Ports;

namespace Monarch.Hosting;

/// <summary>Service collection extensions for Monarch.</summary>
public static class MonarchServiceCollectionExtensions
{
    /// <summary>Registers the Monarch core services and options.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that configures <see cref="MonarchOptions"/>.</param>
    /// <returns>A builder for further composition.</returns>
    public static MonarchBuilder AddMonarch(this IServiceCollection services, Action<MonarchOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MonarchOptions>().Configure(configure);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MonarchOptions>, MonarchOptionsValidator>());
        services.TryAddSingleton<IClock, SystemClock>();
        return new MonarchBuilder(services);
    }
}
