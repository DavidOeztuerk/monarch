using Monarch.Domain;

namespace Monarch.Infrastructure.Boards;

/// <summary>Name mapping shared by the boards.</summary>
internal static class BoardNames
{
    public static string State(QuestState state) => state switch
    {
        QuestState.Proposed => "proposed",
        QuestState.Accepted => "accepted",
        QuestState.InProgress => "in_progress",
        QuestState.InReview => "in_review",
        QuestState.Verified => "verified",
        QuestState.Cleared => "cleared",
        QuestState.Failed => "failed",
        QuestState.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    public static QuestState ParseState(string? text) =>
        Enum.TryParse<QuestState>((text ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty), true, out var s) ? s : QuestState.Proposed;

    public static Rank ParseRank(string? text) => Enum.TryParse<Rank>(text, true, out var r) ? r : Rank.E;

    public static QuestKind ParseKind(string? text) => Enum.TryParse<QuestKind>(text, true, out var k) ? k : QuestKind.Main;
}
