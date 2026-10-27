namespace Monarch.Infrastructure.Ledger;

/// <summary>Options for the JSONL ledger.</summary>
public sealed class JsonlLedgerOptions
{
    /// <summary>Gets or sets the directory that holds ledger files.</summary>
    public string Directory { get; set; } = ".monarch/ledger";
}
