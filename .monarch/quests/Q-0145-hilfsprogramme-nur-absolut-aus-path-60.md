---
id: Q-0145
gate: G-0107
kind: main
rank: A
state: in_review
dod:
  - git und gh werden absolut aus PATH aufgelöst, ohne cwd, ohne Verzeichnis des Binaries und ohne relative oder leere PATH-Einträge; ein Fehlschlag ist ein klarer Fehler ohne Rückfall.
  - Alle Process.Start-Stellen sind geprüft: Git.cs, MergeMethodResolver.cs, GitAnchor.cs, GitHubTokenProvider.cs; WakeCommands, SecretScanHook und Statusline nutzen bereits absolute Pfade, das steht im ADR.
  - Test mit Stub im cwd und im Binary-Ordner: Der Stub läuft nicht, auch nicht auf dem Hook-Pfad.
  - Heikle Quest: Design-ADR im PR, vom Warden freigegeben vor dem Code.
scope:
  include: ["src/Monarch.Application/**", "src/Monarch.Cli/**", "src/Monarch.Infrastructure.Ledger/**", "tests/**", "docs/adr/**"]
  exclude: []
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
boss: "Ein Stub namens git oder gh im cwd oder im Binary-Ordner wird statt des echten Programms gestartet, auch auf dem Hook-Pfad."
board: { pr: 77 }
---
# Hilfsprogramme nur absolut aus PATH (#60)
