using System.Text;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// ADR-0018: checks the branch-local commits (reachable from <c>HEAD</c>, not from a trusted state) for changes to ledger files that this worktree
/// did not write. Trusted states are the worktree's base (<c>.monarch/local/ledger-base</c>) and <c>refs/remotes/origin/&lt;branch&gt;</c> of the integration
/// and protected branches. Once git has shown that a repository is there, every git failure or timeout is a finding, never "no change".
/// </summary>
internal static class LedgerHistory
{
    private const string FetchHint = "Run 'git fetch' first: a stale remote state is the most common harmless cause.";

    /// <summary>Whether <paramref name="text"/> is a full commit id (SHA-1 or SHA-256, lowercase hex).</summary>
    public static bool IsCommitId(string text) => text.Length is 40 or 64 && text.All(char.IsAsciiHexDigitLower);

    /// <summary>Adds an <see cref="LedgerViolationKind.UnanchoredCommit"/> finding per ledger file changed in a branch-local commit that this worktree did not write.</summary>
    /// <param name="root">Project root.</param>
    /// <param name="anchored">Files in the local anchor (<c>files</c> and <c>restore</c>); they are protected by the anchor and skipped here.</param>
    /// <param name="branches">Integration branch and protected branches.</param>
    /// <param name="timeout">Time limit of each git call.</param>
    /// <param name="violations">Receives the findings.</param>
    public static void Check(string root, ISet<string> anchored, IEnumerable<string> branches, TimeSpan timeout, List<LedgerViolation> violations)
    {
        var names = branches.Where(IsBranchName).Distinct(StringComparer.Ordinal).ToList();
        var branchText = names.Count == 0 ? "develop/main" : string.Join("/", names);

        var inside = GitAnchor.Isolated(root, ["rev-parse", "--is-inside-work-tree"], null, timeout);
        if (inside.Status == GitAnchor.GitStatus.Unavailable)
        {
            return;
        }

        if (inside.Status == GitAnchor.GitStatus.TimedOut)
        {
            Fail(violations, "git timed out while looking for the repository");
            return;
        }

        if (!inside.Ok || inside.Text.Trim() != "true")
        {
            return;
        }

        var baseId = ReadBase(root);
        var inputs = new List<string> { "HEAD^{commit}" };
        if (baseId is not null)
        {
            inputs.Add(baseId + "^{commit}");
        }

        inputs.AddRange(names.Select(n => "refs/remotes/origin/" + n + "^{commit}"));
        var resolvedRaw = GitAnchor.Isolated(root, ["cat-file", "--batch-check"], Encoding.ASCII.GetBytes(string.Join("\n", inputs) + "\n"), timeout);
        if (!resolvedRaw.Ok)
        {
            Fail(violations, resolvedRaw.Status == GitAnchor.GitStatus.TimedOut ? "git timed out while resolving the trusted states" : "git failed while resolving the trusted states");
            return;
        }

        var answers = resolvedRaw.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (answers.Length != inputs.Count)
        {
            Fail(violations, "git answered the resolution of the trusted states with an unexpected number of lines");
            return;
        }

        var resolved = answers.Select(a => a.Split(' ') is { Length: >= 2 } p && p[1] == "commit" && IsCommitId(p[0]) ? p[0] : null).ToList();
        if (resolved[0] is not { } head)
        {
            return; // no commit yet: nothing is branch-local
        }

        var trusted = resolved.Skip(1).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        if (trusted.Count == 0)
        {
            var origin = GitAnchor.Isolated(root, ["config", "--get", "remote.origin.url"], null, timeout);
            if (origin.Status == GitAnchor.GitStatus.TimedOut)
            {
                Fail(violations, "git timed out while looking for the remote 'origin'");
            }
            else if (origin.Ok && origin.Text.Trim().Length > 0)
            {
                violations.Add(new LedgerViolation(LedgerFiles.LedgerDir, 0, LedgerViolationKind.UnanchoredCommit,
                    $"No trusted state: the worktree has no valid base and no remote state of {branchText} is resolvable although 'origin' is configured. {FetchHint}"));
            }

            return;
        }

        var list = GitAnchor.Isolated(root, ["rev-list", "--parents", "--topo-order", "--reverse", head, "--not", .. trusted], null, timeout);
        if (!list.Ok)
        {
            Fail(violations, list.Status == GitAnchor.GitStatus.TimedOut ? "git timed out in rev-list" : "git rev-list failed");
            return;
        }

        var commits = list.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length > 0)
            .Select(p => (Id: p[0], ParentCount: p.Length - 1))
            .ToList();
        if (commits.Count == 0)
        {
            return;
        }

        var diff = GitAnchor.Isolated(
            root,
            ["diff-tree", "--stdin", "-r", "--root", "--no-renames", "-z", "--relative", "-m", "--raw", "--", LedgerFiles.LedgerDir],
            Encoding.ASCII.GetBytes(string.Join("\n", commits.Select(c => c.Id)) + "\n"),
            timeout);
        if (!diff.Ok)
        {
            Fail(violations, diff.Status == GitAnchor.GitStatus.TimedOut ? "git timed out in diff-tree" : "git diff-tree failed");
            return;
        }

