---
id: Q-0181
gate: G-0124
kind: main
rank: C
state: cleared
dod:
  - Test mit DoD-Texten, die mit Raute beginnen, Raute nach Leerzeichen, Doppelpunkt und Backtick enthalten, liest den vollstaendigen Text zurueck; vorher rot
  - DoD-Eintraege im Quest-Schreiber sind doppelt gequotet; danach gruen
  - dotnet test Monarch.Infrastructure.Tests gruen
scope:
  include: ["src/Monarch.Infrastructure.Boards/LocalFileBoard.cs", "tests/Monarch.Infrastructure.Tests/LocalFileBoardTests.cs"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1.2, gold_usd: 0 }
board: { pr: 143 }
---
# DoD-Texte im Quest-Schreiber quoten; Regressionstest (Issue 50, Ersatz fuer abandoned Q-0176)
