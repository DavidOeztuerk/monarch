---
id: Q-0156
gate: G-0112
kind: main
rank: A
state: cleared
dod:
  - Umsetzung nach dem freigegebenen Nachtrag zu ADR-0018, TDD: neue Tests vorher rot, danach grün
  - Trick B als Test: Checkout ohne Start-Commit, Anker mit Einträgen, fremde Ledger-Historie, git remote remove origin, gefälschte Zeile committet → Befund
  - Gegenproben grün: frischer Klon ohne Anker bleibt ok, Repository ohne fremde Ledger-Historie bleibt ok, Checkout mit Start-Commit bleibt wie bisher
  - Kein bestehender Test aufgeweicht; einzig A_repository_without_remote_and_without_base_skips_the_history_check wird nach Owner-Entscheid vom 14.11. strenger (Befund statt ok) und umbenannt; jede andere Teständerung ist ein Stopp
  - Umsetzung durch knight, Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Infrastructure.Ledger/**", "tests/Monarch.Infrastructure.Tests/**", "tests/Monarch.Cli.Tests/**", "docs/adr/0018-ledger-historie-im-verify.md"]
  exclude: [".github/**", "bench/**", "plugin/hooks/**"]
estimate: { session_pct: 25, weekly_pct: 6, gold_usd: 0 }
boss: "dotnet test grün mit den neuen Fällen (vorher rot), CI auf dem Gate-Branch grün auf beiden Betriebssystemen"
board: { pr: 101 }
---
# verify meldet Befund ohne origin und Start-Commit bei fremder Ledger-Historie (Trick B)
