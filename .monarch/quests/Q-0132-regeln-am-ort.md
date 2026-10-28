---
id: Q-0132
gate: G-0103
kind: main
rank: A
severity: medium
risk_tags: [policy]
state: proposed
scope:
  repo: monarch
  include: ["src/Monarch.Cli/QuestCommands.cs", "src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Infrastructure.Evidence/**", "src/Monarch.Policy/ToolCallPolicy.cs", "tests/**", "plugin/skills/**", "plugin/README.md", "docs/adr/0007-*"]
  exclude: []
estimate: { session_pct: 12, weekly_pct: 2, gold_usd: 0 }
dod:
  - quest start gibt Scope (include/exclude) und Regel 1 aus – Scope gilt für jeden Schreibzugriff (auch Scratchpad, Temp, Notizen); außerhalb nur über gate_plan (Folge-Quests), knowledge_write (Notizen) oder nach submit
  - SessionStart-Briefing nennt aktive Quest mit Scope, Regel 1 und „Merge nur über monarch gate merge nach develop“
  - Deny-Text des Scope Locks nennt den Weg (gate_plan, knowledge_write oder erst submitten); keine Varianten
  - gate merge verweigert, wenn der letzte Lauf auf dem Integrations-Branch nicht grün ist; nach dem Merge prüft er den Lauf auf dem Merge-Commit und meldet rot/abgebrochen deutlich (Ledger-Event)
  - Tests rot → grün; keine abgeschwächten Tests
boss: "gate merge mit rotem develop-Lauf → verweigert ohne PUT; quest start und Scope-Deny nennen gate_plan/knowledge_write/submit"
---
# Regeln dort, wo sie gebraucht werden

Monarch nennt seine Regeln selbst (quest start, Briefing, Deny-Text), statt dass sie nur in Prosa stehen (Memory „monarch-guards-before-acting“); develop muss vor und nach dem Merge grün sein.
