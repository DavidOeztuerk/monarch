namespace Monarch.Policy;

/// <summary>
/// Flaches Wildcard-Muster eines Bash-Tokens für den Treffer-Beweis (ADR-0014 Entscheidung 1c). Jeder Wildcard darf <c>/</c> überspannen.
/// Zeichenklassen werden nur als positive Menge aus literalen Zeichen ausgewertet (jedes Mitglied einzeln gefaltet); Negation, Bereiche,
/// <c>[:…:]</c>, <c>[=…=]</c>, <c>[.….]</c> und nicht einfach faltbare Mitglieder gelten als beliebiges Zeichen. Ein unterminiertes <c>[</c> ist literal.
/// </summary>
internal sealed class FlatPattern
{
    private enum Kind { Literal, Any, Star, Set }

    private readonly List<(Kind Kind, char Char, string Set)> _items = [];

    private FlatPattern() { }

    public static FlatPattern Parse(string tail)
    {
        var p = new FlatPattern();
        for (var i = 0; i < tail.Length; i++)
        {
            var c = tail[i];
            if (c == '*') { if (p._items.Count == 0 || p._items[^1].Kind != Kind.Star) p._items.Add((Kind.Star, ' ', "")); }
            else if (c == '?') p._items.Add((Kind.Any, ' ', ""));
            else if (c == '[' && TryClass(tail, i, out var item, out var end)) { p._items.Add(item); i = end; }
            else foreach (var f in PathFold.Fold(c.ToString())) p._items.Add((Kind.Literal, f, ""));
        }

        return p;
    }

    private static bool TryClass(string s, int open, out (Kind, char, string) item, out int end)
    {
        item = default;
        end = open;
        var i = open + 1;
        var negated = i < s.Length && s[i] is '!' or '^';
        if (negated) i++;
        var first = i;
        if (i < s.Length && s[i] == ']') i++; // ] direkt nach [ bzw. [! ist Mitglied
        while (i < s.Length && s[i] != ']')
        {
            if (s[i] == '[' && i + 1 < s.Length && s[i + 1] is ':' or '=' or '.')
            {
                var close = s.IndexOf(s[i + 1] + "]", i + 2, StringComparison.Ordinal);
                if (close >= 0) { i = close + 2; continue; }
            }

            i++;
        }
        if (i >= s.Length) return false; // unterminiert: literales [
        end = i;
        var body = s[first..i];
        var any = negated || body.Contains("[:", StringComparison.Ordinal) || body.Contains("[=", StringComparison.Ordinal) || body.Contains("[.", StringComparison.Ordinal);
        for (var k = 1; !any && k < body.Length - 1; k++) if (body[k] == '-') any = true; // Bereich
        if (!any)
        {
            var set = new System.Text.StringBuilder();
            foreach (var m in body)
            {
                var f = PathFold.Fold(m.ToString());
                if (f.Length != 1) { any = true; break; }
                set.Append(f);
            }

            if (!any) { item = (Kind.Set, ' ', set.ToString()); return true; }
        }

        item = (Kind.Any, ' ', "");
        return true;
    }

    /// <summary>True, wenn das Muster einen String erzeugen kann, der mit <paramref name="text"/> beginnt.</summary>
    public bool CanStartWith(string text) => Run(text, full: false);

    /// <summary>True, wenn das Muster genau <paramref name="text"/> erzeugen kann.</summary>
    public bool Matches(string text) => Run(text, full: true);

    private bool Run(string text, bool full)
    {
        var n = _items.Count;
        var cur = new bool[n + 1];
        cur[0] = true;
        Close(cur);
        foreach (var c in text)
        {
            var next = new bool[n + 1];
            var any = false;
            for (var i = 0; i < n; i++)
            {
                if (!cur[i]) continue;
                var (kind, ch, set) = _items[i];
                switch (kind)
                {
                    case Kind.Star: next[i] = true; any = true; break;
                    case Kind.Any: next[i + 1] = true; any = true; break;
                    case Kind.Literal: if (ch == c) { next[i + 1] = true; any = true; } break;
                    default: if (set.Contains(c, StringComparison.Ordinal)) { next[i + 1] = true; any = true; } break;
                }
            }

            if (!any) return false;
            Close(next);
            cur = next;
        }

        return !full || cur[n];
    }

    private void Close(bool[] states)
    {
        for (var i = 0; i < _items.Count; i++)
            if (states[i] && _items[i].Kind == Kind.Star) states[i + 1] = true;
    }
}
