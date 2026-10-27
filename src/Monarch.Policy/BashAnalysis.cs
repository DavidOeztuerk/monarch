namespace Monarch.Policy;

/// <summary>
/// Heuristische, konservative Analyse von Bash-Kommandos ohne Shell-Parser und ohne Regex.
/// Zerlegt in Segmente, entfernt Präfixe und Quotes und kanonisiert Git-Aufrufe.
/// </summary>
internal static class BashAnalysis
{
    private static readonly string[] Wrappers = ["sudo", "env", "command", "exec", "nohup", "time", "nice"];
    private static readonly string[] SudoOptionsWithValue = ["-u", "-g", "-h", "-p", "-U", "-C", "-D", "-R", "-T"];
    private static readonly string[] Shells = ["bash", "sh", "zsh", "dash"];
    private const string PathSeparators = " \t\r\n;|&()<>\"'`=,{}";

    /// <summary>Ein analysiertes Kommando-Segment.</summary>
    /// <param name="Text">Normalisierter Text (Quotes entfernt, Whitespace kollabiert, Präfixe entfernt).</param>
    /// <param name="Tokens">Tokens des normalisierten Texts; Index 0 ist das Kommandowort.</param>
    /// <param name="Canonical">Kanonische Form (Git-Globaloptionen entfernt, Force-Push vereinheitlicht).</param>
    public sealed record Segment(string Text, string[] Tokens, string Canonical)
    {
        /// <summary>Kommandowort ohne Verzeichnis, sonst leer.</summary>
        public string Word => Tokens.Length == 0 ? "" : Path.GetFileName(Tokens[0]);
    }

    /// <summary>Alle Segmente (inklusive verschachtelter <c>bash -c</c>-Strings, Tiefe max. 3).</summary>
    public static List<Segment> Segments(string command, int depth = 0)
    {
        var result = new List<Segment>();
        foreach (var raw in SplitRaw(command))
        {
            var tokens = Strip(Tokenize(raw));
            if (tokens.Length == 0) continue;
            var text = string.Join(' ', tokens);
            result.Add(new Segment(text, tokens, Canonicalize(tokens, text)));
            if (depth < 3 && tokens[0] == "eval" && tokens.Length > 1)
                result.AddRange(Segments(string.Join(' ', tokens[1..]), depth + 1));
            if (depth < 3 && Array.IndexOf(Shells, Path.GetFileName(tokens[0])) >= 0)
            {
                var c = Array.IndexOf(tokens, "-c");
                if (c >= 0 && c + 1 < tokens.Length) result.AddRange(Segments(string.Join(' ', tokens[(c + 1)..]), depth + 1));
            }
        }

        return result;
    }

    /// <summary>
    /// Pfad-artige Tokens eines Textes. Quotes werden zuerst entfernt (<c>.mon""arch</c> ist für die Shell <c>.monarch</c>), dann wird an Whitespace,
    /// Operatoren und <c>=</c> getrennt. Enthält ein Token einen Backslash, kommt zusätzlich die Variante ohne Backslash hinzu (<c>.mon\arch</c>).
    /// </summary>
    public static List<string> PathTokens(string text)
    {
        text = StripQuotes(text);
        var list = new List<string>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var sep = i == text.Length || PathSeparators.Contains(text[i], StringComparison.Ordinal);
            if (!sep) { if (start < 0) start = i; }
            else if (start >= 0)
            {
                var token = text[start..i];
                list.Add(token);
                if (token.Contains('\\', StringComparison.Ordinal)) list.Add(token.Replace("\\", "", StringComparison.Ordinal));
                start = -1;
            }
        }

