---
id: Q-0136
gate: G-0104
kind: main
rank: A
state: abandoned
dod:
  - Design zuerst: docs/adr/0015-relatives-latenz-gate.md beschreibt den A/B-Vergleich PR-Binary gegen develop-Binary auf demselben Runner (verschränkte Messung, Schwelle aus gemessenen A/A-Läufen mit Zahlen und Run-IDs, u. a. Run 41038797796) und zeigt, dass echte Regressionen weiter auffallen; p99 <= 20 ms bleibt das Kriterium auf dem echten Mac (MVP-Gate C1) -> eine Warden-Runde (Design) vor TDD
  - bench/test-gate.sh erweitert, vorher rot, danach grün: Fehler-Injektion (synthetische Regression auf den Samples des PR-Binaries) -> A/B-Gate rot; A/A mit gleicher Verteilung und Einzelausreißern -> grün; die bestehenden Prüfungen (p95 besteht, p99 fällt) bleiben unverändert
  - ci.yml: A/B-Schritt blockierend auf Linux und macOS, auch für den Wrapper-Modus; die macOS-Quarantäne (ci.yml:81-88) entfällt nur, wenn der A/B-Schritt den Wrapper-Modus abdeckt; LIMIT_MS wird nicht angehoben, absolute p50/p95/p99 stehen weiter in Log und Job-Summary
  - Beleg: CI-Lauf auf dem Gate-Branch mit A/B-Gate (Run-ID und Zahlen in ADR-0015 oder PR); ADR-0011 Regel 9 und der ci.yml-Kommentar (Z. 50-52) verweisen auf ADR-0015
  - Warden-Runde über den Code mit Verdict am Head-SHA; kein Test und keine Schwelle aufgeweicht
  - Abhängigkeit: Start nach Q-0133 bis Q-0135 (Owner-Reihenfolge)
scope:
  include: [".github/workflows/ci.yml", "bench/**", "docs/adr/0015-relatives-latenz-gate.md", "docs/adr/0011-bedrohungsmodell-phase-0.md"]
  exclude: ["src/**"]
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "bench/test-gate.sh: injizierte Regression -> A/B-Gate rot, A/A-Rauschen -> grün; der CI-Lauf auf dem Gate-Branch gatet PR gegen develop auf demselben Runner"
---
# CI-Latenz: relatives A/B-Gate PR gegen develop (#8)
