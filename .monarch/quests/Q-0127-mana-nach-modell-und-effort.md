---
id: Q-0127
gate: G-0101
kind: main
rank: C
severity: medium
risk_tags: []
state: proposed
scope:
  repo: monarch
  include: ["src/Monarch.Domain/**", "src/Monarch.Cli/**", "src/Monarch.Mcp/**", "tests/**", "docs/adr/0012-*", "plugin/skills/**", "plugin/README.md"]
  exclude: ["src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/ProjectPaths.cs"]
estimate: { session_pct: 12, weekly_pct: 2, gold_usd: 0 }
dod:
  - ADR-0012 nach Design-Auflagen des Wardens (G-0101) ergänzt: Beobachtung = Quest, Schlüssel = Paar des Main; eine Einheit (Faktor); nicht kalibrieren bei Reset, veraltetem Snapshot oder Δpp_geschätzt < 1; Faktor geklemmt 0,5–4, EMA startet mit Startfaktor; Effort-Quelle gemessen, sonst unknown; Subagent-Modell aus Transcript (message.model), Frontmatter nur Fallback
  - mana, mana_forecast, quest accept zeigen Paar, Faktor, n; Test mit Snapshot am Limit gegen Deny-Liste (senk|reduzier|niedriger|lower|reduce|switch|wechsel|/effort|/model|--effort) für mana, mana_forecast, quest accept, rest und Skill-Texte
  - Test: Resolve()-Limits mit und ohne Kalibrierdatei identisch
  - Tests rot → grün; keine abgeschwächten Tests
boss: "monarch mana zeigt für (opus, xhigh) und (sonnet, default) getrennte Faktoren mit Kalibrierstatus; Deny-Listen-Test am Limit grün"
---
# Mana-Schätzung je (Modell, Effort)

Ersetzt Q-0126 (abgebrochen: Scope enthielt policy-Pfade bei Rank C, vom Warden als Abschwächung gewertet). Siehe ADR-0012.
