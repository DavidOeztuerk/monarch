# ADR-0008 — Selbst-Weckruf nach `resets_at`

**Datum:** 28.10.2026 · **Status:** angenommen (Design-Review Warden G-0101, Auflagen eingearbeitet) · **Quest:** Q-0128 (G-0101; ersetzt Q-0114)

## Problem

Monarch stoppt am Mana-Limit (5h 90 %, 7d 96 %), also **vor** dem echten
Nutzungslimit. Die native Fortsetzung von Claude Code greift aber erst am echten
Limit. Damit bleibt eine Session nach `/rest` liegen, bis jemand sie anstößt.

**Anlassfall (28.10.):** Die Session monarch-b4 wurde neu gestartet. Sie lebte
weiter, aber der Weckruf 08:53 war weg (`CronList` leer): Ein `CronCreate` liegt
nur im Speicher des Session-Prozesses. Ein Außen-Job muss also auch dann
sinnvoll handeln, wenn die Session **lebt**, ihr Cron aber fehlt.

## Gemessen (Claude Code 2.1.312, 27./28.10.2026)

| Mechanismus | Verhalten | Eignung |
|---|---|---|
| `CronCreate` (One-Shot) | nur im Speicher der Session („durable: no effect“), feuert nur im Leerlauf, Jitter bis 90 s bei :00/:30 | trägt, solange der Session-Prozess lebt; geht bei Absturz, Neustart oder `claude stop` verloren (Anlassfall) |
| `ScheduleWakeup` | nur im `/loop`-Dynamic-Modus, 60–3600 s | ungeeignet: Reset liegt oft > 1 h entfernt, und kein `/loop` aktiv |
| `claude --bg --resume <session-id> "<prompt>"` | **gemessen:** eine gestoppte Hintergrund-Session läuft unter derselben ID mit vollem Kontext weiter und bearbeitet den neuen Prompt (Probe `wake-probe`, Haiku: „ERSTENS“ → stop → resume → „ZWEITENS“). Läuft die Session noch, startet Claude laut `--help` eine **Kopie** | geeignet, aber nur nach Statusprüfung und `claude stop` |
| `claude agents --json` | liefert je Eintrag `id`, `sessionId`, `kind` (`background`/`interactive`) und `status` (idle/busy) beziehungsweise `state` (working/done/blocked) | Statusprüfung ohne TTY |
| native Fortsetzung am Nutzungslimit | greift nur am echten Limit | ergänzt, ersetzt nicht |
| launchd (`StartCalendarInterval`, `RunAtLoad`) | User-LaunchAgent; die Plist kennt **kein Jahr** (nur Monat, Tag, Stunde, Minute) | `RunAtLoad` ist belegt (Lauf beim Laden der Plist); **unbelegt** ist, ob ein verpasster Termin nach Ruhezustand oder Login nachgeholt wird. Darauf baut dieser ADR nicht |
| `systemd-run --user --on-calendar` | transienter Timer | überlebt keinen Reboot (siehe Grenzen) |

## Entscheidung

### 1. In der Session (primär, Pflicht)

`/rest` am Limit legt einen `CronCreate`-One-Shot auf `resets_at + 3 min` an, mit
einer Minute ungleich :00 und :30 (Jitter-Minuten, lokal gerechnet). Der Prompt
ist ein **festes Template**: „Mana-Reset: lies <recap-pfad> und mach weiter.“
`monarch rest` rechnet die Weckzeit und gibt sie lokal und UTC aus, dazu den
Cron-Ausdruck und den Prompt. Der Weckruf der Session ist Pflicht; ohne ihn endet
`/rest` nicht (Skill `rest`, Abschnitt „Weckruf“).

Die Weckzeit ist das **spätere** `resets_at` der überschrittenen Fenster + 3 min.
Am 7d-Limit ist das das Wochen-Reset, nicht das 5h-Reset.

### 2. Außerhalb der Session (Fallback, Opt-in des Owners)

`monarch wake schedule --session <uuid> --at <iso>` legt einen einmaligen Job an
(`wake cancel <uuid>` entfernt ihn). Der Job ruft `monarch wake fire <uuid> --at
<iso>` auf **Weckzeit + Grace** (3 min, länger als der Cron-Jitter von 90 s, damit
der In-Session-Cron zuerst feuert). `fire` entscheidet so:

1. **Zeitprüfung.** Vor Weckzeit + Grace: nichts tun, der Job bleibt (so läuft
   `RunAtLoad` direkt nach dem Anlegen ins Leere). Älter als 24 h: `--at` ist
   veraltet, `WakeSkipped` (Grund `stale`), Job entfernen. Eine Plist kennt kein
   Jahr; ohne diese Prüfung würde ein alter Job ein Jahr später feuern.
2. **Statusprüfung.** `claude agents --json` muss durchlaufen und die Session
   listen; sonst **kein Resume** (`WakeSkipped`, Grund `agents-failed` oder
   `session-not-listed`).
