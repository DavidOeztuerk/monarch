---
id: Q-0179
gate: G-0124
kind: main
rank: A
state: abandoned
dod:
  - Test: Gate mit Quest in proposed wird von gate merge verweigert; vorher rot, danach gruen
  - Gate mit Quest in in_review oder cleared bleibt erlaubt; Test
  - dotnet test -c Release gruen; Warden-Schlussrunde mit Verdict am Head
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.2, gold_usd: 0 }
---
# gate merge verweigert Gates mit Quests unter in_review (Schutzluecke, Issue 142)
