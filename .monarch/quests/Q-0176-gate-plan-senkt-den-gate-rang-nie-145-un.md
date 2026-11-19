---
id: Q-0176
gate: G-0125
kind: main
rank: A
state: cleared
dod:
  - "Gate mit Rang S bleibt S nach einem Folge-Aufruf von gate_plan mit niedrigerem Rang (Test vorher rot, danach grün)"
  - "BoardNames.ParseRank: Zahl wie 99 fällt auf Rang E zurück statt einen ungültigen Rang zu liefern (Test rot vorher)"
  - "Keine Schwelle und kein Test abgeschwächt"
scope:
  include: ["src/Monarch.Cli/McpTools.cs", "src/Monarch.Infrastructure.Boards/BoardNames.cs", "src/Monarch.Infrastructure.Boards/LocalFileBoard.cs", "tests/Monarch.Cli.Tests/**", "tests/Monarch.Infrastructure.Tests/**"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1, gold_usd: 0 }
boss: "Gate S bleibt S, ungültiger Rang wird verworfen"
board: { pr: 146 }
---
# gate_plan senkt den Gate-Rang nie (#145) und Rang-Parsing härten
