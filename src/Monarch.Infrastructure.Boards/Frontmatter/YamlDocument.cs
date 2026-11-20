namespace Monarch.Infrastructure.Boards.Frontmatter;

/// <summary>Parses a plain YAML document (no frontmatter fences), e.g. <c>.monarch/system.yaml</c>.</summary>
public static class YamlDocument
{
    /// <summary>Parses block maps by indentation, inline lists and maps, and trailing comments.</summary>
    /// <param name="text">The YAML text.</param>
    /// <returns>The root map.</returns>
    /// <exception cref="FormatException">The text is not valid in the supported YAML subset.</exception>
    public static YamlMap Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return YamlReader.ParseBlock(text.Split('\n').Select(l => l.TrimEnd('\r')));
    }
}
