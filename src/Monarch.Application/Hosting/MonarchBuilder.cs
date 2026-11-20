using Microsoft.Extensions.DependencyInjection;

namespace Monarch.Hosting;

/// <summary>Fluent builder used to compose Monarch services.</summary>
/// <param name="services">The service collection being configured.</param>
public sealed class MonarchBuilder(IServiceCollection services)
{
    /// <summary>Gets the underlying service collection.</summary>
    public IServiceCollection Services { get; } = services;
}
