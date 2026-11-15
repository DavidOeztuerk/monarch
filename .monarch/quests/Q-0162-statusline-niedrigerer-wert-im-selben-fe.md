---
id: Q-0162
gate: G-0116
kind: main
rank: E
state: in_review
dod:
  - Maximum-Regel bleibt: hoehere Nutzung oder spaeteres resets_at gewinnt wie bisher (bestehende StatuslineTests unveraendert gruen)
  - Niedrigerer Wochenwert bei gleichem resets_at wird uebernommen, wenn zwei frische Snapshots derselben Session mindestens 10 Minuten auseinander liegen, der spaetere nicht unter dem ersten und unter dem Maximum liegt; uebernommen wird der spaetere Wert
  - Frisch heisst: der Snapshot hat ein 5h-Fenster, und es ist mindestens so neu wie das gespeicherte (spaeteres resets_at oder gleiches mit mindestens gleicher Nutzung); fehlt das 5h-Fenster, zaehlt der Snapshot nicht
  - Bei jeder Uebernahme ein Ledger-Ereignis mit altem und neuem Wert im Session-Ledger des Projekts; ohne erwecktes Projekt nur in mana.json
  - Rohwert je Fenster wird mitgespeichert und in monarch mana angezeigt
  - TDD: neue Tests vorher rot, danach gruen; kein bestehender Test aufgeweicht
scope:
  include: ["src/Monarch.Cli/Statusline.cs", "src/Monarch.Cli/Dtos.cs", "src/Monarch.Cli/ManaCommands.cs", "src/Monarch.Cli/ManaStore.cs", "src/Monarch.Cli/State.cs", "tests/Monarch.Cli.Tests/**"]
  exclude: [".github/**", "bench/**", "plugin/hooks/**", "src/Monarch.Policy/**"]
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "dotnet test gruen mit den neuen Faellen (vorher rot), CI auf dem Gate-Branch gruen auf beiden Betriebssystemen, Warden-Schlussrunde approve"
board: { pr: 114 }
---
# Statusline: niedrigerer Wert im selben Fenster nach zwei frischen Snapshots uebernehmen, Rohwert speichern (Issue 113)
