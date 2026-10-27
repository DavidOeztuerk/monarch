namespace Monarch.Application.Ports;

/// <summary>Port that provides the current time.</summary>
public interface IClock
{
    /// <summary>Gets the current UTC time.</summary>
    DateTimeOffset UtcNow { get; }
}
