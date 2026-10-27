# ADR-0006 — Apache-2.0, privat bis zur Namensprüfung; eigene ID-Bereiche

**Datum:** 27.10.2026 · **Status:** angenommen

## Lizenz

Das Konzept nennt in `plugin.json` `"license": "MIT"`, in Entscheidung D4 aber
Apache-2.0. Es gilt **Apache-2.0** (D4 und Vorgabe des Owners, Patent-Grant). Das Repo
`DavidOeztuerk/monarch` ist **privat**, bis Name und Marke geprüft sind (D1).

## Gates und Quests von Monarch selbst

Monarch wird mit Monarchs Regeln gebaut: ein Gate = ein Branch `monarch/G-xxxx-<slug>`
= ein PR, jede Quest mindestens ein Commit mit Quest-ID.

- IDs im Monarch-Repo beginnen bei **G-0100 / Q-0100**. So bleiben sie in Berichten
  von den Beweis-Backlogs (G-0001–G-0013, Q-0001–Q-0041) unterscheidbar.
- **G-0100 „Phase 0 – Awakening“ hat Rank A.** Die Policy-Engine und die
  Ledger-Integrität sind Security-Code. Der PR wird geöffnet, nicht gemergt; der
  Owner gibt ihn frei. Installiert wird bis dahin über einen lokalen Marketplace auf
  den Checkout.

| Quest | Inhalt (Auftrag) |
|---|---|
| Q-0100 | A0 Prüfpunkte → ADR-0001…0005 |
| Q-0101 | A1 Repo-Gerüst |
| Q-0102 | A2 Domain + Policy (TDD, Property-Tests) |
| Q-0103 | A1 Infrastructure: JSONL-Ledger mit Hash-Kette, SQLite-Read-Model, Boards, Evidence |
| Q-0104 | A3 CLI-Modi (hook, statusline, mcp, Commands) |
| Q-0105 | A4 Plugin + lokaler Marketplace |
| Q-0106 | A5 CI, AOT-Publish, Hook-Latenz-Gate, Ledger-Manipulationstest |
