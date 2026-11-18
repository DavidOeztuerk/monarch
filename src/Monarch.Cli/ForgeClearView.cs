using System.Collections.Concurrent;
using Monarch.Application.Net;
using Monarch.Application.Ports;
using Monarch.Domain;
using Monarch.Infrastructure.Boards;
using Monarch.Policy;

namespace Monarch.Cli;

/// <summary>
/// Read-only derived state "cleared (Forge)" for <c>quest list</c> and <c>status</c> (Issue 72, way C). A quest stored as in_review or verified with a linked PR counts as
/// cleared when the forge reports that PR as merged into the integration branch and every relevant check on the PR head is green (the CI rule of <c>quest verify</c>).
/// Nothing is written. Per PR: one <c>GET pulls/N</c> and one check-runs request, at most <see cref="MaxParallel"/> PRs at a time, one total deadline.
/// </summary>
internal static class ForgeClearView
{
    /// <summary>Total time allowed for all forge requests of one call.</summary>
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(5);

    /// <summary>Most PRs asked at the same time.</summary>
    public const int MaxParallel = 4;

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
        var byPr = new SortedDictionary<int, List<string>>();
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

        using var cts = new CancellationTokenSource(deadline ?? DefaultDeadline);
        var tokens = env.Tokens ?? new GitHubTokenProvider();
        if (tokens.GetToken() is null)
        {
            return Unknown("no GitHub token");
        }

        var shared = env with { Tokens = tokens };
        var integration = SystemConfigReader.Load(paths.Root).IntegrationBranch;
        var pulls = Forge.Pulls(shared);
        var cleared = new ConcurrentBag<string>();
        var timedOut = new ConcurrentBag<int>();
        var failed = new ConcurrentBag<(int Pr, string Why)>();
        using var gate = new SemaphoreSlim(MaxParallel);
        var work = byPr.Select(entry => Task.Run(async () =>
        {
            var (pr, ids) = (entry.Key, entry.Value);
            try
            {
                await gate.WaitAsync(cts.Token).ConfigureAwait(false);
                try
                {
                    var pull = await pulls.GetPullAsync(forge.Owner, forge.Repo, pr, cts.Token).ConfigureAwait(false);
                    if (pull.Merged && !string.IsNullOrEmpty(pull.MergeCommitSha) && string.Equals(pull.BaseRef, integration, StringComparison.OrdinalIgnoreCase)
                        && EvidenceCommands.CheckCi(shared, paths, forge, pull.HeadSha, cts.Token).Result.Kind == StepKind.Done)
                    {
                        foreach (var id in ids)
                        {
                            cleared.Add(id);
                        }
                    }
                }
                finally
                {
                    gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                timedOut.Add(pr);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or InvalidDataException or System.Text.Json.JsonException or FormatException)
            {
                failed.Add((pr, ex.GetType().Name));
            }
        })).ToArray();
        Task.WaitAll(work);

        var notes = new List<string>();
        if (!timedOut.IsEmpty)
        {
            notes.Add("forge did not answer in time for " + string.Join(", ", timedOut.Order().Select(n => $"#{n}")));
        }

        if (!failed.IsEmpty)
        {
            notes.Add("forge not reachable for " + string.Join(", ", failed.OrderBy(f => f.Pr).Select(f => $"#{f.Pr} ({f.Why})")));
        }

        return new Result(new HashSet<string>(cleared, StringComparer.Ordinal), notes.Count == 0 ? null : string.Join("; ", notes));
    }

    private static Result Unknown(string reason) => new(new HashSet<string>(StringComparer.Ordinal), reason);

    /// <summary>Hint line for output when the derivation failed.</summary>
    public static string NoteLine(string note) => $"note: cleared (Forge) could not be derived: {note}; showing the stored state for the rest.";
}
