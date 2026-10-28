---
id: Q-0126
gate: G-0101
kind: main
rank: C
severity: medium
risk_tags: []
state: proposed
scope:
  repo: monarch
  include: ["src/Monarch.Domain/**", "src/Monarch.Policy/ManaPolicy.cs", "src/Monarch.Cli/**", "src/Monarch.Mcp/**", "tests/**", "docs/adr/0012-*", "plugin/skills/mana/**", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 12, weekly_pct: 2, gold_usd: 0 }
dod:
  - Schätzung und Kalibrierung je Paar (Modell, Effort); Startwerte aus den gemessenen Δpp von Lauf 2 (ADR-0012)
  - Modell und Effort wählt der User; Monarch rechnet sie nur ein und schlägt nie vor, sie zu senken
  - mana/mana_forecast/quest accept nutzen die Paar-Kalibrierung; Tests rot → grün
boss: "monarch mana --estimate liefert für (opus, xhigh) und (sonnet, default) unterschiedliche, kalibrierte Kosten und gibt keine Effort-Empfehlung aus"
---
# Mana-Schätzung je (Modell, Effort)

Siehe ADR-0012.
