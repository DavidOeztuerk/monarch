using System.Text.Json;
using Monarch.Application.Ports;

namespace Monarch.Infrastructure.Tests;

internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "monarch-test-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, true);
        }
        catch (IOException)
        {
        }
    }
}

internal sealed class FixedClock(DateTimeOffset start, TimeSpan? step = null) : IClock
{
    private long _ticks = start.UtcTicks;

    public FixedClock()
        : this(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero))
    {
    }

    public DateTimeOffset UtcNow => new(Interlocked.Add(ref _ticks, (step ?? TimeSpan.FromSeconds(1)).Ticks), TimeSpan.Zero);
}

internal static class P
{
    public static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
