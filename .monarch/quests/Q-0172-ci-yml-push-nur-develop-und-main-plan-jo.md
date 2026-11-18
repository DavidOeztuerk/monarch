---
id: Q-0172
gate: G-0122
kind: main
rank: A
state: cleared
dod:
  - push-Trigger nur auf develop und main
  - pull_request unverändert
  - bei push und PR nach develop: build-test nur ubuntu-latest, aot nur linux-x64
  - macOS-Jobs (build-test, aot osx-arm64, Latenz-Gate) nur bei PR nach main, workflow_dispatch und schedule
  - schedule-Lauf checkt develop aus
  - actionlint oder YAML-Parse ohne Fehler; bisherige Latenz-Schritte unverändert
  - Kommentar zu C1 nennt den Owner-Entscheid
scope:
  include: [".github/workflows/ci.yml"]
  exclude: []
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
boss: "Ein Push auf develop und ein PR nach develop starten nur die Linux-Jobs; ein PR nach main startet die volle Matrix."
board: { pr: 133 }
---
# ci.yml: push nur develop und main, plan-Job, macOS-Matrix nur bei PR nach main, workflow_dispatch und schedule
