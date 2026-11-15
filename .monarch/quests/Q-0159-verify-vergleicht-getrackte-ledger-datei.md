---
id: Q-0159
gate: G-0114
kind: main
rank: A
state: in_review
dod:
  - Umsetzung nach dem Nachtrag zu ADR-0010, TDD: die roten Tests aus Q-0158 werden gruen
  - Gegenproben gruen: gesetzte Index-Kennzeichen ohne Aenderung bleiben ok; unveraenderte committete Datei ohne lokalen Anker bleibt anchored by git; bestehende Tests unveraendert
  - Kein bestehender Test aufgeweicht; Latenz-Gates unveraendert gruen
  - Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Infrastructure.Ledger/**", "tests/Monarch.Infrastructure.Tests/**", "docs/adr/0010-grenzen-der-lokalen-ledger-verankerung.md"]
  exclude: [".github/**", "bench/**", "plugin/hooks/**"]
estimate: { session_pct: 20, weekly_pct: 5, gold_usd: 0 }
boss: "dotnet test gruen mit den neuen Faellen (vorher rot), CI auf dem Gate-Branch gruen auf beiden Betriebssystemen"
---
# verify vergleicht getrackte Ledger-Dateien byteweise mit HEAD (#103)
