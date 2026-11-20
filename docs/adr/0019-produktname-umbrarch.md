# ADR-0019 — Produktname Umbrarch

**Datum:** 19.11.2026 · **Status:** angenommen. Freigabe laut Owner-Prompt vom 19.11.2026, wörtlich: „Hält sich das ADR an Entscheid und Umfang dieser Prompt, gilt es als freigegeben. Nur bei Abweichungen fragst du.“ · **Überarbeitet** am 19.11.2026 nach Warden-Designrunde 1 (PR-Kommentar 7009959115) und den Owner-Antworten unten; **Freigabe des überarbeiteten Stands:** Owner-Entscheid vom 19.11.2026, wörtlich: „ADR-0019 im Stand 59bbaf45 ist freigegeben. Die Lücke im Mischzustand 5 (Aufruf von außen in ein erwecktes Projekt, Wrapper 0.1.1 mit Binary 0.1.0) nehme ich hin; sie kommt als eigenes Issue in den Milestone v0.2. Freigegeben sind damit auch: dieser Eintrag im Status, die Antwort in „Nicht in diesem ADR“ und die Korrektur des Belegs zu Zeile 105 aus #155.“ · **Quest:** Q-0185 (G-0127) · **Folge-Gate v0.2:** #153 · **Löst ab:** ADR-0007, Zeile 81 (Warden-Typen), siehe Entscheidung 5b

## Kurz in einfacher Sprache

