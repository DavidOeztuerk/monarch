using System.Globalization;
using System.Text;

namespace Monarch.Policy;

/// <summary>Ein Kommando-Segment, wie es der Lexer sieht: Wörter (Quotes, Escapes, <c>$'…'</c> und Brace-Expansion aufgelöst), Umleitungen, Heredoc-Text.</summary>
internal sealed class RawSegment
{
    /// <summary>Wörter nach Quote-Entfernung und Expansion.</summary>
    public List<string> Words { get; } = [];

    /// <summary>Ziele von <c>&gt;</c>, <c>&gt;&gt;</c>, <c>&gt;|</c>, <c>&amp;&gt;</c>, <c>&gt;&amp;datei</c>.</summary>
    public List<string> OutRedirects { get; } = [];

    /// <summary>Ziele von <c>&lt;</c>.</summary>
    public List<string> InRedirects { get; } = [];

    /// <summary>Heredoc-Bodies und Here-Strings dieses Segments (null: keine).</summary>
    public StringBuilder? Heredoc { get; set; }

    /// <summary>True, wenn das Segment mit <c>|</c> in das nächste mündet.</summary>
    public bool PipesToNext { get; set; }

    /// <summary>
    /// Subshell-Grenzen vor diesem Segment in der Reihenfolge des Auftretens: <c>(</c> öffnet eine Subshell (auch Befehlsersetzung, Backticks, Prozesssubstitution),
    /// <c>)</c> schließt sie. Innerhalb einer Subshell wechseln <c>cd</c> und <c>pushd</c> das Arbeitsverzeichnis nur bis zum Ende der Subshell.
    /// </summary>
    public string Events { get; set; } = "";

    public bool IsEmpty => Words.Count == 0 && OutRedirects.Count == 0 && InRedirects.Count == 0 && Heredoc is null;
}

/// <summary>
/// Kleiner Shell-Lexer ohne Regex (ADR-0011 Regel 1). Zerlegt in Segmente an <c>; | &amp; ( ) \n</c>, löst Quotes, <c>\</c>-Escapes, <c>$'…'</c> und
/// Brace-Expansion auf, normalisiert <c>$(pwd)</c> zu <c>$PWD</c> und <c>$(git rev-parse --show-toplevel)</c> zu <c>$__TOP__</c>, erkennt Umleitungen und
/// trennt Heredoc-Bodies (Daten) vom Kommando. Befehlsersetzungen (<c>$(…)</c>, Backticks, <c>&lt;(…)</c>) werden rekursiv gelext und als eigene Segmente
/// vor das umgebende Segment gestellt. Er ist heuristisch und konservativ, kein vollständiger Bash-Parser.
/// </summary>
internal static class ShellLexer
{
    /// <summary>Platzhalter für das Git-Toplevel (<c>$(git rev-parse --show-toplevel)</c>).</summary>
    public const string TopMarker = "$__TOP__";

    private const int MaxDepth = 6;
    private const int MaxExpansions = 512;

    private sealed record Pending(RawSegment Owner, string Delim, bool StripTabs, bool Quoted);

    private sealed class State(string s)
    {
        public readonly string S = s;
        public int I;
        public int Depth;
        public char Close = '\0';
        public List<RawSegment> Done = [];
        public RawSegment Cur = new();
        public List<RawSegment> Subs = [];
        public List<Pending> Pend = [];
        public string Events = "";
    }

    private readonly record struct Word(string Text, string Quoted);

    /// <summary>Zerlegt ein Bash-Kommando in Segmente.</summary>
    public static List<RawSegment> Lex(string command) => Lex(command, 0);

    private static List<RawSegment> Lex(string command, int depth)
    {
        var st = new State(command) { Depth = depth };
        Run(st, '\0');
        return st.Done;
    }

    private static List<RawSegment> Block(State st, char close)
    {
        var (done, cur, subs, pend, c) = (st.Done, st.Cur, st.Subs, st.Pend, st.Close);
        st.Done = [];
        st.Cur = new RawSegment();
        st.Subs = [];
        st.Pend = [];
        st.Close = close;
        st.Depth++;
        st.Events += "(";
        Run(st, close);
        st.Events += ")";
        st.Depth--;
        var result = st.Done;
        (st.Done, st.Cur, st.Subs, st.Pend, st.Close) = (done, cur, subs, pend, c);
        return result;
    }

