# ADR-0019 — Produktname Umbrarch

**Datum:** 19.11.2026 · **Status:** angenommen. Freigabe laut Owner-Prompt vom 19.11.2026, wörtlich: „Hält sich das ADR an Entscheid und Umfang dieser Prompt, gilt es als freigegeben. Nur bei Abweichungen fragst du.“ · **Quest:** Q-0185 (G-0127) · **Folge-Gate v0.2:** #153 · **Löst ab:** ADR-0007, Zeile 81 (Warden-Typen), siehe Entscheidung 5b

## Kurz in einfacher Sprache

- Das Produkt heißt ab jetzt **Umbrarch**. Der alte Name kollidiert im Claude-Plugin-Verzeichnis mit einem anderen Plugin.
- Jetzt ändert sich nur, was Nutzer sehen: Plugin-Name, Marketplace, MCP-Server, Verweise wie `umbrarch:warden`, Slash-Befehle wie `/umbrarch:awaken` und die READMEs.
- Innen bleibt vorerst alles beim Codenamen Monarch: Binary `monarch`, Ordner `.monarch/` und `~/.monarch`, Namespaces `Monarch.*`. Das kommt in v0.2 mit Migration (#153).
- Die Regel für read-only Shadows wird **strenger, nicht lockerer**: Ein Shadow, der read-only ist, bleibt read-only, egal welcher Plugin-Name vor dem Doppelpunkt steht.
- Für `gate merge` zählt nur noch ein Warden-Lauf von `umbrarch:warden` oder `warden`. Der alte Name `monarch:warden` zählt nicht mehr. Das ist ein Austausch eines Namens gegen einen anderen, ohne Wildcard.
- Der Plugin-Name steht im Code an genau einer Stelle.

## Owner-Entscheid (wörtlich)

Owner im Chat, 19.11.2026:

> „Neuer Produktname: Umbrarch. ‚Monarch‘ kollidiert im Claude-Plugin-Verzeichnis mit dem Plugin von Monarch Money. Jetzt nur die sichtbare Schicht; interne Namen folgen in v0.2 mit Migration. Das Repo bleibt öffentlich.“

## Markencheck

Owner im Chat, 19.11.2026, wörtlich: „Markencheck: TMview am 19.11.2026 ohne Treffer zu „UMBRARCH“ (ins ADR aufnehmen).“ Die Session hat die Suche nicht selbst wiederholt; Beleg ist diese Angabe des Owners.

## Repo

Das GitHub-Repo heißt `DavidOeztuerk/umbrarch` und ist öffentlich. Beleg: `gh repo view DavidOeztuerk/umbrarch --json visibility` am 19.11.2026 ergibt `PUBLIC`; `git remote -v` zeigt `https://github.com/DavidOeztuerk/umbrarch.git`. Der Nachtrag in ADR-0006 hält fest, dass die Namensprüfung erledigt ist.

## Umfang (Zusammenfassung der Owner-Prompt vom 19.11.2026)

Jetzt (sichtbare Schicht):

1. `plugin/.claude-plugin/plugin.json`: name `umbrarch`. `.claude-plugin/marketplace.json`: Marketplace `umbrarch-local`, Plugin `umbrarch`. Beschreibungen angepasst.
2. `plugin/.mcp.json`: Server `umbrarch`. Die Tools heißen danach `mcp__plugin_umbrarch_umbrarch__*`. Alle `tools:`-Listen in `plugin/agents/*.md` und jede Nennung in Skills und READMEs folgen.
3. Verweise `monarch:<name>` werden `umbrarch:<name>`: in Skills, Agents, `CLAUDE.md`, `RuleTexts.StandardRulesLine`, Hook-Texten und Slash-Befehlen (`/umbrarch:awaken` …).
4. Der Plugin-Name steht im Code an genau einer Stelle (eine Konstante).
5. Schutzregeln fail-closed, mit Tests, auch für den Übergang „neues Binary, altes Plugin“ (Entscheidung 5a und 5b unten).
6. `README.md`, `docs/`, `plugin/README.md`: Produktname Umbrarch, Repo-URL `github.com/DavidOeztuerk/umbrarch`, neue Installationsbefehle und der Hinweis zum Codenamen.

Bleibt bis v0.2, wörtlich aus der Owner-Prompt (eigenes Gate mit Migration, #153):

```text
Binary und Wrapper monarch, ~/.monarch, .monarch/, theme: monarch, Branch-Präfix monarch/, Namespaces und Projekte Monarch.*, monarch.slnx, Issue-Marker <!-- monarch:Q-… --> (sonst doppelte Board-Issues), Ledger-Inhalte (Hash-Kette; alte actor-Werte „monarch:*“ bleiben).
```

## Entscheidung 4 — Plugin-Name als eine Konstante

`RuleTexts.PluginName = "umbrarch"` in `src/Monarch.Cli/RuleTexts.cs`. Die Datei liegt im Risk-Tag `policy` (`.monarch/system.yaml:20`), eine spätere Änderung des Namens ist also eine Rank-A-Quest. Davon abgeleitet sind:

- die Warden-Typen in `GateMergeCommands.cs` (Entscheidung 5b),
- der Verweis auf den Skill `umbrarch:regeln` in `RuleTexts.StandardRulesLine` (Briefing beim SessionStart),
- der Name, den der MCP-Server beim Start meldet (`McpTools.cs:26`, heute das Literal `"monarch"`; Punkt 2 und 4).

Auch der Doku-Kommentar und die Meldung zum Warden-Lauf in `GateMergeCommands.cs` (heute `:613`, `:641`) nennen den Namen über die Konstante, nicht als Literal. Die Policy (`ToolCallPolicy.cs`) braucht den Namen nicht mehr (Entscheidung 5a). Ein Repo-Test prüft, dass in `src/**/*.cs` genau eine Zeile `umbrarch` nennt und dass Manifeste, MCP-Server und `tools:`-Listen der Agents mit der Konstante übereinstimmen.

## Entscheidung 5a — ShadowReadOnly ohne Plugin-Präfix

Owner-Prompt, wörtlich: „a) ShadowReadOnly (ToolCallPolicy.cs): das Präfix bis zum letzten „:“ immer abschneiden, egal welcher Plugin-Name. Ein alter oder fremder Präfix („monarch:warden“, „x:scout“) bleibt so read-only. Das ist strenger als heute, nicht lockerer.“

**Heute** (`src/Monarch.Policy/ToolCallPolicy.cs:451-464` auf dc8ec9c): Abgeschnitten wird nur das Präfix `monarch:` (`:455`). Read-only ist ein Shadow, wenn der rohe Agent-Typ oder der Name ohne `monarch:` in `read_only_shadows` steht (`:457`). Folge: `x:scout` darf schreiben. Nach dem Wechsel käme der Warden als `umbrarch:warden`; mit dem heutigen Code dürfte er schreiben. Der Namenswechsel ohne diese Änderung wäre also eine Lockerung.

**Neu:**

- Der Name eines Agent-Typs ist der Teil nach dem letzten `:`; ohne `:` ist es der ganze Wert.
- Dasselbe gilt für jeden Eintrag in `read_only_shadows`. Das ist eine Ableitung der Session, kein Owner-Zitat (Begründung unten; technische Prüfung durch den Warden).
- Read-only ist ein Shadow, wenn sein Name gleich dem Namen eines Eintrags ist. Verglichen wird wie heute zeichengenau (ordinal).

Beispiele mit der Standardliste `warden, scout`: `warden`, `monarch:warden`, `umbrarch:warden`, `x:scout` und `a:b:warden` sind read-only. `umbrarch:knight` darf schreiben.

**Warum auch die Einträge:** Ein Eintrag `monarch:scribe` (so steht es im Ledger, Test `ToolCallPolicyTests.cs:207-208`) schützt heute den Shadow `monarch:scribe`. Nach dem Wechsel heißt er `umbrarch:scribe`. Würde nur der Agent-Typ gekürzt, fiele dieser Schutz weg.

**Strenger, nie lockerer (Beweis):** Heute ist ein Shadow read-only, wenn der rohe Typ `t` in der Liste steht oder `t` ohne `monarch:` darin steht. In beiden Fällen ist der Name von `t` gleich dem Namen dieses Eintrags, denn ein vorne entferntes `monarch:` ändert nichts an dem, was nach dem letzten `:` steht. Jeder Fall, der heute read-only ist, bleibt es also. Neu read-only werden nur:

- Fälle mit einem anderen Präfix (`x:scout`, `umbrarch:warden`),
- nackte oder anders präfigierte Namen zu einem Eintrag mit Präfix (`scribe` und `umbrarch:scribe` zu `monarch:scribe`),
- ein Typ mit leerem Namen (`warden:`), aber nur, wenn auch ein Eintrag einen leeren Namen hat (etwa `x:`, über `read_only_shadows` in `system.yaml` möglich, `SystemConfig.cs:53`, `:99`). Mit der Standardliste ist er wie heute nicht read-only.

Unverändert bleiben: welche Werkzeuge und Git-Befehle verweigert werden (`:458-463`) und die Groß- und Kleinschreibung.

## Entscheidung 5b — Warden-Nachweis nur umbrarch:warden und warden

Owner-Prompt, wörtlich: „b) Warden-Nachweis für gate verdict (GateMergeCommands.WardenTypes): nur „umbrarch:warden“ und „warden“. Kein alter Name, keine Wildcard. Ein alter Präfix führt zur Verweigerung.“

