---
id: Q-0130
gate: G-0101
kind: main
rank: A
severity: high
risk_tags: [policy]
state: cleared
scope:
  repo: monarch
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Policy/ProtectBranchesRule.cs", "tests/Monarch.Cli.Tests/**", "tests/Monarch.Policy.Tests/**", "docs/adr/0007-*", "docs/adr/0008-*", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1, gold_usd: 0 }
dod:
  - Blocker Code-Phase Warden G-0101 (B1): gate merge verweigert eine Base aus protected_branches (case-insensitiv) vor dem Merge und nach dem Merge; integration_branch in protected_branches wird beim Einlesen der Config abgelehnt
  - F6: monarch wake fire ist für Shadows gesperrt (GateOnlyMain)
  - Tests rot → grün (integration_branch main, Base main, wake fire Shadow)
boss: "system.yaml git.integration_branch: main → Config-Fehler; gate merge mit PR-Base main → verweigert, kein PUT"
board: { pr: 15 }
---
# gate merge nie in einen geschützten Branch

Blocker aus der Code-Phase des Wardens G-0101 (GateMergeCommands.cs:108-111, :146) plus F6.