    private static void Run(State st, char close)
    {
        var s = st.S;
        var parens = 0;
        while (st.I < s.Length)
        {
            var c = s[st.I];
            var next = st.I + 1 < s.Length ? s[st.I + 1] : '\0';
            switch (c)
            {
                case ' ' or '\t' or '\r':
                    st.I++;
                    break;
                case '\n':
                    st.I++;
                    ReadHeredocBodies(st);
                    EndSegment(st, false);
                    break;
                case ';' or '|' or '&':
                    if (c == '&' && next == '>') { st.I++; Redirect(st); break; }
                    if (next == c) { st.I += 2; EndSegment(st, false); }
                    else if (c == '|') { st.I += next == '&' ? 2 : 1; EndSegment(st, true); }
                    else { st.I++; EndSegment(st, false); }
                    break;
                case '(':
                    parens++;
                    st.I++;
                    EndSegment(st, false);
                    st.Events += "(";
                    break;
                case ')':
                    st.I++;
                    if (close == ')' && parens == 0) { Finish(st); return; }
                    EndSegment(st, false);
                    if (parens > 0) { parens--; st.Events += ")"; }
                    break;
                case '`' when close == '`':
                    st.I++;
                    Finish(st);
                    return;
                case '<' or '>':
                    Redirect(st);
                    break;
                case '#':
                    // Kommentar: ein # am Wortanfang außerhalb von Quotes beendet das Wort bis zum Zeilenende (der Zeilenumbruch bleibt Trenner).
                    while (st.I < s.Length && s[st.I] != '\n') st.I++;
                    break;
                default:
                    var w = LexWord(st);
                    if (IsFileDescriptor(w) && st.I < s.Length && s[st.I] is '<' or '>') break;
                    AddWord(st, w);
                    break;
            }
        }

        Finish(st);
    }

    private static void Finish(State st)
    {
        ReadHeredocBodies(st);
        EndSegment(st, false);
    }

    private static bool IsFileDescriptor(Word w)
    {
        if (w.Text.Length == 0 || w.Quoted.Contains('1', StringComparison.Ordinal)) return false;
        foreach (var ch in w.Text) if (!char.IsAsciiDigit(ch)) return false;
        return true;
    }

    private static void EndSegment(State st, bool pipe)
    {
        if (!st.Cur.IsEmpty)
        {
            st.Cur.PipesToNext = pipe;
            st.Done.AddRange(st.Subs);
            st.Subs.Clear();
            st.Cur.Events = st.Events;
            st.Events = "";
            st.Done.Add(st.Cur);
        }
        else if (st.Subs.Count > 0)
        {
            st.Done.AddRange(st.Subs);
            st.Subs.Clear();
        }

        st.Cur = new RawSegment();
    }

    private static void AddWord(State st, Word w)
    {
        foreach (var e in ExpandBraces(w.Text, w.Quoted))
            if (e.Length > 0) st.Cur.Words.Add(e);
    }

    // ---- Wörter
    private static Word LexWord(State st)
    {
        var s = st.S;
        var t = new StringBuilder();
        var q = new StringBuilder();
        while (st.I < s.Length)
        {
            var c = s[st.I];
            if (c is ' ' or '\t' or '\n' or '\r' or ';' or '|' or '&' or '(' or ')' or '<' or '>') break;
            if (c == '`' && st.Close == '`') break;
            switch (c)
            {
                case '\\':
                    if (st.I + 1 < s.Length)
                    {
                        if (s[st.I + 1] != '\n') Append(t, q, s[st.I + 1], true);
                        st.I += 2;
                    }
                    else { Append(t, q, '\\', true); st.I++; }
                    break;
                case '\'':
                    st.I++;
                    while (st.I < s.Length && s[st.I] != '\'') Append(t, q, s[st.I++], true);
                    st.I++;
                    break;
                case '"':
                    st.I++;
                    DoubleQuoted(st, t, q);
                    break;
                case '`':
                    st.I++;
                    st.Subs.AddRange(Block(st, '`'));
                    break;
                case '$':
                    Dollar(st, t, q, false);
                    break;
                default:
                    Append(t, q, c, false);
                    st.I++;
                    break;
            }
        }

        return new Word(t.ToString(), q.ToString());
    }

