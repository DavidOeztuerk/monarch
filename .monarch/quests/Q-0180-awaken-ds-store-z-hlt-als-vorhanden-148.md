---
id: Q-0180
gate: G-0126
kind: main
rank: D
state: cleared
dod:
  - "/.DS_Store gilt nicht als vorhanden (matcht nur im Repo-Root); awaken ergänzt dann .DS_Store"
  - ".DS_Store und **/.DS_Store gelten als vorhanden, awaken ergänzt nichts doppelt"
  - "Test vorher rot, danach grün"
scope:
  include: ["src/Monarch.Cli/AwakenCommand.cs", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 2, weekly_pct: 0.5, gold_usd: 0 }
boss: "Finder-Dateien unter .monarch/ werden ignoriert"
board: { pr: 150 }
---
# awaken: /.DS_Store zählt als vorhanden (#148)
