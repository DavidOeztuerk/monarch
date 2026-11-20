namespace Monarch.Domain;

/// <summary>Mana-Kosten in den drei realen Budgets: 5h-Fenster, 7d-Fenster, Geld.</summary>
/// <param name="SessionPct">Prozentpunkte im 5h-Fenster.</param>
/// <param name="WeeklyPct">Prozentpunkte im 7d-Fenster.</param>
/// <param name="GoldUsd">API-Kosten in USD.</param>
public readonly record struct ManaCost(double SessionPct, double WeeklyPct, decimal GoldUsd)
{
    /// <summary>Addiert zwei Kosten komponentenweise.</summary>
    public static ManaCost operator +(ManaCost a, ManaCost b) =>
        new(a.SessionPct + b.SessionPct, a.WeeklyPct + b.WeeklyPct, a.GoldUsd + b.GoldUsd);
}

/// <summary>Momentaufnahme der Budgets. Quelle: Statusline-Sensor bzw. Token-Ledger.</summary>
/// <param name="SessionUsedPct">Verbrauch im 5h-Fenster in Prozent.</param>
/// <param name="SessionResetsAt">Reset des 5h-Fensters.</param>
/// <param name="WeeklyUsedPct">Verbrauch im 7d-Fenster in Prozent.</param>
/// <param name="WeeklyResetsAt">Reset des 7d-Fensters.</param>
/// <param name="GoldSpentUsd">Bisher ausgegebene USD.</param>
/// <param name="GoldBudgetUsd">USD-Budget.</param>
/// <param name="SessionReservePct">Recap-Reserve im 5h-Fenster.</param>
/// <param name="WeeklyReservePct">Recap-Reserve im 7d-Fenster.</param>
public sealed record ManaPool(
    double SessionUsedPct, DateTimeOffset SessionResetsAt,
    double WeeklyUsedPct, DateTimeOffset WeeklyResetsAt,
    decimal GoldSpentUsd, decimal GoldBudgetUsd,
    double SessionReservePct = 10, double WeeklyReservePct = 4)
{
    /// <summary>True, wenn die Kosten in alle Budgets passen, ohne die Recap-Reserve anzutasten. Monarch gibt nie Geld aus.</summary>
    public bool CanAfford(ManaCost cost) =>
        SessionUsedPct + cost.SessionPct <= 100 - SessionReservePct &&
        WeeklyUsedPct + cost.WeeklyPct <= 100 - WeeklyReservePct &&
        GoldSpentUsd + cost.GoldUsd <= GoldBudgetUsd * (1 - (decimal)SessionReservePct / 100);
}

/// <summary>Abo-Plan, aus dem die Start-Grenzen folgen (ADR-0012 Nachtrag Q-0189).</summary>
public enum PlanKind
{
    /// <summary>Pro.</summary>
    Pro,
    /// <summary>Max (früher Max 5x).</summary>
    Max,
    /// <summary>Ultra (früher Max 20x); kein 5h-Fenster.</summary>
    Ultra,
    /// <summary>API-Key (Budget-Modus).</summary>
    ApiKey,
}

/// <summary>Modellklasse für die Grenzen: Fable, Mythos und Unbekanntes zählen wie Opus.</summary>
public enum ModelClass
{
    /// <summary>Opus (auch Fable, Mythos, unbekannt).</summary>
    Opus,
    /// <summary>Sonnet.</summary>
    Sonnet,
    /// <summary>Haiku.</summary>
    Haiku,
}

/// <summary>Abbildung der Modellfamilie auf die Modellklasse.</summary>
public static class ModelClasses
{
    /// <summary>Sonnet und Haiku; alles andere (opus, fable, mythos, unknown, null) ist Opus.</summary>
    public static ModelClass Of(string? family) => family?.ToLowerInvariant() switch
    {
        "sonnet" => ModelClass.Sonnet,
        "haiku" => ModelClass.Haiku,
        _ => ModelClass.Opus,
    };
}

/// <summary>Arbeitsgrenzen pro Fenster in Prozent; es gilt die zuerst erreichte.</summary>
/// <param name="FiveHourPct">Grenze im 5h-Fenster.</param>
/// <param name="SevenDayPct">Grenze im 7d-Fenster.</param>
public sealed record ManaLimits(double FiveHourPct, double SevenDayPct)
{
    /// <summary>
    /// Startwerte je Plan und Modell. Ultra hat kein 5h-Fenster; meldet die Statusline eines, gelten die Werte von Max.
    /// Beim API-Key sind beide Grenzen 90 % des Budgets, für jedes Modell.
    /// </summary>
    public static ManaLimits For(PlanKind plan, ModelClass model) => plan switch
    {
        PlanKind.Pro => model switch
        {
            ModelClass.Sonnet => new ManaLimits(93, 97),
            ModelClass.Haiku => new ManaLimits(95, 98),
            _ => new ManaLimits(90, 96),
        },
        PlanKind.Max or PlanKind.Ultra => model switch
        {
            ModelClass.Sonnet => new ManaLimits(96, 98),
            ModelClass.Haiku => new ManaLimits(97, 98),
            _ => new ManaLimits(94, 98),
        },
        _ => new ManaLimits(90, 90),
    };

    /// <summary>Startwerte je Plan für Opus (Anzeige "start limits"; Fable und Unbekanntes zählen wie Opus).</summary>
    public static ManaLimits ForPlan(PlanKind plan) => For(plan, ModelClass.Opus);
}

/// <summary>Sensor-Momentaufnahme der beiden Rate-Limit-Fenster.</summary>
/// <param name="FiveHourUsedPct">Zuletzt gemessener Verbrauch im 5h-Fenster.</param>
/// <param name="FiveHourResetsAt">Reset des 5h-Fensters.</param>
/// <param name="SevenDayUsedPct">Zuletzt gemessener Verbrauch im 7d-Fenster.</param>
/// <param name="SevenDayResetsAt">Reset des 7d-Fensters.</param>
/// <param name="IsStale">True, wenn der Messwert veraltet ist oder fehlte (letzter bekannter Wert).</param>
/// <param name="EstimatedSinceSnapshot5h">Geschätzte Prozentpunkte seit dem Snapshot im 5h-Fenster (nur bei <paramref name="IsStale"/>).</param>
/// <param name="EstimatedSinceSnapshot7d">Geschätzte Prozentpunkte seit dem Snapshot im 7d-Fenster (nur bei <paramref name="IsStale"/>).</param>
/// <param name="HasFiveHour">False bei Ultra ohne 5h-Fenster: die 5h-Werte sind dann leer und zählen nicht.</param>
public sealed record ManaSnapshot(
    double FiveHourUsedPct, DateTimeOffset FiveHourResetsAt,
    double SevenDayUsedPct, DateTimeOffset SevenDayResetsAt,
    bool IsStale = false, double EstimatedSinceSnapshot5h = 0, double EstimatedSinceSnapshot7d = 0, bool HasFiveHour = true)
{
    /// <summary>Wirksamer 5h-Verbrauch: letzter Wert plus Schätzung, falls veraltet.</summary>
    public double Effective5h => IsStale ? FiveHourUsedPct + EstimatedSinceSnapshot5h : FiveHourUsedPct;

    /// <summary>Wirksamer 7d-Verbrauch: letzter Wert plus Schätzung, falls veraltet.</summary>
    public double Effective7d => IsStale ? SevenDayUsedPct + EstimatedSinceSnapshot7d : SevenDayUsedPct;
}
