using System.ComponentModel.DataAnnotations;

namespace Monarch.Hosting;

/// <summary>Top-level Monarch configuration.</summary>
public sealed class MonarchOptions
{
    /// <summary>Gets or sets the percentage of mana held in reserve (0 to 100).</summary>
    [Range(0, 100)]
    public int ManaReservePct { get; set; } = 10;

    /// <summary>Gets or sets the project root that holds the <c>.monarch</c> folder.</summary>
    public string Root { get; set; } = ".";

    /// <summary>Gets or sets the presentation theme.</summary>
    public Theme Theme { get; set; } = Theme.Monarch;
}