3. **Nur eine idle Session ohne Aktivität.** Läuft sie (`busy`/`working`) oder
   ist sie nicht idle, oder gibt es seit `resets_at` (Weckzeit − 3 min)
   Ledger-Aktivität dieser Session (der In-Session-Cron hat gegriffen), passiert
   nichts außer `WakeSkipped`. Das verhindert die Kopie.
4. **Hintergrund-Session** (`kind: background`), idle und ohne Aktivität seit
   `resets_at`: erst `claude stop <id>`, dann `claude --bg --resume <session-id>
   "<festes Prompt>"`, dann `WakeFired`. **Interaktive Session** (der Anlassfall:
   die Session lebt, nur der Cron fehlt): `WakeSkipped` (Grund
   `interactive-session`); ein interaktives Terminal wird nie von außen
   gestoppt.
5. Nach jedem Ergebnis entfernt sich der Job selbst (launchd: Plist löschen,
   dann `launchctl bootout` als letzter Schritt, weil bootout den laufenden Job
   beendet; systemd: `systemctl --user stop`).

Ledger: `WakeScheduled`, `WakeFired`, `WakeSkipped` (und `WakeCancelled`) in der
Session-Datei der Session.

**launchd:** `RunAtLoad` plus Zeitprüfung in `fire`; `StartCalendarInterval`
(Monat, Tag, Stunde, Minute lokal) auf Weckzeit + Grace. **systemd:**
`systemd-run --user --on-calendar=<UTC>`.

### 3. Rechte (G2 / Regel 7)

Der Fallback ist eine dauerhafte Änderung am System des Owners. Das Opt-in steht
im geschützten `system.yaml` (`mana.wake: launchd|systemd|off`, Default `off`) und
ist damit Sache des Owners, nicht des Agenten. Erst danach darf der Main
`wake schedule` ausführen, und nur so:

- Das **Binary prüft das Opt-in selbst**; ohne Opt-in wird verweigert.
- Der Prompt ist ein **festes Template**. Agent-Freitext gelangt nie in einen
  persistenten Job (es gibt kein `--prompt`/`--prompt-file`); der Job kennt nur
  Session-UUID und Zeit.
- Die Session-ID wird als **UUID geprüft** (kein Pfad-, Label- oder
  Argument-Einschleusen).
- ProgramArguments laufen **ohne Shell**, mit absoluten Pfaden (minimaler `PATH`
  unter launchd, der absolute `claude`-Pfad kommt als `MONARCH_CLAUDE_BIN`);
  `WorkingDirectory` ist das Projekt.
- **Höchstens ein Job je Session** (Record unter `.monarch/local/wake/`).
- **Shadows** (`agent_type` gesetzt) werden verweigert: die Hook-Regel
  `protect_branches` sperrt `monarch wake schedule|cancel` für Shadows wie
  `gate merge|verdict` (Q-0129); zusätzlich verweigert das Binary bei gesetztem
  `MONARCH_AGENT_TYPE`.

## Folgen

- `/rest` (Skill) bekommt den Abschnitt „Weckruf“: CronCreate ist Pflicht,
  launchd/systemd nur bei Opt-in.
- `monarch rest` gibt Weckzeit lokal und UTC, Cron-Ausdruck und Prompt aus.
- Tests laufen hermetisch: `HOME` zeigt auf ein Temp-Verzeichnis (nie
  `~/Library/LaunchAgents`), Fake-`launchctl`, Fake-`systemd-run`/`systemctl` und
  Fake-`claude` über `PATH`; die Plattform kommt aus `mana.wake` und ist auf jedem
  OS testbar.

## Grenzen

- **Rest-Race:** Wacht die Session zwischen der Statusprüfung und dem `--resume`
  selbst auf (Respawn), entsteht eine Kopie. Das Fenster ist klein, aber nicht
  null; `fire` prüft vor `stop` und nicht atomar.
- **launchd:** Die Plist kennt kein Jahr. Das Nachholen verpasster Termine ist
  unbelegt; gesichert sind nur `RunAtLoad` und die Zeitprüfung in `fire`
  (veraltetes `--at` wird verworfen). Bei ausgeschaltetem Mac feuert nichts.
- **systemd-run ist transient** und überlebt keinen Reboot (auch kein Ende der
  User-Session). Der Fallback trägt unter Linux also nur über das Ende des
  Session-Prozesses, nicht über einen Neustart des Rechners (steht auch in
  `plugin/README.md`).
- **Fehlende Session:** Ist die Session nicht in `claude agents --json`
  gelistet, wird nicht resumt (`session-not-listed`). Das ist bewusst
  konservativ; ein Resume gestoppter, nicht mehr gelisteter Sessions bräuchte
  eine eigene Entscheidung.
- **Interaktive Sessions** werden nie gestoppt oder resumt; fehlt ihr Cron,
  bleibt nur `WakeSkipped` im Ledger.
