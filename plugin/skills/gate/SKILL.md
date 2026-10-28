---
name: gate
description: Verwaltet Gates (Arbeitspakete aus Issues): importieren, auflisten, anzeigen, mit der Forge synchronisieren. Verwenden bei /gate, 'Issue importieren', 'Gates anzeigen'.
---

# Gate

- `~/.monarch/bin/monarch gate import <ref>`: Issue/Ticket als Gate uebernehmen.
- `~/.monarch/bin/monarch gate list`: alle Gates mit Rank und Status.
- `~/.monarch/bin/monarch gate show <G-xxxx>`: Details, Quests, Boss.
- `~/.monarch/bin/monarch gate sync`: Zustand mit der Forge abgleichen.
- Zerlegung in Quests nur ueber den Agenten `monarch:strategist` auf Basis verifizierter Scout-Funde; Rank kommt aus der Policy, nicht aus Bauchgefuehl.
- Gate-Branch: `monarch/G-xxxx-<slug>`.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel nach Rank: E-C nur per Squash nach gruener CI und unabhaengigem Warden; B-A PR oeffnen, niemals selbst mergen; S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
