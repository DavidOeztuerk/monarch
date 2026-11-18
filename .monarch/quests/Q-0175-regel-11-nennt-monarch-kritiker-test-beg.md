---
id: Q-0175
gate: G-0123
kind: side
rank: E
state: proposed
dod:
  - Test prüft nur die Zeile von Regel 11 und verlangt monarch:kritiker; vor der Textänderung rot, danach grün
  - regeln/SKILL.md Regel 11 enthält monarch:kritiker
scope:
  include: ["plugin/skills/regeln/SKILL.md", "tests/Monarch.Cli.Tests/KritikerShadowTests.cs"]
  exclude: []
estimate: { session_pct: 2, weekly_pct: 0.5, gold_usd: 0 }
board: { pr: 140 }
---
# Regel 11 nennt monarch:kritiker; Test begrenzt auf die Regel-Zeile (#137)
