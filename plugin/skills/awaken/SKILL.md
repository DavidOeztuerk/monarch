---
name: awaken
description: Richtet Monarch im aktuellen Repository ein (Repo-Identitaet, Plan, Risk-Tags). Verwenden bei /awaken, 'Monarch einrichten', 'Repo initialisieren' oder wenn .monarch/ fehlt.
---

# Awaken

1. Frage den Nutzer genau einmal nach seinem Claude-Plan (Pro, Max5, Max20, API-Key). Keine Rueckfrage danach.
2. Lege Risk-Tags fest (Pfade/Bereiche mit erhoehtem Risiko, z.B. auth, migrations, infra). Schlage sie aus der Repo-Struktur vor, lass sie bestaetigen.
3. Fuehre aus: `~/.monarch/bin/monarch awaken --plan <plan> --risk-tags <tag1,tag2>`
4. Gib die Ausgabe zur Repo-Identitaet (Name, Remote, Hash) unveraendert wieder; erfinde nichts dazu.
5. Weise darauf hin: Hooks und Statusline werden erst nach einem Neustart von Claude Code aktiv. Statusline-Verdrahtung (`refreshInterval: 30`) steht in der Plugin-README.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel nach Rank: E-C nur per Squash nach gruener CI und unabhaengigem Warden; B-A PR oeffnen, niemals selbst mergen; S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
