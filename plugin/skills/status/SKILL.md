---
name: status
description: Zeigt den Monarch-Zustand (Level, aktive Quest, Gates, Mana, Ledger). Verwenden bei /status, 'wo stehen wir', 'was ist offen'.
---

# Status

1. `~/.monarch/bin/monarch status` (fuer maschinenlesbar `--json`).
2. Fasse in hoechstens 5 Zeilen zusammen: aktives Gate/Quest, Mana, naechster Schritt.
3. Bei Auffaelligkeiten im Ledger: `~/.monarch/bin/monarch ledger verify` ausfuehren und Ergebnis melden.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel nach Rank: E-C nur per Squash nach gruener CI und unabhaengigem Warden; B-A PR oeffnen, niemals selbst mergen; S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
