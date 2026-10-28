# ADR-0013 — MCP: Projekt je Aufruf (Roots vor Parameter vor cwd)

**Datum:** 28.10.2026 · **Status:** vorgeschlagen, überarbeitet nach Design-Review (Warden G-0103); Code nach T1–T4 · **Quest:** Q-0131 (G-0103)

## Problem und Messung (F4)

Der MCP-Server (`src/Monarch.Mcp/McpServer.cs`, Tools in `src/Monarch.Cli/McpTools.cs`) ermittelt das Projekt über `ProjectPaths.Locate(env.Cwd, …)`, also aus dem cwd seines Prozesses. Das ist das Start-cwd der Claude-Code-Session. Startet die Session in `~/Projects` (nicht erweckt), scheitern alle Tools mit „not an awakened project“, auch `gate_plan` und `knowledge_write`. Die Arbeitsregel schreibt genau diese beiden vor: Solange eine Quest läuft, gehen Folge-Quests nur über `gate_plan`, Notizen nur über `knowledge_write`. Ein per Bash geändertes cwd erreicht den MCP-Prozess nicht.

**Gemessen (Claude Code 2.1.312):** Der Client deklariert die Capability `roots:{listChanged:!0}` und beantwortet `roots/list`. Belegt durch die Binary-Strings `roots/list`, `notifications/roots/list_changed` und „MCP: staging root unavailable, omitted from roots/list“.

**Nicht gemessen:** welche Verzeichnisse als Roots kommen. Vermutet: Start-cwd plus `/add-dir`-Verzeichnisse, nicht das per Bash geänderte cwd. Dieses ADR verlässt sich darauf **nicht**. Die Messung (Regel 6) ist Voraussetzung für die Code-Freigabe (Auflage 7); das Design bleibt gültig, wenn die Roots nur das Start-cwd liefern (dann hilft der Parameter), und ebenso, wenn sie mehr liefern.

## Entscheidung

### 0. Effektive Roots (T1)

Die **effektiven Roots** sind die `file://`-Roots des Clients. Liefert der Client keine verwertbaren (Capability nicht deklariert, Liste leer, Fehler, Timeout oder nur Nicht-`file`-URIs), ist das **kanonische (physische) Prozess-cwd der einzige Root**. Das Start-cwd kann der Agent nicht ändern (ein `cd` per Bash erreicht den MCP-Prozess nicht). Damit gibt es immer eine Root-Grenze; die frühere Sonderregel „ohne Roots schreibend nur, wenn gleich cwd-Projekt“ entfällt. Sie hätte `gate_plan` und `knowledge_write` bei einem Start in `~/Projects` ohne Roots unbenutzbar gelassen. Regel 1 (Root-Grenze) und Regel 3.7 gelten unverändert, mit diesen effektiven Roots.

### 1. Auflösungsreihenfolge (je Tool-Aufruf, nie gecacht über Aufrufe)

Der Server fragt `roots/list` (nur wenn der Client `roots` deklariert hat) erst nach `notifications/initialized` und aktualisiert bei `notifications/roots/list_changed`. Roots sind `file://`-URIs; nur Schema `file`, absolut, kanonisiert (Regel 3). Danach gilt je Aufruf:

1. **Expliziter Parameter `project`**, nur wenn er die Validierung (Regel 3) besteht **und** gleich einem effektiven Root ist oder darunter liegt (kanonisch, Komponentenvergleich, kein Präfix-String-Vergleich). Ein Vorfahr eines Roots ist per Parameter **nicht** wählbar. Verletzt er die Root-Grenze: Fehler, **kein** Fallback auf die nächsten Stufen.
2. **Implizite Auflösung auf genau ein erwecktes Projekt:** ein effektiver Root, der selbst erweckt ist, oder sein nächster erweckter **Vorfahr** (`LocateAll`, wie heute `Locate`). Gibt es über alle Roots genau ein verschiedenes Projekt, gilt es; bei null oder mehreren geht es weiter. **Treffer in Unterverzeichnissen werden nie implizit gewählt** (G3, s. u.), schreibend wie lesend. Sie gibt es nur per Parameter.
3. **Prozess-cwd** wie heute (`Locate(env.Cwd, …)`), sofern nicht schon Stufe 2 dasselbe lieferte.
4. Sonst **Fehler mit Weg:** „Kein erwecktes Projekt bestimmt. Parameter `project` angeben (absoluter Pfad eines erweckten Projekts unterhalb der Roots: …)“. Als Kandidaten werden erweckte Projekte **in oder unter** den Roots aufgelistet (Scan bis Tiefe 2, max. 10, kanonisch, sortiert); sie sind genau die per Parameter wählbaren. Vorfahren der Roots erscheinen nicht als Kandidaten, weil der Parameter sie ablehnt. Tiefer liegende Projekte sind per Parameter trotzdem wählbar, nur nicht gelistet.

