using System.Text.Json;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Maintains <c>ledger-heads.json</c>, the tamper anchor for truncation and deletion.</summary>
internal static class LedgerHeadStore
{
    public static void Update(string root, string relative, long seq, string hash)
    {
        var path = LedgerFiles.Full(root, LedgerFiles.HeadsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var guard = LedgerFiles.OpenExclusive(path + ".lock");
        LedgerHeads heads;
        try
        {
            heads = LedgerFiles.ReadHeads(root) ?? new LedgerHeads();
        }
        catch (JsonException)
        {
            heads = new LedgerHeads();
        }

        if (!heads.Files.TryGetValue(relative, out var current) || current.Seq < seq)
        {
            heads.Files[relative] = new LedgerHead { Seq = seq, Hash = hash };
        }

        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(heads, LedgerJsonContext.Default.LedgerHeads));
        File.Move(temp, path, overwrite: true);
    }
}
