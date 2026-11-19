using Monarch.Policy;

namespace Monarch.Cli.Tests;

/// <summary>#149: die Hook-Sicht auf die aktive Quest kennt den Status in_progress aus der Datei.</summary>
public class ActiveQuestPolicyTests
{
    private static ActiveFile File(string state) => new()
    {
        QuestId = "Q-0181",
        GateId = "G-0126",
        Rank = "A",
        Kind = "main",
        Scope = new ActiveScope { Include = ["src/**"], Exclude = [] },
        AcceptedAt = "2026-11-19T00:00:00Z",
        State = state,
    };

    [Fact]
    public void Quest_is_in_progress_only_when_the_active_file_says_so()
    {
        Assert.False(ActiveStore.ToPolicy(File("accepted")).InProgress);
        Assert.True(ActiveStore.ToPolicy(File("in_progress")).InProgress);
        Assert.False(ActiveStore.ToPolicy(File("")).InProgress);
    }
}
