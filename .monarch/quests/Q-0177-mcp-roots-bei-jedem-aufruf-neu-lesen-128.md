---
id: Q-0177
gate: G-0125
kind: main
rank: A
state: cleared
dod:
  - "Vor jedem tools/call wird roots/list neu angefragt, wenn der Client Roots deklariert"
  - "Test: nach Wechsel der Roots löst derselbe Server im nächsten Aufruf die neue Worktree auf (rot vorher, grün nachher)"
  - "Keine Erweiterung der Roots: fehlende Antwort führt zu leeren Roots, wie bisher"
scope:
  include: ["src/Monarch.Mcp/McpServer.cs", "src/Monarch.Cli/McpTools.cs", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.5, gold_usd: 0 }
boss: "Nach Worktree-Wechsel akzeptiert gate_plan die neue Worktree ohne Neustart"
board: { pr: 146 }
---
# MCP-Roots bei jedem Aufruf neu lesen (#128, Owner-Entscheid 19.11.)
