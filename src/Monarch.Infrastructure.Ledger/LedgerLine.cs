using System.Text.Json;
using System.Text.Json.Serialization;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Wire form of one ledger line; property order is the canonical order.</summary>
internal sealed class LedgerLine
{
    public long Seq { get; set; }

    public DateTimeOffset At { get; set; }

    public string Actor { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public JsonElement Payload { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string? Hash { get; set; }
}

/// <summary>Anchor of one ledger file.</summary>
internal sealed class LedgerHead
{
    public long Seq { get; set; }

    public string Hash { get; set; } = string.Empty;
}

/// <summary>Content of <c>ledger-heads.json</c>.</summary>
internal sealed class LedgerHeads
{
    public Dictionary<string, LedgerHead> Files { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LedgerLine))]
[JsonSerializable(typeof(LedgerHeads))]
internal sealed partial class LedgerJsonContext : JsonSerializerContext
{
}
