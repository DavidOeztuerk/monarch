---
id: Q-0190
gate: G-0129
kind: main
rank: A
state: proposed
dod:
  - "Ein Schritt läuft, wenn aktuell + Schritt <= Grenze im 5h- und 7d-Fenster; kein Recap-Abzug in quest accept, mana, rest, status und MCP (der Fall vom 19.11.: 80 + 8 bei Grenze 90 ergibt Proceed)"
  - "Tabelle je Plan, Modell und Fenster wie im Owner-Entscheid; Fable wie Opus, unbekanntes Modell Opus, unbekannter Plan Pro; Fable-Session prüft seven_day; Plan-Aliase max5 = Max, max20 = Ultra"
  - "Ultra ohne 5h-Fenster: ToSnapshot mit Plan, Frische über seven_day.last_seen, Begin/Finish ohne Absturz, rest/Briefing/#87 ohne Epoch 0, Guard prüft nur die Woche"
  - "AdaptiveLimit (Compute, Ewma, Percentile) und Resolved.Recaps/Recap7d entfernt; ohne Messung gilt immer der eingestellte Wert (Test statt der adaptiven Tests)"
  - "system.yaml: work_until_pct gilt für alle Modelle, Einträge je Modell, Gültigkeit 0..100, clamp wird nicht gelesen; awaken schreibt weder work_until_pct noch clamp"
  - "Version 0.1.2 in plugin.json, Wrapper und McpTools; Tests angepasst"
  - "Alle geänderten oder ersetzten alten Tests mit altem und neuem Wert und Grund im PR; Schlussrunde des Wardens über Design und Code zusammen"
scope:
  include: ["src/Monarch.Domain/Mana.cs", "src/Monarch.Policy/ManaPolicy.cs", "src/Monarch.Cli/Support.cs", "src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/ManaCommands.cs", "src/Monarch.Cli/QuestCommands.cs", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/State.cs", "src/Monarch.Cli/ManaCalibrationStore.cs", "src/Monarch.Cli/McpTools.cs", "plugin/.claude-plugin/plugin.json", "plugin/scripts/monarch", "plugin/tests/wrapper-test.sh", "tests/**"]
  exclude: []
estimate: { session_pct: 12, weekly_pct: 3, gold_usd: 0 }
boss: "dotnet test grün, gate check grün"
---
# Umsetzung: Grenze je (Plan, Modell, Fenster), kein doppelter Abzug, Plan-Aliase, Ultra ohne 5h
