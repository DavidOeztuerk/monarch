# Monarch Plugin

Claude Code Plugin fuer Monarch (Gates, Quests, Shadows, Mana). Lizenz: Apache-2.0.

## Installation (lokal)

```sh
cd /Users/davidozturk/Projects/monarch
scripts/install-local.sh
```

Das Skript baut das CLI (`dotnet publish`, aktuelle Plattform), installiert es nach `~/.monarch/bin/<version>/monarch`, schreibt `SHA256SUMS` und setzt den Symlink `~/.monarch/bin/monarch`.

Danach in Claude Code:

```
/plugin marketplace add /Users/davidozturk/Projects/monarch
/plugin install monarch@monarch-local
```

Anschliessend Claude Code **neu starten** (Hooks, MCP-Server und Statusline werden beim Start geladen). Im Projekt dann `/monarch:awaken`.

## Statusline

Eine vorhandene Statusline wird gewrappt. Ihr Kommando gehoert nach `~/.monarch/statusline.json`, danach zeigt `statusLine` in `~/.claude/settings.json` auf Monarch:

```json
{
  "statusLine": {
    "type": "command",
    "command": "~/.monarch/bin/monarch statusline",
    "refreshInterval": 30
  }
}
```

`refreshInterval: 30` schliesst laut ADR-0004 die Luecke, wenn die Main-Session auf Subagents wartet. `awaken` setzt es, falls nicht vorhanden. Neustart erforderlich.

## Wrapper `scripts/monarch`

Aufloesung: `$MONARCH_BIN` (Entwickler-Override, ohne Pruefsumme) -> `~/.monarch/bin/<version>/monarch`. Beim ersten Lauf je Version wird SHA-256 gegen `SHA256SUMS` geprueft. Danach legt der Wrapper einen Hardlink `.verified-inode` auf das gepruefte Binary und eine Stamp-Datei `.verified`, deren mtime er per `touch -r` vom Binary uebernimmt. Der Fast Path (ohne Fork, nur `test`-Builtins) fuehrt das Binary nur aus, wenn `monarch -ef .verified-inode` (gleicher Inode, also nicht ersetzt) und die mtime von Binary und Stamp gleich ist (`-nt` in beide Richtungen). Wird das Binary veraendert oder der Stamp angefasst, folgt wieder die SHA-Pruefung. Grenzen: `test -nt` vergleicht je nach Shell nur Sekunden, und wer Binary und Stamp mtime-genau nachfuehrt, taeuscht den Fast Path; der Stamp ist Schutz gegen Versehen und einfache Manipulation, kein Schutz gegen einen Angreifer mit Schreibrecht (dafuer sorgt der Pfadschutz der Policy). Bei Abweichung wird nicht ausgefuehrt (Meldung auf stderr).

**Fail-closed fuer PreToolUse in erweckten Projekten:** Fehlt das Binary oder `SHA256SUMS`, gibt es kein Hash-Tool oder gibt es keinen `monarch`-Eintrag, gibt `hook pre-tool-use` `ask` mit Begruendung aus, sofern im cwd oder darueber `.monarch/system.yaml` liegt (Shell-Schleife, kein Fork). Eine Binary-Abweichung von `SHA256SUMS` fuehrt ebenfalls zu `ask`.

**Fail-open sonst:** Ausserhalb erweckter Projekte sowie fuer die anderen Hook-Modi (`post-tool-use`, `stop`, ...; sie erlauben nichts) und `statusline` endet der Wrapper mit Exit 0 und einer stderr-Zeile, damit eine fehlende Installation Claude Code nicht blockiert. Alle anderen Modi (`mcp`, ...) enden mit Exit 1 und nennen den Installationsbefehl. Tests: `plugin/tests/wrapper-test.sh`.

## Inhalt

- `hooks/hooks.json`: 7 Events, PreToolUse inkl. `Read`/`Grep` (Secret-Schutz), Timeouts 5 s (Pre/Post) bzw. 10 s.
- `.mcp.json`: Server `monarch`. Tools heissen in Claude Code `mcp__plugin_monarch_monarch__<tool>` (system_status, gate_plan, quest_transition, evidence_sync, mana_forecast, knowledge_write).
- `skills/`: awaken, status, gate, quest, recap, mana, rest.
- `agents/`: scout, strategist, knight, warden. Plugin-Agents ignorieren `hooks`, `mcpServers`, `permissionMode` (ADR-0005); Rechte setzt allein der PreToolUse-Hook.

## Grenzen

