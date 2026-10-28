---
id: Q-0129
gate: G-0101
kind: main
rank: A
severity: medium
risk_tags: [policy]
state: in_review
scope:
  repo: monarch
  include: ["src/Monarch.Policy/ProtectBranchesRule.cs", "tests/Monarch.Policy.Tests/**", "docs/adr/0008-*", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 3, weekly_pct: 1, gold_usd: 0 }
dod:
  - PreToolUse verweigert `monarch wake schedule|cancel` für Shadows (agent_type gesetzt), wie `gate merge|verdict` (Design-Auflage Warden G-0101 zu ADR-0008)
  - Tests rot → grün (Shadow deny, Main allow, auch über ~/.monarch/bin/monarch und bash -c)
  - ADR-0008 Rechte-Abschnitt und README ohne „Folge-Quest“-Vermerk
boss: "Shadow mit agent_type 'monarch:knight' → deny für 'monarch wake schedule --session <uuid> --at <iso>'; Main → allow"
board: { pr: 15 }
---
# Wake nur durch den Main (Hook-Regel)

Restauflage aus Q-0128: Die Shadow-Verweigerung braucht eine Hook-Regel in src/Monarch.Policy (Risk-Tag policy, Rank A), außerhalb des Scopes von Q-0128.
