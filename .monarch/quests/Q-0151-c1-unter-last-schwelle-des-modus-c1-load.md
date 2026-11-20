---
id: Q-0151
gate: G-0110
kind: main
rank: A
state: in_review
dod:
  - Nachtrag zu ADR-0015 in einfacher Sprache: alte Schwelle 1,64, neue 1,70, Begründung Owner-Entscheid vom 12.11.2026 (Option 2), ausdrücklich als Lockerung benannt, Messgrundlage Abendmessung vom 12.11. (evidence/teil-c/latenz-2026-11-12/)
  - bench/test-gate.sh prüft für c1-load die Schwelle 1,70: vorher rot, nach der Änderung grün, deterministisch über BENCH_RESULTS_DIR
  - bench/hook-latency.sh: nur die Schwelle des Modus c1-load ändert sich von 1.64 auf 1.70. Keine CI-Schwelle, kein anderer Modus, kein anderer Wert, keine Schutzprüfung des Modus ändert sich
  - Texte, die die Zahl nennen (Kommentar in ci.yml, docs/lessons.md), stimmen mit 1,70 überein; die CI-Schritte ändern sich nicht
  - Heikle Quest nach Standardregel 3: eine Warden-Runde über Design und Code mit höchstens einer Delta-Runde, Verdict am Head-SHA
scope:
  include: ["bench/hook-latency.sh", "bench/test-gate.sh", "docs/adr/0015-relatives-latenz-gate.md", "docs/lessons.md", ".github/workflows/ci.yml"]
  exclude: ["src/**"]
estimate: { session_pct: 12, weekly_pct: 2, gold_usd: 0 }
boss: "bench/test-gate.sh grün mit dem Fall für 1,70 (vorher rot), CI auf dem Gate-Branch grün auf beiden Betriebssystemen"
board: { pr: 91 }
---
# C1 unter Last: Schwelle des Modus c1-load von 1,64 auf 1,70
