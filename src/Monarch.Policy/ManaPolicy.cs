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

/// <summary>Adaptive Grenze pro Fenster: clamp(100 - (safety * EWMA(letzte 5 Recaps) + p90(Schritte)), min, max).</summary>
public static class AdaptiveLimit
{
    /// <summary>Anzahl Recaps, ab der die Berechnung greift.</summary>
    public const int MinRecaps = 3;

    /// <summary>Anzahl berücksichtigter Recaps für den EWMA.</summary>
    public const int EwmaWindow = 5;

    /// <summary>
    /// EWMA über die letzten fünf Werte (älteste zuerst), Gewicht <paramref name="alpha"/> (Default 0,5) für den jeweils neuesten Wert;
    /// der Startwert ist der erste Wert des Fensters. Leere Eingabe ergibt 0.
    /// </summary>
    public static double Ewma(IReadOnlyList<double> values, double alpha = 0.5)
    {
        if (values.Count == 0) return 0;
        var start = Math.Max(0, values.Count - EwmaWindow);
        var acc = values[start];
        for (var i = start + 1; i < values.Count; i++) acc = (alpha * values[i]) + ((1 - alpha) * acc);
        return acc;
    }

    /// <summary>Perzentil nach Nearest-Rank: Rang = ceil(p/100 * N), begrenzt auf 1..N. Leere Eingabe ergibt 0.</summary>
    public static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var rank = (int)Math.Ceiling(percentile / 100 * sorted.Length);
        return sorted[Math.Clamp(rank, 1, sorted.Length) - 1];
    }

    /// <summary>Berechnet die Grenze; mit weniger als drei Recaps gilt der (geklemmte) Startwert.</summary>
    /// <param name="recapCosts">Gemessene Recap-Kosten in Prozentpunkten, älteste zuerst.</param>
    /// <param name="stepCosts">Gemessene Kosten von Arbeitsschritten.</param>
    /// <param name="start">Startwert der Grenze.</param>
    /// <param name="min">Untere Leitplanke.</param>
    /// <param name="max">Obere Leitplanke.</param>
    /// <param name="safety">Sicherheitsfaktor auf den Recap.</param>
    /// <param name="percentile">Perzentil der Schrittkosten.</param>
    public static double Compute(IReadOnlyList<double> recapCosts, IReadOnlyList<double> stepCosts, double start, double min, double max,
        double safety = 1.5, double percentile = 90)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(min, max);
        if (recapCosts.Count < MinRecaps) return Math.Clamp(start, min, max);
        return Math.Clamp(100 - ((safety * Ewma(recapCosts)) + Percentile(stepCosts, percentile)), min, max);
    }
}

/// <summary>Mana-Guard: entscheidet, ob ein weiterer Schritt noch in die Grenzen inklusive Recap-Reserve passt.</summary>
public static class ManaGuard
{
    /// <summary>Reset-Nähe, unter der ohne Recap pausiert wird.</summary>
    public static readonly TimeSpan SkipRecapIfResetWithin = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Proceed nur, wenn aktuell + Schritt + Recap in BEIDEN Fenstern die Grenze nicht überschreitet. Sonst PauseWithoutRecap,
    /// wenn der 5h-Reset weniger als 10 Minuten entfernt ist, andernfalls RecapAndStop. Bei parallelen Schritten übergibt der Aufrufer die Summe.
    /// Veraltete Werte gehen über <see cref="ManaSnapshot.Effective5h"/> bzw. <see cref="ManaSnapshot.Effective7d"/> ein.
    /// </summary>
    public static ManaDecision Decide(ManaSnapshot now, double stepEstimate5h, double stepEstimate7d, double recapEstimate5h,
        ManaLimits limits, DateTimeOffset clock, double recapEstimate7d = 0)
    {
        var fits = now.Effective5h + stepEstimate5h + recapEstimate5h <= limits.FiveHourPct
            && now.Effective7d + stepEstimate7d + recapEstimate7d <= limits.SevenDayPct;
        if (fits) return ManaDecision.Proceed;
        return now.FiveHourResetsAt - clock < SkipRecapIfResetWithin ? ManaDecision.PauseWithoutRecap : ManaDecision.RecapAndStop;
    }

    /// <summary>MainAndEmergencyOnly, wenn der Wochenverbrauch mehr als <paramref name="tolerancePts"/> über dem verstrichenen Wochenanteil liegt.</summary>
    public static PacingMode Pacing(double weeklyUsedPct, DateTimeOffset weeklyResetsAt, DateTimeOffset clock, double tolerancePts = 15)
    {
        var elapsed = Math.Clamp(1 - ((weeklyResetsAt - clock).TotalDays / 7), 0, 1);
        return weeklyUsedPct > (elapsed * 100) + tolerancePts ? PacingMode.MainAndEmergencyOnly : PacingMode.Normal;
    }
}
