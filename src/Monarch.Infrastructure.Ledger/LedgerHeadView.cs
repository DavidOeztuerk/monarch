using System.Text;

namespace Monarch.Infrastructure.Ledger;

/// <summary>
/// What git says about the ledger files, read once per <c>verify</c> (ADR-0010, addendum 15.11.2026): the commit <c>HEAD</c> points to, the blobs of the
/// ledger files in its tree, and the index as an additional source that can only add findings. Every call is isolated like the history check
/// (ADR-0018). Once git has shown that a repository is there, a failure or timeout of any call is a <see cref="Problems"/> entry, never "unchanged".
/// </summary>
internal sealed class LedgerHeadView
{
    private LedgerHeadView()
    {
    }

    /// <summary>Gets a value indicating whether git is available and the root is inside a work tree.</summary>
    public bool InRepository { get; private set; }

    /// <summary>Gets the commit id <c>HEAD</c> points to; null when the repository has no commit (or git failed, see <see cref="Problems"/>).</summary>
    public string? Commit { get; private set; }

    /// <summary>Gets the content of each regular ledger file (mode 100644/100755, type blob) in the tree of <see cref="Commit"/>, by path relative to the root.</summary>
    public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the ledger entries of the tree that are no regular file (symlink, gitlink) with their mode.</summary>
    public Dictionary<string, string> Special { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the ledger files in the index.</summary>
    public HashSet<string> Index { get; } = new(StringComparer.Ordinal);

    /// <summary>Gets the findings caused by git failing while a repository is there: path (or the ledger folder) and detail.</summary>
    public List<(string File, string Detail)> Problems { get; } = [];

    /// <summary>Reads the state; <see cref="InRepository"/> is false without git or outside a repository (nothing else is filled then).</summary>
    public static LedgerHeadView Read(string root, TimeSpan timeout)
    {
        var view = new LedgerHeadView();
        var inside = GitAnchor.Isolated(root, ["rev-parse", "--is-inside-work-tree"], null, timeout);
        if (inside.Status == GitAnchor.GitStatus.Unavailable)
        {
            return view;
        }

        if (inside.Status == GitAnchor.GitStatus.TimedOut)
        {
            view.InRepository = true;
            view.Fail("git timed out while looking for the repository");
            return view;
        }

        if (!inside.Ok || inside.Text.Trim() != "true")
        {
            return view;
        }

        view.InRepository = true;
        view.ReadCommit(root, timeout);
        view.ReadIndex(root, timeout);
        if (view.Commit is not null)
        {
            view.ReadTree(root, timeout);
        }

        return view;
    }

    /// <summary>Whether the file is in the index and in the tree with exactly these bytes.</summary>
    public bool EqualsCommitted(string file, byte[] bytes) =>
        Index.Contains(file) && Blobs.TryGetValue(file, out var blob) && bytes.AsSpan().SequenceEqual(blob);

    private void Fail(string why, string? file = null) =>
        Problems.Add((file ?? LedgerFiles.LedgerDir, $"The ledger files could not be compared with HEAD: {why}. A git failure is a finding, never 'unchanged'."));

    private static string Why(GitAnchor.GitResult r, string what) => r.Status == GitAnchor.GitStatus.TimedOut ? $"git timed out in {what}" : $"git {what} failed";

    private void ReadCommit(string root, TimeSpan timeout)
    {
        var r = GitAnchor.Isolated(root, ["cat-file", "--batch-check"], Encoding.ASCII.GetBytes("HEAD^{commit}\n"), timeout);
        if (!r.Ok)
        {
            Fail(Why(r, "cat-file"));
            return;
        }

        var answers = r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (answers.Length != 1)
        {
            Fail("git answered the resolution of HEAD with an unexpected number of lines");
            return;
        }

        var parts = answers[0].Split(' ');
        if (parts.Length >= 2 && parts[1] == "commit" && LedgerHistory.IsCommitId(parts[0]))
        {
            Commit = parts[0];
        }
        else if (!(parts.Length == 2 && parts[1] == "missing"))
        {
            Fail("git answered the resolution of HEAD with an unexpected value");
        }
    }

    private void ReadIndex(string root, TimeSpan timeout)
    {
        var r = GitAnchor.TrackedLedgerFiles(root, timeout);
        if (!r.Ok)
        {
            Fail(Why(r, "ls-files"));
            return;
        }

        Index.UnionWith(Encoding.UTF8.GetString(r.Output).Split('\0', StringSplitOptions.RemoveEmptyEntries));
    }

    private void ReadTree(string root, TimeSpan timeout)
    {
        var r = GitAnchor.Isolated(root, ["ls-tree", "-r", "-z", Commit!, "--", LedgerFiles.LedgerDir], null, timeout);
        if (!r.Ok)
        {
            Fail(Why(r, "ls-tree"));
            return;
        }

        var regular = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in Encoding.UTF8.GetString(r.Output).Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            var meta = tab < 0 ? [] : entry[..tab].Split(' ');
            if (meta.Length != 3)
            {
                Fail("git ls-tree answered with an unexpected entry");
                return;
            }

            var path = entry[(tab + 1)..];
            if (meta[0] is "100644" or "100755" && meta[1] == "blob" && LedgerHistory.IsCommitId(meta[2]))
            {
                regular[path] = meta[2];
            }
            else
            {
                Special[path] = meta[0];
            }
        }

        if (regular.Count == 0)
        {
            return;
        }

        var contents = LedgerHistory.ReadBlobs(root, [.. regular.Values], timeout);
        if (contents is null)
        {
            Fail("git cat-file could not deliver the blobs of the HEAD tree");
            return;
        }

        foreach (var (path, id) in regular)
        {
            if (contents.TryGetValue(id, out var bytes) && bytes is not null)
            {
                Blobs[path] = bytes;
            }
            else
            {
                Fail("a blob of the HEAD tree is missing in the object database", path);
            }
        }
    }
}
