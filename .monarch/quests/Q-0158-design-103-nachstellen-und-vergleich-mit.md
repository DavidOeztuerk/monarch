---
id: Q-0158
gate: G-0114
kind: main
rank: A
state: in_review
dod:
  - 
  - Nachtrag zu ADR-0010 Punkt 3 in einfacher Sprache: Vergleich mit HEAD byteweise gegen den HEAD-Blob statt ueber git diff (Index), Begruendung, Grenzen, Kosten, Tests
  - Warden-Designrunde mit hoechstens einer Delta-Runde
scope:
  include: ["docs/adr/0010-grenzen-der-lokalen-ledger-verankerung.md", "tests/Monarch.Infrastructure.Tests/LedgerIndexTrustTests.cs"]
  exclude: ["src/**", ".github/**", "bench/**", "plugin/**"]
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
boss: "Warden-Designrunde approve ueber den Nachtrag; roter Test belegt #103"
---
# Design: #103 nachstellen und Vergleich mit HEAD ohne Index (Nachtrag ADR-0010)
