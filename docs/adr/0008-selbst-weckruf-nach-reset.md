# ADR-0008 — Selbst-Weckruf nach `resets_at`

**Datum:** 28.10.2026 · **Status:** vorgeschlagen (Design-Phase Warden G-0101) · **Quest:** Q-0114 (G-0101)

## Problem

Monarch stoppt am Mana-Limit (5h 90 %, 7d 96 %), also **vor** dem echten
Nutzungslimit. Die native Fortsetzung von Claude Code greift aber erst am echten
Limit. Damit bleibt eine Session nach `/rest` liegen, bis jemand sie anstößt.

## Gemessen (Claude Code 2.1.312, 27.10.2026)

| Mechanismus | Verhalten | Eignung |
|---|---|---|
| `CronCreate` (One-Shot) | nur im Speicher der Session („durable: no effect“), feuert nur im Leerlauf, Jitter bis 90 s bei :00/:30 | trägt, solange der Session-Prozess lebt; geht bei Absturz, Neustart oder `claude stop` verloren. **Beobachtet 28.10.:** der Weckruf 08:53 der Session monarch-b4 war nach dem Session-Neustart weg (`CronList` leer) |
| `ScheduleWakeup` | nur im `/loop`-Dynamic-Modus, 60–3600 s | ungeeignet: Reset liegt oft > 1 h entfernt, und kein `/loop` aktiv |
| `claude --bg --resume <session-id> "<prompt>"` | **gemessen:** eine gestoppte Hintergrund-Session läuft unter derselben ID mit vollem Kontext weiter und bearbeitet den neuen Prompt (Probe `wake-probe`, Haiku: „ERSTENS“ → stop → resume → „ZWEITENS“). Läuft die Session noch, startet Claude laut `--help` eine **Kopie** | geeignet für den Fall „Session ist weg“, braucht vorher eine Statusprüfung |
| `claude agents --json` | liefert je Session `sessionId`, `status` (idle/busy) und `state` (working/done/blocked) | Statusprüfung ohne TTY |
| native Fortsetzung am Nutzungslimit | greift nur am echten Limit | ergänzt, ersetzt nicht |
| launchd (`StartCalendarInterval`) | User-LaunchAgent; verpasste Termine im Ruhezustand laufen beim Aufwachen nach, nach Neustart beim Login | trägt über Session-Ende und Neustart |

## Entscheidung

Zwei sich ergänzende Weckrufe, die sich gegenseitig ausschließen:

1. **In der Session (primär):** `/rest` am Limit legt einen `CronCreate`-One-Shot
   auf `resets_at + 3 min` an, mit einer Minute ungleich :00 und :30.
   Prompt: „Mana-Reset: lies <recap> und mach weiter.“ Der Weckruf der Session
   ist Pflicht; ohne ihn endet `/rest` nicht. Das hält die Skill-Anweisung fest,
   und `monarch rest` gibt Zeitpunkt und Cron-Ausdruck aus.
2. **Außerhalb der Session (Fallback, Opt-in):** `monarch wake schedule
   --session <id> --at <zeit> --prompt-file <datei>` schreibt einen
   User-LaunchAgent `dev.monarch.wake.<id>` (macOS) beziehungsweise einen
   `systemd-run --user --on-calendar`-Timer (Linux). Zum Zeitpunkt läuft
   `monarch wake fire <id>`:
   - Steht die Session in `claude agents --json` als `idle` oder `busy`,
     passiert nichts; der In-Session-Cron übernimmt, und es entsteht keine
     Kopie.
   - Fehlt die Session oder ist sie gestoppt, folgt `claude --bg --resume <id>
     "<prompt>"`.
   - Danach entfernt sich der Job selbst (`launchctl bootout`, plist löschen)
     und schreibt `WakeFired` ins Ledger.

   Der Fallback ist **Opt-in** (`system.yaml: mana.wake: launchd`). Ein
   LaunchAgent ist eine dauerhafte Änderung am System des Owners. Diesen Weg
   schaltet der Owner ein, nicht der Agent.
3. Die Weckzeit ist `max(resets_at)` der überschrittenen Fenster + 3 min. Am
   7d-Limit ist das das Wochen-Reset, nicht das 5h-Reset.

## Folgen

- `/rest` (Skill) bekommt einen Abschnitt „Weckruf“: CronCreate ist Pflicht,
  launchd nur bei `mana.wake: launchd`.
- `monarch rest` gibt aus: Weckzeit lokal und UTC, Cron-Ausdruck und Prompt.
- Tests laufen hermetisch: Fake-`launchctl` und Fake-`claude` über PATH, kein
  echter LaunchAgent in CI.
- Grenze: Bei ausgeschaltetem Mac feuert gar nichts; nach dem Login holt launchd
  den Termin nach.
