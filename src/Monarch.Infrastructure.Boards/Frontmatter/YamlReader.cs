using System.Globalization;
using System.Text;

namespace Monarch.Infrastructure.Boards.Frontmatter;

/// <summary>Reader for the YAML subset used by backlog files.</summary>
internal sealed class YamlReader
{
    private readonly List<(int Indent, string Text)> _items = [];
    private int _i;

    private YamlReader(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart(' ');
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var code = StripComment(trimmed);
            if (code.Length == 0)
            {
                continue;
            }

            if (line.Length > trimmed.Length && line[..(line.Length - trimmed.Length)].Contains('\t', StringComparison.Ordinal))
            {
                throw new FormatException("Tabs are not allowed for indentation.");
            }

            if (trimmed[0] == '\t')
            {
                throw new FormatException("Tabs are not allowed for indentation.");
            }

            _items.Add((line.Length - trimmed.Length, code.TrimEnd()));
        }
    }

    /// <summary>Cuts a trailing <c># comment</c> (a '#' at the start or after whitespace, outside quotes).</summary>
    private static string StripComment(string text)
    {
        char quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (quote == '"' && c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            var opensToken = i == 0 || text[i - 1] is ' ' or '[' or '{' or ',' or ':';
            if (c is '"' or '\'' && opensToken)
            {
                quote = c;
            }
            else if (c == '#' && (i == 0 || text[i - 1] == ' '))
            {
                return text[..i];
            }
        }

        return text;
    }

    public static YamlMap ParseBlock(IEnumerable<string> lines)
    {
        var reader = new YamlReader(lines);
        if (reader._items.Count == 0)
        {
            return YamlMap.Empty;
        }

        var map = reader.ParseMap(reader._items[0].Indent);
        if (reader._i < reader._items.Count)
        {
            throw new FormatException($"Unexpected content: '{reader._items[reader._i].Text}'.");
        }

        return map;
    }

    private static bool IsListItem(string text) => text == "-" || text.StartsWith("- ", StringComparison.Ordinal);

    private YamlMap ParseMap(int indent)
    {
        var entries = new List<KeyValuePair<string, YamlNode>>();
        while (_i < _items.Count)
        {
            var (ind, text) = _items[_i];
            if (ind < indent)
            {
                break;
            }

            if (ind > indent)
            {
                throw new FormatException($"Unexpected indentation at '{text}'.");
            }

            if (IsListItem(text))
            {
                break;
            }

            var colon = KeyColon(text);
            if (colon < 0)
            {
                throw new FormatException($"Expected 'key: value' but found '{text}'.");
            }

            var key = Unquote(text[..colon].Trim());
            var rest = text[(colon + 1)..].Trim();
            _i++;
            YamlNode value;
            if (rest.Length > 0)
            {
                value = ParseInline(rest);
            }
            else if (_i < _items.Count && _items[_i].Indent > indent)
            {
                value = IsListItem(_items[_i].Text) ? ParseList(_items[_i].Indent) : ParseMap(_items[_i].Indent);
            }
            else if (_i < _items.Count && _items[_i].Indent == indent && IsListItem(_items[_i].Text))
            {
                value = ParseList(indent);
            }
            else
            {
                value = new YamlScalar(string.Empty);
            }

            entries.Add(new KeyValuePair<string, YamlNode>(key, value));
        }

        return new YamlMap(entries, false);
    }

    private YamlList ParseList(int indent)
    {
        var items = new List<YamlNode>();
        while (_i < _items.Count && _items[_i].Indent == indent && IsListItem(_items[_i].Text))
        {
            var text = _items[_i].Text;
            var rest = text.Length > 1 ? text[2..].Trim() : string.Empty;
            _i++;
            items.Add(rest.Length == 0 ? new YamlScalar(string.Empty) : ParseInline(rest));
        }

        return new YamlList(items, false);
    }

    private static int KeyColon(string text)
    {
        if (text.Length > 0 && text[0] is '"' or '\'')
        {
            var i = 0;
            _ = ParseQuoted(text, ref i);
            return i < text.Length && text[i] == ':' ? i : -1;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == ':' && (i == text.Length - 1 || text[i + 1] == ' '))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Unquote(string key)
    {
        if (key.Length > 0 && key[0] is '"' or '\'')
        {
            var i = 0;
            return ParseQuoted(key, ref i).Text;
        }

        return key;
    }

    private static YamlNode ParseInline(string text)
    {
        if (text[0] is '[' or '{' or '"' or '\'')
        {
            var i = 0;
            var node = ParseFlow(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length)
            {
                throw new FormatException($"Unexpected trailing content in '{text}'.");
            }

            return node;
        }

        return new YamlScalar(text);
    }

    private static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && s[i] == ' ')
        {
            i++;
        }
    }

    private static YamlNode ParseFlow(string s, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length)
        {
            throw new FormatException($"Unexpected end of flow value in '{s}'.");
        }

        switch (s[i])
        {
            case '[':
            {
                i++;
                var items = new List<YamlNode>();
                while (true)
                {
                    SkipWs(s, ref i);
                    Need(s, i);
                    if (s[i] == ']')
                    {
                        i++;
                        break;
                    }

                    items.Add(ParseFlow(s, ref i));
                    SkipWs(s, ref i);
                    Need(s, i);
                    if (s[i] == ',')
                    {
                        i++;
                    }
                    else if (s[i] == ']')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        throw new FormatException($"Expected ',' or ']' in '{s}'.");
                    }
                }

                return new YamlList(items, true);
            }

            case '{':
            {
                i++;
                var entries = new List<KeyValuePair<string, YamlNode>>();
                while (true)
                {
                    SkipWs(s, ref i);
                    Need(s, i);
                    if (s[i] == '}')
                    {
                        i++;
                        break;
                    }

                    string key;
                    if (s[i] is '"' or '\'')
                    {
                        key = ParseQuoted(s, ref i).Text;
                    }
                    else
                    {
                        var colon = s.IndexOf(':', i);
                        if (colon < 0)
                        {
                            throw new FormatException($"Expected 'key: value' in '{s}'.");
                        }

                        key = s[i..colon].Trim();
                        i = colon;
                    }

                    SkipWs(s, ref i);
                    if (i >= s.Length || s[i] != ':')
                    {
                        throw new FormatException($"Expected ':' after key '{key}' in '{s}'.");
                    }

                    i++;
                    entries.Add(new KeyValuePair<string, YamlNode>(key, ParseFlow(s, ref i)));
                    SkipWs(s, ref i);
                    Need(s, i);
                    if (s[i] == ',')
                    {
                        i++;
                    }
                    else if (s[i] == '}')
                    {
                        i++;
                        break;
                    }
                    else
                    {
                        throw new FormatException($"Expected ',' or '}}' in '{s}'.");
                    }
                }

                return new YamlMap(entries, true);
            }

            case '"' or '\'':
                return ParseQuoted(s, ref i);
            default:
            {
                var start = i;
                while (i < s.Length && s[i] is not (',' or ']' or '}'))
                {
                    i++;
                }

                return new YamlScalar(s[start..i].Trim());
            }
        }
    }

    private static void Need(string s, int i)
    {
        if (i >= s.Length)
        {
            throw new FormatException($"Unterminated flow value in '{s}'.");
        }
    }

    private static YamlScalar ParseQuoted(string s, ref int i)
    {
        var quote = s[i++];
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= s.Length)
            {
                throw new FormatException($"Unterminated quoted string in '{s}'.");
            }

            var c = s[i++];
            if (c == quote)
            {
                if (quote == '\'' && i < s.Length && s[i] == '\'')
                {
                    sb.Append('\'');
                    i++;
                    continue;
                }

                return new YamlScalar(sb.ToString(), quote == '"' ? YamlQuote.Double : YamlQuote.Single);
            }

            if (c == '\\' && quote == '"')
            {
                if (i >= s.Length)
                {
                    throw new FormatException($"Unterminated escape in '{s}'.");
                }

                var e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case '"' or '\\' or '/': sb.Append(e); break;
                    case 'u' when i + 4 <= s.Length && int.TryParse(s.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp):
                        sb.Append((char)cp);
                        i += 4;
                        break;
                    default: sb.Append('\\').Append(e); break;
                }

                continue;
            }

            sb.Append(c);
        }
    }
}
