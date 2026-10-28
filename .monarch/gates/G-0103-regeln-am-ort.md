---
id: G-0103
type: standard
title: Monarch – Regeln dort, wo sie gebraucht werden
rank: A
risk_tags: [policy]
repo: monarch
state: proposed
quests: [Q-0131, Q-0132]
boss: "MCP-Tools arbeiten im Projekt des Aufrufs (nicht im Start-cwd); quest start, Briefing und Deny-Text nennen Scope, Regeln und den Weg; gate merge verweigert bei rotem develop und meldet einen roten develop-Lauf nach dem Merge"
estimate: { session_pct: 30, weekly_pct: 6, gold_usd: 0 }
---
# Monarch – Regeln dort, wo sie gebraucht werden

Anlass (28.10.): monarch-b4 kannte die Scope-Regel nicht vorab (Memory „monarch-guards-before-acting“); die vorgeschriebenen Wege `gate_plan`/`knowledge_write` scheitern, weil der MCP-Server im Start-cwd läuft (F4); ein Merge lief bei nicht grünem develop. Budget ≤ 6 pp Woche, eine Warden-Runde je Phase (projects-74).
