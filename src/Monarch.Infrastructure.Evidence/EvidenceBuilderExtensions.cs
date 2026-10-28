using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Evidence;

namespace Monarch.Hosting;

/// <summary>Evidence registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class EvidenceBuilderExtensions
{
    /// <summary>Registers the GitHub checks evidence source.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder AddGitHubChecksEvidence(this MonarchBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<GitHubTokenProvider>();
        builder.Services.AddSingleton<IEvidenceSource>(sp =>
            new GitHubChecksEvidence(new HttpClient { BaseAddress = new Uri("https://api.github.com/") }, sp.GetRequiredService<GitHubTokenProvider>()));
        return builder;
    }
}
