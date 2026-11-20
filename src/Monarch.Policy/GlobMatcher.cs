namespace Monarch.Policy;

/// <summary>
/// Kleiner, AOT-sicherer Glob-Matcher ohne Regex. Muster und Pfade sind <c>/</c>-getrennt, Vergleich ist ordinal und verankert.
/// <c>**</c> als ganzes Segment steht für null oder mehr Segmente; <c>*</c> und <c>?</c> gelten innerhalb eines Segments.
/// </summary>
public static class GlobMatcher
{
    /// <summary>True, wenn <paramref name="path"/> das Muster <paramref name="pattern"/> vollständig erfüllt.</summary>
    public static bool IsMatch(string pattern, string path) =>
        MatchSegments(pattern.AsSpan(), path.AsSpan(), anc: false);

    /// <summary>True, wenn der Pfad das Muster erfüllt oder ein Vorfahr-Verzeichnis davon ist (Präfix der Segmente).</summary>
    public static bool IsMatchOrAncestor(string pattern, string path) =>
        MatchSegments(pattern.AsSpan(), path.AsSpan(), anc: true);

    /// <summary>Flacher Wildcard-Vergleich (<c>*</c> beliebig lang inkl. <c>/</c> und Leerzeichen, <c>?</c> ein Zeichen), ordinal.</summary>
    public static bool WildcardMatch(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t])) { p++; t++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
            else if (star >= 0) { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    private static bool MatchSegments(ReadOnlySpan<char> pattern, ReadOnlySpan<char> path, bool anc)
    {
        if (pattern.IsEmpty) return path.IsEmpty;
        var pMore = Split(pattern, out var pSeg, out var pRest);
        if (pSeg is "**")
        {
            if (!pMore) return true;
            if (MatchSegments(pRest, path, anc)) return true;
            if (path.IsEmpty) return anc;
            Split(path, out _, out var skipped);
            return MatchSegments(pattern, skipped, anc);
        }

        if (path.IsEmpty) return anc || OnlyDoubleStars(pattern);
        var more = Split(path, out var seg, out var rest);
        if (!WildcardMatch(pSeg, seg)) return false;
        if (!pMore) return !more;
        if (!more) return anc || OnlyDoubleStars(pRest);
        return MatchSegments(pRest, rest, anc);
    }

    private static bool OnlyDoubleStars(ReadOnlySpan<char> pattern)
    {
        while (!pattern.IsEmpty)
        {
            var more = Split(pattern, out var seg, out var rest);
            if (seg is not "**") return false;
            pattern = more ? rest : default;
        }

        return true;
    }

    private static bool Split(ReadOnlySpan<char> s, out ReadOnlySpan<char> segment, out ReadOnlySpan<char> rest)
    {
        var i = s.IndexOf('/');
        if (i < 0) { segment = s; rest = default; return false; }
        segment = s[..i];
        rest = s[(i + 1)..];
        return true;
    }
}
