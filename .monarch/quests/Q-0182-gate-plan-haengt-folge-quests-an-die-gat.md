---
id: Q-0182
gate: G-0124
kind: main
rank: C
state: cleared
dod:
  - Test: zweiter gate_plan mit Folge-Quest fuehrt alle Quests in der Gate-Datei; vorher rot
  - gate show zeigt alle Quests; danach gruen
  - dotnet test Monarch.Cli.Tests gruen
scope:
  include: ["src/Monarch.Cli/McpTools.cs", "tests/Monarch.Cli.Tests/McpTests.cs"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.2, gold_usd: 0 }
board: { pr: 143 }
---
# gate_plan haengt Folge-Quests an die Gate-Liste an (Issue 92, Ersatz fuer abandoned Q-0177)
