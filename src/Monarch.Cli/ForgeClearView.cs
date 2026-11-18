using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;

namespace Monarch.Cli;

/// <summary>
/// Read-only derived state "cleared (Forge)" for <c>quest list</c> and <c>status</c> (Issue 72, way C). A quest stored as in_review or verified with a linked PR counts as
/// cleared when the forge reports that PR as merged and every relevant check on the PR head is green (the CI rule of <c>quest verify</c>). Nothing is written.
/// </summary>
internal static class ForgeClearView
{
    /// <summary>Total time allowed for all forge requests of one call.</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Result of a derivation.</summary>
    /// <param name="Cleared">Ids of quests the forge proves as cleared.</param>
    /// <param name="Note">Hint when the forge could not answer (stored state stays as is), else null.</param>
    internal sealed record Result(IReadOnlySet<string> Cleared, string? Note)
    {
        /// <summary>No derivation and no hint.</summary>
        public static Result None { get; } = new(new HashSet<string>(StringComparer.Ordinal), null);
    }

    /// <summary>Derives the forge-proven cleared quests among <paramref name="quests"/>.</summary>
    public static Result Derive(CliEnv env, ProjectPaths paths, IReadOnlyList<BoardQuest> quests, TimeSpan? deadline = null)
    {
        var board = new LocalFileBoard(paths.Root);
        var byPr = new Dictionary<int, List<string>>();
        foreach (var q in quests.Where(q => q.State is QuestState.InReview or QuestState.Verified))
        {
            if (board.FindQuestFile(q.Id.Value) is { } file && BoardFiles.Link(file).Pr is { } pr)
            {
                if (!byPr.TryGetValue(pr, out var ids))
                {
                    byPr[pr] = ids = [];
                }

                ids.Add(q.Id.Value);
            }
        }

        if (byPr.Count == 0)
        {
            return Result.None;
        }

        if (Forge.Resolve(paths) is not { } forge)
        {
            return Unknown("no repository found for the forge");
        }

        if ((env.Tokens ?? new GitHubTokenProvider()).GetToken() is null)
        {
            return Unknown("no GitHub token");
        }

        var cleared = new HashSet<string>(StringComparer.Ordinal);
        using var cts = new CancellationTokenSource(deadline ?? DefaultDeadline);
        try
        {
            var evidence = Forge.Evidence(env);
            foreach (var (pr, ids) in byPr.OrderBy(p => p.Key))
            {
                if (evidence.GetMergeAsync(forge.Owner, forge.Repo, pr, cts.Token).GetAwaiter().GetResult() is null)
                {
                    continue;
                }

                var head = evidence.GetPullHeadAsync(forge.Owner, forge.Repo, pr, cts.Token).GetAwaiter().GetResult();
                if (EvidenceCommands.CheckCi(env, paths, forge, head, cts.Token).Result.Kind == StepKind.Done)
                {
                    cleared.UnionWith(ids);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException or System.Text.Json.JsonException or FormatException)
        {
            return new Result(new HashSet<string>(StringComparer.Ordinal), ex is OperationCanceledException ? "forge did not answer in time" : $"forge not reachable ({ex.GetType().Name})");
        }

        return new Result(cleared, null);
    }

    private static Result Unknown(string reason) => new(new HashSet<string>(StringComparer.Ordinal), reason);

    /// <summary>Hint line for output when the derivation failed.</summary>
    public static string NoteLine(string note) => $"note: cleared (Forge) could not be derived: {note}; showing the stored state.";
}
