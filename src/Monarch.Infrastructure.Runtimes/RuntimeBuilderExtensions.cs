using Microsoft.Extensions.DependencyInjection;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Runtimes;

namespace Monarch.Hosting;

/// <summary>Runtime registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class RuntimeBuilderExtensions
{
    /// <summary>Registers the Claude Code agent runtime.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder AddClaudeCodeRuntime(this MonarchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IAgentRuntime, ClaudeCodeRuntime>();
        return builder;
    }
}
