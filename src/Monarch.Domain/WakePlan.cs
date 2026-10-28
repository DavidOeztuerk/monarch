using System.Globalization;

namespace Monarch.Domain;

/// <summary>Zeitfenster eines Weckrufs relativ zur Weckzeit.</summary>
public enum WakeWindow
{
    /// <summary>Weckzeit samt Grace noch nicht erreicht (zum Beispiel RunAtLoad direkt nach dem Anlegen).</summary>
    NotYet,

    /// <summary>Fällig: Zeitprüfung bestanden.</summary>
    Due,

    /// <summary>Älter als <see cref="WakePlan.MaxAge"/>; ein veraltetes <c>--at</c> wird verworfen.</summary>
    Stale,
}

/// <summary>Reine Regeln des Weckrufs (ADR-0008): Weckzeit, Cron-Ausdruck, festes Prompt-Template, UUID-Prüfung, Zeitfenster.</summary>
public static class WakePlan
{
    /// <summary>Abstand zwischen <c>resets_at</c> und der Weckzeit.</summary>
    public static readonly TimeSpan Slack = TimeSpan.FromMinutes(3);

    /// <summary>Nachfrist des Außen-Jobs nach der Weckzeit; länger als der Cron-Jitter von 90 s, damit der In-Session-Cron zuerst feuert.</summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(3);

    /// <summary>Älter als das ist ein <c>--at</c> veraltet (eine Plist kennt kein Jahr).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

    /// <summary>Weckzeit: <c>resets_at</c> + 3 min, auf volle Minuten, die lokale Minute weder :00 noch :30 (Jitter-Minuten).</summary>
    public static DateTimeOffset WakeTime(DateTimeOffset resetsAt, TimeZoneInfo zone)
    {
        var t = resetsAt + Slack;
        t = new DateTimeOffset(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);
        while (TimeZoneInfo.ConvertTime(t, zone).Minute is 0 or 30)
        {
            t = t.AddMinutes(1);
        }

        return t;
    }

    /// <summary>Der <c>resets_at</c>, aus dem die Weckzeit entstand (Näherung: Weckzeit minus <see cref="Slack"/>).</summary>
    public static DateTimeOffset ResetsAt(DateTimeOffset wake) => wake - Slack;

    /// <summary>Cron-Ausdruck <c>M H D Mon *</c> in lokaler Zeit.</summary>
    public static string Cron(DateTimeOffset wake, TimeZoneInfo zone)
    {
        var l = TimeZoneInfo.ConvertTime(wake, zone);
        return string.Create(CultureInfo.InvariantCulture, $"{l.Minute} {l.Hour} {l.Day} {l.Month} *");
    }

    /// <summary>Das feste Prompt-Template. Agent-Freitext gelangt nie in einen persistenten Job.</summary>
    public static string Prompt(string recapPath) => $"Mana-Reset: lies {recapPath} und mach weiter.";

    /// <summary>Kanonische UUID (8-4-4-4-12 Hex mit Bindestrichen), sonst nichts.</summary>
    public static bool IsSessionId(string? id) =>
        id is { Length: 36 } && Guid.TryParseExact(id, "D", out _);

    /// <summary>Ordnet <paramref name="now"/> dem Fenster um die Weckzeit zu.</summary>
    public static WakeWindow Classify(DateTimeOffset now, DateTimeOffset at)
    {
        if (now < at + Grace)
        {
            return WakeWindow.NotYet;
        }

        return now - at > MaxAge ? WakeWindow.Stale : WakeWindow.Due;
    }
}
