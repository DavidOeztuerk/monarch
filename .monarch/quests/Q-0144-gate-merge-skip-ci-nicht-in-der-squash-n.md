---
id: Q-0144
gate: G-0107
kind: main
rank: A
state: in_review
dod:
  - Bei squash setzt gate merge commit_title und commit_message selbst (PR-Titel und Quests, ohne Commit-Liste); Test: [skip ci] in einem Branch-Commit erscheint nicht im PUT.
  - Steht [skip ci] (oder eine von GitHub gleich behandelte Form) im PR-Titel, gelangt es nicht in den PUT; Test.
  - Bei merge_method merge verweigert gate merge vor dem PUT, wenn der Head-Commit des PRs [skip ci] trägt; Test.
  - ci.yml hat workflow_dispatch; die Skills nennen die Falle.
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Infrastructure.Evidence/**", "src/Monarch.Application/Ports/**", "tests/Monarch.Cli.Tests/**", "tests/Monarch.Infrastructure.Tests/**", ".github/workflows/ci.yml", "plugin/skills/**", "docs/adr/0007-merge-policy-nach-zielbranch.md"]
  exclude: []
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
boss: "Ein [skip ci] aus Branch-Commit oder PR-Titel gelangt in die Squash-Nachricht und unterdrückt die CI auf develop."
board: { pr: 77 }
---
# gate merge: [skip ci] nicht in der Squash-Nachricht (#64)
