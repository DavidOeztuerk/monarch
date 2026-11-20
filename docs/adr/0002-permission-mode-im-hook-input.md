# ADR-0002 — Permission-Mode wird je Tool-Call gelesen, nie beim Sessionstart

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Prüfpunkt 2 („Enthält der Hook-Input den Permission-Mode? Nötig für
`refuse_high_rank_in_bypass_mode`.“)

## Gemessen

Claude Code 2.1.311, dieselbe Probe wie ADR-0001 (`--permission-mode default`):

| Event | `permission_mode` |
|---|---|
| `SessionStart` | **leer** |
| `PreToolUse`, `PostToolUse`, `SubagentStop`, `Stop` | `"default"` |

Die Doku nennt die Werte `default`, `plan`, `acceptEdits`, `auto`, `dontAsk`,
`bypassPermissions` (der Modus „Manual“ kommt als `default`).

## Entscheidung

1. `refuse_high_rank_in_bypass_mode` wird **in `PreToolUse` je Call** ausgewertet,
   nicht beim Sessionstart: dort ist das Feld leer, und der Modus kann während der
   Session wechseln (Shift+Tab).
2. Bei aktiver Quest mit Rank ≥ A und `permission_mode` ∈ {`bypassPermissions`,
   `dontAsk`} verweigert der Hook mutierende Tools (`deny` mit Begründung). Bei
   Rank S gilt das zusätzlich für `auto` und `acceptEdits`.
3. Ein fehlendes oder unbekanntes `permission_mode` wird wie `default` behandelt
   und im Ledger als `policy.unknown_mode` gebucht – nie als Freibrief.

## Folgen

`SessionStart` kann den Modus nicht im Briefing nennen; die Statusline zeigt ihn
nicht. Das ist eine bewusste Lücke, kein Fehler.
