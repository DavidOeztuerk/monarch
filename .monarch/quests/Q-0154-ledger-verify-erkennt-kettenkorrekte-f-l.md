---
id: Q-0154
gate: G-0111
kind: main
rank: A
state: in_progress
dod:
  - Umsetzung nach dem freigegebenen ADR-0018; ADR-0010, ADR-0011 (G3) und plugin/README.md (Grenzen) nennen die verbleibenden Grenzen gleichlautend
  - Die zwei Fälle aus Issue 71 als Tests, vorher rot, danach grün: K-delete-last (letzte Zeile einer getrackten fremden Datei gelöscht und normal committet) und K-append-chained (kettenkorrekte Zeile angehängt und normal committet) melden einen Befund und Exit 1
  - Gegenproben bleiben grün: die schon erkannten Fälle (K-change, K-delete-mid, nicht committete Änderungen, gelöschte Datei) melden weiter ihren Befund; legitime Abläufe bleiben ok (eigene Commits der Worktree, Merge von develop, neue Worktree auf bestehendem Branch, frischer Klon); die Grenze Klon ohne Anker bleibt als erwartetes nicht erkennbar im Test festgehalten
  - Keine Schutzregel gelockert: kein bestehender Test, keine Schwelle, kein Hook und keine Policy aufgeweicht; Git läuft im Append-Pfad höchstens einmal je Worktree
  - Heikle Quest nach Standardregel 3: Umsetzung durch knight, Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Infrastructure.Ledger/**", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/ManaCommands.cs", "src/Monarch.Cli/GateMergeCommands.cs", "tests/Monarch.Infrastructure.Tests/**", "tests/Monarch.Cli.Tests/**", "docs/adr/0018-ledger-historie-im-verify.md", "docs/adr/0010-grenzen-der-lokalen-ledger-verankerung.md", "docs/adr/0011-bedrohungsmodell-phase-0.md", "plugin/README.md", "docs/lessons.md"]
  exclude: [".github/**", "bench/**", "plugin/hooks/**"]
estimate: { session_pct: 35, weekly_pct: 8, gold_usd: 0 }
boss: "dotnet test grün mit den neuen Fällen (vorher rot), CI auf dem Gate-Branch grün auf beiden Betriebssystemen, und ein Nachlauf der Fälle K-delete-last und K-append-chained mit dem gebauten Binary in einer Kopie eines frischen Klons, Ausgabe im PR"
---
# ledger verify erkennt kettenkorrekte Fälschung in einem normalen Commit (Issue 71)