**Warum Unterverzeichnis-Treffer nie implizit gewählt werden (G3):** Eine Köder-`.monarch/system.yaml` in einem geklonten Repository unter dem Root wäre sonst womöglich der einzige Treffer und würde stillschweigend gewählt. Der Parameter verlangt ausdrückliche Absicht und taucht im Ledger und in der Antwort auf.

**Warum ein Root mit mehreren erweckten Projekten darunter nicht auto-auflöst:** `~/Projects` enthält `monarch`, `.monarch-worktrees/monarch-G-0103` und weitere. „Nimm das erste/neueste/das mit aktiver Quest“ wäre Raten, und ein Fehlgriff schreibt in die falsche Quest-Historie (Ledger-Kette, XP, `gate_plan`-Dateien). Das ist leise und schwer rückholbar, der Fehler dagegen billig: ein zweiter Aufruf mit `project`.

### 2. Warum Roots vor cwd, aber Parameter vor Roots

Roots stammen vom Client (User-Konfiguration, `/add-dir`) bzw. sind das unveränderliche Start-cwd. Deshalb sind sie die **Grenze** für den Parameter (Regel 1). Der Parameter schlägt die implizite Auflösung, weil er eine ausdrückliche Absicht ist und genau den mehrdeutigen Fall löst. Einschränkung zur Unabhängigkeit vom Agenten: siehe G2 in §8.

### 3. Validierung des Parameters `project`

Reihenfolge, jede Stufe fail-closed, Fehlertext nennt die Stufe:

1. Zeichenkette, nicht leer, **absolut** (kein `~`, keine Variablen, kein relativer Pfad; relativ wäre vom Prozess-cwd abhängig, genau das Problem).
2. Kanonisierung wie `PathResolver`: lexikalisch (`..`, `.`, doppelte Separatoren) **und** physisch (Symlinks aufgelöst, auf APFS mit der Faltungstabelle aus ADR-0011 Regel 8). Beide Ergebnisse müssen auf dasselbe Verzeichnis zeigen; weicht die physische Auflösung aus dem lexikalischen Baum heraus, gilt der physische Pfad für alle weiteren Prüfungen (auch die Root-Grenze).
3. Verzeichnis **existiert**.
4. `<projekt>/.monarch/system.yaml` existiert als reguläre Datei (nicht nur ein Köder-`.monarch`, ADR-0011 Regel 6). Hinweis: `Locate` prüft heute nur das Verzeichnis; die neue Prüfung gilt für Parameter **und** für implizite Treffer.
5. **Physischer Vergleich wie `LocateAll` (ProjectPaths.cs:60-64) (T4):** `<projekt>/.monarch` darf physisch weder `~/.monarch` noch `MONARCH_HOME` sein (Identität des aufgelösten Verzeichnisses, nicht Pfadstring). Das Projekt darf außerdem nicht unter `~/.monarch` oder `MONARCH_HOME` liegen, auch nicht per Symlink. Fälle: `project=$HOME` (dessen `.monarch` ist `~/.monarch`) und das **Elternverzeichnis von `MONARCH_HOME`**, falls `MONARCH_HOME` dort als `<elternverz>/.monarch` liegt.
6. Der Parameter bezeichnet die **Projekt-Root**, nicht ein Unterverzeichnis: Ist `.monarch` nicht direkt darunter, ist es ein Fehler (keine Aufwärtssuche, sonst wäre `project=<Unterordner>` eine zweite Mehrdeutigkeit).
7. Root-Grenze (Regel 1) mit den effektiven Roots.

Die Auflösung liefert ein Tupel `(ProjectPaths, Quelle)` mit `Quelle ∈ {parameter, roots, cwd}`. **Alle weiteren Dateioperationen des Aufrufs leiten ihre Pfade ausschließlich aus diesem einen `ProjectPaths` ab.**

