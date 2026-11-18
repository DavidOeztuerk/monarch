# ADR-0001 — Subagent-Identität kommt aus dem Hook-Input

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Technische Prüfpunkte vor Phase 1, Punkt 1 („Identifiziert der Hook-Input den aufrufenden Subagent?“)

## Annahme im Konzept

Unklar. Der Fallback war „eine Quest = ein Worktree, Zuordnung über `cwd`“.

## Gemessen

Claude Code **2.1.311**, Headless-Probe (`claude -p --model haiku`) mit Logging-Hooks
auf allen Events, ein projektlokaler Agent `probe`, der selbst ein Bash-Tool ruft:

| Event | Main-Thread | im Subagent |
|---|---|---|
| `PreToolUse` | keine `agent_*`-Felder | `agent_id` (z. B. `ac6fc473…`), `agent_type: "probe"` |
| `PostToolUse` | keine | dieselben |
| `SubagentStop` | – | `agent_id`, `agent_type` |

Die Doku (`code.claude.com/docs/en/hooks`, „Common input fields“) bestätigt das:
`agent_id` ist die „unique identifier for the subagent or in-process teammate“, und
`agent_type` ist gesetzt, „when the session uses `--agent` or the hook fires inside a
subagent“. Zusätzlich gibt es `SubagentStart` mit beiden Feldern.

## Entscheidung

1. **Shadow-Zuordnung über `agent_id`/`agent_type` aus dem Hook-Input.** Ein Tool-Call
   ohne `agent_id` gehört dem Main-Thread (Orchestrator).
2. **Quest-Zuordnung:** Je Session ist höchstens eine Quest aktiv (`quest accept`);
   alle Calls der Session, Main und Subagents, buchen auf sie. `agent_type` bestimmt
   die Shadow-Klasse (scout, strategist, knight, warden) für Metriken und Policy.
3. Der `cwd`-Fallback bleibt nur für Läufe ohne aktive Quest: liegt `cwd` in einem
   Worktree `monarch/G-…`, wird das Gate daraus abgeleitet.
4. `hooks.json` bleibt bei den sieben Events des Konzepts; `SubagentStart` ist für
   die Zuordnung nicht nötig, weil `PreToolUse` die Identität schon trägt.

## Folgen

- Agent Teams (Teammates) sind nicht gemessen; der Prüfpunkt bleibt offen.
- `agent_type` ist bei Plugin-Agents der Name aus dem Frontmatter; Policy-Regeln
  dürfen ihn als Schlüssel verwenden (siehe ADR-0005).

## Nachtrag (18.11.2026, Q-0166): subagent-stop meldet Hintergrund-Shells

**Status:** Entwurf, überarbeitet nach der Warden-Designrunde (changes-requested am
Head 2ee936a); wartet auf die Delta-Runde. Oben bleibt alles wörtlich
stehen. Anlass: #116, Punkt 1.

### Owner-Entscheide (16.11.2026)

Wörtlich, Antworten des Owners im Chat:

6. „Q-0166 bucht je Hintergrund-Shell nur Programmname, PID und Laufzeit. Keine Argumente, auch nicht gekürzt. Das Repo ist öffentlich.“
7. „Eine Messung mit einem echten headless Lauf ist erlaubt, dokumentiert wie ADR-0001. Erreicht der Hinweis die Hauptsession nicht: nur das Ledger-Ereignis bauen.“

Die Nummern folgen der Fragenliste vom 16.11.2026; 1–5 stehen im Nachtrag zu
ADR-0003. Alles Folgende ist **Zusammenfassung** (Messung) oder **Vorschlag**
(Design).

### Gemessen (Zusammenfassung)

Claude Code **2.1.327**, drei Headless-Läufe am 18.11.2026 mit
`claude -p --model haiku --output-format stream-json --verbose
--allowedTools "Agent,Task,Bash"` in einem Probe-Ordner außerhalb des Repos.
Logging-Hooks auf `SubagentStart`, `SubagentStop`, `PreToolUse`, `PostToolUse`
und `Stop` schrieben den Input und `ps -axo pid,ppid,pgid,etime,command` je
Event. Ein Agent `probe` startet mit dem Bash-Tool `sleep 301` im Hintergrund und
endet sofort.

