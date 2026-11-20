using System.Text.RegularExpressions;

namespace Monarch.Infrastructure.Evidence;

/// <summary>
/// The one place that knows the markers with which GitHub skips workflow runs on a push: <c>[skip ci]</c>, <c>[ci skip]</c>, <c>[no ci]</c>,
/// <c>[skip actions]</c>, <c>[actions skip]</c> and the trailer <c>skip-checks: true</c> (case-insensitive). Q-0144 (Issue #64).
/// </summary>
public static partial class SkipMarker
{
    [GeneratedRegex(@"\[\s*(?:skip\s+ci|ci\s+skip|no\s+ci|skip\s+actions|actions\s+skip)\s*\]|skip-checks\s*:\s*true", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    /// <summary>True when the text carries a skip marker.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether a marker is present.</returns>
    public static bool Contains(string? text) => text is not null && Pattern().IsMatch(text);

    /// <summary>Removes every marker, repeatedly (a removal may join a new one), and collapses whitespace to single spaces.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without markers, on one line.</returns>
    public static string Strip(string? text)
    {
        var current = text ?? string.Empty;
        while (Pattern().IsMatch(current))
        {
            current = Pattern().Replace(current, string.Empty);
        }

        return WhiteSpace().Replace(current, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}
