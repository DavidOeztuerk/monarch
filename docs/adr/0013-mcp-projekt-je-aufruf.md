# ADR-0013 — MCP: Projekt je Aufruf (Roots vor Parameter vor cwd)

**Datum:** 28.10.2026 · **Status:** vorgeschlagen (Design-Phase Warden G-0103) · **Quest:** Q-0131 (G-0103)

## Problem und Messung (F4)

Der MCP-Server (`src/Monarch.Mcp/McpServer.cs`, Tools in `src/Monarch.Cli/McpTools.cs`) ermittelt das Projekt über `ProjectPaths.Locate(env.Cwd, …)`, also aus dem cwd seines Prozesses. Das ist das Start-cwd der Claude-Code-Session. Startet die Session in `~/Projects` (nicht erweckt), scheitern alle Tools mit „not an awakened project“, auch `gate_plan` und `knowledge_write`. Die Arbeitsregel schreibt genau diese beiden vor: Solange eine Quest läuft, gehen Folge-Quests nur über `gate_plan`, Notizen nur über `knowledge_write`. Ein per Bash geändertes cwd erreicht den MCP-Prozess nicht.

**Gemessen (Claude Code 2.1.312):** Der Client deklariert die Capability `roots:{listChanged:!0}` und beantwortet `roots/list`. Belegt durch die Binary-Strings `roots/list`, `notifications/roots/list_changed` und „MCP: staging root unavailable, omitted from roots/list“.

**Nicht gemessen:** welche Verzeichnisse als Roots kommen. Vermutet: Start-cwd plus `/add-dir`-Verzeichnisse, nicht das per Bash geänderte cwd. Dieses ADR verlässt sich darauf **nicht**. Entscheidung 8 verlangt eine Messung in der TDD-Phase; das Design bleibt gültig, wenn die Roots nur das Start-cwd liefern (dann hilft der Parameter), und ebenso, wenn sie mehr liefern.

## Entscheidung

### 1. Auflösungsreihenfolge (je Tool-Aufruf, nie gecacht über Aufrufe)

Der Server fragt beim ersten Aufruf `roots/list` (nur wenn der Client `roots` deklariert hat) und aktualisiert bei `notifications/roots/list_changed`. Roots sind `file://`-URIs; nur Schema `file`, absolut, kanonisiert (Regel 3). Danach gilt je Aufruf:

1. **Expliziter Parameter `project`**, nur wenn er die Validierung (Regel 3) besteht **und**, sofern Roots vorhanden sind, gleich einem Root ist oder darunter liegt (kanonisch, Komponentenvergleich, kein Präfix-String-Vergleich). Verletzt er die Root-Grenze: Fehler, **kein** Fallback auf die nächsten Stufen.
2. **Genau ein erwecktes Projekt** in oder unter den Roots (Suche: der Root selbst und seine Vorfahren per `LocateAll`, dazu Unterverzeichnisse bis Tiefe 2, ohne `.git`, `node_modules`, Symlink-Folgen). Bei null oder mehr als einem Treffer geht es weiter.
3. **Prozess-cwd** wie heute (`Locate(env.Cwd, …)`).
4. Sonst **Fehler mit Weg:** „Kein erwecktes Projekt bestimmt. Parameter `project` angeben (absoluter Pfad eines erweckten Projekts unterhalb der Roots: …)“. Bei mehreren Kandidaten werden sie (max. 10, kanonisch) aufgelistet.

**Warum ein Root mit mehreren erweckten Projekten darunter nicht auto-auflöst:** `~/Projects` enthält `monarch`, `.monarch-worktrees/monarch-G-0103` und weitere. „Nimm das erste/neueste/das mit aktiver Quest“ wäre Raten, und ein Fehlgriff schreibt in die falsche Quest-Historie (Ledger-Kette, XP, `gate_plan`-Dateien). Das ist leise und schwer rückholbar, der Fehler dagegen billig: ein zweiter Aufruf mit `project`. Eindeutigkeit ist daher die Bedingung für implizite Auflösung; alles andere verlangt eine ausdrückliche Absicht. Ebenso gilt: ein erweckter **Vorfahr** des Roots wird nur gezählt, wenn er der einzige Treffer ist.

### 2. Warum Roots vor cwd, aber Parameter vor Roots

Roots stammen vom Client (User-Konfiguration, `/add-dir`), nicht vom Agenten; sie sind die einzige Instanz, die der Agent nicht schreibt. Deshalb sind sie die **Grenze** für den Parameter (Regel 1). Der Parameter schlägt die implizite Roots-Suche, weil er eine ausdrückliche Absicht ist und genau den mehrdeutigen Fall löst.

