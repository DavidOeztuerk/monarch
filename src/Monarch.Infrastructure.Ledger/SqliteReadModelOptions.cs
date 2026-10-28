namespace Monarch.Infrastructure.Ledger;

/// <summary>Options for the SQLite read model.</summary>
/// <param name="Path">Path of the database file.</param>
public sealed record SqliteReadModelOptions(string Path);