    private static void Append(StringBuilder t, StringBuilder q, char c, bool quoted)
    {
        t.Append(c);
        q.Append(quoted ? '1' : '0');
    }

    private static void AppendRaw(StringBuilder t, StringBuilder q, string text)
    {
        t.Append(text);
        q.Append('1', text.Length);
    }

    private static void DoubleQuoted(State st, StringBuilder t, StringBuilder q)
    {
        var s = st.S;
        while (st.I < s.Length && s[st.I] != '"')
        {
            var c = s[st.I];
            if (c == '\\' && st.I + 1 < s.Length)
            {
                var n = s[st.I + 1];
                if (n is '"' or '\\' or '$' or '`') { Append(t, q, n, true); st.I += 2; }
                else if (n == '\n') st.I += 2;
                else { Append(t, q, '\\', true); st.I++; }
            }
            else if (c == '$') Dollar(st, t, q, true);
            else if (c == '`') { st.I++; st.Subs.AddRange(Block(st, '`')); }
            else { Append(t, q, c, true); st.I++; }
        }

        st.I++;
    }

    private static void Dollar(State st, StringBuilder t, StringBuilder q, bool inDouble)
    {
        var s = st.S;
        var next = st.I + 1 < s.Length ? s[st.I + 1] : '\0';
        if (next == '\'' && !inDouble)
        {
            st.I += 2;
            AnsiC(st, t, q);
        }
        else if (next == '(' && st.Depth < MaxDepth)
        {
            if (StartsAt(s, st.I + 2, "pwd)")) { AppendRaw(t, q, "$PWD"); st.I += 6; return; }
            if (StartsAt(s, st.I + 2, "git rev-parse --show-toplevel)")) { AppendRaw(t, q, TopMarker); st.I += 2 + "git rev-parse --show-toplevel)".Length; return; }
            st.I += 2;
            st.Subs.AddRange(Block(st, ')'));
        }
        else if (next == '{')
        {
            var depth = 0;
            var start = st.I;
            var i = st.I + 1;
            for (; i < s.Length; i++)
            {
                if (s[i] == '{') depth++;
                else if (s[i] == '}' && --depth == 0) break;
            }

            var end = Math.Min(i + 1, s.Length);
            var raw = s[start..end];
            AppendRaw(t, q, raw is "${HOME}" or "${PWD}" ? "$" + raw[2..^1] : raw);
            st.I = end;
        }
        else
        {
            Append(t, q, '$', inDouble);
            st.I++;
        }
    }

    private static bool StartsAt(string s, int index, string value) =>
        index + value.Length <= s.Length && string.CompareOrdinal(s, index, value, 0, value.Length) == 0;

    private static void AnsiC(State st, StringBuilder t, StringBuilder q)
    {
        var s = st.S;
        while (st.I < s.Length && s[st.I] != '\'')
        {
            var c = s[st.I++];
            if (c != '\\' || st.I >= s.Length) { Append(t, q, c, true); continue; }
            var e = s[st.I++];
            switch (e)
            {
                case 'a': Append(t, q, '\a', true); break;
                case 'b': Append(t, q, '\b', true); break;
                case 'e' or 'E': Append(t, q, '\u001b', true); break;
                case 'f': Append(t, q, '\f', true); break;
                case 'n': Append(t, q, '\n', true); break;
                case 'r': Append(t, q, '\r', true); break;
                case 't': Append(t, q, '\t', true); break;
                case 'v': Append(t, q, '\v', true); break;
                case '\\' or '\'' or '"' or '?': Append(t, q, e, true); break;
                case 'x': AppendCode(st, t, q, 16, 2); break;
                case 'u': AppendCode(st, t, q, 16, 4); break;
                case 'U': AppendCode(st, t, q, 16, 8); break;
                case >= '0' and <= '7': st.I--; AppendCode(st, t, q, 8, 3); break;
                default: Append(t, q, '\\', true); Append(t, q, e, true); break;
            }
        }

        st.I++;
    }

