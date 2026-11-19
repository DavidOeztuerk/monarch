---
id: Q-0178
gate: G-0124
kind: main
rank: A
state: abandoned
dod:
  - Test: Projekt in einer zweiten Worktree unterhalb der Roots-Liste wird nach Wechsel akzeptiert; vorher rot
  - Fremdes Projekt ausserhalb der Roots bleibt abgelehnt; Test
  - dotnet test -c Release gruen
scope:
  include: ["src/Monarch.Cli/McpTools.cs", "src/Monarch.Cli/ProjectPaths.cs"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 2, gold_usd: 0 }
---
# MCP-Roots werden pro Aufruf neu gelesen, neue Worktree wird akzeptiert (Issue 128)
