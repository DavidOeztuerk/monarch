using System.ComponentModel.DataAnnotations;

namespace Monarch.Infrastructure.Ledger;

/// <summary>Options for the JSONL ledger.</summary>
public sealed class JsonlLedgerOptions
{
    /// <summary>Gets or sets the project root; ledger files live below <c>&lt;Root&gt;/.monarch/ledger</c>.</summary>
    [Required]
    public string Root { get; set; } = ".";
}
