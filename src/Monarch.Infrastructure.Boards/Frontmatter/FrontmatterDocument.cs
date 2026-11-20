namespace Monarch.Infrastructure.Boards.Frontmatter;

/// <summary>A markdown file with YAML frontmatter. The body is kept byte-for-byte.</summary>
/// <param name="Root">The frontmatter map.</param>
/// <param name="Body">Everything after the closing <c>---</c> line, unchanged.</param>
public sealed record FrontmatterDocument(YamlMap Root, string Body)
{
    /// <summary>Parses a document.</summary>
    /// <param name="text">The full file text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="FormatException">The frontmatter is missing or malformed.</exception>
    public static FrontmatterDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith("---\n", StringComparison.Ordinal) && !text.StartsWith("---\r\n", StringComparison.Ordinal))
        {
            throw new FormatException("Missing frontmatter: file must start with '---'.");
        }

        var pos = text.IndexOf('\n', StringComparison.Ordinal) + 1;
        var lines = new List<string>();
        var bodyStart = -1;
        while (pos < text.Length)
        {
            var nl = text.IndexOf('\n', pos);
            var end = nl < 0 ? text.Length : nl;
            var next = nl < 0 ? text.Length : nl + 1;
            var line = text[pos..end].TrimEnd('\r');
            if (line == "---")
            {
                bodyStart = next;
                break;
            }

            lines.Add(line);
            pos = next;
        }

        if (bodyStart < 0)
        {
            throw new FormatException("Unterminated frontmatter: closing '---' not found.");
        }

        return new FrontmatterDocument(YamlReader.ParseBlock(lines), text[bodyStart..]);
    }

    /// <summary>Writes the document: <c>---</c>, frontmatter, <c>---</c>, body.</summary>
    /// <returns>The file text.</returns>
    public string Write() => string.Concat("---\n", YamlWriter.WriteBlock(Root), "---\n", Body);
}
