using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Evidence;

/// <summary>Placeholder evidence source reading GitHub check runs.</summary>
public sealed class GitHubChecksEvidence : IEvidenceSource
{
    /// <summary>Initializes a new instance; not implemented yet.</summary>
    /// <exception cref="NotImplementedException">Always.</exception>
    public GitHubChecksEvidence() => throw new NotImplementedException();
}
