---
id: Q-0181
gate: G-0126
kind: main
rank: A
state: in_review
dod:
  - "Edit, Write, MultiEdit, NotebookEdit auf Worktree-Branch monarch/G-* ohne in_progress-Quest werden verweigert; Deny nennt quest accept und quest start"
  - "Ausnahme nur .monarch/recap/** und .monarch/knowledge/** (wie beim Scope-Lock)"
  - "Tests vorher rot, danach grün; keine Lockerung anderer Regeln"
scope:
  include: ["src/Monarch.Policy/**", "src/Monarch.Cli/State.cs", "tests/Monarch.Policy.Tests/**", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.5, gold_usd: 0 }
boss: "Kein Edit auf einem Gate-Branch vor quest start"
board: { pr: 150 }
---
# Hook verweigert Edit/Write auf Gate-Branch ohne in_progress-Quest (#149, Owner 19.11.)
