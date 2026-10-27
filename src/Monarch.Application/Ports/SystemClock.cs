namespace Monarch.Application.Ports;

/// <summary>Default <see cref="IClock"/> backed by <see cref="TimeProvider"/>.</summary>
/// <param name="timeProvider">The time source.</param>
public sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    /// <summary>Initializes a new instance using <see cref="TimeProvider.System"/>.</summary>
    public SystemClock()
        : this(TimeProvider.System)
    {
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow => timeProvider.GetUtcNow();
}
