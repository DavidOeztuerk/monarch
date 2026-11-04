---
id: Q-0138
gate: G-0105
kind: main
rank: A
state: in_review
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0014-fehlalarme-ohne-lockerung.md (zu 1b/1c :42-61 und Warum keine Schutzregel fällt :170-180): Ursache von Issue 55 mit Datei:Zeile, Owner-Entscheid STRENG vom 30.10., Tabelle aller Wechsel gegenüber d45e6b9 mit Unmöglichkeitsbeweis (Maßstab ADR-0014 1c); eine Warden-Runde, höchstens eine Delta-Runde, Verdict vor dem ersten Code-Commit
  - Allow, Tests vorher rot und danach grün, echte Kommandos aus Issue 55: `sed 's/.*/new/' f` und `sed -e 's/.*/e/' f`; sonst wird nichts frei, die ADR-Tabelle enthält genau diese zwei Wechsel von deny zu allow (Owner-Entscheid STRENG)
  - Deny bleibt und steht im ADR-Nachtrag als bekannter Fehlalarm, je ein Test: `awk '/.*/ {print | "sort"}' f` und `rm -rf dist/.*` (`dist/..` ist die Projekt-Root)
  - Deny bleibt (Regression gegen d45e6b9, jeder neue Fall dort vorher als deny gegengeprüft; ein dort schon erlaubter Fall ist ein eigener Befund, kein Teil dieser Quest): alle Fälle in tests/Monarch.Policy.Tests/Adr0014Tests.cs:55-82 und :148-159 unverändert, neu je ein Test: `rm -rf .?/*` an der Root, `rm -rf dist/.?/.monarch/ledger`, `rm -rf dist/.*/.monarch`, `cp -R x/ledger dist/.?/.monarc?`, `chmod -R 000 dist/.*`, `sed 's/x/y/w s/.?/.monarch/ledger/x' f`, `sed -i 's/a/b/' dist/.?/.monarch/ledger/x`
  - Kein Test gelöscht oder gelockert; TDD vorher rot, danach grün; Hook-Latenz in CI unverändert grün (LIMIT_MS 20); Warden-Runde über den Code mit Verdict am Head-SHA; plugin/README.md Grenzen nachgezogen, falls sich der Text ändert
  - Abhängigkeit: nach Q-0137 (Owner-Reihenfolge), technisch unabhängig
scope:
  include: ["src/Monarch.Policy/ProtectPathsRule.cs", "src/Monarch.Policy/FlatPattern.cs", "src/Monarch.Policy/ToolCallPolicy.cs", "tests/Monarch.Policy.Tests/Adr0014*.cs", "docs/adr/0014-fehlalarme-ohne-lockerung.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 25, weekly_pct: 3.5, gold_usd: 0 }
boss: "Adr0014Tests: `sed 's/.*/new/' f` und `sed -e 's/.*/e/' f` -> allow; `awk '/.*/ {print | \"sort\"}' f`, `rm -rf dist/.*`, `rm -rf .?/*`, `rm -rf dist/.?/.monarch/ledger`, `sed 's/x/y/w s/.?/.monarch/ledger/x' f` und alle bisherigen Deny-Fälle (:55-82, :148-159) -> deny"
board: { pr: 57 }
---
# Policy: '.*'-Segment ohne Pauschaltreffer (#55), ohne Lockerung
