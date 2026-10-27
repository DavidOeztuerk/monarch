using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Boards;

/// <summary>Placeholder quest board backed by local files.</summary>
public sealed class LocalFileBoard : IQuestBoard
{
    /// <summary>Initializes a new instance; not implemented yet.</summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public LocalFileBoard() => throw new NotImplementedException();
}
