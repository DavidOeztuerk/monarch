---
name: gate
description: Verwaltet Gates (Arbeitspakete aus Issues): importieren, auflisten, anzeigen, mit der Forge synchronisieren. Verwenden bei /gate, 'Issue importieren', 'Gates anzeigen'.
---

# Gate

- `~/.monarch/bin/monarch gate import <ref>`: Issue/Ticket als Gate uebernehmen.
- `~/.monarch/bin/monarch gate list`: alle Gates mit Rank und Status.
- `~/.monarch/bin/monarch gate show <G-xxxx>`: Details, Quests, Boss.
- `~/.monarch/bin/monarch gate sync`: Zustand mit der Forge abgleichen.
- `~/.monarch/bin/monarch gate verdict <G-xxxx> --verdict approve|changes --source <url>`: Verdict des unabhaengigen Wardens zum aktuellen Head-SHA eintragen (nur der Main, nach dem Warden-Lauf).
- `~/.monarch/bin/monarch gate merge <G-xxxx>`: einziger Merge-Weg (nur der Main): prueft ledger verify, genau einen PR, Base = `develop`, Rank (S verweigert), gruene CI wie `quest verify`, gruene CI am Head von `develop` (fehlend, laufend oder abgebrochen verweigert; abgebrochen ist unbelegt, die Meldung nennt Lauf-ID und `gh run rerun`; ist `develop` rot, geht nur ein belegter Fix-PR durch: develop-Head ist Vorfahr des PR-Heads, alle PR-Checks gruen, die auf `develop` roten Checks sind am PR-Head gruen, `GateMerged` haelt `overridden` und `integration_head` fest), Verdict plus Warden-Lauf; dann Squash mit `sha`-Bindung und `GateMerged`. Danach prueft der Befehl den Lauf auf dem Merge-Commit (Default 60 s, Polling 30 s; `IntegrationRunChecked` mit green/red/cancelled/pending; alles ausser green beendet mit Exit 1, der Merge bleibt; der ganze Befehl endet im Default unter 2 min). Laeuft die CI noch: `~/.monarch/bin/monarch gate check <G-xxxx>` (nur der Main) liest das letzte `GateMerged`, prueft den Lauf nach und haengt `IntegrationRunChecked` an. Danach `evidence sync`.
- Zerlegung in Quests nur ueber den Agenten `monarch:strategist` auf Basis verifizierter Scout-Funde; Rank kommt aus der Policy, nicht aus Bauchgefuehl.
- Gate-Branch: `monarch/G-xxxx-<slug>`.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel (ADR-0007): Merge nur nach `develop` (Integrations-Branch) ueber `~/.monarch/bin/monarch gate merge <G-xxxx>`, nie nach main/master (das merged nur der Owner). Rank E-A, nach gruener CI und unabhaengigem Warden-Verdict (`gate verdict`); `gh pr merge` und direkte Pushes auf geschuetzte Branches verweigert der Hook. S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Secret-Scan: gitleaks laeuft ueber den pre-commit-Hook, der Agent ruft es nicht auf. Bricht ein Commit mit einem gitleaks-Fund ab: stoppen und melden, nie `--no-verify`. Meldet `monarch status` keinen aktiven Hook, dem Owner Bescheid sagen.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
