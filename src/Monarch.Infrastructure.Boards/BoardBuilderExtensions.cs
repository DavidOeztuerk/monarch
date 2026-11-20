using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Boards;

namespace Monarch.Hosting;

/// <summary>Board registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class BoardBuilderExtensions
{
    /// <summary>Registers a quest board implementation, e.g. <see cref="LocalFileBoard"/>.</summary>
    /// <typeparam name="TBoard">The board type.</typeparam>
    /// <param name="builder">The builder.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder AddBoard<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBoard>(this MonarchBuilder builder)
        where TBoard : class, IQuestBoard
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddSingleton<IQuestBoard, TBoard>();
        return builder;
    }

    /// <summary>Registers the GitHub issues board; it also becomes the <see cref="IQuestBoard"/> unless another board is registered.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Options callback (<see cref="GitHubBoardOptions.Owner"/>, <see cref="GitHubBoardOptions.Repo"/>).</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder AddGitHubBoard(this MonarchBuilder builder, Action<GitHubBoardOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        builder.Services.AddOptions<GitHubBoardOptions>().Configure(configure);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GitHubBoardOptions>, GitHubBoardOptionsValidator>());
        builder.Services.TryAddSingleton<GitHubTokenProvider>();
        builder.Services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<GitHubBoardOptions>>().Value;
            return new GitHubIssuesBoard(new HttpClient { BaseAddress = new Uri(options.ApiBase) }, options, sp.GetRequiredService<GitHubTokenProvider>());
        });
        builder.Services.TryAddSingleton<IQuestBoard>(sp => sp.GetRequiredService<GitHubIssuesBoard>());
        return builder;
    }
}
