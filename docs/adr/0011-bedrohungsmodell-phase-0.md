# ADR-0011 — Bedrohungsmodell Phase 0: Leitplanke im Prozess, Erkennung danach, Durchsetzung außerhalb später

**Datum:** 28.10.2026 · **Status:** vorgeschlagen, überarbeitet nach Design-Review (Warden, 28.10.) · **Quest:** Q-0122 (G-0100)

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
| G2 Policy nicht still abschaltbar | **ja für plausible Wege**. Die Quellen der Hook- und Policy-Konfiguration sind abschließend: `.monarch/system.yaml`, `.monarch/quests/**`, `.monarch/gates/**`, `.monarch/local/**`, `~/.monarch/**`, `~/.claude/settings*.json`, `~/.claude/plugins/**`, `<projekt>/.claude/settings*.json` | Schutzpfade; Hook-Fehler → ask; Config-Fehler → eingebaute Defaults |
| G3 Ledger-Manipulation erkennbar | **ja gegen plausible Fehlgriffe.** Gegen gezielte Fälschung nur für gepushte Commits auf einem geschützten Branch; lokal committet nur ohne History-Rewrite; ungetrackt sind „Datei + Anker gemeinsam gefälscht“ und „frischer Klon“ nicht erkennbar (ADR-0010) | Hash-Kette, Anker, Git-Abgleich; `ledger verify` läuft vor `quest clear` und `evidence sync` (Clear nur bei sauberem Ledger); Anker-Resets quittiert nur der Owner |
| G4 Gezielte Umgehung verhindern | **nein** (dokumentierte Grenze) | erst mit Durchsetzung außerhalb des Agent-Prozesses (ADR-0009) |

**Ausdrücklich außerhalb von Phase 0 (dokumentiert in `plugin/README.md`, Abschnitt „Grenzen“):**
- Code, den der Agent ausführt (Tests, Build, Git-Hooks, Skripte wie `scripts/install-local.sh`), wirkt mit vollen Rechten, und der Hook sieht nur den Aufruf.
- Sessions ohne erwecktes Projekt haben keine Policy.
- MCP-Tools mit Pfad-Argumenten sowie Hooks Dritter mit `updatedInput`.
- Evidence-Integrität: Workflows, Tests und Bench-Grenzen sind über Scope Lock und Risk Tags (`infra` ≥ A) geregelt, nicht über Schutzpfade.

## Maßstab für Reviews (reproduzierbar)

Jeder Fund nennt **Klasse, Ziel und Plausibilitätsbeleg**. Es gibt drei Klassen:

