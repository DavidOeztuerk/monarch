---
id: Q-0113
gate: G-0101
kind: main
rank: A
severity: high
risk_tags: [policy]
state: cleared
scope:
  repo: monarch
  include: ["src/Monarch.Policy/**", "src/Monarch.Cli/**", "src/Monarch.Infrastructure.Evidence/**", "src/Monarch.Domain/**", "tests/**", "docs/adr/0007-*", "plugin/README.md", "plugin/skills/**"]
  exclude: []
estimate: { session_pct: 18, weekly_pct: 4, gold_usd: 0 }
dod:
  - ADR-0007 vom Warden (Design-Phase) freigegeben, bevor Code entsteht
  - system.yaml git.protected_branches (Default main, master; Datei kann nur ergänzen) und git.integration_branch (Default develop)
  - PreToolUse verweigert git push auf geschützte Branches in jeder Schreibweise (Refspec, HEAD:main, +main, :main, --delete, --all, --mirror, Push ohne Refspec auf geschütztem aktuellen Branch), gh pr merge, gh api …/pulls/*/merge und …/merges, GraphQL mergePullRequest/enablePullRequestAutoMerge/mergeBranch
  - monarch gate verdict <G> und monarch gate merge <G> prüfen Base = Integrations-Branch, Rank (S verweigert), CI grün am Head-SHA, Warden-Verdict zum Head-SHA plus ShadowRunCompleted eines Wardens; Merge mit sha-Bindung, GateMerged mit Merge-Commit
  - Tests rot → grün je Form; Latenz p99 ≤ 20 ms lokal
boss: "PreToolUse verweigert git push origin HEAD:main und gh pr merge; monarch gate merge verweigert ohne Warden-Verdict zum Head-SHA"
board: { pr: 15 }
---
# Merge-Policy nach Zielbranch

Siehe ADR-0007. Claude merged nur in den Integrations-Branch; geschützte Branches (main/master) bleiben dem Owner.