### 4. Verhalten je Tool

Das Schema jedes Tools bekommt das optionale Feld `project` (string, absoluter Pfad). Das cwd der Tools (`env.Cwd`) wird für den Aufruf auf das aufgelöste Projekt gesetzt (`env with { Cwd = paths.Root }`), damit Delegation (`StatusCommand`, `QuestCommands`, `EvidenceCommands`) nicht still das Prozess-cwd benutzt. **Jede Tool-Antwort beginnt mit `project: <root> (source: parameter|roots|cwd)`** (Gegenmittel zu G1); bei fehlendem Projekt mit `project: null`.

| Tool | Verhalten |
|---|---|
| `system_status` (lesend) | Wie oben. Ohne Projekt Fehler mit Weg. |
| `mana_forecast` (lesend) | **Funktioniert ohne Projekt.** Der Snapshot liegt global unter `~/.monarch`, der Kalibrierfaktor aber **je Projekt** in `local/mana-calibration.json`. Ohne aufgelöstes Projekt gilt Faktor 1,0; die Ausgabe nennt `project:null`, „unkalibriert“ und plan-default. Mit Projekt wie heute. Offen für die Code-Phase: prüfen, was `ManaCommands.Forecast` sonst aus `paths` liest. Der Parameter wird nicht verlangt, aber akzeptiert und validiert. |
| `quest_transition` (schreibend) | Projekt Pflicht. Die Quest-ID wird nur im aufgelösten Projekt gesucht; nie projektübergreifend. |
| `gate_plan` (schreibend) | Projekt Pflicht. Schreibt nur in dessen `.monarch/gates` und `.monarch/quests` (bestehende Risk-Tag-Prüfung gegen dessen Quest-Scope). |
| `knowledge_write` (schreibend) | Projekt Pflicht. Bleibt auf `.monarch/knowledge/` **dieses** Projekts beschränkt (relativer Pfad, kein `..`, kein Symlink-Ausbruch, wie heute), gemessen an der Wurzel des aufgelösten Projekts. |
| `evidence_sync` (schreibend) | Projekt Pflicht. Ruft Forge und Ledger nur des aufgelösten Projekts. |

Schreibende Tools mit Quelle `parameter` unterliegen zusätzlich dem Gegenmechanismus aus Punkt 5.

### 5. Sicherheit gegen ADR-0011

**Befund.** ADR-0011 nennt „MCP-Tools mit Pfad-Argumenten“ ausdrücklich als Lücke außerhalb von Phase 0. Der Parameter `project` ist genau so ein Argument: Der Hook (`ToolCallPolicy`) sieht MCP-Aufrufe nicht, weder Roots noch Scope Lock noch Schutzpfade des Ziel-Projekts. Ein Agent kann also per `project` in ein **fremdes erwecktes Projekt** schreiben, etwa dort `quest_transition accept` auslösen, `gate_plan` Dateien anlegen lassen oder `knowledge_write` aufrufen. Das ist **durch die Policy nicht abgedeckt**.

**Einordnung.** Es ist kein neuer Fähigkeitsgewinn, aber eine neue bequeme und unauffällige Route: Der Agent kann dasselbe heute per Bash (`cd /fremd && ~/.monarch/bin/monarch quest accept Q-…`), das ADR-0011 Regel 2 als legitime Arbeit positiv listet. Der Parameter darf die Grenze nicht **zusätzlich** aufweichen. Er wird so begrenzt:

