using Monarch.Domain;

namespace Monarch.Policy;

/// <summary>Entscheidung des Mana-Guards.</summary>
public enum ManaDecision
{
    /// <summary>Weiterarbeiten.</summary>
    Proceed,
    /// <summary>Recap schreiben und anhalten.</summary>
    RecapAndStop,
    /// <summary>Ohne Recap pausieren, Reset steht kurz bevor.</summary>
    PauseWithoutRecap,
}

/// <summary>Pacing-Modus des Wochenfensters.</summary>
public enum PacingMode
{
    /// <summary>Alle Quest-Typen.</summary>
    Normal,
    /// <summary>Nur Main und Emergency.</summary>
    MainAndEmergencyOnly,
}

/// <summary>Mana-Guard: entscheidet, ob ein weiterer Schritt noch bis zur Arbeitsgrenze passt (ADR-0012 Nachtrag Q-0189).</summary>
public static class ManaGuard
{
    /// <summary>Reset-Nähe, unter der ohne Recap pausiert wird.</summary>
    public static readonly TimeSpan SkipRecapIfResetWithin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Proceed nur, wenn aktuell + Schritt in jedem vorhandenen Fenster die Grenze nicht überschreitet; der Rest bis 100 % ist die Recap-Reserve
    /// und wird nicht noch einmal abgezogen. Sonst PauseWithoutRecap, wenn der Reset des maßgeblichen Fensters weniger als 10 Minuten entfernt ist
    /// (5h-Fenster, bei fehlendem 5h-Fenster die Woche), andernfalls RecapAndStop. Bei parallelen Schritten übergibt der Aufrufer die Summe.
    /// Veraltete Werte gehen über <see cref="ManaSnapshot.Effective5h"/> bzw. <see cref="ManaSnapshot.Effective7d"/> ein.
    /// </summary>
    public static ManaDecision Decide(ManaSnapshot now, double stepEstimate5h, double stepEstimate7d, ManaLimits limits, DateTimeOffset clock)
    {
        var fits = (!now.HasFiveHour || now.Effective5h + stepEstimate5h <= limits.FiveHourPct)
            && now.Effective7d + stepEstimate7d <= limits.SevenDayPct;
        if (fits) return ManaDecision.Proceed;
        var reset = now.HasFiveHour ? now.FiveHourResetsAt : now.SevenDayResetsAt;
        return reset - clock < SkipRecapIfResetWithin ? ManaDecision.PauseWithoutRecap : ManaDecision.RecapAndStop;
    }

    /// <summary>MainAndEmergencyOnly, wenn der Wochenverbrauch mehr als <paramref name="tolerancePts"/> über dem verstrichenen Wochenanteil liegt.</summary>
    public static PacingMode Pacing(double weeklyUsedPct, DateTimeOffset weeklyResetsAt, DateTimeOffset clock, double tolerancePts = 15)
    {
        var elapsed = Math.Clamp(1 - ((weeklyResetsAt - clock).TotalDays / 7), 0, 1);
        return weeklyUsedPct > (elapsed * 100) + tolerancePts ? PacingMode.MainAndEmergencyOnly : PacingMode.Normal;
    }
}
