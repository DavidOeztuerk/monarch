---
name: recap
description: Schreibt oder liest Uebergabenotizen (Recap, naechster Schritt, Entscheidungen). Verwenden bei /recap, am Mana-Limit, vor Sitzungsende oder Kontext-Kompaktierung.
---

# Recap

- `~/.monarch/bin/monarch recap`: Stand der Sitzung festhalten.
- `~/.monarch/bin/monarch recap --next "<Schritt>"`: naechsten konkreten Schritt eintragen.
- `~/.monarch/bin/monarch recap --decision "<Entscheidung und Grund>"`: Entscheidung dauerhaft festhalten.
- Inhalt: was ist fertig (mit Evidence), was offen, welche Datei/Zeile als Nächstes. Keine Geheimnisse, keine Vermutungen als Fakten.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel (ADR-0007): Merge nur nach `develop` (Integrations-Branch) ueber `~/.monarch/bin/monarch gate merge <G-xxxx>`, nie nach main/master (das merged nur der Owner). Rank E-A, nach gruener CI und unabhaengigem Warden-Verdict (`gate verdict`); `gh pr merge` und direkte Pushes auf geschuetzte Branches verweigert der Hook. S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