**Gegenmechanismus (Pflicht).**
1. **Effektive Roots sind die Grenze, nicht die Policy** (§0): Parameter nur unter einem effektiven Root (Regel 1). Das gilt auch ohne Client-Roots (dann das Start-cwd), schreibend wie lesend.
2. **Kein Cross-Projekt-Zugriff:** ein Aufruf, ein Projekt (Regel 3, letzter Satz); ein Test erzwingt, dass der Aufruf keine Datei außerhalb des aufgelösten Projekts (und des `~/.monarch`-Snapshots für Mana) berührt.
3. **Sichtbarkeit im Ziel:** Das Ledger des **Zielprojekts** erhält bei schreibenden Tools das Ereignis mit Quelle (§6), sodass ein Fremdschreiben dort in der Hash-Kette steht und `ledger verify` sowie das Review es sehen. Das **schützt nicht**, es macht erkennbar.
4. **Tool-Pfade:** `gate_plan`/`knowledge_write` schreiben nur in `.monarch/gates|quests|knowledge`; `system.yaml` und `~/.monarch` bleiben für MCP unerreichbar (Regel 3.5 und Test).
5. **`local/` ist für MCP erreichbar, aber nur über das Binary (T2).** Das Binary schreibt dort tatsächlich:
   - jeder Ledger-Append: `local/ledger-heads.json` und `local/writer-id` (JsonlLedgerStore.cs:9-11, LedgerFiles.cs:14);
   - `quest_transition`: `local/active.json` (QuestCommands.cs:102) und `local/mana-calibration.json` (ManaCalibrationStore.cs:42/174).

   **Folge:** Ein fremdes `accept`/`start` setzt die **aktive Quest** des fremden Projekts und damit dessen **Scope Lock**. Das ist der gewichtigste Effekt eines Fremdschreibens, nicht nur ein Ledger-Eintrag. Die Tests prüfen daher nicht „`local/` unverändert“, sondern: nach `project=A` sind in B alle Dateien einschließlich `local/` byte-identisch; in A ändern sich nur die erwarteten Dateien (`.monarch/gates|quests|knowledge`, Ledger, `local/ledger-heads.json`, `local/writer-id`, bei `quest_transition` zusätzlich `local/active.json` und `local/mana-calibration.json`); `system.yaml` bleibt unverändert.

**Restrisiko (dokumentieren in `plugin/README.md`, „Grenzen“):** siehe §8, G1 und G2.

### 6. Ledger (T3)

Jeder MCP-Aufruf eines **schreibenden** Tools protokolliert im Ledger des aufgelösten Projekts ein Ereignis `McpProjectResolved` (vor der eigentlichen Wirkung, Hash-Kette): `tool`, `project` (kanonischer Pfad), `source` (`parameter`|`roots`|`cwd`), `roots_count`, `cwd_project` (Projekt des Prozess-cwd oder leer), `session`.

- **`session`** ist `CLAUDE_CODE_SESSION_ID` des Aufrufers (über `env.Vars`), sonst `mcp-<id>`. Sie kommt **nie** aus `local/current-session` des Zielprojekts (State.cs:171); sonst würde die Sitzung eines fremden Projekts vorgetäuscht.
- **Lesende Tools** (`readOnlyHint`, McpServer.cs:124) schreiben **kein** Ledger, auch nicht bei `source=parameter`. Ihre Sichtbarkeit ist der Antwort-Header (§4).
- **`McpRootsObserved`** geht **nur auf stderr** (kein Ledger), ebenso `mcp: roots=[…]`.
- Schlägt die Auflösung fehl, gibt es kein Ledger (kein Projekt) und die Fehlermeldung geht auf stderr des Servers.
- Ein Ledger-Append schreibt zusätzlich die `local/`-Dateien aus §5.5.

**Messung der Roots (Code-Phase, Pflicht, Auflage 7):** Beim Empfang von `roots/list` und `roots/list_changed` schreibt der Server eine Zeile `mcp: roots=[…]` auf stderr. Damit wird in einer echten Session gemessen: (a) Start in `~/Projects`, (b) Start im Projekt, (c) nach `/add-dir`, (d) nach `cd` per Bash, (e) **Subagent mit Worktree-Isolation** (welche Roots sieht der MCP-Server, wenn der Subagent in einem Worktree läuft?), (f) **Ist das Start-cwd immer ein Root?** (Anlass: die Meldung „staging root … omitted from roots/list“ legt nahe, dass Roots ausgelassen werden können). Das Ergebnis wird als Nachtrag in dieses ADR eingetragen, **bevor der Code freigegeben wird**. Fällt (f) mit „nein“ aus, bleibt §0 trotzdem tragfähig (Start-cwd als Root bei leerer Liste), die Aussage zu nicht deklarierten Roots muss dann aber um „Start-cwd fehlt in nichtleerer Liste“ ergänzt werden.

### 7. Testplan (hermetisch)