1. **Literal → Blocker.** Diese Fälle gehören dazu:
   - Der Schutzpfad steht nach Normalisierung im Kommandotext. Normalisiert werden Quotes, `\`-Escapes, Brace-Expansion, `$'…'`, `~`, `$HOME`, `$PWD`, `$(pwd)`, `$(git rev-parse --show-toplevel)`, Shell-Schlüsselwörter und `builtin`/`command`.
   - Das wirksame cwd (Session-cwd, `cd`/`pushd`/`-C`-Ziel) liegt in einer Schutzzone oder ist ein Vorfahr davon und das Segment mutiert.
   - Ein Link wird im selben Kommando auf einen Vorfahren oder einen Teil einer Schutzzone angelegt.
   - Eine pfadlose Arbeitsbaum-Mutation, die untracked Ledger-Dateien verwirft.
2. **Zweck → Follow-up.** Der Pfad entsteht nur durch eine Transformation, die keinen anderen Zweck als Verschleierung hat (Kodierung, String-Arithmetik, Homoglyphen wie `ſ`), oder er steht nur in einem Dateiinhalt, etwa einem Interpreter-Skript.
3. **Beleg.** Ein Follow-up gilt nur, wenn die Grenze in `plugin/README.md` steht und die Folge für das Ledger unter G3 erkennbar ist. Fehlt eines davon, ist der Fund ein Blocker.

## Entscheidung (Regeln für Q-0122)

1. **Normalisierung vor der Analyse** wie in Klasse 1. Shell-Schlüsselwörter (`if then else elif fi for while until do done case esac { } ! time`), `\`-Escapes und `builtin`/`command` werden entfernt. Braces und `$'…'` werden expandiert bzw. dekodiert.
2. **Fail-closed bei Nennung.** Nennt ein Segment einen Schutzpfad oder ein Token mit einer Komponente `.monarch`, und ist es nicht als lesend bekannt, dann ist es deny. Folgende Ausnahmen bleiben legitime Arbeit:
   - Das **Kommandowort** zählt nicht als Nennung, etwa `~/.monarch/bin/monarch …`.
   - Positivliste: das Monarch-Binary (außer Owner-Befehlen, Punkt 7) sowie `git add`/`commit`/`push` ohne Force, weil Q-0108 Ledger-Dateien committen muss.
   - Heredoc-Bodies und Werte von `-m`/`--message`/`--body`/`-F` sind Daten, außer sie gehen an eine Shell oder einen Interpreter.
3. **cwd als Ziel.** Liegt das wirksame cwd (nach `cd`/`pushd`/`-C`) in einer Schutzzone oder ist es ein Vorfahr davon (inklusive der Projekt-Root) und mutiert ein Segment ohne Pfadangabe (`tar -x`, `unzip`, `git clean -f`, `curl -O`), dann ist es deny. `cd`-Ziele werden **logisch und physisch** aufgelöst, und beide gelten als Basis.
4. **Symlinks:** `ln -s`/`ln` mit einem Link-Ziel, das Vorfahr oder Teil einer Schutzzone ist, ist deny. Andere Links bleiben erlaubt.
5. **Pfadlose Arbeitsbaum-Mutationen** (`git stash -u`/`--include-untracked`, `git checkout -f`, `git switch --discard-changes`/`-f`, `git reset --keep`/`--merge`) → ask.
6. **Projekte:**
   - Relative Tokens dienen ebenfalls der Projektsuche.
   - Jedes erweckte Vorfahr-Projekt wird **vollständig** ausgewertet, Scope Lock eingeschlossen; das strengste Ergebnis gilt. Nebenwirkung: Ein verschachteltes, eigenständiges Projekt unterliegt dem Scope der Eltern-Quest. Eine Registrierung verschachtelter Projekte ist ein Follow-up.
   - Ein `.monarch`-Verzeichnis anlegen (`mkdir`, Write, `git clone`/`worktree add`/`tar -x` mit Ziel in einem erweckten Projekt) ist deny. Das macht nur `monarch awaken` (Binary).
7. **Owner-Befehle** (`monarch ledger ack-reset`, `permit`, `promote`) sind für den Agenten deny. Erkannt wird das am Kommandowort-Basename `monarch`, unabhängig vom Pfad. Zusätzlich verlangt `ledger ack-reset` ein interaktives Terminal (stdin ist ein TTY), und die Verify-Meldung richtet sich an den Owner. Ein Agent, der ein Pseudo-Terminal erzeugt (`script`), fällt unter G4.
8. **Unicode:** Der Build läuft mit `InvariantGlobalization=true`, deshalb wirken `Normalize` und kulturabhängige Faltung nicht. Monarch faltet selbst: ASCII-Lowercase plus eine Tabelle der Nicht-ASCII-Zeichen, die APFS auf ASCII faltet. Gemeint sind mindestens U+017F `ſ`→`s` und U+212A `K`→`k`. Weitere Homoglyphen fallen unter Klasse 2.
9. **Latenz:** Der Bench bekommt Szenarien für zwei Vorfahr-Projekte und einen langen Heredoc. Für Q-0122 gilt p99 ≤ 20 ms auf der Zielmaschine (GATE_PCT=99) als Evidence; die CI bleibt bei p95.

## Folgen

- Wardens prüfen gegen G1–G3 und den Maßstab. G4 und die ausdrücklich ausgenommenen Punkte sind Follow-ups mit Beleg.
- `plugin/README.md` bekommt den Abschnitt „Grenzen“ (G4, ausgenommene Punkte, Homoglyphen, Interpreter-Skripte, Fehlalarme mit Ausweg).
- ADR-0010 nennt zusätzlich die Fälschung „frischer Klon“ (Anker und `writer-id` gelöscht, Dateien auf HEAD).
- Der Owner trägt beim Merge nach `main` die Restrisiken von G4 bewusst.
