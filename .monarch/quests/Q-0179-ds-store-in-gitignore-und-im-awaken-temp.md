---
id: Q-0179
gate: G-0125
kind: side
rank: E
state: cleared
dod:
  - "Repo-.gitignore enthält .DS_Store"
  - "awaken ergänzt .DS_Store, wenn es fehlt; Test rot vorher, grün nachher"
  - "ledger verify bleibt unverändert streng"
scope:
  include: [".gitignore", "src/Monarch.Cli/AwakenCommand.cs", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 1, weekly_pct: 0.5, gold_usd: 0 }
boss: "Finder-Datei wird nicht mehr mit committet"
board: { pr: 146 }
---
# .DS_Store in .gitignore und im awaken-Template (#100)
