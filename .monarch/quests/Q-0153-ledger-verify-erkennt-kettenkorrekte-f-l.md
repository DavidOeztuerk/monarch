---
id: Q-0153
gate: G-0111
kind: main
rank: A
state: in_progress
dod:
  - docs/adr/0018-ledger-historie-im-verify.md in einfacher Sprache: was verify heute prüft, was neu dazukommt, welche Grenzen bleiben (Klon ohne Anker, History-Rewrite), welche legitimen Abläufe ok bleiben
  - Der Ansatz deckt die Fälle K-delete-last und K-append-chained aus Issue 71 ab und lockert keine Schutzregel
  - Warden-Designrunde mit höchstens einer Delta-Runde; nur major-Befunde werden vor der Umsetzung eingearbeitet, minor werden Issues
scope:
  include: ["docs/adr/0018-ledger-historie-im-verify.md"]
  exclude: ["src/**", "tests/**", ".github/**", "bench/**"]
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "Warden-Verdict approve über ADR-0018"
---
# ledger verify erkennt kettenkorrekte Fälschung in einem normalen Commit (Issue 71)