        var entries = ParseDiff(diff.Output);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in commits.Where(c => c.ParentCount >= 2))
        {
            foreach (var e in Candidates(entries, c.Id, anchored))
            {
                wanted.Add(e.New);
                wanted.Add(e.Old);
            }
        }

        wanted.RemoveWhere(IsZero);
        Dictionary<string, byte[]?>? contents = wanted.Count == 0 ? [] : ReadBlobs(root, wanted, timeout);
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (id, parentCount) in commits)
        {
            var short7 = id[..7];
            var candidates = Candidates(entries, id, anchored);
            if (parentCount < 2)
            {
                foreach (var e in candidates.Where(e => reported.Add(e.Path)))
                {
                    var action = e.Status switch { 'A' => "creates", 'D' => "deletes", _ => "changes" };
                    violations.Add(new LedgerViolation(e.Path, 0, LedgerViolationKind.UnanchoredCommit,
                        $"Commit {short7} (normal commit) {action} this ledger file; the commit is on no remote state of {branchText} and the file was not written by this worktree. {FetchHint}"));
                }

                continue;
            }

            foreach (var group in candidates.GroupBy(e => e.Path, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                if (reported.Contains(group.Key))
                {
                    continue;
                }

                if (MergeProblem(group.ToList(), parentCount, contents) is { } problem)
                {
                    reported.Add(group.Key);
                    violations.Add(new LedgerViolation(group.Key, 0, LedgerViolationKind.UnanchoredCommit,
                        $"Commit {short7} (merge) {problem}; the commit is on no remote state of {branchText} and the file was not written by this worktree. {FetchHint}"));
                }
            }
        }
    }

    private static string? MergeProblem(List<DiffEntry> entries, int parentCount, Dictionary<string, byte[]?>? contents)
    {
        var result = entries[0].New;
        if (IsZero(result))
        {
            return "removes this ledger file although a parent has it";
        }

        if (entries.Count >= parentCount && !entries.Exists(e => e.Old == e.New))
        {
            return "gives this ledger file a version that none of the parents has";
        }

        if (contents is null || !contents.TryGetValue(result, out var resultBytes) || resultBytes is null)
        {
            return "could not be read from git (a git failure counts as a finding)";
        }

        foreach (var e in entries.Where(e => !IsZero(e.Old)))
        {
            if (!contents.TryGetValue(e.Old, out var parentBytes) || parentBytes is null)
            {
                return "could not be read from git (a git failure counts as a finding)";
            }

            if (!resultBytes.AsSpan().StartsWith(parentBytes))
            {
                return "shortens or recomposes this ledger file: a parent version is not the beginning of the result";
            }
        }

        return null;
    }

    private static List<DiffEntry> Candidates(Dictionary<string, List<DiffEntry>> entries, string commit, ISet<string> anchored) =>
        entries.TryGetValue(commit, out var list)
            ? [.. list.Where(e => !anchored.Contains(e.Path))]
            : [];

    /// <summary>Parses <c>diff-tree --stdin -z --raw</c>: a commit id token, then per entry a <c>:mode mode old new status</c> token and a path token.</summary>
    private static Dictionary<string, List<DiffEntry>> ParseDiff(byte[] output)
    {
        var result = new Dictionary<string, List<DiffEntry>>(StringComparer.Ordinal);
        var tokens = Encoding.UTF8.GetString(output).Split('\0');
        string? commit = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (t.Length == 0)
            {
                continue;
            }

            if (t[0] != ':')
            {
                commit = t.Trim();
                continue;
            }

            var f = t.Split(' ');
            if (commit is null || f.Length < 5 || i + 1 >= tokens.Length)
            {
                continue;
            }

            if (!result.TryGetValue(commit, out var list))
            {
                result[commit] = list = [];
            }

            list.Add(new DiffEntry(tokens[++i], f[2], f[3], f[4][0]));
        }

        return result;
    }

    private static Dictionary<string, byte[]?>? ReadBlobs(string root, HashSet<string> blobs, TimeSpan timeout)
    {
        var order = blobs.OrderBy(b => b, StringComparer.Ordinal).ToList();
        var r = GitAnchor.Isolated(root, ["cat-file", "--batch"], Encoding.ASCII.GetBytes(string.Join("\n", order) + "\n"), timeout);
        if (!r.Ok)
        {
            return null;
        }

        var data = r.Output;
        var result = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var pos = 0;
        foreach (var id in order)
        {
            var eol = Array.IndexOf(data, (byte)'\n', pos);
            if (eol < 0)
            {
                return null;
            }

            var header = Encoding.ASCII.GetString(data, pos, eol - pos).Split(' ');
            pos = eol + 1;
            if (header.Length >= 3 && header[0] == id && header[1] == "blob" && int.TryParse(header[2], out var size) && size >= 0 && pos + size < data.Length + 1)
            {
                result[id] = data[pos..(pos + size)];
                pos += size + 1;
            }
            else if (header.Length == 2 && header[1] == "missing")
            {
                result[id] = null;
            }
            else
            {
                return null;
            }
        }

        return result;
    }

    private static string? ReadBase(string root)
    {
        try
        {
            var path = LedgerFiles.Full(root, LedgerFiles.BasePath);
            if (!File.Exists(path))
            {
                return null;
            }

            var bytes = LedgerFiles.ReadAllShared(path);
            if (bytes.Length > 200)
            {
                return null;
            }

            var text = Encoding.ASCII.GetString(bytes).Trim();
            return IsCommitId(text) ? text : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsBranchName(string name) =>
        name.Length > 0 && char.IsAsciiLetterOrDigit(name[0]) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '/' or '-')
        && !name.Contains("..", StringComparison.Ordinal) && !name.Contains("//", StringComparison.Ordinal) && !name.EndsWith('/') && !name.EndsWith(".lock", StringComparison.Ordinal);

    private static bool IsZero(string id) => id.All(c => c == '0');

    private static void Fail(List<LedgerViolation> violations, string why) =>
        violations.Add(new LedgerViolation(LedgerFiles.LedgerDir, 0, LedgerViolationKind.UnanchoredCommit, $"The ledger history could not be checked: {why}. A git failure is a finding, never 'no change'."));

    private sealed record DiffEntry(string Path, string Old, string New, char Status);
}
