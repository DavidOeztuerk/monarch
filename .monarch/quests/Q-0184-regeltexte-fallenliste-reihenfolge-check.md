---
id: Q-0184
gate: G-0124
kind: side
rank: E
state: cleared
dod:
  - regeln/SKILL.md enthaelt die Liste bekannter Fallen und die Reihenfolge-Checkliste je Gate; Test vorher rot
  - Regel 7 trennt kosmetische minor-Befunde (bleiben im PR-Kommentar) und Nacharbeit-Issue; Test vorher rot
  - warden.md: Schutzregel greift nicht oder nicht fuer alle Akteure ist immer major; Test vorher rot
  - dotnet test Monarch.Cli.Tests gruen
scope:
  include: ["plugin/skills/regeln/SKILL.md", "plugin/agents/warden.md", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1, gold_usd: 0 }
board: { pr: 143 }
---
# Regeltexte: Fallenliste, Reihenfolge-Checkliste, Regel 7 und Schutzregel-Befund in warden.md (Ersatz fuer abandoned Q-0180)
