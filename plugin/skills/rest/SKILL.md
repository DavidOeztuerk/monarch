---
name: rest
description: Beendet die Arbeit geordnet (Rest): sichert Stand, schliesst Sitzung ab. Verwenden bei /rest, 'Feierabend', 'Pause machen'.
---

# Rest

1. Laufende Arbeit an einen sicheren Punkt bringen (Tests gruen oder Zustand klar dokumentiert; nichts halb committen).
2. `~/.monarch/bin/monarch recap --next "<Schritt>"`, dann `~/.monarch/bin/monarch rest`.
3. Offene Quests nicht auf verified/cleared setzen; nur Forge-Evidence zaehlt.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel nach Rank: E-C nur per Squash nach gruener CI und unabhaengigem Warden; B-A PR oeffnen, niemals selbst mergen; S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
