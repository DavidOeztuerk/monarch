namespace Monarch.Infrastructure.Boards.Frontmatter;

/// <summary>Quoting style of a scalar.</summary>
public enum YamlQuote
{
    /// <summary>Plain scalar.</summary>
    None,
    /// <summary>Double-quoted scalar.</summary>
    Double,
    /// <summary>Single-quoted scalar.</summary>
    Single,
}

/// <summary>Node of the YAML subset used by backlog files.</summary>
public abstract record YamlNode;

/// <summary>A scalar.</summary>
/// <param name="Text">Unescaped text.</param>
/// <param name="Quote">Quoting style.</param>
public sealed record YamlScalar(string Text, YamlQuote Quote = YamlQuote.None) : YamlNode;

/// <summary>A list, either inline (<c>[a, b]</c>) or block (<c>- a</c>).</summary>
/// <param name="Items">The items.</param>
/// <param name="Flow"><see langword="true"/> for the inline form.</param>
public sealed record YamlList(IReadOnlyList<YamlNode> Items, bool Flow) : YamlNode
{
    /// <inheritdoc />
    public bool Equals(YamlList? other) => other is not null && Flow == other.Flow && Items.SequenceEqual(other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Flow, Items.Count);
}

/// <summary>An ordered map, either inline (<c>{ k: v }</c>) or block.</summary>
/// <param name="Entries">The entries in source order.</param>
/// <param name="Flow"><see langword="true"/> for the inline form.</param>
public sealed record YamlMap(IReadOnlyList<KeyValuePair<string, YamlNode>> Entries, bool Flow) : YamlNode
{
    /// <summary>Gets an empty block map.</summary>
    public static YamlMap Empty => new([], false);

    /// <summary>Gets the value of a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The node, or <see langword="null"/>.</returns>
    public YamlNode? Get(string key)
    {
        foreach (var e in Entries)
        {
            if (e.Key == key)
            {
                return e.Value;
            }
        }

        return null;
    }

    /// <summary>Gets a scalar value as text.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The text, or <see langword="null"/> when absent or not a scalar.</returns>
    public string? GetString(string key) => Get(key) is YamlScalar s ? s.Text : null;

    /// <summary>Gets a list of scalars as texts; a single scalar counts as a one-item list.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The texts; empty when absent.</returns>
    public IReadOnlyList<string> GetStrings(string key) => Get(key) switch
    {
        YamlList l => [.. l.Items.OfType<YamlScalar>().Select(s => s.Text)],
        YamlScalar { Text.Length: > 0 } s => [s.Text],
        _ => [],
    };

    /// <summary>Returns a copy with the key set; an existing key keeps its position.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The new map.</returns>
    public YamlMap With(string key, YamlNode value)
    {
        var list = Entries.ToList();
        var i = list.FindIndex(e => e.Key == key);
        var entry = new KeyValuePair<string, YamlNode>(key, value);
        if (i >= 0)
        {
            list[i] = entry;
        }
        else
        {
            list.Add(entry);
        }

        return this with { Entries = list };
    }

    /// <inheritdoc />
    public bool Equals(YamlMap? other) => other is not null && Flow == other.Flow && Entries.SequenceEqual(other.Entries);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Flow, Entries.Count);
}