- Lauf A: Agent im Vordergrund.
- Lauf B: Agent im Hintergrund.
- Lauf C: Agent im Hintergrund; die Hauptsession startet vorher selbst
  `sleep 302` im Hintergrund.

Ergebnisse:

1. **Felder von `SubagentStop`:** `agent_id`, `agent_transcript_path`,
   `agent_type`, `background_tasks`, `cwd`, `effort`, `hook_event_name`,
   `permission_mode`, `prompt_id`, `session_crons`, `session_id`,
   `stop_hook_active`, `transcript_path`. `SubagentStart` hat kein
   `background_tasks`.
2. **`background_tasks` gilt für die ganze Session, nicht für den Agenten.**
   Lauf C: Die Liste beim `SubagentStop` enthielt `sleep 302` der Hauptsession,
   den Agenten selbst (`type: "subagent"`) und `sleep 301` des Agenten. Felder je
   Shell: `id`, `type: "shell"`, `status`, `description`, `command`. Es gibt keine
   PID, keine Startzeit und keinen Besitzer. Der Wert von `status` war in allen
   Läufen `"running"`; andere Werte sind nicht gemessen.
3. **Zuordnung zum Agenten:** `PostToolUse` des Bash-Tools trägt `agent_id` und
   `tool_response.backgroundTaskId`. Lauf C: `sleep 302` ohne `agent_id` mit
   `b4vuuc7bd`, `sleep 301` mit der `agent_id` des Agenten und `ba1kuj3i7`. Beim
   `SubagentStop` desselben Laufs standen in `background_tasks` genau diese IDs:
   `{"id":"b4vuuc7bd",…,"command":"sleep 302"}` und
   `{"id":"ba1kuj3i7",…,"command":"sleep 301"}`. Die Task-ID aus `PostToolUse`
   ist also die `id` in `background_tasks`. Der Monarch-Hook `post-tool-use` sieht
   Bash (plugin/hooks/hooks.json:28).
4. **Lebensdauer:**
   - Vordergrund (A): Die Shell lief beim `SubagentStop` und war beim `Stop` der
     Hauptsession zwei Sekunden später weg.
   - Hintergrund (B, C): Die Shell lief beim `SubagentStop` und bei allen
     folgenden `Stop`-Events weiter. In C endete sie nach rund 300 s von selbst;
     `claude -p` wartete so lange. Das passt zu den Beobachtungen in #116.
5. **Prozesse:** In Lauf A war der Elternprozess des Probe-Hooks der
   Claude-Code-Prozess (`hook_pid=73040 ppid=72849`). Die Hintergrund-Shell lief
   als Hülle `/bin/zsh -c source …shell-snapshots… && eval '<Befehl>'` mit
   demselben Elternprozess (`73011 72849`), der Befehl als Kind
   (`73012 73011 sleep 301`). Einfache Anführungszeichen im Befehl stehen in der
   Hülle als `'"'"'` (gesehen an der Hülle der eigenen Session, Befehl mit
   `grep -E '[s]leep …'`). Gemessen ist das nur in Lauf A und nur mit dem
   Probe-Hook. **Schluss, nicht gemessen:** Für den Monarch-Hook gilt dasselbe,
   weil Claude Code den Hook-Befehl startet (plugin/hooks/hooks.json:43) und der
   Wrapper das Binary mit `exec` startet (plugin/scripts/monarch:62, :79).
6. **Hinweis an die Hauptsession:**
   - `SubagentStop` gab `hookSpecificOutput.additionalContext` mit einem Marker
     aus. Der Marker kam in keinem Lauf bei der Hauptsession an. In Lauf A
     nannte die Hauptsession auf Nachfrage nur den Marker aus `PostToolUse`.
   - `PostToolUse` des Agent-Tools mit `additionalContext` erreichte die
     Hauptsession (A, B). Bei einem Hintergrund-Agenten feuert es aber beim Start
     (`status: "async_launched"`, Lauf B), bevor es Shells gibt.
   - Ergebnis: Für den Fall aus #116 (Hintergrund-Agent) gibt es keinen
     gemessenen Weg zur Hauptsession. Nach Owner-Entscheid 7 baut Q-0166 nur das
     Ledger-Ereignis.

