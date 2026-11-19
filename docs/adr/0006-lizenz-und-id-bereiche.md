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

## Nachtrag 19.11.2026 — Namensprüfung erledigt, Repo öffentlich (Q-0185, ADR-0019)

Auftrag des Owners im Chat, 19.11.2026, wörtlich: „Die Tabelle kommt in ADR-0019 „Produktname Umbrarch“ mit dem Entscheid oben, dazu ein Nachtrag in ADR-0006 (Namensprüfung erledigt, Repo öffentlich).“

- **Namensprüfung erledigt.** Der Produktname ist jetzt Umbrarch (ADR-0019). Owner im Chat, 19.11.2026, wörtlich: „Markencheck: TMview am 19.11.2026 ohne Treffer zu „UMBRARCH“ (ins ADR aufnehmen).“
- **Repo öffentlich.** Das Repo heißt `DavidOeztuerk/umbrarch` und ist öffentlich (`gh repo view DavidOeztuerk/umbrarch --json visibility` am 19.11.2026: `PUBLIC`). Owner im Chat, 19.11.2026, wörtlich: „Das Repo bleibt öffentlich.“
- Die Bedingung oben („privat, bis Name und Marke geprüft sind“) ist damit erfüllt. Der Text oben bleibt als Verlauf stehen.