**Heute** (`src/Monarch.Cli/GateMergeCommands.cs:18`): `["monarch:warden", "warden"]`, geprüft per exaktem Vergleich (`:638`). Das legt auch ADR-0007, Zeile 81 fest.

**Neu:** `[PluginName + ":warden", "warden"]`, also genau `umbrarch:warden` und `warden`. Der Vergleich bleibt exakt; der Agent-Typ wird hier **nicht** gekürzt. Wie heute zählen genau zwei feste Namen; `monarch:warden` wird durch `umbrarch:warden` ersetzt. Strenger ist das nur für den alten Namen, eine Lockerung über weitere Namen gibt es nicht. Ein Lauf von `monarch:warden`, `x:warden`, `umbrarch:warden-extra` oder `Umbrarch:warden` zählt nicht. `gate merge` verweigert dann mit „no warden run (SubagentStop of umbrarch:warden or warden) …“. Für diesen Punkt löst dieses ADR die Zeile 81 in ADR-0007 ab. ADR-0007 selbst bleibt unverändert (CLAUDE.md Regel 3).

## Übergang zwischen altem und neuem Stand

Der Wrapper startet `~/.monarch/bin/<version>/monarch` mit der Version aus `plugin/scripts/monarch:4` (`MONARCH_VERSION=0.1.0`, Pfad `:65-67`). `scripts/install-local.sh` installiert unter der Version aus `plugin/.claude-plugin/plugin.json:3` (`0.1.0`; Skript `:9`, `:25`). G-0127 ändert keine der beiden Versionsangaben. Altes und neues Binary liegen also unter demselben Pfad, und beide Mischformen sind möglich.