Nicht gemessen: Teammates, interaktive Sessions, fremde Sitzungen auf derselben
Maschine, Linux (dort ist die Hülle vermutlich `bash`), Wiederaufnahme eines
Agenten per SendMessage (gleiche `agent_id`, zweites `SubagentStop`).

### Vorschlag: Design (nach der Designrunde am Head 2ee936a überarbeitet)

Die DoD von Q-0166 nennt noch „Befehl“ im Ereignis. Owner-Entscheid 6 geht vor:
nur Programmname, PID und Laufzeit.

**Lesen des Inputs (tolerant):**

- `tool_response` und `background_tasks` werden als rohes JSON gelesen
  (`JsonElement`), nicht fest typisiert. Eine unerwartete Form ist kein
  Parse-Fehler des ganzen Inputs. So hängen `ToolUsed` und `ShadowRunCompleted`
  nicht an diesen Feldern.
- Im Hook `subagent-stop` wird `ShadowRunCompleted` zuerst angehängt, wie heute.
  `gate merge` braucht es (GateMergeCommands.cs:637-641, ADR-0007:80). Die
  Shell-Erkennung läuft danach in einem eigenen `try`; ein Fehler dort geht nur
  nach `hook-errors.log`.

**Merken beim Start (Hook `post-tool-use`):**

- Nur wenn `agent_id` gesetzt ist (Kriterium wie im Nachtrag zu ADR-0003), das
  Tool Bash heißt und `tool_response.backgroundTaskId` ein nicht leerer String ist.
  Sonst nur eine Feldprüfung, kein Datei-IO.
- Ablage unabhängig vom `cwd`, außerhalb jedes Repos:
  `<CliEnv.Home>/local/background-shells/<agent_id>/<task_id>.json`, eine
  Datei je Task, atomar geschrieben. `CliEnv.Home` ist schon `~/.monarch`
  (oder `MONARCH_HOME`, CliEnv.cs:9, :27-31), der Pfad ist also
  `~/.monarch/local/…`, nie `~/.monarch/.monarch/…`. `agent_id` und `task_id` im Pfad nur aus
  `[A-Za-z0-9_-]`, sonst kein Eintrag. Inhalt: Programmname und Startzeit (UTC).
- Kein Eintrag im getrackten Ledger. `ToolUsed` bleibt unverändert.

**Programmname (Datenschutz, Owner-Entscheid 6):**

- Zuerst wird das ganze erste Wort des Befehls geprüft: nur `[A-Za-z0-9._+/-]`,
  also kein `=`, kein `$`, keine Anführungszeichen. Erst danach zählt der
  Dateiname nach dem letzten `/`. Ist er leer oder länger als 32 Zeichen, gilt
  `unbekannt`; gekürzt wird nie.
- Besteht das erste Wort die Prüfung nicht: `unbekannt`. Beispiel:
  `AWS_SECRET_ACCESS_KEY=wJal…/K7M…/bPxR… cmd` ergibt `unbekannt`, kein Teil des
  Werts.

**Melden beim Ende (Hook `subagent-stop`):**

- Shells des Agenten sind seine Task-Dateien, deren Task-ID in
  `background_tasks` mit `type: "shell"` und `status: "running"` steht
  (Messung 2 und 3).
- Je Shell: Programmname, PID und Laufzeit in Sekunden (jetzt minus Startzeit).
  Keine Argumente, keine Beschreibung, keine Task-ID (Owner-Entscheid 6).
- **PID:** nur wenn mindestens eine Shell gefunden ist, ein Aufruf
  `ps -ww -A -o pid=,ppid=,command=`. `ps` nur absolut aus `PATH` (ADR-0016),
  mit einer eigenen `ToolLocator`-Instanz ohne stderr-Ausgabe; fehlt `ps`, nur
  ein Eintrag in `hook-errors.log`. Die Ausgabe wird wie in GitAnchor.cs:112-140
  über abgewartete Tasks gelesen. Frist 2 s. Läuft `ps` länger, wartet der Hook
  nicht weiter und meldet ohne PID; er beendet `ps` nicht.
