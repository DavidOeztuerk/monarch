---
id: Q-0138
gate: G-0105
kind: main
rank: A
state: proposed
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0014-fehlalarme-ohne-lockerung.md (zu 1b/1c :42-61 und 'Warum keine Schutzregel fällt' :170-180): Ursache
  - Allow, Tests vorher rot und danach grün, echte Kommandos aus
  - `rm -rf dist/.*` ist Owner-Entscheid: `dist/..` ist die Projekt-Root, das literale `rm -rf dist/..` ist heute deny (Root als Vorfahr bei destruktiven Befehlen). Ohne Owner-Freigabe bleibt es deny (Test) und steht im ADR als bekannter Fehlalarm; mit Freigabe nur über den Beweis 'rm/rmdir entfernen . und .. als letzte Komponente nie (POSIX)', begrenzt auf rm/rmdir und eine letzte Komponente
  - Deny bleibt (Regression gegen d45e6b9, jeder neue Fall dort vorher als deny gegengeprüft; ein dort schon erlaubter Fall ist ein eigener Befund, kein Teil dieser Quest): alle Fälle in tests/Monarch.Policy.Tests/Adr0014Tests.cs:55-82 und :148-159 unverändert, neu je ein Test: `rm -rf .?/*` an der Root, `rm -rf dist/.?/.monarch/ledger`, `rm -rf dist/.*/.monarch`, `cp -R x/ledger dist/.?/.monarc?`, `chmod -R 000 dist/.*`, `sed 's/x/y/w s/.?/.monarch/ledger/x' f`, `sed -i 's/a/b/' dist/.?/.monarch/ledger/x`; jeder deny->allow-Wechsel gegenüber d45e6b9 steht mit Unmöglichkeitsbeweis (Maßstab ADR-0014 1c) in der ADR-Tabelle, der Owner bestätigt die Tabelle
  - Kein Test gelöscht oder gelockert; TDD vorher rot, danach grün; Hook-Latenz in CI unverändert grün (LIMIT_MS 20); Warden-Runde über den Code mit Verdict am Head-SHA; plugin/README.md 'Grenzen' nachgezogen, falls sich der Text ändert
  - Abhängigkeit: nach Q-0137 (Owner-Reihenfolge), technisch unabhängig
scope:
  include: ["src/Monarch.Policy/ProtectPathsRule.cs", "src/Monarch.Policy/FlatPattern.cs", "src/Monarch.Policy/ToolCallPolicy.cs", "tests/Monarch.Policy.Tests/Adr0014*.cs", "docs/adr/0014-fehlalarme-ohne-lockerung.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 25, weekly_pct: 3.5, gold_usd: 0 }
boss: "Adr0014Tests: `sed 's/.*/new/' f`, `sed -e 's/.*/e/' f`, `awk '/.*/ {print | \"sort\"}' f` -> allow; `rm -rf .?/*`, `rm -rf dist/.?/.monarch/ledger`, `sed 's/x/y/w s/.?/.monarch/ledger/x' f` und alle bisherigen Deny-Fälle (:55-82, :148-159) -> deny"
---
# Policy: '.*'-Segment ohne Pauschaltreffer (#55), ohne Lockerung