| Binary | Plugin | Agent-Typen | ShadowReadOnly | Warden-Nachweis |
|---|---|---|---|---|
| neu | alt (`monarch@monarch-local`) | `monarch:*` | read-only bleibt (5a) | `monarch:warden` zählt nicht, `gate merge` verweigert (5b) |
| neu | neu (`umbrarch@umbrarch-local`) | `umbrarch:*` | read-only (5a) | `umbrarch:warden` zählt |
| alt | neu | `umbrarch:*` | **read-only greift nicht**: das alte Binary kürzt nur `monarch:` | `umbrarch:warden` zählt nicht, `gate merge` verweigert |

**Risiko in der dritten Zeile:** Ein altes Binary mit dem neuen Plugin lässt Warden und Scout schreiben. Dieses ADR schließt das nicht im Code; das alte Binary lässt sich nicht nachträglich ändern. Abhilfe ist die Reihenfolge beim Owner: erst den Checkout aktualisieren und `scripts/install-local.sh` ausführen (neues Binary), dann das Plugin wechseln, dann Claude Code neu starten. Zwischen diesen Schritten keine Claude-Code-Session neu starten: Der lokale Marketplace liest das Plugin aus dem Checkout (`claude plugin list` am 19.11.2026: `monarch@monarch-local`, „Read from: /Users/davidozturk/Projects/monarch/plugin“).