- Treffer ist die Hülle (in Lauf A 73011, nicht `sleep` 73012): ein Prozess,
  dessen Elternprozess der Elternprozess des Hooks ist und dessen Befehlszeile
  `eval '<Befehl>'` enthält, Anführungszeichen wie gemessen kodiert. Den eigenen
  Elternprozess liest der Hook aus derselben Liste, aus der Zeile mit
  `Environment.ProcessId`. Genau ein Treffer: seine PID. Sonst: keine PID im
  Ereignis. Die Befehlszeile wird nur verglichen, nie gespeichert oder geloggt.
- Mindestens eine Shell: ein Ledger-Ereignis `BackgroundShellsLeft` mit
  `agent_id`, `agent_type`, `quest` und `shells` (Liste aus `program`, `pid`,
  `runtime_s`; als `JsonElement` übergeben, State.cs:200). Keine Shell: kein
  Ereignis.
- Danach löscht der Hook die gemeldeten und die nicht mehr laufenden
  Task-Dateien des Agenten. Eine zweite Meldung nach einer Wiederaufnahme nennt
  dann nur neue Shells.
- Aufräumen: Task-Dateien älter als 24 h löscht der Hook bei jedem
  `subagent-stop`, für alle Agenten. So bleibt nichts liegen, wenn ein
  `SubagentStop` ausbleibt. Eine kaputte Datei wird übersprungen und geloggt.
- Der Hook meldet nur. Er beendet keinen Prozess, auch nicht das eigene
  `ps`-Kind, sendet kein Signal und gibt nichts aus, auch kein
  `decision: block`. Fehler gehen nur nach `hook-errors.log`, Exit 0. Der
  Kommentar HookRunner.cs:9 wird angepasst.
- Hinweis an die Hauptsession: keiner (Messung 6, Owner-Entscheid 7). Vorschlag
  (nicht vom Owner entschieden): auch bei Agenten im Vordergrund keiner. Der
  gemessene Weg dort (`PostToolUse` des Agent-Tools) bräuchte eine Änderung an
  `plugin/hooks/hooks.json`, und in Lauf A endete die Shell ohnehin mit dem
  Agenten. Die Hauptsession prüft weiter selbst mit `ps` (#116).

**Tests (Nähte):** Prozessliste, Elternprozess des Hooks und Uhr sind über
`CliEnv` injizierbar. Ohne Injektion liest der Hook seinen Elternprozess aus der
Prozessliste (Zeile mit `Environment.ProcessId`). Fälle: keine Task-Datei, kaputte Task-Datei, kaputtes
`background_tasks`, kaputtes `tool_response`, Programmname mit `/` im Wert,
null, ein und zwei PID-Treffer, zweites `SubagentStop` nach Wiederaufnahme.
Integrationsfall: Der Test startet `/bin/sh -c "eval 'sleep 30'; true"` als Kind
(der Folgebefehl verhindert, dass `sh` sich per `exec` durch `sleep` ersetzt) und
setzt die eigene PID als Elternprozess des Hooks ein. Danach läuft das Kind
noch.

**Grenzen (offen benannt):**

- Hintergrund-Shells aus anderen Werkzeugen als Bash werden nicht erkannt.
- Befehle, die sich selbst ablösen (`nohup … &`, `setsid`), beenden die Hülle;
  sie werden nicht erkannt.
- Endet die Hauptsession vor dem `SubagentStop`, gibt es kein Ereignis.
- Das Aufräumen nach 24 h trifft auch Task-Dateien von Agenten, die länger
  laufen; deren Shells meldet der Hook dann nicht.
- Die Kodierung der Befehlszeile und die Hülle sind nur auf macOS gemessen,
  nicht dokumentiert. Ändert Claude Code sie, oder ist die Hülle unter Linux
  anders, fehlt nur die PID; Programmname und Laufzeit bleiben.
