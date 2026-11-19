---
id: Q-0182
gate: G-0126
kind: side
rank: E
state: cleared
dod:
  - "Fallenliste enthält: mit EnterWorktree in die Worktree wechseln, nie mit cd; Testdateien gleich in den Quest-Scope aufnehmen (z. B. tests/<Projekt>/**)"
  - "ADR-0013 Nachtrag: Roots werden vor jedem tools/call neu gelesen (Q-0177), mit Begründung"
  - "Regeltext-Tests bleiben grün"
scope:
  include: ["plugin/skills/regeln/SKILL.md", "docs/adr/0013-mcp-projekt-je-aufruf.md", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 2, weekly_pct: 0.5, gold_usd: 0 }
boss: "Die Fallen stehen da, wo der Agent sie liest"
board: { pr: 150 }
---
# Fallenliste in regeln/SKILL.md und ADR-0013-Nachtrag (Roots je Aufruf)
