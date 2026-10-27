using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Monarch.Application.Ports;
using Monarch.Infrastructure.Boards;

namespace Monarch.Hosting;

/// <summary>Board registration extensions for <see cref="MonarchBuilder"/>.</summary>
public static class BoardBuilderExtensions
{
    /// <summary>Registers a quest board implementation.</summary>
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

    /// <summary>Registers the GitHub issues board.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="configure">Options callback.</param>
    /// <returns>The same builder.</returns>
    public static MonarchBuilder AddGitHubBoard(this MonarchBuilder builder, Action<GitHubBoardOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new GitHubBoardOptions();
        configure(options);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<GitHubIssuesBoard>();
        return builder;
    }
}
