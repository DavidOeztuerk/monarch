---
id: Q-0183
gate: G-0124
kind: main
rank: A
state: cleared
dod:
  - Test: Gate mit Quest in proposed wird von gate merge verweigert; vorher rot, danach gruen
  - Gate mit Quests in in_review bleibt erlaubt; bestehende Merge-Tests gruen
  - dotnet test Monarch.Cli.Tests gruen; Warden-Schlussrunde mit Verdict am Head
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "tests/Monarch.Cli.Tests/GateMergeTests.cs"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.2, gold_usd: 0 }
board: { pr: 143 }
---
# gate merge verweigert Gates mit Quests unter in_review (Schutzluecke, Issue 142, Ersatz fuer abandoned Q-0179)