        return list;
    }

    /// <summary>Entfernt einfache und doppelte Anführungszeichen (wie die Shell beim Zusammensetzen eines Wortes).</summary>
    public static string StripQuotes(string text) =>
        text.Replace("\"", "", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal);

    /// <summary>True, wenn das Token einen Shell-Wildcard enthält (<c>*</c>, <c>?</c>, <c>[</c>).</summary>
    public static bool IsGlob(string token) => token.AsSpan().IndexOfAny('*', '?', '[') >= 0;

    /// <summary>
    /// Text zum Suchen verschleierter Pfadnennungen in Interpreter-Code: kleingeschrieben, ohne Quotes, Backslashes, <c>+</c>, Kommas und Whitespace
    /// (<c>'.mon' + 'arch'</c> wird zu <c>.monarch</c>).
    /// </summary>
    public static string Compress(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '"' or '\'' or '\\' or '+' or ',' or ' ' or '\t' or '\n' or '\r') continue;
            sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static readonly string[] Interpreters = ["python", "python3", "perl", "ruby", "node", "php", "osascript", "deno", "bun"];

    /// <summary>True für Interpreter-Kommandowörter (python, python3, python3.12, perl, ruby, node, php, osascript, deno, bun).</summary>
    public static bool IsInterpreter(string word)
    {
        if (Array.IndexOf(Interpreters, word) >= 0) return true;
        if (!word.StartsWith("python", StringComparison.Ordinal)) return false;
        for (var i = "python".Length; i < word.Length; i++) if (!char.IsAsciiDigit(word[i]) && word[i] != '.') return false;
        return true;
    }

    /// <summary>
    /// True, wenn das Segment einen Interpreter mit Code von der Kommandozeile oder von stdin startet: ein Inline-Flag (<c>-c -e -E -i -p -r</c>, auch
    /// gebündelt wie <c>-pi</c> oder <c>-ne</c>; <c>--eval</c>, <c>--print</c>; <c>deno eval</c>), kein Positionsargument (stdin) oder <c>-</c>.
    /// Ein Interpreter mit einer Skriptdatei ist es nicht (dokumentierte Grenze).
    /// </summary>
    public static bool IsInlineInterpreter(Segment seg)
    {
        if (!IsInterpreter(seg.Word)) return false;
        for (var i = 1; i < seg.Tokens.Length; i++)
        {
            var t = seg.Tokens[i];
            if (t == "-" || t == "eval") return true;
            if (t.StartsWith("--", StringComparison.Ordinal)) { if (t is "--eval" or "--print" || t.StartsWith("--eval=", StringComparison.Ordinal)) return true; continue; }
            if (t.StartsWith('-')) { if (t.AsSpan(1).IndexOfAny("ceEipr") >= 0) return true; continue; }
            return false; // erstes Positionsargument: Skriptdatei, alles danach gehört dem Skript
        }

        return true; // nur Flags: Code kommt von stdin
    }

    /// <summary>
    /// Ziele von Umleitungen mit dem Zeichen <paramref name="redirect"/> (<c>&gt;</c> oder <c>&lt;</c>). Dateideskriptor-Umleitungen
    /// (<c>2&gt;&amp;1</c>), Heredocs und Prozesssubstitution zählen nicht.
    /// </summary>
    public static List<string> RedirectTargets(string command, char redirect)
    {
        var targets = new List<string>();
        for (var i = 0; i < command.Length; i++)
        {
            if (command[i] != redirect) continue;
            var j = i + 1;
            while (j < command.Length && command[j] == redirect) j++;
            if (redirect == '<' && (j - i > 1 || (j < command.Length && command[j] == '('))) { i = j - 1; continue; }
            if (j < command.Length && command[j] == '&') { i = j; continue; }
            while (j < command.Length && command[j] is ' ' or '\t') j++;
            var k = j;
            while (k < command.Length && !PathSeparators.Contains(command[k], StringComparison.Ordinal)) k++;
            if (k > j) targets.Add(command[j..k]);
            else if (j < command.Length && command[j] is '"' or '\'')
            {
                var q = j + 1;
                var e = q;
                while (e < command.Length && command[e] != command[j]) e++;
                if (e > q) targets.Add(command[q..e]);
            }

            i = Math.Max(i, k - 1);
        }

        return targets;
    }

    /// <summary>
    /// Verzeichnisse, die ein Kommando ansteuert: Ziele von <c>cd</c>/<c>pushd</c> (<c>Changes</c>: das Arbeitsverzeichnis wechselt für die folgenden Segmente)
    /// sowie <c>git -C dir</c>, <c>--git-dir</c> und <c>--work-tree</c> (in beiden Schreibweisen).
    /// </summary>
    public static List<(string Path, bool Changes)> DirectoryTargets(List<Segment> segments)
    {
        var list = new List<(string, bool)>();
        foreach (var seg in segments)
        {
            var t = seg.Tokens;
            if (seg.Word is "cd" or "pushd")
            {
                var i = 1;
                while (i < t.Length && t[i].StartsWith('-') && t[i].Length > 1) i++;
                if (i < t.Length) list.Add((t[i], true));
                continue;
            }

            if (seg.Word != "git") continue;
            for (var i = 1; i < t.Length && t[i].StartsWith('-'); i++)
            {
                if (t[i] is "-C" or "--git-dir" or "--work-tree")
                {
                    if (i + 1 < t.Length) list.Add((t[++i], false));
                }
                else if (t[i].StartsWith("--git-dir=", StringComparison.Ordinal)) list.Add((t[i]["--git-dir=".Length..], false));
                else if (t[i].StartsWith("--work-tree=", StringComparison.Ordinal)) list.Add((t[i]["--work-tree=".Length..], false));
                else if (t[i] is "-c") i++;
            }
        }

        return list;
    }

    /// <summary>Git-Unterkommando (nach Globaloptionen wie <c>-C dir</c>) und sein Index, sonst <c>null</c>.</summary>
    public static string? GitSubcommand(string[] tokens, out int index)
    {
        index = -1;
        if (tokens.Length == 0 || Path.GetFileName(tokens[0]) != "git") return null;
        var i = 1;
        while (i < tokens.Length && tokens[i].StartsWith('-')) i += tokens[i] is "-C" or "-c" or "--git-dir" or "--work-tree" ? 2 : 1;
        if (i >= tokens.Length) return null;
        index = i;
        return tokens[i];
    }

    private static List<string> SplitRaw(string command)
    {
        var parts = new List<string>();
        var sb = new System.Text.StringBuilder();
        void Flush() { if (sb.Length > 0) { parts.Add(sb.ToString()); sb.Clear(); } }
        for (var i = 0; i < command.Length; i++)
        {
            var c = command[i];
            var next = i + 1 < command.Length ? command[i + 1] : '\0';
            var prev = i > 0 ? command[i - 1] : '\0';
            if ((c == '&' && next == '&') || (c == '|' && next == '|') || (c == '$' && next == '(')) { Flush(); i++; }
            else if (c == '&' && (prev == '>' || next == '>')) sb.Append(c);
            else if (c is ';' or '|' or '\n' or '\r' or '(' or ')' or '`' or '&') Flush();
            else sb.Append(c);
        }

        Flush();
        return parts;
    }

    private static string[] Tokenize(string segment) =>
        segment.Replace("\"", "", StringComparison.Ordinal).Replace("'", "", StringComparison.Ordinal)
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static string[] Strip(string[] tokens)
    {
        var i = 0;
        while (i < tokens.Length)
        {
            var t = tokens[i];
            if (IsAssignment(t)) { i++; continue; }
            if (Array.IndexOf(Wrappers, t) < 0) break;
            i++;
            while (i < tokens.Length && tokens[i].StartsWith('-'))
            {
                i += t == "sudo" && Array.IndexOf(SudoOptionsWithValue, tokens[i]) >= 0 ? 2 : 1;
            }
        }

        return i >= tokens.Length ? [] : tokens[i..];
    }

    private static bool IsAssignment(string t)
    {
        var eq = t.IndexOf('=', StringComparison.Ordinal);
        if (eq <= 0) return false;
        for (var i = 0; i < eq; i++) if (!(char.IsAsciiLetterOrDigit(t[i]) || t[i] == '_')) return false;
        return !char.IsAsciiDigit(t[0]);
    }

    private static string Canonicalize(string[] tokens, string text)
    {
        var sub = GitSubcommand(tokens, out var idx);
        if (sub is null) return text;
        var rest = tokens[(idx + 1)..];
        if (sub == "push" && rest.Any(IsForceFlag)) return "git push --force";
        return rest.Length == 0 ? $"git {sub}" : $"git {sub} {string.Join(' ', rest)}";
    }

    private static bool IsForceFlag(string t) =>
        t is "-f" or "--force" or "--force-if-includes"
        || t.StartsWith("--force-with-lease", StringComparison.Ordinal)
        || (t.Length > 1 && t[0] == '+' )
        || (t.Length > 1 && t[0] == '-' && t[1] != '-' && t.Contains('f', StringComparison.Ordinal));
}
