using Monarch.Domain;

namespace Monarch.Application.Ports;

/// <summary>A gate as held by a board.</summary>
/// <param name="Id">Gate id.</param>
/// <param name="Title">Title.</param>
/// <param name="Rank">Rank.</param>
/// <param name="State">Free-form state name, e.g. <c>proposed</c>.</param>
/// <param name="Quests">Ids of the quests that belong to the gate.</param>
/// <param name="Boss">Boss criterion, if any.</param>
public sealed record BoardGate(GateId Id, string Title, Rank Rank, string State, IReadOnlyList<QuestId> Quests, string? Boss);

/// <summary>A quest as held by a board.</summary>
/// <param name="Id">Quest id.</param>
/// <param name="Gate">Owning gate.</param>
/// <param name="Title">Title.</param>
/// <param name="Kind">Kind.</param>
/// <param name="Rank">Rank.</param>
/// <param name="State">Lifecycle state.</param>
/// <param name="DefinitionOfDone">Definition of done items.</param>
/// <param name="Scope">Path scope.</param>
public sealed record BoardQuest(QuestId Id, GateId Gate, string Title, QuestKind Kind, Rank Rank, QuestState State, IReadOnlyList<string> DefinitionOfDone, QuestScope Scope);

/// <summary>Port for the board that holds quests and gates.</summary>
public interface IQuestBoard
{
    /// <summary>Lists all gates.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The gates.</returns>
    Task<IReadOnlyList<BoardGate>> ListGatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Lists all quests.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The quests.</returns>
    Task<IReadOnlyList<BoardQuest>> ListQuestsAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a gate idempotently.</summary>
    /// <param name="gate">The gate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task UpsertGateAsync(BoardGate gate, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a quest idempotently.</summary>
    /// <param name="quest">The quest.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task.</returns>
    Task UpsertQuestAsync(BoardQuest quest, CancellationToken cancellationToken = default);
}
