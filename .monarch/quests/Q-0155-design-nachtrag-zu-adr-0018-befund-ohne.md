---
id: Q-0155
gate: G-0112
kind: main
rank: A
state: in_review
dod:
  - Nachtrag in docs/adr/0018-ledger-historie-im-verify.md in einfacher Sprache: Trick B (git remote remove origin) ergibt einen Befund statt still auszulassen, mit enger Bedingung gegen Falsch-Positive; der G-0111-Test, der das Auslassen festhielt, wird nach Owner-Entscheid vom 14.11. strenger, das steht offen im ADR
  - Owner-Entscheide in ADR-0018, ADR-0010, ADR-0011 (G3) und plugin/README.md wörtlich korrigiert: update-ref entschieden am 13.11., set-url plus fetch entschieden am 14.11., git fetch . HEAD:refs/remotes/origin/develop nur als Vorschlag (in
  - Warden-Designrunde mit höchstens einer Delta-Runde
scope:
  include: ["docs/adr/0018-ledger-historie-im-verify.md", "docs/adr/0010-grenzen-der-lokalen-ledger-verankerung.md", "docs/adr/0011-bedrohungsmodell-phase-0.md", "plugin/README.md"]
  exclude: ["src/**", "tests/**", ".github/**", "bench/**"]
estimate: { session_pct: 12, weekly_pct: 3, gold_usd: 0 }
boss: "Warden-Verdict approve über den Nachtrag und Owner hat den Diff der Entscheid-Stellen gesehen"
---
# Design: Nachtrag zu ADR-0018 (Befund ohne origin und Basis) und Korrektur der Owner-Entscheide