Grundlage ist ADR-0011 (Bedrohungsmodell Phase 0). Der Hook ist eine **Leitplanke gegen plausible Fehlgriffe**, keine Sandbox. Gezielte Umgehung durch einen Agenten oder durch Inhalte, die er kopiert (Injection), verhindert Phase 0 **nicht** (G4). Erkennbar sind solche Versuche nur ueber Ledger-Anker, Git und Forge-Evidence (G3); echte Durchsetzung ausserhalb des Agent-Prozesses folgt mit ADR-0009.

### Was Phase 0 garantiert

- G1: plausible Fehlgriffe gegen Schutzpfade in Sessions mit erwecktem Projekt (Bash, Write, Edit, MultiEdit, NotebookEdit).
- G2: die Policy ist auf plausiblen Wegen nicht still abschaltbar (`.monarch/system.yaml`, `quests/`, `gates/`, `local/`, `~/.monarch/**`, `~/.claude/settings*.json`, `~/.claude/plugins/**`, `<projekt>/.claude/settings*.json`).
- G3: Ledger-Manipulation ist gegen plausible Fehlgriffe erkennbar. `ledger verify` laeuft vor `quest clear` und `evidence sync`; bei Befunden wird nicht geclear't (Event `ClearRefusedLedgerInvalid`). Anker-Resets quittiert nur der Owner im Terminal (`monarch ledger ack-reset <hash>` verlangt ein TTY auf stdin).

### Merge-Policy (ADR-0007, Q-0113)

- Regel `protect_branches` im PreToolUse-Hook, fuer Main und Shadows: `git push` auf `git.protected_branches` (Default main, master; `system.yaml` kann nur ergaenzen) wird verweigert, in jeder Schreibweise (Refspec, `HEAD`, `+`, `:ziel`, `--delete`, `--all`, `--mirror`, Wildcards, `-C`, `--git-dir`, `GIT_DIR=`, Worktrees, `bash -c`, `eval`). Ein Push ohne Refspec liest HEAD, Upstream und `remote.<r>.push`/`mirror` per Dateizugriff. Ein direkter Push auf `git.integration_branch` (Default develop) fragt nach (`ask`).
- `gh pr merge`, `gh api` auf `…/pulls/<n>/merge`, `…/merges`, `git/refs/heads/<geschuetzt>` und GraphQL `mergePullRequest`, `enablePullRequestAutoMerge`, `enqueuePullRequest`, `mergeBranch` werden verweigert.
- Gemerged wird nur mit `monarch gate verdict` und `monarch gate merge` (beides nur der Main, Shadows werden verweigert; dasselbe gilt fuer `monarch gate check`, das den Lauf auf dem Merge-Commit nachprueft). Der Merge ist nur nach `develop` und fuer Rank E-A moeglich; Clear-Evidence bleibt der Forge-Merge. Ein Merge ohne `GateMerged` meldet `quest clear` bzw. `ledger verify` als Hinweis. `gate merge` verweigert bei abgebrochenem (unbelegt, mit Lauf-ID und Rerun-Hinweis), fehlendem oder noch laufendem CI-Lauf am Head von `develop`; bei rotem `develop` geht nur ein belegter Fix-PR durch (develop-Head ist Vorfahr des PR-Heads, PR-Checks gruen, die roten Checks am PR-Head gruen; `GateMerged` haelt `overridden` und `integration_head`). Nach dem Merge prueft der Befehl den Lauf auf dem Merge-Commit (`IntegrationRunChecked` green/red/cancelled/pending; alles ausser green: Exit 1, der Merge bleibt; Wartezeit ueber `MONARCH_MERGE_WAIT_SECONDS`, Default 60, Forge-Fristen ueber `MONARCH_MERGE_FORGE_SECONDS`, Default 20; der Befehl endet im Default unter 2 min). `monarch gate check <G-xxxx>` holt ein `pending` spaeter nach. `quest start` und das SessionStart-Briefing nennen Scope und die zwei Regeln (Scope gilt fuer jeden Schreibzugriff; Deny heisst stoppen, keine Varianten).

### Weckruf nach dem Mana-Limit (ADR-0008, Q-0128)