### 3. Validierung des Parameters `project`

Reihenfolge, jede Stufe fail-closed, Fehlertext nennt die Stufe:

1. Zeichenkette, nicht leer, **absolut** (kein `~`, keine Variablen, kein relativer Pfad; relativ wäre vom Prozess-cwd abhängig, genau das Problem).
2. Kanonisierung wie `PathResolver`: lexikalisch (`..`, `.`, doppelte Separatoren) **und** physisch (Symlinks aufgelöst, auf APFS mit der Faltungstabelle aus ADR-0011 Regel 8). Beide Ergebnisse müssen auf dasselbe Verzeichnis zeigen; weicht die physische Auflösung aus dem lexikalischen Baum heraus, gilt der physische Pfad für alle weiteren Prüfungen (auch die Root-Grenze).
3. Verzeichnis **existiert**.
4. `<projekt>/.monarch/system.yaml` existiert als reguläre Datei (nicht nur ein Köder-`.monarch`, ADR-0011 Regel 6). Hinweis: `Locate` prüft heute nur das Verzeichnis; die neue Prüfung gilt für Parameter **und** für Roots-Treffer.
5. Das Projekt ist **nicht** `~/.monarch`, **nicht** `MONARCH_HOME`, und liegt nicht darunter (kanonisch verglichen), auch nicht per Symlink.
6. Der Parameter bezeichnet die **Projekt-Root**, nicht ein Unterverzeichnis: Ist `.monarch` nicht direkt darunter, ist es ein Fehler (keine Aufwärtssuche, sonst wäre `project=<Unterordner>` eine zweite Mehrdeutigkeit).
7. Root-Grenze (Regel 1), wenn Roots bekannt sind.

Die Auflösung liefert ein Tupel `(ProjectPaths, Quelle)` mit `Quelle ∈ {parameter, roots, cwd}`. **Alle weiteren Dateioperationen des Aufrufs leiten ihre Pfade ausschließlich aus diesem einen `ProjectPaths` ab.**

### 4. Verhalten je Tool

Das Schema jedes Tools bekommt das optionale Feld `project` (string, absoluter Pfad). Das cwd der Tools (`env.Cwd`) wird für den Aufruf auf das aufgelöste Projekt gesetzt (`env with { Cwd = paths.Root }`), damit Delegation (`StatusCommand`, `QuestCommands`, `EvidenceCommands`) nicht still das Prozess-cwd benutzt.

| Tool | Verhalten |
|---|---|
| `system_status` (lesend) | Wie oben. Ohne Projekt Fehler mit Weg. |
| `mana_forecast` (lesend) | **Funktioniert ohne Projekt.** Mana ist global (Snapshot unter `~/.monarch`, F4 vom 27.10; Faktor je Paar, ADR-0012). Ohne aufgelöstes Projekt rechnet es mit den Plan-Defaults und weist ausdrücklich darauf hin („kein Projekt: Plan-Defaults, keine adaptiven Projektlimits“). Mit Projekt wie heute. Offen für die TDD-Phase: Prüfen, was `ManaCommands.Forecast` aus `paths` liest; nur das darf Projektbezug bleiben. Der Parameter wird für dieses Tool **nicht** verlangt, aber akzeptiert und validiert. |
| `quest_transition` (schreibend) | Projekt Pflicht. Die Quest-ID wird nur im aufgelösten Projekt gesucht; nie projektübergreifend. |
| `gate_plan` (schreibend) | Projekt Pflicht. Schreibt nur in dessen `.monarch/gates` und `.monarch/quests` (bestehende Risk-Tag-Prüfung gegen dessen Quest-Scope). |
| `knowledge_write` (schreibend) | Projekt Pflicht. Bleibt auf `.monarch/knowledge/` **dieses** Projekts beschränkt (relativer Pfad, kein `..`, kein Symlink-Ausbruch, wie heute), gemessen an der Wurzel des aufgelösten Projekts. |
| `evidence_sync` (schreibend) | Projekt Pflicht. Ruft Forge und Ledger nur des aufgelösten Projekts. |

Schreibende Tools mit Quelle `parameter` unterliegen zusätzlich dem Gegenmechanismus aus Punkt 5.

### 5. Sicherheit gegen ADR-0011

