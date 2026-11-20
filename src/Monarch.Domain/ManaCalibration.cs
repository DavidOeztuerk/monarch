namespace Monarch.Domain;

/// <summary>Das Paar (Modell, Effort), mit dem ein Schritt gerechnet wird (ADR-0012). Beide Teile stammen aus einem festen Vokabular.</summary>
/// <param name="Model">Modellfamilie (opus, sonnet, haiku, fable, mythos) oder unknown.</param>
/// <param name="Effort">Effort (low, medium, high, xhigh, max, default) oder unknown.</param>
public readonly record struct ModelEffort(string Model, string Effort)
{
    private static readonly string[] Families = ["opus", "sonnet", "haiku", "fable", "mythos"];
    private static readonly string[] Efforts = ["low", "medium", "high", "xhigh", "max", "default"];

    /// <summary>Unbekanntes Paar.</summary>
    public static ModelEffort Unknown => new("unknown", "unknown");

    /// <summary>Stabiler Schlüssel für die Kalibrierdatei.</summary>
    public string Key => Model + "|" + Effort;

    /// <summary>Normalisiert Rohwerte auf das feste Vokabular; alles andere wird <c>unknown</c>.</summary>
    public static ModelEffort Normalize(string? modelId, string? effort)
    {
        var model = Families.FirstOrDefault(f => modelId?.Contains(f, StringComparison.OrdinalIgnoreCase) == true) ?? "unknown";
        var eff = Efforts.FirstOrDefault(e => string.Equals(e, effort?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "unknown";
        return new ModelEffort(model, eff);
    }

    /// <summary>Anzeigeformat.</summary>
    public override string ToString() => $"({Model}, {Effort})";
}

/// <summary>Faktor auf die Quest-Schätzung und Zahl der Beobachtungen, die ihn getragen haben.</summary>
/// <param name="Factor">Faktor (Ist / Schätzung), geklemmt auf 0,5 bis 4.</param>
/// <param name="N">Zahl der kalibrierten Beobachtungen.</param>
public sealed record PairFactor(double Factor, int N)
{
    /// <summary>Startfaktor ohne Beobachtung.</summary>
    public static PairFactor Start => new(ManaCalibrator.StartFactor, 0);

    /// <summary>True ab <see cref="ManaCalibrator.MinObservations"/> Beobachtungen.</summary>
    public bool IsCalibrated => N >= ManaCalibrator.MinObservations;
}

/// <summary>Ergebnis einer Beobachtung.</summary>
/// <param name="Factor">Faktor nach der Beobachtung (unverändert, wenn nicht kalibriert).</param>
/// <param name="Calibrated">True, wenn die Beobachtung in die EMA einging.</param>
/// <param name="Reason">Ausschlussgrund, sonst null.</param>
public sealed record CalibrationOutcome(PairFactor Factor, bool Calibrated, string? Reason);

/// <summary>Kalibrierung des Faktors je Paar (ADR-0012). Rein; ändert nie Grenzen.</summary>
public static class ManaCalibrator
{
    /// <summary>Glättung der EMA.</summary>
    public const double Alpha = 0.3;

    /// <summary>Untere Klemme des Faktors.</summary>
    public const double MinFactor = 0.5;

    /// <summary>Obere Klemme des Faktors.</summary>
    public const double MaxFactor = 4;

    /// <summary>Startfaktor der EMA.</summary>
    public const double StartFactor = 1.0;

    /// <summary>Beobachtungen bis zum Status kalibriert.</summary>
    public const int MinObservations = 3;

    /// <summary>Kleinste geschätzte Δpp, die noch kalibriert.</summary>
    public const double MinEstimatePp = 1;

    /// <summary>Verbucht eine Quest-Beobachtung (Δpp geschätzt gegen Δpp ist).</summary>
    public static CalibrationOutcome Observe(PairFactor current, double estimatedPp, double actualPp, bool windowReset, bool stale, bool overlapWithoutTokens)
    {
        var reason = windowReset ? "window-reset"
            : stale ? "stale-snapshot"
            : overlapWithoutTokens ? "overlap-without-token-data"
            : estimatedPp < MinEstimatePp ? "estimate-below-1pp"
            : null;
        if (reason is not null)
        {
            return new CalibrationOutcome(current, false, reason);
        }

        var observed = Clamp(actualPp / estimatedPp);
        var next = Clamp(Alpha * observed + (1 - Alpha) * current.Factor);
        return new CalibrationOutcome(new PairFactor(next, current.N + 1), true, null);
    }

    /// <summary>Festes Anzeigeformat: Paar, Faktor, Kalibrierstatus. Keine Empfehlung.</summary>
    public static string Display(ModelEffort pair, PairFactor f) =>
        FormattableString.Invariant($"Paar {pair}: Faktor {f.Factor:0.00}, {(f.IsCalibrated ? "kalibriert" : "unkalibriert")} (n={f.N})");

    private static double Clamp(double v) => Math.Min(MaxFactor, Math.Max(MinFactor, v));
}
