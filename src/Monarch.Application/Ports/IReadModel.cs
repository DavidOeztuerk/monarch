namespace Monarch.Application.Ports;

/// <summary>A projected quest row.</summary>
/// <param name="Id">Quest id.</param>
/// <param name="Gate">Owning gate id.</param>
/// <param name="State">Lifecycle state name, snake_case.</param>
/// <param name="Rank">Rank name.</param>
/// <param name="Kind">Quest kind name.</param>
/// <param name="Xp">Booked XP.</param>
/// <param name="UpdatedAt">Time of the last event that touched the quest (ISO 8601, UTC).</param>
public sealed record QuestRow(string Id, string? Gate, string State, string? Rank, string? Kind, long Xp, string UpdatedAt);

/// <summary>A projected gate row.</summary>
/// <param name="Id">Gate id.</param>
/// <param name="Rank">Rank name.</param>
/// <param name="Title">Title.</param>
public sealed record GateRow(string Id, string? Rank, string? Title);

/// <summary>A projected raw event row.</summary>
/// <param name="File">Ledger file (relative, <c>/</c> separated).</param>
/// <param name="Seq">Sequence number within the file.</param>
/// <param name="Type">Event type.</param>
/// <param name="At">Event time (ISO 8601, UTC).</param>
/// <param name="Actor">Originator.</param>
/// <param name="Payload">Payload JSON text.</param>
public sealed record EventRow(string File, long Seq, string Type, string At, string Actor, string Payload);

/// <summary>Port for the queryable read model projected from the ledger.</summary>
public interface IReadModel
{
    /// <summary>Drops the model and replays all ledger files below the project root in (at, file, seq) order.</summary>
    /// <param name="root">Project root.</param>
    void Rebuild(string root);

    /// <summary>Applies one ledger entry incrementally; already applied entries are ignored.</summary>
    /// <param name="entry">The entry.</param>
    void Apply(LedgerEntry entry);

    /// <summary>Gets all quests ordered by id.</summary>
    /// <returns>The rows.</returns>
    IReadOnlyList<QuestRow> GetQuests();

    /// <summary>Gets all gates ordered by id.</summary>
    /// <returns>The rows.</returns>
    IReadOnlyList<GateRow> GetGates();

    /// <summary>Gets all events ordered by (at, file, seq).</summary>
    /// <returns>The rows.</returns>
    IReadOnlyList<EventRow> GetEvents();
}
