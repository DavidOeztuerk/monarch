---
id: Q-0149
gate: G-0108
kind: main
rank: C
state: in_review
dod:
  - Test zuerst rot: tests/Monarch.Domain.Tests/ManaCalibrationTests.cs (Theory Pair_is_normalised_to_a_fixed_vocabulary, :52-60) bekommt Zeilen claude-fable-5-2 -> fable, claude-fable-5-1 -> fable, claude-mythos-5-2 -> mythos; ein Cli-Test in tests/Monarch.Cli.Tests/CalibrationTests.cs: Session in mana.json mit claude-fable-5-2 und CLAUDE_EFFORT medium -> `mana` zeigt „Paar (fable, medium)“
  - Fix: fable und mythos ins feste Vokabular (ManaCalibration.cs:8, Doku-Kommentar :4); Normalize (:18-23) sonst unverändert; unbekannter Freitext bleibt unknown (bestehende Zeile lower-the-model bleibt grün) und gelangt nie in eine Ausgabe
  - Grenzen, Resolve() und Guard unverändert (ADR-0012 Punkt 7), nur der Schätzfaktor je Paar ändert sich; der Deny-Listen-Test aus ADR-0012 Punkt 6 (CalibrationTests.cs:12, :160-207) bleibt grün; kein Test gelöscht oder gelockert; dotnet build und test -c Release grün
  - Nachtrag in docs/adr/0012-mana-nach-modell-und-effort.md (einfache Sprache): Vokabular aus Punkt 4 (:44-46) um fable und mythos erweitert, Anlass
  - Abhängigkeit: keine (technisch unabhängig von Q-0148)
scope:
  include: ["src/Monarch.Domain/ManaCalibration.cs", "tests/Monarch.Domain.Tests/**", "tests/Monarch.Cli.Tests/**", "docs/adr/0012-mana-nach-modell-und-effort.md"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 1.2, gold_usd: 0 }
boss: "ManaCalibrationTests: claude-fable-5-2 und claude-fable-5-1 -> fable, claude-mythos-5-2 -> mythos sind vor dem Fix rot und danach grün; Cli-Test: `mana` zeigt „Paar (fable, medium)“; lower-the-model bleibt unknown, Deny-Listen-Test bleibt grün"
---
# Mana-Kalibrierung kennt fable und mythos (#82)