Alle Tests laufen ohne Netz und ohne echtes `$HOME`: Temp-Verzeichnisse, `MONARCH_HOME` und `UserHome` (via `CliEnv`) injiziert. Fake-Client: ein Test-Harness spricht JSON-RPC über `StringReader`/`StringWriter` mit deterministischen Request-IDs, deklariert `roots` und beantwortet `roots/list` mit vorgegebenen URIs.

- **Reihenfolge:** Parameter unter Root → parameter; kein Parameter, Root selbst erweckt → roots; erweckter Vorfahr des Roots → roots; ein einziges erwecktes Projekt nur in einem Unterverzeichnis (auch Köder-Klon) → **kein** impliziter Treffer, Fehler mit Kandidatenliste; zwei erweckte Projekte unter Root → Fehler mit Kandidatenliste; Root nicht erweckt und cwd erweckt → cwd; nichts → Fehler „Parameter `project` angeben“; Parameter außerhalb aller Roots → Fehler **ohne** Fallback auf cwd; Parameter = Vorfahr des Roots → Fehler.
- **Roots-Ereignisse:** `list_changed` ersetzt die Liste; Client ohne `roots`-Capability; leere Liste; `roots/list` antwortet mit Fehler oder Timeout (effektiver Root = cwd, Zeile auf stderr); Nicht-`file`-URI wird ignoriert; Präfix-Falle (`/a/proj` vs. `/a/proj-evil`); `file://localhost/…`, Percent-Decoding; fremder Host wird verworfen.
- **Ohne Roots (T1):** Start-cwd `~/Projects` (nicht erweckt), `gate_plan` und `knowledge_write` mit `project=<erwecktes Projekt darunter>` funktionieren; `project` außerhalb des cwd → Fehler.
- **Pfad-Varianten für `project`:** relativ, `~/x`, `$HOME/x`, leer, Datei statt Verzeichnis, nicht existent, existent aber nicht erweckt, `.monarch` ohne `system.yaml` (Köder), Symlink auf erwecktes Projekt außerhalb der Roots (Deny, physisch geprüft), Symlink innerhalb auf innerhalb (ok), `..`-Ausbruch aus dem Root, Unterverzeichnis eines Projekts, `~/.monarch`, `MONARCH_HOME`, Symlink darauf, Pfad darunter, **`project=$HOME` und Elternverzeichnis von `MONARCH_HOME` (T4)**, APFS-Faltungsvariante (`ſ`; plattformabhängig, nur macOS erwartet Deny, ubuntu anderes Ergebnis).
- **Projektgrenzen:** Zwei erweckte Projekte A/B unter einem Root. Aufruf mit `project=A`: Datei-Hash-Vergleich **aller** Dateien von B (inkl. `local/`) vor und nach jedem der sechs Tools identisch; `quest_transition` mit einer Quest-ID, die nur in B existiert, schlägt in A fehl; `knowledge_write` mit `../../B/.monarch/knowledge/x` und per Symlink in A auf B → Deny; `gate_plan` schreibt nur nach `A/.monarch/gates|quests`; in A ändert sich nur die erwartete Dateimenge (§5.5). **Zwilling-Worktree:** Haupt-Checkout und Worktree mit identischer Quest-ID; `project=<Worktree>` ändert den Haupt-Checkout nicht und umgekehrt. **Verschachteltes A/x:** erwecktes Projekt A mit erwecktem Unterprojekt A/x; ohne Parameter wird A/x nie implizit gewählt, `project=A` schreibt nicht in A/x.
- **Mana:** `mana_forecast` ohne Projekt liefert Faktor 1,0, `project:null`, „unkalibriert“, plan-default; mit Projekt unverändert (Regressionsvergleich), Faktor aus dessen `local/mana-calibration.json`.
- **Ledger:** `McpProjectResolved` steht im Ledger des Zielprojekts mit richtiger Quelle und korrekter Session (`CLAUDE_CODE_SESSION_ID` bzw. `mcp-<id>`, nie `local/current-session` des Ziels), nicht in einem anderen; `ledger verify` bleibt grün; **lesende Tools schreiben nichts** (weder Ledger noch `local/`); Auflösungsfehler schreiben nirgends.
- **Antwort:** Jede Tool-Antwort beginnt mit `project: <root> (source: …)`.
- **Protokoll:** `tools/call` vor der Roots-Antwort wartet mit injiziertem Timeout und fällt dann auf den cwd-Root zurück; Antworten werden per id zugeordnet.
- **Rückwärtskompatibilität:** bestehende MCP-Tests ohne Roots und ohne Parameter bleiben grün (Quelle cwd); sie müssen den neuen Antwort-Header berücksichtigen.

