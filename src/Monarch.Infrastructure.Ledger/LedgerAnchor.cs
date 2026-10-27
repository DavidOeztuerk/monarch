using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Condition of <c>ledger-heads.json</c>.</summary>
internal enum AnchorStatus
{
    /// <summary>The anchor file does not exist.</summary>
    Missing,

    /// <summary>The anchor is well-formed in every entry.</summary>
    Ok,

    /// <summary>The anchor is unreadable, not JSON, has the wrong shape, or contains at least one invalid entry.</summary>
    Invalid,
}

/// <summary>Result of reading the head anchor. <see cref="Heads"/> holds only the valid entries.</summary>
internal sealed record AnchorState(AnchorStatus Status, LedgerHeads Heads, IReadOnlyList<AnchorProblem> Problems)
{
    /// <summary>Gets a short, single-line description of all problems.</summary>
    public string Describe() => string.Join("; ", Problems.Select(p => p.Detail));
}

/// <summary>One defect of the anchor. <see cref="File"/> is the ledger file concerned, or the anchor path for file-level defects.</summary>
internal sealed record AnchorProblem(string File, string Detail);

/// <summary>Tolerant, non-throwing reader for the head anchor: no anchor content may crash a caller.</summary>
internal static class LedgerAnchor
{
    public static AnchorState Read(string root)
    {
        var path = LedgerFiles.Full(root, LedgerFiles.HeadsPath);
        if (Directory.Exists(path))
        {
            return Invalid("Head anchor path is a directory.");
        }

        if (!File.Exists(path))
        {
            return new AnchorState(AnchorStatus.Missing, new LedgerHeads(), []);
        }

        byte[] bytes;
        try
        {
            bytes = LedgerFiles.ReadAllShared(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Invalid("Head anchor is unreadable: " + ex.GetType().Name + ".");
        }

        try
        {
            using var doc = JsonDocument.Parse(bytes);
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            return Invalid("Head anchor is not valid JSON.");
        }
    }

    private static AnchorState Invalid(string detail) =>
        new(AnchorStatus.Invalid, new LedgerHeads(), [new AnchorProblem(LedgerFiles.HeadsPath, detail)]);

    private static AnchorState Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Head anchor root is not a JSON object.");
        }

        if (!root.TryGetProperty("files", out var files))
        {
            return Invalid("Head anchor has no 'files' property.");
        }

        if (files.ValueKind != JsonValueKind.Object)
        {
            return Invalid("Head anchor 'files' is not a JSON object.");
        }

        var heads = new LedgerHeads();
        var problems = new List<AnchorProblem>();
        foreach (var entry in files.EnumerateObject())
        {
            var detail = Validate(entry.Value, out var head);
            if (detail is null)
            {
                heads.Files[entry.Name] = head!;
            }
            else
            {
                problems.Add(new AnchorProblem(entry.Name, "Anchor entry is invalid: " + detail));
            }
        }

        return new AnchorState(problems.Count == 0 ? AnchorStatus.Ok : AnchorStatus.Invalid, heads, problems);
    }

    private static string? Validate(JsonElement value, out LedgerHead? head)
    {
        head = null;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return "not an object.";
        }

        if (!value.TryGetProperty("seq", out var seq) || seq.ValueKind != JsonValueKind.Number)
        {
            return "'seq' is missing or not a number.";
        }

        if (!seq.TryGetInt64(out var n) || n < 1)
        {
            return "'seq' must be an integer >= 1.";
        }

        if (!value.TryGetProperty("hash", out var hash) || hash.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(hash.GetString()))
        {
            return "'hash' is missing or not a non-empty string.";
        }

        head = new LedgerHead { Seq = n, Hash = hash.GetString()! };
        return null;
    }
}
