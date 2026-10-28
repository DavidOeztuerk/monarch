---
name: quest
description: Fuehrt Quests durch den Lebenszyklus (accept, start, submit, abandon, fail, pr, verify, clear). Verwenden bei /quest, 'Quest starten', 'Quest abgeben', 'PR fuer Quest'.
---

# Quest

- `~/.monarch/bin/monarch quest list` / `quest show <Q-xxxx>`: Uebersicht.
- `~/.monarch/bin/monarch quest accept <Q-xxxx>` -> `quest start <Q-xxxx>` (immer nur eine Quest gleichzeitig, im Quest-Scope).
- Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>`; Commits im Conventional-Commit-Format mit Quest-ID.
- `quest submit <Q-xxxx>` -> `quest pr <Q-xxxx>` -> `evidence sync` (`~/.monarch/bin/monarch evidence sync`).
- `quest verify` / `quest clear` nur mit Forge-Evidence (CI gruen, unabhaengiger Warden bei E-C). Nie ohne.
- `quest abandon` / `quest fail` mit kurzer Begruendung; danach `recap`.
- Implementierung an `monarch:knight` delegieren, Review an `monarch:warden`.

## Grundregeln (gelten immer)

- CLI nur per Bash: `~/.monarch/bin/monarch ...`. Nie raten, was das CLI ausgeben wuerde.
- Quest-Lifecycle: `quest accept` -> `quest start` -> Arbeit auf dem Gate-Branch `monarch/G-xxxx-<slug>` -> `quest submit` -> `quest pr` -> `evidence sync`. `quest verify` und `quest clear` nur auf Basis von Forge-Evidence (CI gruen, Review), nie aus eigener Behauptung.
- Merge-Regel (ADR-0007): Merge nur nach `develop` (Integrations-Branch) ueber `~/.monarch/bin/monarch gate merge <G-xxxx>`, nie nach main/master (das merged nur der Owner). Rank E-A, nach gruener CI und unabhaengigem Warden-Verdict (`gate verdict`); `gh pr merge` und direkte Pushes auf geschuetzte Branches verweigert der Hook. S: jede veraendernde Aktion einzeln beim Nutzer erfragen.
- Mana-Regel: vor jedem Schritt `~/.monarch/bin/monarch mana --estimate`. Am Limit: bis zu einem sicheren Punkt fertig machen, `~/.monarch/bin/monarch recap`, dann stoppen. Reset in weniger als 10 Minuten: ohne Recap pausieren.
- Nie Credential-Dateien lesen (`.env`, Schluessel, Tokens, `~/.ssh`, `~/.aws`, Keychains).
