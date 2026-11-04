---
id: Q-0141
gate: G-0105
kind: main
rank: A
state: abandoned
dod:
  - Design zuerst (einfache Sprache, im PR): docs/adr/0015-relatives-latenz-gate.md beschreibt den A/B-Vergleich PR-Binary gegen Basis-Binary auf demselben Runner, verschränkt gemessen; Basis je Ereignis (pull_request: Base-SHA; push: merge-base mit develop bzw. Vorgänger auf develop); Schwelle aus gemessenen A/A-Läufen mit Zahlen und Run-IDs (u. a. Run 41038797796: Versuch 1 rot mit p50 10-12 ms statt ~7, Rerun grün); Nachweis, dass echte Regressionen auffallen; p99 <= 20 ms bleibt Kriterium auf dem echten Mac (MVP-Gate C1); permissions bleiben contents: read (ci.yml:8-10), kein Artefakt-Download aus fremden Läufen; beide Varianten zum absoluten CI-Gate beschrieben (Owner-Entscheid). Eine Warden-Runde + höchstens eine Delta-Runde
  - bench/test-gate.sh erweitert, vorher rot, danach grün: Fehler-Injektion (synthetische Regression auf den PR-Samples) -> A/B rot; A/A mit gleicher Verteilung und Einzelausreißern -> grün; bestehende Prüfungen (p95 besteht, p99 fällt, Ausgabe mit p95-Spalte) unverändert
  - Echte Fehler-Injektion: das PR-Binary einmal hinter einem Verzögerungs-Wrapper (nur im Bench, kein src-Code) -> A/B rot in einem CI-Lauf; Run-ID im ADR
  - ci.yml: A/B-Schritt blockierend auf Linux und macOS, auch im Wrapper-Modus; die macOS-Quarantäne (Kommentar :81-86, continue-on-error :88) entfällt nur, wenn A/B den Wrapper-Modus blockierend abdeckt; LIMIT_MS (:53) bleibt 20; absolute p50/p95/p99 weiter in Log und Job-Summary; der absolute Direkt-Schritt (:79-80) bleibt blockierend, außer der Owner entscheidet anders (Lockerung); cancel-in-progress (:13-14) gilt nicht mehr für Pushes auf develop, damit jeder develop-Head einen Lauf hat
  - Beleg: grüner CI-Lauf auf dem Gate-Branch mit A/B-Gate auf beiden OS (Run-ID und Zahlen in ADR-0015 oder PR); ADR-0011 Regel 9 (:70) und ci.yml-Kommentar (:50-52) verweisen auf ADR-0015
  - Kein Test und keine Schwelle aufgeweicht; Warden-Runde über den Code mit Verdict am Head-SHA
  - Abhängigkeit: nach Q-0137 bis Q-0140 (Owner-Reihenfolge), technisch unabhängig
scope:
  include: [".github/workflows/ci.yml", "bench/**", "docs/adr/0015-relatives-latenz-gate.md", "docs/adr/0011-bedrohungsmodell-phase-0.md"]
  exclude: ["src/**"]
estimate: { session_pct: 28, weekly_pct: 4, gold_usd: 0 }
boss: "bench/test-gate.sh: injizierte Regression -> A/B rot, A/A-Rauschen mit Ausreißern -> grün; der CI-Lauf auf dem Gate-Branch gatet PR- gegen Basis-Binary auf demselben Runner (beide OS, auch Wrapper)"
---
# CI-Latenz: relatives A/B-Gate PR gegen Basis auf demselben Runner (#8)