### 8. Grenzen und Folgen (ehrlich nach ADR-0011)

- **G1 — Fehlgriff unter einem Root.** Ein Agent unter einem Root kann in ein fremdes Projekt schreiben, etwa wenn er Worktree und Hauptcheckout bei identischen Quest-IDs verwechselt. Das ist ein plausibler Fehlgriff und **kein G4-Fall** (kein feindlicher Agent nötig). Es fällt unter ADR-0011 „Ausdrücklich außerhalb“. Gegenmittel, die mindern, aber nicht verhindern: der Antwort-Header `project: <root> (source: …)` bei jedem Aufruf und der Ledger-Eintrag im Ziel.
- **G2 — „Roots kann der Agent nicht setzen“ gilt nur eingeschränkt.** Im nicht erweckten Start-cwd gibt es keine Policy (ADR-0011). Dort kann der Agent `.claude/settings*.json` (`additionalDirectories`) schreiben und so die Roots erweitern. Die Root-Grenze ist daher dort nur eine Hürde, keine Durchsetzung. Wer das nicht will, startet die Session im Projekt statt in einem Elternverzeichnis.
- **G3 — Köder-`system.yaml`.** Ein Klon unter dem Root kann eine `.monarch/system.yaml` mitbringen. Abwehr: implizit nur Root selbst oder Vorfahr, Unterverzeichnisse nur per Parameter (§1).
- Der Parameter bleibt ein MCP-Pfad-Argument im Sinn von ADR-0011 und eine dokumentierte Grenze (G4 bleibt für feindliche Agenten); effektive Roots und Ledger-Sichtbarkeit mindern sie, heben sie nicht auf.
- Die Messung der Roots (§6) ist Voraussetzung für die Aussage „Start in `~/Projects` ist gelöst“; fällt sie anders aus als vermutet, bleibt der Parameter der Weg, und die Fehlermeldung der Stufe 4 ist die Bedienungsanleitung.
- Der Kandidaten-Scan geht bis Tiefe 2. Das deckt `.monarch-worktrees/<name>` (Tiefe 2) ab; Projekte tiefer als 2 erscheinen nicht in der Liste, sind aber per Parameter wählbar, solange sie unter einem Root liegen.
- Die Eindeutigkeitsregel verlangt bei Mehrfachtreffern einen zusätzlichen Aufruf. Das ist gewollt.
- Folge-Quests: Hook-seitige Sicht auf MCP-Aufrufe (ADR-0009), Registrierung verschachtelter Projekte (ADR-0011 Regel 6), `plugin/README.md` „Grenzen“ um Parameter, G1 und G2 ergänzen (DoD der Umsetzungs-Quest).
- Keine Änderung an `PathResolver`, Policy oder Risk Tags in diesem ADR; Umsetzung ist eine eigene Quest mit TDD.

## Auflagen für die Code-Phase

1. `roots/list` erst nach `notifications/initialized` anfragen. Antworten per id zuordnen; McpServer.cs:73 beantwortet sie heute mit -32600. Ein `tools/call` vor der Antwort wartet mit injiziertem Timeout. Nur `file://` mit leerem Host oder `localhost`, Percent-Decoding.
2. `ProjectPaths.Root` ist der physische realpath. Eine Delegation mit `Cwd=Root` ergibt denselben Root.
3. `CliEnv` bekommt `UserHome`, für hermetische Tests (ProjectPaths.cs:55).
4. Der Scan folgt keinen Symlinks, überspringt unlesbare Verzeichnisse (macOS TCC) und sortiert die Kandidaten.
5. Der ſ-Test wird plattformabhängig (ubuntu/macos).
6. Tests: Zwilling-Worktree mit gleicher Quest-ID, verschachteltes A/x, `$HOME` und Elternverzeichnis von `MONARCH_HOME`, `tools/call` vor der Roots-Antwort, Session-Zuordnung, lesende Tools schreiben nichts, die Antwort nennt das Projekt, deterministische Request-IDs im StringReader-Harness.
7. Die Roots-Messung mit echten Daten (a–f) steht als Nachtrag im ADR, bevor der Code freigegeben wird.
