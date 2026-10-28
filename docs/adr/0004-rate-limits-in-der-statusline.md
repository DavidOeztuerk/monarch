# ADR-0004 — Mana-Sensor über `rate_limits` der Statusline, mit festem Refresh

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Prüfpunkt 4 („Wie zuverlässig liefert die Statusline `rate_limits`?“)

## Gemessen

- **Lauf 1** (Claude Code 2.1.310, Plan Pro, `monarch-proof/mana-calibration.md`):
  `rate_limits` in **allen** Snapshots vorhanden, nie `stale`. Werte in ganzen
  Prozent, `resets_at` in Epoch-Sekunden. Snapshot nur beim Statusline-Refresh,
  **Verzögerung bis ~2 min** beobachtet.
- **Lauf 2** (2.1.311): unverändert vorhanden (`~/.monarch/mana.json`, 27.10.2026).

Doku (`code.claude.com/docs/en/statusline`): `rate_limits` erscheint „only for
claude.ai Pro and Max subscribers … and only after the first API response in the
session“; jedes Fenster kann unabhängig fehlen. Refresh ereignisgetrieben mit
300 ms Debounce, zusätzlich beim Erreichen von `resets_at` und optional per
`refreshInterval` (Sekunden, Minimum 1). Ereignisse können verstummen, solange die
Main-Session auf Hintergrund-Subagents wartet.

## Entscheidung

1. Der Sensor bleibt die Statusline (`monarch statusline`), die eine vorhandene
   Statusline wrappt und deren Ausgabe unverändert durchreicht.
2. `/awaken` setzt `refreshInterval: 30`, wenn noch keins gesetzt ist. Das schließt
   die 2-Minuten-Lücke bei wartenden Subagents.
3. Fehlt ein Fenster, wird der letzte Snapshot mit Alter weitergeführt
   (`stale_after` 10 min). Ein fehlender Wert ist nie 0 %: der Scheduler rechnet dann
   mit `letzter Wert + Summe der geschätzten Schritte seitdem`.
4. Ohne Abo (API-Key) gibt es kein Mana-Fenster; Monarch arbeitet dann nur mit Gold
   (Budget) und sagt das im Briefing.
