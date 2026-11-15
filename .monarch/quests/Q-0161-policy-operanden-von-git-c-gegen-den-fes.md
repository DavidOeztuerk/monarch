---
id: Q-0161
gate: G-0115
kind: main
rank: A
state: proposed
dod:
  - Umsetzung nach ADR-0019, TDD: die roten Tests aus Q-0160 werden gruen
  - Gegenproben gruen, kein bestehender Test geaendert oder aufgeweicht, Hook-Latenz-Gates unveraendert gruen
  - plugin/README.md Abschnitt Grenzen bzw. Fehlalarme nachgezogen
  - Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Policy/**", "tests/Monarch.Policy.Tests/**", "docs/adr/0019-fehlalarme-ohne-lockerung-iii.md", "plugin/README.md"]
  exclude: [".github/**", "bench/**"]
estimate: { session_pct: 20, weekly_pct: 5, gold_usd: 0 }
boss: "dotnet test gruen mit den neuen Faellen (vorher rot), CI auf dem Gate-Branch gruen auf beiden Betriebssystemen"
---
# Policy: Operanden von git -C gegen den festen Ordner aufloesen (Issue 105)
