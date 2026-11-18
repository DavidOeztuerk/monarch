---
id: Q-0174
gate: G-0123
kind: main
rank: A
state: cleared
dod:
  - Test mit agent_type monarch:warden und monarch:scout ist vor dem Fix rot (Deny erwartet, Allow geliefert) und nach dem Fix grün
  - monarch:knight darf weiterhin schreiben; monarch:scribe mit Config-Erweiterung wird verweigert
  - dotnet test tests/Monarch.Policy.Tests grün (1047/1047)
  - Warden-Schlussrunde mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Policy/ToolCallPolicy.cs", "tests/Monarch.Policy.Tests/ToolCallPolicyTests.cs"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1, gold_usd: 0 }
board: { pr: 140 }
---
# Shadow-Schutz erkennt monarch:-Präfix (#136)