- `monarch rest` gibt die Weckzeit (`resets_at` + 3 min, Minute nie :00/:30; lokal und UTC), den CronCreate-Ausdruck und den festen Prompt aus. Der CronCreate-One-Shot ist Pflicht, liegt aber nur im Speicher der Session und geht bei Neustart oder `claude stop` verloren.
- Fallback ausserhalb der Session, nur mit Opt-in des Owners (`system.yaml`: `mana.wake: launchd|systemd`, Default `off`): `monarch wake schedule --session <uuid> --at <iso>`, `wake fire`, `wake cancel`. Der Job startet nach Weckzeit plus 3 min Grace und setzt eine **Hintergrund-Session** (`claude agents --json`: `kind: background`) nur dann per `claude stop` und `claude --bg --resume` fort, wenn sie idle ist und es seit `resets_at` keine Ledger-Aktivitaet gab. Interaktive Sessions und jeder Fehler von `claude agents` fuehren zu `WakeSkipped`, nie zu einer Kopie. Das Prompt ist ein festes Template; Freitext des Agenten gelangt nie in den Job.
- **Grenze systemd:** `systemd-run --user` legt einen transienten Timer an. Er ueberlebt keinen Reboot und kein Ende der User-Session.
- **Grenze launchd:** Eine Plist kennt kein Jahr. Der Job hat `RunAtLoad`, `fire` prueft die Zeit selbst und verwirft ein `--at`, das aelter als 24 h ist. Ob launchd einen verpassten Termin nach dem Login nachholt, ist nicht belegt; belegt ist nur der Lauf bei `RunAtLoad`.
- **Grenze Rest-Race:** Wacht die Session zwischen Statuspruefung und `--resume` selbst auf, entsteht eine Kopie.
- **Shadow-Verweigerung:** Die Hook-Regel `protect_branches` sperrt `monarch wake schedule|cancel` für Shadows wie `gate merge|verdict` (Q-0129); zusätzlich verweigert das Binary bei gesetztem `MONARCH_AGENT_TYPE`. Grenze wie überall: ein Shadow, der das Binary über ein Skript oder einen anderen Namen aufruft, fällt unter ADR-0011 Klasse 2.

### Ausdruecklich ausserhalb von Phase 0