**Befund.** ADR-0011 nennt „MCP-Tools mit Pfad-Argumenten“ ausdrücklich als Lücke außerhalb von Phase 0. Der Parameter `project` ist genau so ein Argument: Der Hook (`ToolCallPolicy`) sieht MCP-Aufrufe nicht, weder Roots noch Scope Lock noch Schutzpfade des Ziel-Projekts. Ein Agent kann also per `project` in ein **fremdes erwecktes Projekt** schreiben, etwa dort `quest_transition accept` auslösen, `gate_plan` Dateien anlegen lassen oder `knowledge_write` aufrufen. Das ist **durch die Policy nicht abgedeckt**.

**Einordnung.** Es ist kein neuer Fähigkeitsgewinn, aber eine neue bequeme und unauffällige Route: Der Agent kann dasselbe heute per Bash (`cd /fremd && ~/.monarch/bin/monarch quest accept Q-…`), das ADR-0011 Regel 2 als legitime Arbeit positiv listet; der Hook der Session wertet das fremde Projekt nur aus, wenn es Vorfahr des cwd ist. Das ist die in G4 dokumentierte Grenze. Der Parameter darf sie nicht **zusätzlich** aufweichen. Er ist also ein Problem, soweit er sie bequemer macht, und wird so begrenzt:

**Gegenmechanismus (Pflicht).**
1. **Roots sind die Grenze, nicht die Policy.** Mit Roots: Parameter nur unter einem Root (Regel 1). Das ist eine echte Grenze, weil Roots vom Client kommen und der Agent sie nicht setzen kann (außer der User per `/add-dir`).
2. **Ohne Roots** (Client deklariert `roots` nicht oder liefert eine leere Liste): `project` ist für **lesende** Tools erlaubt; für **schreibende** Tools nur, wenn es mit dem Projekt des Prozess-cwd übereinstimmt (dann ist es redundant). Sonst Fehler. Damit schaltet der Parameter keinen neuen Schreibweg gegen unbegrenzte Pfade frei.
3. **Kein Cross-Projekt-Zugriff:** ein Aufruf, ein Projekt (Regel 3, letzter Satz); ein Test erzwingt, dass der Aufruf keine Datei außerhalb des aufgelösten Projekts (und `~/.monarch`-Snapshot für Mana) berührt.
4. **Sichtbarkeit im Ziel:** Das Ledger des **Zielprojekts** erhält das Ereignis mit Quelle (Punkt 6), sodass ein Fremdschreiben dort in der Hash-Kette steht und `ledger verify` sowie das Review es sehen (G3). Das **schützt nicht**, es macht erkennbar.
5. **Keine Berührung der Schutzpfade:** `gate_plan`/`knowledge_write` schreiben nur in `.monarch/gates|quests|knowledge`; `system.yaml`, `local/`, `~/.monarch` bleiben für MCP unerreichbar (Regel 3.5 und Test).

**Restrisiko (dokumentieren in `plugin/README.md`, „Grenzen“):** Liegt ein fremdes erweckte Projekt unter einem Root (typisch `~/Projects`), kann der Agent dort per Parameter oder Bash Quests annehmen. Das ist G4 und bleibt es bis zur Durchsetzung außerhalb des Prozesses (ADR-0009). Wer das nicht will, startet die Session in dem Projekt statt in einem Elternverzeichnis.

### 6. Ledger

Jeder MCP-Aufruf eines **schreibenden** Tools protokolliert im Ledger des aufgelösten Projekts ein Ereignis `McpProjectResolved` (vor der eigentlichen Wirkung, Hash-Kette): `tool`, `project` (kanonischer Pfad), `source` (`parameter`|`roots`|`cwd`), `roots_count`, `cwd_project` (Projekt des Prozess-cwd oder leer), `session`. Lesende Tools protokollieren dasselbe nur, wenn `source=parameter` (kein Ledger-Rauschen bei Statusabfragen, aber jede Abweichung vom Gewohnten bleibt sichtbar). Schlägt die Auflösung fehl, gibt es kein Ledger (kein Projekt) und die Fehlermeldung geht auf stderr des Servers.

**Messung der Roots (TDD-Phase, Pflicht):** Beim Empfang von `roots/list` und `roots/list_changed` schreibt der Server eine Zeile `mcp: roots=[…]` auf stderr; sobald ein Projekt aufgelöst ist, zusätzlich das Ledger-Ereignis `McpRootsObserved` (kanonische Roots, Anzahl). Damit wird in einer echten Session gemessen: (a) Start in `~/Projects`, (b) Start im Projekt, (c) nach `/add-dir`, (d) nach `cd` per Bash. Das Ergebnis wird als Nachtrag in dieses ADR eingetragen. Unabhängig vom Ergebnis ändert sich die Reihenfolge nicht.

