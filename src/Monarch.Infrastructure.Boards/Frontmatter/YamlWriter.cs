using System.Text;

namespace Monarch.Infrastructure.Boards.Frontmatter;

/// <summary>Writer for the YAML subset used by backlog files; keeps inline/block style and quoting of the source.</summary>
internal static class YamlWriter
{
    public static string WriteBlock(YamlMap root)
    {
        var sb = new StringBuilder();
        WriteEntries(sb, root.Entries, 0);
        return sb.ToString();
    }

    private static void WriteEntries(StringBuilder sb, IReadOnlyList<KeyValuePair<string, YamlNode>> entries, int indent)
    {
        var pad = new string(' ', indent);
        foreach (var (key, value) in entries)
        {
            sb.Append(pad).Append(Key(key)).Append(':');
            switch (value)
            {
                case YamlScalar { Text.Length: 0, Quote: YamlQuote.None }:
                    sb.Append('\n');
                    break;
                case YamlScalar s:
                    sb.Append(' ').Append(Scalar(s, false)).Append('\n');
                    break;
                case YamlList { Flow: false, Items.Count: > 0 } l:
                    sb.Append('\n');
                    foreach (var item in l.Items)
                    {
                        sb.Append(pad).Append("  - ").Append(item is YamlScalar si ? Scalar(si, false) : Flow(item)).Append('\n');
                    }

                    break;
                case YamlMap { Flow: false, Entries.Count: > 0 } m:
                    sb.Append('\n');
                    WriteEntries(sb, m.Entries, indent + 2);
                    break;
                default:
                    sb.Append(' ').Append(Flow(value)).Append('\n');
                    break;
            }
        }
    }

    private static string Flow(YamlNode node) => node switch
    {
        YamlScalar s => Scalar(s, true),
        YamlList l => "[" + string.Join(", ", l.Items.Select(Flow)) + "]",
        YamlMap { Entries.Count: 0 } => "{}",
        YamlMap m => "{ " + string.Join(", ", m.Entries.Select(e => Key(e.Key) + ": " + Flow(e.Value))) + " }",
        _ => throw new ArgumentOutOfRangeException(nameof(node)),
    };

    private static string Key(string key) => key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.') ? key : Double(key);

    private static string Scalar(YamlScalar s, bool flow) => s.Quote switch
    {
        YamlQuote.Double => Double(s.Text),
        YamlQuote.Single when !s.Text.Contains('\n', StringComparison.Ordinal) && !s.Text.Contains('\r', StringComparison.Ordinal) => "'" + s.Text.Replace("'", "''", StringComparison.Ordinal) + "'",
        YamlQuote.Single => Double(s.Text),
        _ => NeedsQuotes(s.Text, flow) ? Double(s.Text) : s.Text,
    };

    private static bool NeedsQuotes(string t, bool flow)
    {
        if (t.Length == 0)
        {
            return flow;
        }

        if (t[0] is '[' or '{' or '"' or '\'' || char.IsWhiteSpace(t[0]) || char.IsWhiteSpace(t[^1]) || t.Contains('\n', StringComparison.Ordinal) || t.Contains('\r', StringComparison.Ordinal))
        {
            return true;
        }

        return flow && (t.IndexOfAny([',', '[', ']', '{', '}']) >= 0 || t.Contains(": ", StringComparison.Ordinal) || t.EndsWith(':'));
    }

    private static string Double(string t)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in t)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case < ' ': sb.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)); break;
                default: sb.Append(c); break;
            }
        }

        return sb.Append('"').ToString();
    }
}
