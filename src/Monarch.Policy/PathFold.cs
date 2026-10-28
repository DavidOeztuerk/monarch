using System.Text;

namespace Monarch.Policy;

/// <summary>
/// Faltung von Pfaden für den Vergleich mit Schutzpfaden (ADR-0011 Regel 8). Der Build läuft mit <c>InvariantGlobalization=true</c>, deshalb wirken
/// <c>string.Normalize</c> und kulturabhängige Faltung nicht. Gefaltet wird selbst: ASCII-Lowercase, übrige Zeichen per <see cref="char.ToLowerInvariant"/>
/// und die Tabelle aus Unicode <c>CaseFolding.txt</c> (Status C und F, Ergebnis rein ASCII, auch 1 zu n). Fullwidth und andere Homoglyphen werden nicht gefaltet.
/// </summary>
public static class PathFold
{
    /// <summary>Gefaltete Form; ein String ohne Großbuchstaben und ohne Nicht-ASCII-Zeichen wird unverändert zurückgegeben (kein Alloc).</summary>
    public static string Fold(string s)
    {
        var i = 0;
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c >= 0x80 || c is >= 'A' and <= 'Z') break;
        }

        if (i == s.Length) return s;
        var sb = new StringBuilder(s.Length + 4);
        sb.Append(s, 0, i);
        for (; i < s.Length; i++)
        {
            var c = s[i];
            if (c < 0x80) { sb.Append(c is >= 'A' and <= 'Z' ? (char)(c | 0x20) : c); continue; }
            var pair = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
            var len = pair ? 2 : 1;
            var code = pair ? char.ConvertToUtf32(c, s[i + 1]) : c;
            if (CaseFoldTable.Lookup(code) is { } folded) sb.Append(folded);
            else if (len == 2) sb.Append(c).Append(s[i + 1]);
            else sb.Append(char.ToLowerInvariant(c));
            i += len - 1;
        }

        return sb.ToString();
    }
}