    private static void AppendCode(State st, StringBuilder t, StringBuilder q, int radix, int maxDigits)
    {
        var s = st.S;
        var value = 0;
        var n = 0;
        while (n < maxDigits && st.I < s.Length && DigitValue(s[st.I], radix) is var d and >= 0)
        {
            value = value * radix + d;
            st.I++;
            n++;
        }

        if (n == 0) { Append(t, q, radix == 16 ? 'x' : '0', true); return; }
        if (value is >= 0 and < 0x110000 && !(value is >= 0xD800 and < 0xE000)) AppendRaw(t, q, char.ConvertFromUtf32(value));
    }

    private static int DigitValue(char c, int radix)
    {
        var v = c is >= '0' and <= '9' ? c - '0' : c is >= 'a' and <= 'f' ? c - 'a' + 10 : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
        return v < radix ? v : -1;
    }

    // ---- Umleitungen und Heredocs
    private static void Redirect(State st)
    {
        var s = st.S;
        var op = s[st.I++];
        var peek = st.I < s.Length ? s[st.I] : '\0';
        if (peek == '(' && st.Depth < MaxDepth)
        {
            st.I++;
            st.Subs.AddRange(Block(st, ')'));
            return;
        }

        if (op == '<')
        {
            if (peek == '<')
            {
                st.I++;
                if (st.I < s.Length && s[st.I] == '<') { st.I++; HereString(st); }
                else HereDocument(st);
                return;
            }

            if (peek == '&') { st.I++; SkipDescriptor(st); return; }
            if (peek == '>')
            {
                // <> öffnet zum Lesen und Schreiben: das Ziel zählt als Eingabe und als Ausgabe.
                st.I++;
                var both = new List<string>();
                ReadTarget(st, both);
                st.Cur.InRedirects.AddRange(both);
                st.Cur.OutRedirects.AddRange(both);
                return;
            }

            ReadTarget(st, st.Cur.InRedirects);
            return;
        }

        if (peek is '>' or '|') { st.I++; peek = st.I < s.Length ? s[st.I] : '\0'; }
        if (peek == '&' && st.I + 1 < s.Length && (char.IsAsciiDigit(s[st.I + 1]) || s[st.I + 1] == '-')) { st.I++; SkipDescriptor(st); return; }
        if (peek == '&') st.I++;
        ReadTarget(st, st.Cur.OutRedirects);
    }

    private static void SkipDescriptor(State st)
    {
        var s = st.S;
        while (st.I < s.Length && (char.IsAsciiDigit(s[st.I]) || s[st.I] == '-')) st.I++;
    }

    private static void SkipBlanks(State st)
    {
        while (st.I < st.S.Length && st.S[st.I] is ' ' or '\t') st.I++;
    }

    private static void ReadTarget(State st, List<string> into)
    {
        SkipBlanks(st);
        if (st.I >= st.S.Length || st.S[st.I] is '\n' or ';' or '|' or '&' or '(' or ')' or '<' or '>') return;
        var w = LexWord(st);
        foreach (var e in ExpandBraces(w.Text, w.Quoted)) if (e.Length > 0) into.Add(e);
    }

    private static void HereString(State st)
    {
        SkipBlanks(st);
        if (st.I >= st.S.Length) return;
        var w = LexWord(st);
        (st.Cur.Heredoc ??= new StringBuilder()).Append(w.Text).Append('\n');
    }

    private static void HereDocument(State st)
    {
        var s = st.S;
        var strip = st.I < s.Length && s[st.I] == '-';
        if (strip) st.I++;
        SkipBlanks(st);
        if (st.I >= s.Length) return;
        var w = LexWord(st);
        st.Cur.Heredoc ??= new StringBuilder();
        st.Pend.Add(new Pending(st.Cur, w.Text, strip, w.Quoted.Contains('1', StringComparison.Ordinal)));
    }

