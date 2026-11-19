---
id: Q-0177
gate: G-0124
kind: main
rank: C
state: abandoned
dod:
  - Zweiter gate_plan-Aufruf mit Folge-Quest fuehrt beide Quests in der Gate-Datei; Test vorher rot
  - gate show zeigt beide Quests, Gate-Datei und Anzeige stimmen ueberein
  - dotnet test -c Release gruen
scope:
  include: ["src/Monarch.Cli/McpTools.cs", "src/Monarch.Infrastructure.Boards/**"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.2, gold_usd: 0 }
---
# gate_plan haengt Folge-Quests an die Gate-Liste an, statt sie zu ersetzen (Issue 92)
