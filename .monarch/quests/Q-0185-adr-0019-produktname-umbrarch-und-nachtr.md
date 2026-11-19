---
id: Q-0185
gate: G-0127
kind: main
rank: A
state: in_review
dod:
  - "ADR-0019 enthält den Owner-Entscheid vom 19.11.2026 wörtlich und den Markencheck (TMview 19.11.2026 ohne Treffer zu UMBRARCH)"
  - "Bestandsaufnahme als Tabelle (Datei, Fundstelle, jetzt oder v0.2) aus git grep -n -i monarch ohne .monarch/ledger, mit Befehl und Commit"
  - "Design der Schutzregeln in einfacher Sprache: ShadowReadOnly schneidet das Präfix bis zum letzten Doppelpunkt immer ab; Warden-Nachweis nur umbrarch:warden und warden"
  - "Nachtrag in ADR-0006: Namensprüfung erledigt, Repo öffentlich"
  - "Warden-Designrunde vor dem ersten Code-Commit (heikle Quest)"
scope:
  include: ["docs/adr/0019-produktname-umbrarch.md", "docs/adr/0006-lizenz-und-id-bereiche.md"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1, gold_usd: 0 }
boss: "Kritiker findet keine unbelegte Behauptung; Warden approve zum Design"
board: { pr: 154 }
---
# ADR-0019 Produktname Umbrarch und Nachtrag ADR-0006