- Das Produkt heißt ab jetzt **Umbrarch**. Der alte Name kollidiert im Claude-Plugin-Verzeichnis mit einem anderen Plugin.
- Jetzt ändert sich nur, was Nutzer sehen: Plugin-Name, Marketplace, MCP-Server, Verweise wie `umbrarch:warden`, Slash-Befehle wie `/umbrarch:awaken` und die READMEs.
- Innen bleibt vorerst alles beim Codenamen Monarch: Binary `monarch`, Ordner `.monarch/` und `~/.monarch`, Namespaces `Monarch.*`. Das kommt in v0.2 mit Migration (#153).
- Die Regel für read-only Shadows wird **strenger, nicht lockerer**: Ein Shadow, der read-only ist, bleibt read-only, egal welcher Plugin-Name vor dem Doppelpunkt steht.
- Für `gate merge` zählt nur noch ein Warden-Lauf von `umbrarch:warden` oder `warden`. Der alte Name `monarch:warden` zählt nicht mehr. Das ist ein Austausch eines Namens gegen einen anderen, ohne Wildcard.
- Der Plugin-Name steht im Code an genau einer Stelle.
- Die Version steigt auf 0.1.1. Dann findet ein neuer Wrapper ein altes Binary nicht und fragt nach, wenn der Aufruf aus einem erweckten Projekt kommt. **Aufrufe von außen in ein erwecktes Projekt laufen in diesem Mischzustand ohne Regel** (Lücke, Abschnitt „Übergang“; vom Owner am 19.11.2026 hingenommen, Issue #158). Das Release heißt v0.1.1.
- Der Owner muss die Befehle zum Wechsel in fester Reihenfolge ausführen und danach prüfen, dass nur `umbrarch` aktiv ist (Abschnitt „Übergang“).

## Owner-Entscheid (wörtlich)

Owner im Chat, 19.11.2026:

> „Neuer Produktname: Umbrarch. ‚Monarch‘ kollidiert im Claude-Plugin-Verzeichnis mit dem Plugin von Monarch Money. Jetzt nur die sichtbare Schicht; interne Namen folgen in v0.2 mit Migration. Das Repo bleibt öffentlich.“

## Owner-Antworten auf die offenen Fragen (wörtlich)

Der erste Stand dieses ADR (Head 84b6a10) hatte drei Fragen offen. Antworten des Owners, 19.11.2026, wörtlich aus der Fortsetzungs-Prompt:

1. „Alte Dokumente bleiben, wie sie sind: ADR-0001 bis 0018, docs/lessons.md und docs/evidence/ behalten den Namen Monarch.“
2. „Skill-Beschreibungen bekommen den Namen Umbrarch jetzt, weil sie im Plugin sichtbar sind. Briefing-Kopf und CLAUDE.md-Titel erst in v0.2.“
3. „Ja, Versionssprung auf 0.1.1. Fail-closed geht vor dem Release-Namen. Das Release heißt v0.1.1.“

Damit sind keine Fragen mehr offen. Ableitungen der Session aus diesen Antworten (keine Owner-Zitate): Die Skill-Beschreibungen (`description` in `plugin/skills/*/SKILL.md`, je Zeile 3) nennen künftig Umbrarch; Briefing-Kopf und Titelzeile von `CLAUDE.md` bleiben bis v0.2 (so sagt es die Antwort selbst). Auf andere Texte im Binary dehnt die Session die Antwort nicht aus. Der Versionssprung betrifft die Stellen im Abschnitt „Übergang“.

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
6. `README.md`, `docs/`, `plugin/README.md`: Produktname Umbrarch, Repo-URL `github.com/DavidOeztuerk/umbrarch`, neue Installationsbefehle und der Hinweis zum Codenamen. Pfade zum lokalen Ordner stehen als Platzhalter `<repo>`. Von `docs/` ändert sich nur, was die Owner-Antwort 1 nicht ausnimmt (ADR-0001 bis 0018, `docs/lessons.md` und `docs/evidence/` bleiben).
7. Die `description` der Skills (`plugin/skills/*/SKILL.md`) nennt Umbrarch (Owner-Antwort 2).
8. Version 0.1.1 (Owner-Antwort 3, Abschnitt „Übergang“).

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

**Warum auch die Einträge:** Ein Eintrag `monarch:scribe` (so ein Eintrag steht im Test `ToolCallPolicyTests.cs:207-208`, dessen Kommentar lautet „so schreibt man es aus dem Ledger ab“) schützt heute den Shadow `monarch:scribe`. Nach dem Wechsel heißt er `umbrarch:scribe`. Würde nur der Agent-Typ gekürzt, fiele dieser Schutz weg.

**Strenger, nie lockerer (Beweis):** Heute ist ein Shadow read-only, wenn der rohe Typ `t` in der Liste steht oder `t` ohne `monarch:` darin steht. In beiden Fällen ist der Name von `t` gleich dem Namen dieses Eintrags, denn ein vorne entferntes `monarch:` ändert nichts an dem, was nach dem letzten `:` steht. Jeder Fall, der heute read-only ist, bleibt es also. Neu read-only werden nur:

- Fälle mit einem anderen Präfix (`x:scout`, `umbrarch:warden`),
- nackte oder anders präfigierte Namen zu einem Eintrag mit Präfix (`scribe` und `umbrarch:scribe` zu `monarch:scribe`),
- ein Typ mit leerem Namen (`warden:`), aber nur, wenn auch ein Eintrag einen leeren Namen hat (etwa `x:`, über `read_only_shadows` in `system.yaml` möglich, `SystemConfig.cs:53`, `:99`). Mit der Standardliste ist er wie heute nicht read-only.

Unverändert bleiben: welche Werkzeuge und Git-Befehle verweigert werden (`:458-463`) und die Groß- und Kleinschreibung.

## Entscheidung 5b — Warden-Nachweis nur umbrarch:warden und warden

Owner-Prompt, wörtlich: „b) Warden-Nachweis für gate verdict (GateMergeCommands.WardenTypes): nur „umbrarch:warden“ und „warden“. Kein alter Name, keine Wildcard. Ein alter Präfix führt zur Verweigerung.“ Anmerkung der Session: Geprüft wird der Nachweis in `gate merge` (`GateMergeCommands.cs:470`, `:616-650`), nicht in `gate verdict`; `WardenTypes` ist dieselbe Konstante.

**Heute** (`src/Monarch.Cli/GateMergeCommands.cs:18`): `["monarch:warden", "warden"]`, geprüft per exaktem Vergleich (`:638`). Das legt auch ADR-0007, Zeile 81 fest.

**Neu:** `[PluginName + ":warden", "warden"]`, also genau `umbrarch:warden` und `warden`. Der Vergleich bleibt exakt; der Agent-Typ wird hier **nicht** gekürzt. Wie heute zählen genau zwei feste Namen; `monarch:warden` wird durch `umbrarch:warden` ersetzt. Strenger ist das nur für den alten Namen, eine Lockerung über weitere Namen gibt es nicht. Ein Lauf von `monarch:warden`, `x:warden`, `umbrarch:warden-extra` oder `Umbrarch:warden` zählt nicht. `gate merge` verweigert dann mit „no warden run (SubagentStop of umbrarch:warden or warden) …“. Für diesen Punkt löst dieses ADR die Zeile 81 in ADR-0007 ab. ADR-0007 selbst bleibt unverändert (CLAUDE.md Regel 3).

## Übergang zwischen altem und neuem Stand

**Versionssprung (Owner-Antwort 3):** Mit G-0127 steigt die Version von 0.1.0 auf **0.1.1**: `plugin/.claude-plugin/plugin.json:3`, `MONARCH_VERSION` in `plugin/scripts/monarch:4` und `ServerVersion` in `src/Monarch.Cli/McpTools.cs:17` (Vorschlag der Session: dieselbe Zahl im MCP-Handshake). `scripts/install-local.sh` liest die Version aus `plugin.json` (`:9`), installiert unter `~/.monarch/bin/0.1.1/` (`:25`), setzt den Symlink `~/.monarch/bin/monarch` neu (`ln -sfn`) und lässt `~/.monarch/bin/0.1.0/` unberührt. `plugin/tests/wrapper-test.sh` liest die Version aus dem Wrapper (`:6`); der neue Fall für „Wrapper 0.1.1, nur Binary 0.1.0 installiert“ (Q-0187) nennt 0.1.1 und 0.1.0 fest. Das Release heißt v0.1.1.

Der Wrapper startet `~/.monarch/bin/<version>/monarch` (`plugin/scripts/monarch:65-67`). Findet er dieses Binary nicht, ruft er `fail()` (`:43-57`). Fail-closed ist das nur in einem Fall: Hook `pre-tool-use`, und die cwd aus der Hook-Eingabe oder `$PWD` liegt in einem erweckten Projekt (`awakened()`, `:19-33`); dann fragt der Wrapper nach (`:46-48`). **Lücke:** Ein Aufruf aus einem nicht erweckten Ordner, der ein erwecktes Projekt über Zielpfad oder absolute Pfad-Token erreicht, endet mit Exit 0 ohne jede Regel (`:50-51`; als allgemeiner Fail-open-Fall festgehalten in `plugin/tests/wrapper-test.sh:43`; `:58-59` prüft nur, dass ein im Befehl zitiertes cwd nicht als cwd der Hook-Eingabe gilt). Das Binary 0.1.0 hätte diese Aufrufe mit protect_paths, protect_secrets und protect_branches geprüft (`src/Monarch.Cli/HookRunner.cs:83-104`, `:188-189`, `:224-235`, `:310`; `src/Monarch.Policy/ToolCallPolicy.cs:76-85`; Beleg des Wardens, Delta-Runde). Ein Befehl wie `git -C <erwecktes Projekt> push origin main` aus einem nicht erweckten Ordner liefe also ohne Regel. Der Versionssprung macht also den Fall „cwd im erweckten Projekt“ strenger, den Fall „von außen hinein“ aber **lockerer**, solange Wrapper und Binary nicht zusammenpassen. Das ist eine bewusst benannte Folge der Owner-Antwort 3; der Owner hat die Lücke am 19.11.2026 hingenommen (Issue #158, Milestone v0.2). Alle anderen Hooks und die Statuszeile bleiben fail-open (`:50-51`), MCP-Server und CLI enden mit Exit 1 (`:52-55`).

### Zustände

| # | Binary | Plugin geladen | ShadowReadOnly | Warden-Nachweis | Beleg und Folge |
|---|---|---|---|---|---|
| 1 | 0.1.1 | `umbrarch@umbrarch-local` | read-only (5a) | `umbrarch:warden` zählt | Sollzustand. |
| 2 | 0.1.1 | `monarch@monarch-local`, Registrierung zeigt auf einen **noch alten** Checkout | Hooks: der alte Wrapper sucht 0.1.0 und findet es (die Installation lässt es stehen); sie laufen mit dem alten Code, `monarch:*` bleibt dort read-only wie bisher | `gate merge` läuft über den Symlink `~/.monarch/bin/monarch` (`plugin/skills/quest/SKILL.md:21`, `scripts/install-local.sh:38`) mit 0.1.1: `monarch:warden` zählt nicht, `gate merge` verweigert (5b) | Gemischter Stand: Hooks alt, `gate merge` neu. Das Briefing nennt `monarch:regeln` und passt zum geladenen Plugin. |
| 3 | 0.1.1 | `monarch@monarch-local`, Registrierung zeigt auf den **aktualisierten** Checkout | unbekannt | unbekannt | **Nicht belegt.** Das Manifest im Checkout heißt jetzt `umbrarch`, die Registrierung `monarch@monarch-local`. Ob Claude Code das Plugin dann lädt, hat die Session nicht geprüft. Lädt es kein Plugin, läuft **kein Hook**, und keine Regel greift (fail-open). Der Code kann das nicht abfangen. Darum die feste Reihenfolge unten und die Prüfung nach dem Neustart. |
| 4 | 0.1.1 | beide Plugins aktiv | je nach Hook | je nach Hook | **Nicht belegt.** Zwei Hook-Sätze riefen denselben Wrapper doppelt auf. Ob das eintritt, zeigt `claude plugin list` nach dem Neustart. Vermeiden: erst deinstallieren, dann installieren. |
| 5 | 0.1.0 | `umbrarch@umbrarch-local` | nicht ausgewertet: die Hooks erreichen das Binary 0.1.0 nicht; der Wrapper fragt nach (cwd oder `$PWD` erweckt), sonst **ohne Regel** | `gate merge` über den Symlink nutzt das Binary, das dort liegt (siehe Zustand 2 und Vorbedingung unten) | Der Wrapper aus dem neuen Checkout sucht `~/.monarch/bin/0.1.1/monarch`, findet es nicht und fragt bei `pre-tool-use` nach, wenn cwd oder `$PWD` in einem erweckten Projekt liegt (fail-closed). Aufrufe von außen in ein erwecktes Projekt laufen ohne Regel (Lücke, siehe oben). Andere Hooks laufen nicht (fail-open, `session-start` ohne Ausgabe: **kein Briefing**), der MCP-Server endet mit Exit 1. Ein Test (Q-0187) sichert den Fall mit Nachfrage ab; die Lücke ist als allgemeiner Fail-open-Fall in `plugin/tests/wrapper-test.sh:43` festgehalten (vom Owner am 19.11.2026 hingenommen, Issue #158). |
| 6 | beliebig | kein Plugin geladen | – | – | Kein Hook, keine Regel. Der Wrapper wird nie aufgerufen, auch die Version 0.1.1 hilft nicht. Nur die Reihenfolge der Owner-Befehle und die Prüfung nach dem Neustart schützen davor. |

Mit der Version 0.1.1 ist die frühere Mischform „altes Binary, neues Plugin“ (Zeile 5) fail-closed **nur für Aufrufe, deren cwd oder `$PWD` in einem erweckten Projekt liegt**; für Aufrufe von außen hinein entsteht die Lücke (Abschnitt oben). Zeile 3, 4 und 6 sind durch den Code nicht abgedeckt; dort gilt die feste Reihenfolge.

Das Briefing kommt vom SessionStart-Hook des geladenen Plugins (`plugin/hooks/hooks.json:8`, `src/Monarch.Cli/HookRunner.cs:327`, `:461`) und läuft mit dem Binary, das dessen Wrapper findet. In Zustand 2 nennt es `monarch:regeln` (passt zum alten Plugin). In Zustand 5 kommt gar kein Briefing. Ein fehlendes Briefing ist also ein Warnzeichen, aber kein Schutz.

**Betrifft alle Einträge:** Sieht ein altes Binary neue Agent-Typen (`MONARCH_BIN` auf das alte Binary, oder Zustand 4 mit altem Checkout), lässt es jeden Listeneintrag in `read_only_shadows` (Datei `system.yaml`) mit anderem Präfix wirkungslos, nicht nur Warden und Scout.

**Vorbedingung:** Das Plugin wird aus dem Haupt-Checkout gelesen. Bis Schritt 5 unten wird der Haupt-Checkout nicht aktualisiert (kein `git pull` im Haupt-Checkout nach dem Merge), und vor dem Merge von G-0127 läuft kein `install-local.sh` aus der Worktree: Sonst entstehen Zustand 5 (laufende Sessions) bzw. Zustand 2 (`gate merge` verweigert den eigenen Merge). Wird der Haupt-Checkout trotzdem früher aktualisiert, ist jede Session, die danach und vor Schritt 2 startet, in Zustand 3 (Binary dann 0.1.0).

**Aufgezeichnete Warden-Läufe:** Ein im Ledger stehender `SubagentStop` von `monarch:warden` zählt mit dem neuen Binary nicht mehr (5b). G-0127 selbst wird mit dem **alten** Binary gemergt; sein Warden-Lauf heißt `monarch:warden` und zählt dort. Alle späteren Gates brauchen einen Lauf von `umbrarch:warden` oder `warden`.

### Owner-Befehle in fester Reihenfolge

Die Reihenfolge ist Pflicht (Zeile 3, 4 und 6). `<repo>` ist der lokale Ordner des Repos. Die Unterbefehle sind mit `claude plugin --help`, `claude plugin uninstall --help` und `claude plugin marketplace --help` am 19.11.2026 belegt.

1. Alle Claude-Code-Sessions beenden. Für Schritt 4 sind alle Worktrees aufgeräumt: `git worktree list` zeigt nur den Haupt-Checkout (auch ein liegengebliebener Worktree wie `.claude/worktrees/G-0115` muss dann weg, sonst zeigen seine Verweise nach dem Umbenennen ins Leere).
2. `claude plugin uninstall monarch@monarch-local`
3. `claude plugin marketplace remove monarch-local`
4. Ordner umbenennen (für den Übergang selbst nicht nötig; Owner, Fortsetzungs-Prompt 19.11.2026, wörtlich: „Die Owner-Befehle enthalten auch die Umbenennung des lokalen Ordners: nach dem Aufräumen (git worktree list zeigt nur den Haupt-Checkout) und nach marketplace remove folgen mv ~/Projects/monarch ~/Projects/umbrarch und git worktree prune; danach pull, install-local.sh und marketplace add ~/Projects/umbrarch.“): `mv ~/Projects/monarch ~/Projects/umbrarch`, dann `git -C ~/Projects/umbrarch worktree prune`. Ab hier ist `<repo>` gleich `~/Projects/umbrarch`. Hinweis: Der Auto-Memory von Claude Code liegt unter einem Pfad, der vom Ordnernamen abhängt (`~/.claude/projects/-Users-davidozturk-Projects-monarch/`); nach dem Umbenennen beginnt eine Session unter dem neuen Namen ohne ihn, bis der Ordner dort umbenannt wird.
5. `git -C <repo> pull --ff-only origin develop`
6. `<repo>/scripts/install-local.sh`
7. `claude plugin marketplace add <repo>`
8. `claude plugin install umbrarch@umbrarch-local`
9. `claude plugin list` zeigt nur `umbrarch@umbrarch-local`.
10. Claude Code neu starten.

Bricht ein Schritt ab, besonders Schritt 6 (`install-local.sh`), nicht mit den folgenden weitermachen und keine Claude-Code-Session starten, bis alle Schritte vollständig gelaufen sind und die Prüfung nach dem Neustart bestanden ist (Zustand 6 und Lücke oben).

Zwischen diesen Schritten keine Claude-Code-Session starten: Der lokale Marketplace liest das Plugin aus dem Checkout (`claude plugin list` am 19.11.2026: `monarch@monarch-local`, „Read from: /Users/davidozturk/Projects/monarch/plugin“).

### Prüfung nach dem Neustart

- `claude plugin list` nennt nur `umbrarch@umbrarch-local`, kein `monarch@monarch-local`.
- Das Briefing beim SessionStart nennt `umbrarch:regeln`.
- Hook-Prüfung in einem erweckten Temp-Projekt: Der Agent `umbrarch:scout` hat kein Edit (`plugin/agents/scout.md:5`: `tools: Read, Grep, Glob, Bash`). Er führt darum per Bash `git commit --allow-empty -m probe` aus; das wird mit `shadow_read_only` verweigert (`src/Monarch.Policy/ToolCallPolicy.cs:460-462`). Kommt weder ein Deny noch (Zustand 5) eine Nachfrage des Wrappers, läuft kein Hook (Zeile 3 oder 6); fehlt das Briefing, ist es Zustand 5: nicht weiterarbeiten. Zustand 4 zeigt schon der erste Punkt.

### Berechtigungsregeln in den Einstellungen

Berechtigungsregeln mit dem alten Namen würden still nicht mehr greifen. Prüfung am 19.11.2026: `grep -n -o` nach `mcp__plugin_monarch`, `monarch:<agent>`, `monarch@monarch-local` und `monarch-local` in `~/.claude/settings.json` findet nur `:53` (`monarch@monarch-local`) und `:92` (`monarch-local`); beides sind Plugin- und Marketplace-Einträge, keine Berechtigungsregel. `~/.claude/settings.local.json` und Projekt-Einstellungen gibt es nicht.

## Bestandsaufnahme

Befehl: `git grep -n -i monarch -- . ':!.monarch/ledger'` auf `dc8ec9c` (develop vor G-0127), 2319 Zeilen. Ein Treffer gehört zu „jetzt“, wenn er unter Punkt 1 bis 8 fällt, sonst zu „v0.2“. Einträge, die die Owner-Prompt nicht ausdrücklich nennt, sind mit „Vorschlag“ markiert.

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
| `plugin/skills/*/SKILL.md` | je :3 (`description`, 8 Dateien; `awaken`, `regeln` und `status` nennen „Monarch“) | jetzt | Produktname Umbrarch in der Beschreibung (Owner-Antwort 2) |
| `plugin/skills/*/SKILL.md` | übrige der 73 Zeilen in 8 Dateien | v0.2 | Binary `~/.monarch/bin/monarch`, Branch-Präfix, `.monarch` |
| `plugin/.claude-plugin/plugin.json`, `plugin/scripts/monarch`, `src/Monarch.Cli/McpTools.cs` | :3; :4; :17 | jetzt | Version 0.1.1 (Owner-Antwort 3). `plugin.json:3` und `McpTools.cs:17` sind keine Treffer des Befehls (Zahl, nicht Name); `scripts/monarch:4` steht zugleich in den Wrapper-Zeilen unten. `ServerVersion` in `McpTools.cs:17` ist ein Vorschlag der Session; `install-local.sh` und `wrapper-test.sh` lesen die Version nur |
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
| `src/**` | übrige der 528 Zeilen in 114 Dateien | v0.2 | Namespaces `Monarch.*`, Pfade `.monarch/` und `~/.monarch`, Binary, `MONARCH_*`, Issue-Marker und Label `monarch` (`GitHubIssuesBoard.cs:12`, `:35`, `:67`, `:139`, `:143`, `:184`, `:224`, `:288`); Briefing-Kopf (Owner-Antwort 2); Vorschlag: übrige Texte im Binary, `CompositionRoot.cs:21` (`Repo = "monarch"`; produktiv liest der CLI owner/repo aus `origin` oder `boards.github`, `Support.cs:145-160`) |
| `tests/Monarch.Cli.Tests/GateMergeTests.cs` | :27, :60, :62, :63 | jetzt | Warden-Lauf als `umbrarch:warden` (5b) |
| `tests/Monarch.Cli.Tests/LedgerHistoryCliTests.cs`, `MergeMethodTests.cs` | :173; :65, :214 | jetzt | dito |
| `tests/Monarch.Cli.Tests/RulesAtPlaceTests.cs`, `KritikerShadowTests.cs` | :69, :91; :65 | jetzt | Briefing und Regel 11 (Punkt 3) |
| `tests/Monarch.Policy.Tests/ToolCallPolicyTests.cs` | :188, :205-208 | jetzt | ShadowReadOnly (5a) |
| `tests/**` (alte Präfixe als Testdaten) | `GateMergeTests.cs:333`, `:334`, `:460`, `:1145`, `:1156`; `WakeTests.cs:123`; `ProtectBranchesTests.cs:276`, `:277`, `:292`, `:293` | v0.2 | `monarch:knight` und Ähnliches; bleiben gültige Übergangsfälle |
| `tests/**` | übrige der 1071 Zeilen in 79 Dateien | v0.2 | Namespaces, Pfade, Binary |
| `CLAUDE.md` | :1 | v0.2 (Owner-Antwort 2) | Titel; die Datei enthält keinen Verweis `monarch:<name>` |
| `docs/adr/0006-lizenz-und-id-bereiche.md` | :9 | jetzt | Nachtrag: Namensprüfung erledigt, Repo öffentlich; der alte Text bleibt als Verlauf |
| `docs/adr/0019-produktname-umbrarch.md` | neu | jetzt | dieses ADR |
| `docs/adr/0001` bis `0018` | übrige der 254 Zeilen in 16 Dateien | bleibt (Owner-Antwort 1) | historische Entscheide; Änderung nur mit neuer Owner-Freigabe (CLAUDE.md Regel 3). Ausnahme ist der beauftragte Nachtrag in ADR-0006 |
| `docs/lessons.md`, `docs/evidence/**` | 3 Zeilen; 13 Zeilen in 8 Dateien | bleibt (Owner-Antwort 1) | datierter Bericht (Stand 06.11.2026) und aufgezeichnete Belege. Punkt 6 nimmt sie wegen Owner-Antwort 1 aus; ein umbenannter Beleg zeigte auch nicht mehr, was damals lief |
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
- Die Lücke „Aufruf von außen in ein erwecktes Projekt“ im Mischzustand 5 (Owner-Entscheid vom 19.11.2026, wörtlich: „Die Lücke im Mischzustand 5 (Aufruf von außen in ein erwecktes Projekt, Wrapper 0.1.1 mit Binary 0.1.0) nehme ich hin; sie kommt als eigenes Issue in den Milestone v0.2.“).
- Ein Schutz im Code gegen die Zustände „kein Plugin geladen“ und „beide Plugins aktiv“ (Zeile 3, 4 und 6 im Abschnitt „Übergang“). Dort schützt nur die feste Reihenfolge der Owner-Befehle und die Prüfung nach dem Neustart.