Eine neue Versionsnummer würde die dritte Zeile fail-closed machen: Der neue Wrapper fände kein Binary und fragt in einem erweckten Projekt nach (`plugin/scripts/monarch:46-48`, `:67`). Die Owner-Prompt nennt das Release aber „v0.1.0“. Darum ist das hier nur ein Vorschlag und eine Frage an den Owner, keine Entscheidung.

## Bestandsaufnahme

Befehl: `git grep -n -i monarch -- . ':!.monarch/ledger'` auf `dc8ec9c` (develop vor G-0127), 2319 Zeilen. Ein Treffer gehört zu „jetzt“, wenn er unter Punkt 1 bis 6 fällt, sonst zu „v0.2“. Einträge, die die Owner-Prompt nicht ausdrücklich nennt, sind mit „Vorschlag“ markiert.

| Datei | Fundstelle | jetzt oder v0.2 | Inhalt |
|---|---|---|---|
| `.claude-plugin/marketplace.json` | :2, :3, :7 | jetzt | Marketplace `umbrarch-local`, Beschreibung, Plugin `umbrarch` (Punkt 1) |
| `plugin/.claude-plugin/plugin.json` | :2 | jetzt | name `umbrarch`; Beschreibung :4 ohne Treffer, wird angepasst (Punkt 1) |
| `plugin/.mcp.json` | :3 | jetzt | Server `umbrarch` (Punkt 2) |
| `plugin/.mcp.json` | :4 | v0.2 | Wrapper `scripts/monarch` |
| `plugin/agents/knight.md`, `plugin/agents/strategist.md` | je :5 | jetzt | `tools:`-Liste `mcp__plugin_umbrarch_umbrarch__*` (Punkt 2) |
| `plugin/agents/knight.md`, `plugin/agents/warden.md` | knight :15, :16, :17, :20; warden :19 | v0.2 | Binary, Branch-Präfix `monarch/`, `.monarch` |
| `plugin/skills/gate/SKILL.md` | :14 | jetzt | `umbrarch:strategist` (Punkt 3) |
| `plugin/skills/quest/SKILL.md` | :14 | jetzt | `umbrarch:knight`, `umbrarch:warden` (Punkt 3) |
| `plugin/skills/regeln/SKILL.md` | :55 | jetzt | `umbrarch:kritiker` (Punkt 3) |
| `plugin/skills/*/SKILL.md` | übrige der 73 Zeilen in 8 Dateien | v0.2 | Binary `~/.monarch/bin/monarch`, Branch-Präfix, `.monarch`; Produktname in Beschreibungen (Vorschlag) |
| `plugin/README.md` | :1, :3, :8, :17, :18, :21, :25, :50, :66, :125 | jetzt | Name, Installation, `/umbrarch:awaken`, MCP-Tools, Produktname im Fließtext (Punkt 2, 3, 6) |
| `plugin/README.md` | übrige der 31 Zeilen, dazu in :25 und :66 die Pfade und Befehle | v0.2 | Binary, Wrapper, `~/.monarch`, `.monarch` |
| `README.md` | :1, :3 | jetzt | Produktname (Punkt 6) |
| `README.md` | :10, :11 | v0.2 | Projekt `Monarch.Cli`, Binary |
| `scripts/install-local.sh` | :48 | jetzt | Installationsbefehl `umbrarch@umbrarch-local` (Punkt 6) |
| `scripts/install-local.sh` | übrige der 16 Zeilen | v0.2 | Binary, `~/.monarch` |
| `src/Monarch.Cli/RuleTexts.cs` | :9 | jetzt | Skill `umbrarch:regeln` im Briefing (Punkt 3); `monarch gate merge` in derselben Zeile bleibt (Binary) |
| `src/Monarch.Cli/GateMergeCommands.cs` | :18, :613, :641 | jetzt | Warden-Nachweis (5b) |
| `src/Monarch.Policy/ToolCallPolicy.cs` | :453, :455 | jetzt | ShadowReadOnly (5a) |
| `src/Monarch.Cli/McpTools.cs` | :26 | jetzt | Name des MCP-Servers aus der Konstante (Punkt 2 und 4) |
| `src/**` | übrige der 528 Zeilen in 114 Dateien | v0.2 | Namespaces `Monarch.*`, Pfade `.monarch/` und `~/.monarch`, Binary, `MONARCH_*`, Issue-Marker und Label `monarch` (`GitHubIssuesBoard.cs:12`, `:35`, `:67`, `:139`, `:143`, `:184`, `:224`, `:288`); Vorschlag: Texte im Binary (Briefing-Kopf, Deny-Texte), `CompositionRoot.cs:21` (`Repo = "monarch"`; produktiv liest der CLI owner/repo aus `origin` oder `boards.github`, `Support.cs:145-160`) |
| `tests/Monarch.Cli.Tests/GateMergeTests.cs` | :27, :60, :62, :63 | jetzt | Warden-Lauf als `umbrarch:warden` (5b) |
| `tests/Monarch.Cli.Tests/LedgerHistoryCliTests.cs`, `MergeMethodTests.cs` | :173; :65, :214 | jetzt | dito |
| `tests/Monarch.Cli.Tests/RulesAtPlaceTests.cs`, `KritikerShadowTests.cs` | :69, :91; :65 | jetzt | Briefing und Regel 11 (Punkt 3) |
| `tests/Monarch.Policy.Tests/ToolCallPolicyTests.cs` | :188, :205-208 | jetzt | ShadowReadOnly (5a) |
| `tests/**` (alte Präfixe als Testdaten) | `GateMergeTests.cs:333`, `:334`, `:460`, `:1145`, `:1156`; `WakeTests.cs:123`; `ProtectBranchesTests.cs:276`, `:277`, `:292`, `:293` | v0.2 | `monarch:knight` und Ähnliches; bleiben gültige Übergangsfälle |
| `tests/**` | übrige der 1071 Zeilen in 79 Dateien | v0.2 | Namespaces, Pfade, Binary |
| `CLAUDE.md` | :1 | v0.2 (Vorschlag) | Titel; die Datei enthält keinen Verweis `monarch:<name>` |
| `docs/adr/0006-lizenz-und-id-bereiche.md` | :9 | jetzt | Nachtrag: Namensprüfung erledigt, Repo öffentlich; der alte Text bleibt als Verlauf |
| `docs/adr/0019-produktname-umbrarch.md` | neu | jetzt | dieses ADR |
| `docs/adr/0001` bis `0018` | übrige der 254 Zeilen in 16 Dateien | v0.2 (Vorschlag, Frage an den Owner) | historische Entscheide; Änderung nur mit neuer Owner-Freigabe (CLAUDE.md Regel 3). Ausnahme ist der beauftragte Nachtrag in ADR-0006 |
| `docs/lessons.md`, `docs/evidence/**` | 3 Zeilen; 13 Zeilen in 8 Dateien | v0.2 (Vorschlag, Frage an den Owner) | datierter Bericht (Stand 06.11.2026) und aufgezeichnete Belege. Punkt 6 nennt `docs/`; die Session ändert hier trotzdem nichts, weil ein umbenannter Beleg nicht mehr zeigt, was damals lief |
| `monarch.slnx` | 14 Zeilen | v0.2 | Projekte `Monarch.*` |
| `.github/workflows/ci.yml` | 9 Zeilen | v0.2 | Binary, Artefaktname |
| `plugin/scripts/monarch`, `plugin/hooks/hooks.json`, `plugin/tests/wrapper-test.sh` | 16, 7, 20 Zeilen | v0.2 | Wrapper |
| `bench/hook-latency.sh`, `bench/test-gate.sh`, `e2e/agent-tamper.sh`, `tools/gen-casefold.py` | 28, 5, 18, 3 Zeilen | v0.2 | Binary, `MONARCH_*`, Namespace |
| `.gitignore` | :3 | v0.2 | `.monarch/local/` |
| `.monarch/system.yaml`, `.monarch/recap/RECAP.md` | 8, 9 Zeilen | v0.2 | `theme: monarch`, Pfade, Repo-Links im Recap |
| `.monarch/quests`, `.monarch/gates`, `.monarch/knowledge` | 118 Zeilen in 41 Dateien; 45 in 21; 11 in 6 | v0.2 | Monarch-Stand |
| `.monarch/ledger/**` | nicht im Befehl | v0.2 | Hash-Kette, alte Werte bleiben |

## Nicht in diesem ADR

- Alles unter „v0.2“: eigenes Gate mit Migration (#153).
- Ein Schutz gegen „altes Binary, neues Plugin“ im Code (siehe Übergang).