### 7. Testplan (hermetisch)

Alle Tests laufen ohne Netz und ohne echtes `$HOME`: Temp-Verzeichnisse, `MONARCH_HOME` und `userHome` per Parameter injiziert. Fake-Client: ein Test-Harness spricht JSON-RPC über `StringReader`/`StringWriter`, deklariert `roots` und beantwortet `roots/list` mit vorgegebenen URIs.

- **Reihenfolge:** Parameter unter Root → parameter; kein Parameter, ein erwecktes Projekt unter Root → roots; zwei erweckte Projekte unter Root → Fehler mit Kandidatenliste; Root nicht erweckt und cwd erweckt → cwd; nichts → Fehler „Parameter `project` angeben“; Parameter außerhalb aller Roots → Fehler **ohne** Fallback auf cwd.
- **Roots-Ereignisse:** `list_changed` ersetzt die Liste; Client ohne `roots`-Capability; leere Liste; `roots/list` antwortet mit Fehler oder Timeout (Fallback cwd, Ereignis im stderr); Nicht-`file`-URI wird ignoriert; Präfix-Falle (`/a/proj` vs. `/a/proj-evil`).
- **Pfad-Varianten für `project`:** relativ, `~/x`, `$HOME/x`, leer, Datei statt Verzeichnis, nicht existent, existent aber nicht erweckt, `.monarch` ohne `system.yaml` (Köder), Symlink auf erwecktes Projekt außerhalb der Roots (Deny, physisch geprüft), Symlink innerhalb auf innerhalb (ok), `..`-Ausbruch aus dem Root, Unterverzeichnis eines Projekts, `~/.monarch`, `MONARCH_HOME`, Symlink darauf, Pfad darunter, APFS-Faltungsvariante (`ſ`).
- **Projektgrenzen:** Zwei erweckte Projekte A/B unter einem Root. Aufruf mit `project=A`: Datei-Hash-Vergleich von B vor und nach jedem der sechs Tools identisch; `quest_transition` mit einer Quest-ID, die nur in B existiert, schlägt in A fehl; `knowledge_write` mit `../../B/.monarch/knowledge/x` und per Symlink in A auf B → Deny; `gate_plan` schreibt nur nach `A/.monarch/gates|quests`; Schutzpfade (`system.yaml`, `local/`) bleiben unverändert.
- **Ohne Roots:** `project` bei lesenden Tools ok, bei schreibenden nur gleich cwd-Projekt; sonst Fehler.
- **Mana:** `mana_forecast` ohne Projekt liefert Plan-Defaults samt Hinweis; mit Projekt unverändert (Regressionsvergleich).
- **Ledger:** `McpProjectResolved` steht im Ledger des Zielprojekts mit richtiger Quelle, nicht in einem anderen; `ledger verify` bleibt grün; Auflösungsfehler schreiben nirgends.
- **Rückwärtskompatibilität:** bestehende MCP-Tests ohne Roots und ohne Parameter bleiben unverändert grün (Quelle cwd).

### 8. Grenzen und Folgen

- Die Messung der Roots (Regel 6) ist Voraussetzung für die Aussage „Start in `~/Projects` ist gelöst“; fällt sie anders aus als vermutet, bleibt der Parameter der Weg, und die Fehlermeldung der Stufe 4 ist die Bedienungsanleitung.
- Roots-Suche unter einem Root ist auf Tiefe 2 begrenzt; tiefer liegende Projekte (z. B. Worktrees in `.monarch-worktrees/…`, Tiefe 2) brauchen den Parameter. Die Tiefe wird in der TDD-Phase an realen Layouts geprüft.
- Die Eindeutigkeitsregel verlangt bei Mehrfachtreffern einen zusätzlichen Aufruf. Das ist gewollt.
- Der Parameter ist ein MCP-Pfad-Argument im Sinn von ADR-0011 und bleibt eine dokumentierte G4-Grenze; Roots-Bindung und Ledger-Sichtbarkeit mindern sie, heben sie nicht auf.
- Folge-Quests: Hook-seitige Sicht auf MCP-Aufrufe (ADR-0009), Registrierung verschachtelter Projekte (ADR-0011 Regel 6), `plugin/README.md` „Grenzen“ um den Parameter ergänzen (DoD der Umsetzungs-Quest).
- Keine Änderung an `PathResolver`, Policy oder Risk Tags in diesem ADR; Umsetzung ist eine eigene Quest mit TDD.