- **Code, den der Agent ausfuehrt** (Tests, Build, Git-Hooks, Repo-Skripte) wirkt mit vollen Rechten; der Hook sieht nur den Aufruf.
- **`install-local.sh` ist eine Owner-Aktion.** Es ersetzt Binary und `SHA256SUMS` still und ist fuer den Agenten deny. Das Gleiche gilt fuer `monarch ledger ack-reset`, `permit` und `promote`. Ein Agent, der ein Pseudo-Terminal erzeugt (`script`), faellt unter G4.
- **Sessions ohne erwecktes Projekt haben keine Policy.** Ohne `.monarch` im cwd oder darueber (und ohne Ziel in einem erweckten Projekt) tut der Hook nichts.
- **`.mcp.json` im Projekt und `~/.claude/CLAUDE.md`** sind Konfiguration des Owners und nicht Teil der Hook-Konfiguration; der Hook schuetzt sie nicht.
- **MCP-Parameter `project` (ADR-0013):** Jedes MCP-Tool nimmt optional den absoluten Pfad eines erweckten Projekts. Er gilt nur unter einem effektiven Root (Client-Roots, sonst das Start-cwd des Servers); der Hook sieht diese Aufrufe nicht. Gemessen (Claude Code 2.1.312, headless): Das Start-cwd ist immer ein Root, `--add-dir` erscheint nicht. **G1:** Ein Fehlgriff unter einem Root (etwa Worktree und Hauptcheckout mit gleicher Quest-ID verwechselt) schreibt in das falsche Projekt; gemildert wird er durch die erste Antwortzeile `project: <root> (source: …)` und das Ereignis `McpProjectResolved` im Ledger des Ziels (nur schreibende Tools), nicht verhindert. **G2:** Im nicht erweckten Start-cwd gibt es keine Policy; dort kann der Agent `additionalDirectories` setzen und so die Roots erweitern. Wer das nicht will, startet die Session im Projekt statt in einem Elternverzeichnis.
- **MCP-Tools mit Pfad-Argumenten** (ausser `project`), Hooks Dritter mit `updatedInput` und Hooks im Frontmatter von `.claude/agents/**` und `.claude/skills/**` (vom Agenten schreibbar) sind nicht geschuetzt.
- **Merge-Policy, Klasse 2 (ADR-0007):** Der Hook erkennt bekannte Schreibweisen. Nicht erkannt werden `curl`, `python` oder andere Skripte gegen die GitHub-API, `gh api graphql -F query=@datei` (die Mutation steht in einer Datei), Aliasse (`gh alias`, `git config alias.*`), `git -c`-Overrides, ein `GIT_DIR` aus der Umgebung des Agent-Prozesses sowie Schreibzugriffe ueber die Contents-API. Dagegen hilft nur eine Netzwerk-Policy ausserhalb des Agent-Prozesses (ADR-0011, G4) und das Review des Owners vor dem Merge nach main.
- **Evidence-Integritaet:** Workflows, Tests und Bench-Grenzen schuetzt nur das Warden-Review des Diffs. Risk Tags pruefen heute nur den geplanten Quest-Scope, Scope Lock nur Datei-Tools. Follow-up: Risk Tags bei `quest submit` gegen den PR-Diff.
- **Fehlalarme und Grenzen nach ADR-0014 (Q-0133):**
  - **Worktree-Settings (Klasse 2):** Ein `cd` in `.claude/worktrees/…` zaehlt nicht als `.claude`-Nennung. `cd .claude/worktrees/G && python3 -c "open('../../settings.json','w')"` wird deshalb nicht erkannt, weil der Code den Namen nicht nennt. Ueber das Session-cwd war das schon vorher moeglich.
  - **`perl -pi`/Inline-Code mit `.*`:** `sed`-Skripte ohne `w`, `W`, `e` gelten als Daten. Bei `perl`/`python`-Inline-Code bleibt die Pfadpruefung streng; ein Muster wie `s/(.*?)foo//g` kann blockiert werden. Ausweg: Skriptdatei oder `sed`.
  - **`git restore --staged` / `git reset -- <pfad>` (#47):** Auf Schutzpfaden nur mit woertlichen Argumenten erlaubt (kein `$`, keine Globs, keine Substitution, kein Wrapper, kein `GIT_*`, keine Globaloptionen). Verschleierungen darueber hinaus sind eine Grenze nach ADR-0011, kein Blocker.
- **Ledger, ungetrackt:** Datei und Anker gemeinsam gefaelscht sowie die Faelschung „frischer Klon“ (Anker und `writer-id` geloescht, Dateien auf HEAD) sind lokal nicht erkennbar (ADR-0010). Schutz sind committete und gepushte Ledger-Dateien.
- **Bekannte, offene Follow-ups:**
  - N1: Array-Zuweisungen und `set --` mit Schutzpfad werden nicht als Zuweisung modelliert (#2).
  - N3: `#`-Kommentar innerhalb einzeiliger Backticks wird nicht als solcher erkannt (#3).
  - Datei-vor-Anker: fremd angehaengte Zeilen hinter dem verankerten Head werden uebernommen, wenn die Datei vor dem Anker liegt (#4).
  - Eine fehlende Session-Datei wird leer angelegt (#5).
  - `awk -v` mit Schutzpfad im Wert (#6).
  - `cd ~-`, `cpio -D`, `7z -o` werden als Zielverzeichnis nicht modelliert (#7).
  - Eine per Branchwechsel entfernte, auf dem anderen Branch committete Ledger-Datei bleibt als „missing but anchored“ Befund (ADR-0010, Q-0125; #10).
- **Verschachtelte Projekte:** Jedes erweckte Vorfahr-Projekt wird vollstaendig ausgewertet (Scope Lock eingeschlossen); ein eigenstaendiges Projekt in einem anderen unterliegt damit dem Scope der Eltern-Quest. Eine Registrierung verschachtelter Projekte ist ein Follow-up.
- **Wrapper:** `awakened()` im Wrapper (nur im Fehlerpfad, wenn das Binary fehlt) nutzt das cwd aus dem Hook-Input und `$PWD`, das strengere gilt. Das cwd wird ohne Fork aus der ersten Zeile der Eingabe gelesen; ein Pfad mit JSON-Escape oder eine mehrzeilige Eingabe faellt auf `$PWD` zurueck.

### Bash: Heuristik, nicht Sandbox

Die Policy prueft Bash heuristisch und lieber zu streng. Quotes, `\`-Escapes, Brace-Expansion, `$'...'`, `~`, `$HOME`, `$PWD` und Shell-Schluesselwoerter werden bereinigt, Globs gegen Schutzpfade geprueft, Ziele, die einen Schutzpfad enthalten, verweigert. Nicht erkennbar sind Interpreter-Skriptdateien, zur Laufzeit entstehende Pfade und Netzwerkzugriffe. Der Schutz ist eine Leitplanke, keine Sandbox.

- **Homoglyphen:** Monarch faltet selbst (ASCII-Lowercase plus Tabelle aus Unicode `CaseFolding.txt`, Status C und F, nur rein ASCII-Ergebnisse, z. B. `ſ`, Kelvin-Zeichen, `ß`, Ligaturen). Homoglyphen ausserhalb dieser Tabelle (z. B. Fullwidth `ｓ`) werden nicht gefaltet; auf APFS sind sie auch nicht dieselbe Datei, ein Treffer ist also nur ueber andere Wege denkbar.
- **Interpreter-Skripte und zur Laufzeit gebaute Pfade:** Steht ein Schutzpfad nur in einer Skriptdatei (`python x.py`, `sh x.sh`, `make`) oder entsteht er erst durch Kodierung, String-Arithmetik oder Umgebungsvariablen zur Laufzeit, sieht der Hook ihn nicht. Folge fuer G3: Aenderungen am Ledger bleiben ueber Anker, Git und Forge erkennbar.
- **Shell-Code aus einer Pipe:** Heredocs und Here-Strings an eine Shell werden als Code analysiert, `echo "..." | bash` oder `printf ... | sh` dagegen nicht (Klasse 2 nach ADR-0011). Der Hook sieht ausserdem keine Dateiinhalte.
- **Fail-closed bei Nennung (Regel 2):** Jedes Kommando, das `.monarch` oder einen anderen Schutzpfad nennt und nicht als lesend bekannt ist, wird verweigert, auch wenn es harmlos waere (z. B. `cp .monarch/system.yaml /tmp/x`). Ein Pfad in der eigenen `.monarch` des Projekts, der kein Schutzpfad ist, ist erlaubt: `python3 tool.py .monarch/x`, `rm .monarch/x` und `touch .monarch/x` werden nicht verweigert, weil die `.monarch`-Komponenten-Regel nur fuer eine fremde `.monarch` unterhalb der Root greift (hermetisch mit dem CLI geprueft nach Q-0125); `.monarch/ledger/`, `local/`, `system.yaml` usw. bleiben Schutzpfade. Ausnahmen: das Monarch-Binary ueber seinen kanonischen Pfad (ein nacktes `monarch` nur, wenn PATH auf das installierte Binary unter `~/.monarch/bin` aufloest), `git add`/`commit`/`push` ohne Force, Commit-/PR-Texte (`-m`, `-F`, `--body`) und Heredocs, die nicht an eine Shell gehen. `install-local.sh` ist nur zum Ausfuehren gesperrt; `git add`/`diff`/`cat` darauf bleiben erlaubt. `awk`, `sed`, `uniq`, `sort -o` und `git diff/log/show --output` gelten nicht pauschal als lesend; `<>` und `>|` sind Schreib-Umleitungen.
- **Kommentare und Zuweisungen:** `#` am Wortanfang beginnt einen Kommentar (Apostrophe darin oeffnen kein Quote). Nennt eine Zuweisung (`F=…`, `export`/`declare`/`local`/`readonly`/`typeset`) einen Schutzpfad, ist jedes folgende nicht lesende Segment im selben Aufruf verweigert, auch wenn es die Variable nicht nutzt (z. B. `F=.monarch/system.yaml; dotnet build`); Ausweg: Zuweisung und Arbeit trennen. Nicht modelliert sind Werte aus `read` und nicht-literale Werte.
- **cwd und Zielverzeichnisse (Regel 3):** Geprueft werden das cwd nach `cd`/`pushd`/`popd`/`cd -`/Subshell-Ende (bei Unsicherheit die Vereinigung der Kandidaten) sowie Zielverzeichnisse von `tar -C`/`--directory`, `unzip -d`, `wget -P`/`--directory-prefix` und `curl --output-dir`. Nicht modelliert sind `cd $VAR`, fehlschlagende `cd` und Subshells durch Pipes oder `&` (dort wirkt ein `cd` als wirksam).

### Bekannte Fehlalarme und Ausweg

Die Heuristik ist fail-closed bei Nennung eines Schutzpfads. Legitime Faelle, die davon getroffen werden:

| Kommando | Ausweg |
|---|---|
| `find ... -prune -exec rm ...` (Pfad im Kommando, Schreiber vorhanden) | Pfad ohne `.monarch` im Kommando, oder die Aktion als Owner im Terminal ausfuehren |
| `cp .monarch/system.yaml /tmp/x` (Lesen ueber einen Schreiber) | `cat .monarch/system.yaml` bzw. das Read-Tool verwenden, oder im Terminal kopieren |
| `git checkout .` | konkrete Pfade angeben (`git checkout -- src/`); pfadlos verwirft es ungetrackte/nicht committete Ledger-Dateien |
| `git restore .` | wie oben: `git restore src/` |
| `tar -xzf a.tgz` in der Projekt-Root | `tar -xzf a.tgz -C dir` |
| `curl -LO url` in der Projekt-Root | `curl -L -o datei url` oder `--output-dir dir` |
| `curl ... \| tar -xz` in der Projekt-Root | `tar -xz -C dir` |
| `unzip a.zip` in der Projekt-Root | `unzip a.zip -d dir` |

Reicht der Ausweg nicht, fuehrt der Owner die Aktion selbst im Terminal aus oder erteilt eine Freigabe (`ask`).