    private static void ReadHeredocBodies(State st)
    {
        if (st.Pend.Count == 0) return;
        var s = st.S;
        foreach (var p in st.Pend)
        {
            var body = new StringBuilder();
            while (st.I < s.Length)
            {
                var end = s.IndexOf('\n', st.I);
                var lineEnd = end < 0 ? s.Length : end;
                var line = s.AsSpan(st.I, lineEnd - st.I);
                st.I = end < 0 ? s.Length : end + 1;
                var cmp = p.StripTabs ? line.TrimStart('\t') : line;
                if (cmp.TrimEnd('\r').SequenceEqual(p.Delim)) break;
                body.Append(line).Append('\n');
            }

            p.Owner.Heredoc!.Append(body);
            // Ein unquotiertes Heredoc führt $(…) und Backticks im Body aus.
            if (!p.Quoted && st.Depth < MaxDepth && (body.ToString().Contains("$(", StringComparison.Ordinal) || body.ToString().Contains('`', StringComparison.Ordinal)))
                AddBodySubstitutions(st, body.ToString());
        }

        st.Pend.Clear();
    }

    private static void AddBodySubstitutions(State st, string body)
    {
        var escaped = body.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        var segs = Lex("echo \"" + escaped + "\"", st.Depth + 1);
        if (segs.Count == 0) return;
        var echo = segs[^1]; // das umgebende echo
        segs.RemoveAt(segs.Count - 1);
        if (segs.Count == 0) return;
        segs[0].Events = "(" + segs[0].Events;
        st.Subs.AddRange(segs);
        st.Events += echo.Events + ")";
    }

    // ---- Brace-Expansion (nur unquotierte Klammern)
    private static List<string> ExpandBraces(string text, string quoted)
    {
        if (text.IndexOf('{', StringComparison.Ordinal) < 0) return [text];
        var result = new List<string>();
        Expand(text, quoted, result);
        return result;
    }

    private static void Expand(string t, string q, List<string> result)
    {
        if (result.Count >= MaxExpansions) return;
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] != '{' || q[i] == '1') continue;
            var depth = 0;
            var close = -1;
            var commas = new List<int>();
            for (var j = i; j < t.Length; j++)
            {
                if (q[j] == '1') continue;
                if (t[j] == '{') depth++;
                else if (t[j] == '}') { if (--depth == 0) { close = j; break; } }
                else if (t[j] == ',' && depth == 1) commas.Add(j);
            }

            if (close < 0) continue;
            var pre = t[..i];
            var qpre = q[..i];
            var post = t[(close + 1)..];
            var qpost = q[(close + 1)..];
            if (commas.Count > 0)
            {
                var starts = new List<int> { i + 1 };
                starts.AddRange(commas.Select(c => c + 1));
                for (var k = 0; k < starts.Count; k++)
                {
                    var end = k + 1 < starts.Count ? commas[k] : close;
                    Expand(pre + t[starts[k]..end] + post, qpre + q[starts[k]..end] + qpost, result);
                }

                return;
            }

            var inner = t[(i + 1)..close];
            if (!q.AsSpan(i + 1, close - i - 1).Contains('1') && Sequence(inner) is { } items)
            {
                foreach (var item in items) Expand(pre + item + post, qpre + new string('0', item.Length) + qpost, result);
                return;
            }
        }

        result.Add(t);
    }

    private static List<string>? Sequence(string inner)
    {
        var parts = inner.Split("..");
        if (parts.Length is < 2 or > 3) return null;
        var step = 1;
        if (parts.Length == 3 && (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out step) || step == 0)) return null;
        step = Math.Abs(step);
        var items = new List<string>();
        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            for (var v = a; (a <= b ? v <= b : v >= b) && items.Count < MaxExpansions; v += a <= b ? step : -step)
                items.Add(v.ToString(CultureInfo.InvariantCulture));
            return items;
        }

        if (parts[0].Length == 1 && parts[1].Length == 1 && char.IsAsciiLetter(parts[0][0]) && char.IsAsciiLetter(parts[1][0]))
        {
            char x = parts[0][0], y = parts[1][0];
            for (var v = (int)x; (x <= y ? v <= y : v >= y) && items.Count < MaxExpansions; v += x <= y ? step : -step)
                items.Add(((char)v).ToString());
            return items;
        }

        return null;
    }
}
