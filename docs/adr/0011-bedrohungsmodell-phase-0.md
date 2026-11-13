# ADR-0011 — Bedrohungsmodell Phase 0: Leitplanke im Prozess, Erkennung danach, Durchsetzung außerhalb später

**Datum:** 28.10.2026 · **Status:** angenommen (Design-Review 28.10.: REQUEST_CHANGES → überarbeitet → APPROVE_WITH_CHANGES, Änderungen eingearbeitet) · **Quest:** Q-0122 (G-0100)

## Anlass

Zwei unabhängige Warden-Runden haben jeweils neue Umgehungen der Bash-Analyse
gefunden (PR #1, Kommentare 6455275866 und 6456150588). Beispiele sind ein
Symlink im selben Kommando, `if … then rm`, `\rm`, Unicode-Faltung (`ſ`), ein
Köder-`.monarch` und `tar -C`. Eine Policy, die im Agent-Prozess Shell-Text
analysiert, kann gegen gezielte Umgehung nicht vollständig sein. Ohne
ausdrückliches Modell wird jede Konstruktion zum Blocker, und die Reviews
konvergieren nicht.

## Akteure

- **Agent (Main, Shadows):** kooperativ, aber fehlbar. Er hat Bash, Write, Edit
  und MCP-Tools mit den Rechten des Owners (dieselbe UID) und führt Code aus:
  Tests, Build, Git-Hooks, Repo-Skripte.
- **Injection-Autor:** schreibt Inhalte, die der Agent liest (Issues,
  Webseiten, Code, Tool-Ausgaben). Er kann gezielte Konstruktionen liefern, die
  der Agent nur kopiert. Diese Konstruktionen gehören zu **G4**, auch wenn der
  Agent sie ausführt.
- **Owner:** vertrauenswürdig; merged nach `main`, quittiert Anker-Resets.
- **Forge (GitHub):** externer Anker für gepushte Commits, Checks und Merges.

## Schutzziele und Garantien

| Ziel | Phase 0 garantiert | Wie |
|---|---|---|
| G1 Plausible Fehlgriffe gegen Schutzpfade verhindern | **ja** in Sessions mit erwecktem Projekt; Tools Bash, Write, Edit, MultiEdit, NotebookEdit | deny/ask nach Maßstab (unten); Datei-Tools mit kanonisierten Pfaden |
| G2 Policy nicht still abschaltbar | **ja für plausible Wege**. Folgende Quellen der Hook- und Policy-Konfiguration sind geschützt (abschließend, Ausnahmen siehe „Ausdrücklich außerhalb“): `.monarch/system.yaml`, `.monarch/quests/**`, `.monarch/gates/**`, `.monarch/local/**`, `~/.monarch/**`, `~/.claude/settings*.json`, `~/.claude/plugins/**`, `<projekt>/.claude/settings*.json` | Schutzpfade; Hook-Fehler → ask; Config-Fehler → eingebaute Defaults |
| G3 Ledger-Manipulation erkennbar | **ja gegen plausible Fehlgriffe.** Gegen gezielte Fälschung nur für gepushte Commits auf einem geschützten Branch; lokal committet nur ohne History-Rewrite; ungetrackt sind „Datei + Anker gemeinsam gefälscht“ und „frischer Klon“ nicht erkennbar (ADR-0010) | Hash-Kette, Anker, Git-Abgleich; `ledger verify` läuft vor `quest clear` und `evidence sync` (Clear nur bei sauberem Ledger); Anker-Resets quittiert nur der Owner |
| G4 Gezielte Umgehung verhindern | **nein** (dokumentierte Grenze) | erst mit Durchsetzung außerhalb des Agent-Prozesses (ADR-0009) |

**Ausdrücklich außerhalb von Phase 0 (dokumentiert in `plugin/README.md`, Abschnitt „Grenzen“):**
- Code, den der Agent ausführt (Tests, Build, Git-Hooks, Repo-Skripte), wirkt mit vollen Rechten, und der Hook sieht nur den Aufruf. Ausnahme: `install-local.sh` ist eine Owner-Aktion (Regel 7), weil sie Binary und SUMS still ersetzt.
- Sessions ohne erwecktes Projekt haben keine Policy.
- MCP-Tools mit Pfad-Argumenten, Hooks Dritter mit `updatedInput` und Hooks im Frontmatter von `.claude/agents/**` und `.claude/skills/**` (vom Agenten schreibbar).
- Evidence-Integrität: Workflows, Tests und Bench-Grenzen schützt nur das Warden-Review des Diffs. Risk Tags prüfen heute nur den geplanten Quest-Scope (`McpTools.cs:262`), Scope Lock nur Datei-Tools (`ToolCallPolicy.cs:300`). Follow-up: Risk Tags bei `quest submit` gegen den PR-Diff.

## Maßstab für Reviews (reproduzierbar)

Jeder Fund nennt **Klasse, Ziel und Plausibilitätsbeleg**. Es gibt drei Klassen:

1. **Literal → Blocker.** Diese Fälle gehören dazu:
   - Der Schutzpfad steht nach Normalisierung im Kommandotext. Normalisiert werden Quotes, `\`-Escapes, Brace-Expansion, `$'…'`, `~`, `$HOME`, `$PWD`, `$(pwd)`, `$(git rev-parse --show-toplevel)`, Shell-Schlüsselwörter und `builtin`/`command`.
   - Das wirksame cwd (Session-cwd, `cd`/`pushd`/`-C`-Ziel) liegt in einer Schutzzone oder ist ein Vorfahr davon, und das Segment schreibt pfadlos ins cwd (abschließende Liste in Regel 3).
   - Ein Link wird im selben Kommando auf einen Vorfahren oder einen Teil einer Schutzzone angelegt.
   - Eine pfadlose Arbeitsbaum-Mutation, die ungetrackte oder nicht committete Ledger-Dateien verwirft.
2. **Zweck → Follow-up.** Der Pfad entsteht nur durch eine Transformation, die keinen anderen Zweck als Verschleierung hat (Kodierung, String-Arithmetik, Homoglyphen außerhalb der Faltungstabelle aus Regel 8, z. B. Fullwidth `ｓ`), oder er steht nur in einem Dateiinhalt, etwa einem Interpreter-Skript.
3. **Beleg.** Ein Follow-up gilt nur, wenn die Grenze in `plugin/README.md` steht und die Folge für das Ledger unter G3 erkennbar ist. Fehlt eines davon, ist der Fund ein Blocker.

## Entscheidung (Regeln für Q-0122)

1. **Normalisierung vor der Analyse** wie in Klasse 1. Shell-Schlüsselwörter (`if then else elif fi for while until do done case esac { } ! time`), `\`-Escapes und `builtin`/`command` werden entfernt. Braces und `$'…'` werden expandiert bzw. dekodiert.
2. **Fail-closed bei Nennung.** Nennt ein Segment einen Schutzpfad oder ein Token mit einer Komponente `.monarch` **unterhalb der Projekt-Root, die nicht die eigene `.monarch` des Projekts ist** (eine fremde, verschachtelte oder per `..` erreichte), und ist es nicht als lesend bekannt, dann ist es deny. Ein Pfad in der eigenen `.monarch` des Projekts, der kein Schutzpfad ist (z. B. `.monarch/x`), ist **kein** Treffer: `python3 tool.py .monarch/x`, `rm .monarch/x` und `touch .monarch/x` sind erlaubt (am 28.10. hermetisch mit dem CLI geprüft; `rm .monarch/ledger/x`, `touch .monarch/local/x` und `python3 tool.py .monarch/system.yaml` bleiben deny). Was als „Nennung“ gilt, präzisiert ADR-0014 (Fehlalarme #28, #14, #47: Glob-Präzision, Code-Bereiche beim Interpreter, `git restore --staged`), ohne Regel 2 zu lockern. Folgende Ausnahmen bleiben legitime Arbeit:
   - Das **Kommandowort** zählt nicht als Nennung, etwa `~/.monarch/bin/monarch …`.
   - Positivliste: das Monarch-Binary über seinen **kanonischen, aufgelösten Pfad** (`~/.monarch/bin/<version>/monarch` bzw. der Symlink darauf), nicht über den Basenamen (außer Owner-Befehlen, Regel 7), sowie `git add`/`commit`/`push` ohne Force, weil Q-0108 Ledger-Dateien committen muss.
   - Heredoc-Bodies sind Daten, außer sie gehen an eine Shell oder einen Interpreter. Werte von `-m`/`--message`/`-F`/`--file` sind nur bei `git commit|tag|merge|stash push` Daten, `--body`/`--title`/`-b`/`-t` nur bei `gh`. Anderswo sind `-m`/`-F` Flags (z. B. `git checkout -m`, `ln -sF`).
3. **cwd als Ziel.** Liegt das wirksame cwd (nach `cd`/`pushd`/`-C`) in einer Schutzzone oder ist es ein Vorfahr davon (inklusive der Projekt-Root) und **schreibt ein Segment pfadlos ins cwd**, dann ist es deny. Die Liste ist abschließend: `tar -x` ohne `-C`, `unzip` ohne `-d`, `cpio -i`, `curl -O`/`-J`, `wget` ohne `-O`/`-P`, `git clean -f` ohne Pfad. `dotnet build/test`, `npm install`, `git pull/merge` und Ähnliches sind davon nicht betroffen. `cd`-Ziele werden **logisch und physisch** aufgelöst, und beide gelten als Basis.
4. **Symlinks:** `ln -s`/`ln` mit einem Link-Ziel, das Vorfahr oder Teil einer Schutzzone ist, ist deny. Andere Links bleiben erlaubt.
5. **Pfadlose Arbeitsbaum-Mutationen** (`git stash -u`/`--include-untracked`, `git checkout -f`, `git switch --discard-changes`/`-f`, `git reset --keep`/`--merge`) → ask.
6. **Projekte:**
   - Relative Tokens dienen ebenfalls der Projektsuche.
   - Jedes erweckte Vorfahr-Projekt wird **vollständig** ausgewertet, Scope Lock eingeschlossen; das strengste Ergebnis gilt. Nebenwirkung: Ein verschachteltes, eigenständiges Projekt unterliegt dem Scope der Eltern-Quest. Eine Registrierung verschachtelter Projekte ist ein Follow-up.
   - Ein neues `.monarch`-Verzeichnis per `mkdir` oder Write in einem erweckten Projekt anzulegen ist deny; das macht nur `monarch awaken` (Binary). Ein Köder über `git clone`/`worktree add`/`tar -x` ist durch „Vorfahr vollständig“ wirkungslos und bleibt erlaubt (Vendoring, Archive, Worktrees).
7. **Owner-Befehle** (`monarch ledger ack-reset`, `permit`, `promote`) und `install-local.sh` sind für den Agenten deny. Erkannt werden sie an einem **Token mit Basename `monarch` irgendwo im Segment**, gefolgt vom Owner-Unterkommando (also auch `script -q /dev/null monarch ledger ack-reset`), bzw. an einem Token mit Basename `install-local.sh`. Zusätzlich verlangt `ledger ack-reset` ein interaktives Terminal (stdin ist ein TTY), und die Verify-Meldung richtet sich an den Owner. Ein Agent, der ein Pseudo-Terminal erzeugt (`script`), fällt unter G4.
8. **Unicode:** Der Build läuft mit `InvariantGlobalization=true`, deshalb wirken `Normalize` und kulturabhängige Faltung nicht. Monarch faltet selbst: ASCII-Lowercase plus eine Tabelle, abgeleitet aus Unicode `CaseFolding.txt` (Status C und F), mit allen Einträgen, deren Ergebnis rein ASCII ist. Das sind auch 1→n-Ersetzungen. Auf APFS gemessen am 28.10.: `ſ`→`s`, `K`→`k`, `ß`/`ẞ`→`ss`, `ﬀ ﬁ ﬂ ﬃ ﬄ ﬅ ﬆ` → `ff fi fl ffi ffl st st` werden als dieselbe Datei aufgelöst; Fullwidth `ｓ` nicht. Andere Homoglyphen fallen unter Klasse 2.
9. **Latenz:** Der Bench bekommt Szenarien für zwei Vorfahr-Projekte und einen langen Heredoc. Für Q-0122 gilt p99 ≤ 20 ms auf der Zielmaschine (GATE_PCT=99) als Evidence; die CI gatet wieder auf p99 (Q-0125; die Rücknahme auf p95 aus Q-0121 ist zurückgenommen, die Messdaten zum Runner-Rauschen stehen in Commit 5abdd63, ein relatives A/B-Gate folgt als eigene Quest). **Nachtrag 11.11.2026 (Q-0148):** Die CI gatet seit ADR-0015 das Verhältnis des Hooks zum Programmstart am Median statt p99 in Millisekunden; p99 ≤ 20 ms auf der Zielmaschine bleibt unverändert das Kriterium (C1). Das ist eine vom Owner entschiedene Lockerung des CI-Gates, siehe `docs/adr/0015-relatives-latenz-gate.md`. **Nachtrag 12.11.2026 (Q-0150):** Nach Owner-Entscheid gelten die 20 ms im p99 auf ruhiger Maschine; unter Last gilt auf dem Mac ein Verhältnis von höchstens 1,64 zum Programmstart (`GATE_MODE=c1-load`, Nachtrag in ADR-0015). Die CI ändert sich dadurch nicht. **Nachtrag 13.11.2026 (Q-0151):** Nach Owner-Entscheid vom 12.11. gilt unter Last 1,70 statt 1,64. Das ist eine Lockerung; siehe Nachtrag vom 13.11. in ADR-0015. Die CI ändert sich dadurch nicht.

## Folgen

- Wardens prüfen gegen G1–G3 und den Maßstab. G4 und die ausdrücklich ausgenommenen Punkte sind Follow-ups mit Beleg.
- `plugin/README.md` bekommt den Abschnitt „Grenzen“ (G4, ausgenommene Punkte, Homoglyphen, Interpreter-Skripte, Fehlalarme mit Ausweg). Er ist **DoD von Q-0122**, denn ohne ihn sind nach Klasse 3 alle Follow-ups Blocker.
- ADR-0010 nennt zusätzlich die Fälschung „frischer Klon“ (Anker und `writer-id` gelöscht, Dateien auf HEAD).
- Der Owner trägt beim Merge nach `main` die Restrisiken von G4 bewusst.
