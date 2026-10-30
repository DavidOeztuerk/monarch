# ADR-0014 — Policy: Fehlalarme ohne Lockerung (#28, #14, #47)

**Datum:** 30.10.2026 · **Status:** vorgeschlagen (Design, wartet auf Warden-Verdict vor dem ersten Code-Commit) · **Quest:** Q-0133 (G-0104)

## Kontext

ADR-0011 legt fest, was die Bash-Analyse im Agent-Prozess leisten muss: Klasse 1 (Literal) ist Blocker, Regel 2 ist fail-closed bei Nennung, Interpreter-Code ist Klasse 2 (Heuristik, Follow-up mit Beleg). Im Einsatz sind drei Fehlalarme aufgefallen, bei denen die Policy harmlose Befehle blockiert (deny), obwohl **kein Schutzpfad genannt wird**: #28, #14 und #47. Jeder Fehlalarm kostet den Agenten Zeit und treibt ihn zu Umwegen. Die Korrektur darf aber keine Klasse-1-Nennung durchlassen. Dieses ADR belegt je Fehlalarm die Ursache, legt die engste Korrektur fest und zeigt am Maßstab von ADR-0011, dass keine Schutzregel fällt. Leitlinie: Jede Korrektur macht die Treffermenge nur dort kleiner, wo das Token oder der Text einen Schutzpfad beweisbar nicht benennen kann.

Kürzel: PP = `src/Monarch.Policy/ProtectPathsRule.cs`, TCP = `src/Monarch.Policy/ToolCallPolicy.cs`, BA = `src/Monarch.Policy/BashAnalysis.cs`.

## Kurz in einfacher Sprache

- Monarch blockiert manchmal Befehle, die gar nichts Geschütztes anfassen. Drei Fälle sind jetzt bekannt.
- Fall 1: Ein Filter wie `.[0].message` in `gh api … --jq` sieht für die Prüfung aus wie ein Platzhalter-Pfad, der `.monarch` treffen könnte. Künftig sind Filter-Werte bei `gh` Daten, und ein Platzhalter zählt nur noch, wenn er wirklich `.monarch`, `.claude/settings…` oder `.claude/plugins` treffen kann.
- Fall 2: Ein Python-Block nach `cd ~/Projects/.monarch-worktrees/…` wurde blockiert, weil `.monarch-worktrees` den Text `.monarch` enthält. Künftig zählt außerhalb des Programmtexts nur der echte Ordnername `.monarch` bzw. `.claude`; im Programmtext selbst bleibt es streng wie bisher.
- Fall 3: `git restore --staged .monarch` nimmt Dateien nur aus der Vormerkung (Index) zurück und ändert weder die Datei auf der Platte noch die Historie. Genau diese enge Form (und `git reset -- <pfad>`) wird erlaubt, alles andere bleibt gesperrt.
- Nichts davon macht Monarch gegen einen absichtlichen Angriff schwächer: Alle Fälle, die ADR-0011 als „Literal“ zählt, bleiben blockiert (`rm -rf .*`, `mv .mon[a]rch x`, `git restore --staged --worktree .monarch`, `python3 -c` mit `'.mon' + 'arch'`).
- Die Tests dafür kommen erst nach der Freigabe dieses Designs.

## Fehlalarm 1 — #28: `gh api … --jq '.[0].message'` und `.*` in sed/perl

**Befehl:** `gh api repos/o/r/commits --jq '.[0].message'` → deny (Regel 2). Gleiche Familie: `sed 's/.*x//'`, `perl -pe 's/.*?foo//g'` (Kommentar im Issue).

**Ursache (verifiziert):**
1. `gh` gilt nicht als lesend (TCP:46-52, `ReadOnlyWords` enthält kein `gh`), also werden alle Argumente als Pfade geprüft.
2. `DropDataValues` nimmt bei `gh` nur `--body/--title/-b/-t` heraus (PP:576). Der Wert von `--jq` bleibt Pfad-Kandidat.
3. `Find` (PP:658) sieht `IsGlob` (BA:143: `*`, `?` oder `[` irgendwo im Token) und zerlegt das Token mit `GlobParts` (TCP:276-284) am ersten Wildcard in Verzeichnis und Namensanfang. Für `.[0].message` ist das Verzeichnis das cwd und der Namensanfang `.`.
4. `Zone.HitGlob` (PP:80-110) prüft nur, ob der literale Präfix `.` ein Anfang von `.monarch/...` ist (`lit.StartsWith(prefix)`, Zeile 100). Der Rest des Tokens (`[0].message`) wird nie ausgewertet. Deshalb trifft jedes Token, das mit `.` beginnt und dann einen Wildcard hat, die Zone.

### Entscheidung 1a — Werte von `--jq`, `-q`, `--template` sind bei `gh` Daten

`DropDataValues` (PP:576) bekommt für `gh` zusätzlich `-q`, `--jq` und `--template` dazu. Gedroppt wird **genau das nächste Token** nach dem Flag, wie bei `--body`. Weitere Argumente bleiben Pfad-Kandidaten. Die Schreibweisen `--jq=…` und `-q…` sind schon heute kein Treffer, weil `Find` Tokens mit `-` am Anfang überspringt (PP:660). Nach einem blanken `--` wird nichts mehr gedroppt (dort gehören Argumente einem Unterprogramm, etwa `gh repo clone o/r -- -q`).

Begründung: Der Wert ist in jedem `gh`-Unterkommando ein jq-Ausdruck bzw. ein Go-Template, der intern ausgewertet wird und keine Datei schreiben kann. Eine Umleitung `gh api … --jq '.' > .monarch/ledger/x` ist **kein** Wert dieses Flags: `RedirectTargets` (BA) prüft sie separat und bleibt deny.

### Entscheidung 1b — Glob-Präzision: ein Glob-Token trifft nur, wenn es einen Schutzpfad treffen kann

`HitGlob` bleibt der billige Vorfilter, wie heute (nur Präfix). Wird er positiv, kommt ein zweiter Test dazu, der den **ganzen** Rest des Tokens auswertet. Der Treffer entfällt nur, wenn der zweite Test **beweist**, dass das Token keinen Pfad in der Zone (bzw. bei destruktiven Wörtern keinen Vorfahren der Zone) benennen kann.

**Exakte Regel (`GlobMatcher.CanMatchZone`, neu):**
- Eingabe: das Token ab dem kanonischen Verzeichnis aus `GlobParts` (pro Basisverzeichnis), case-gefaltet mit `PathFold.Fold` über das **ganze** Token inklusive Zeichenklassen (`[A-Z]` wird `[a-z]`), und die Zone als Segmentliste ihres Musters (`.monarch/**`, `.claude/settings*.json`, `.claude/plugins/**`, `~/.monarch/**` usw.).
- Segmentweiser Abgleich mit dem vorhandenen `WildcardMatch`, erweitert um Zeichenklassen: `[abc]`, Bereiche `[a-z]`, Negation `[!x]` und `[^x]`, POSIX-Klassen `[[:alpha:]]` (als „beliebiges Zeichen“ gewertet, konservativ).
- **Konservativ bei Unsicherheit:**
  - Ein Token-Segment mit `*` darf auch mehrere Zonen-Segmente (`/` eingeschlossen) überspannen. Grund: `git`-Pathspec und `find -path` lassen `*` über `/` laufen, die Shell nicht.
  - Ein unvollständiges `[` (keine schließende `]`) oder `[]` ohne Inhalt gilt als literales `[`. Ein Bereich mit umgekehrten Grenzen (`[z-a]`) oder ein sonst fehlerhafter Inhalt gilt als „beliebiges Zeichen“. Eine fehlerhafte Klasse zählt nie als „kann nichts treffen“, wenn irgendeine gängige Lesart trifft.
  - Zonen-Segmente mit Wildcard (`settings*.json`) werden im Präfix-Modus geprüft: Das Token-Segment muss einen Text mit dem literalen Anfang `settings` benennen können.
  - Quoting wird **nicht** ausgewertet (Regel 1 entfernt Quotes). Das Token `'.monarch/**'` in `git rm '.monarch/**'` wird vom Werkzeug selbst als Pathspec gelesen; ein einfaches „in Quotes ist es Daten“ wäre falsch.
- Ergebnis: Treffer, wenn ein Pfad X existieren kann, den das Token benennt und der in der Zone liegt (nicht-destruktiv) bzw. in der Zone liegt oder ein Vorfahr der Zone ist (destruktiv). Liegt das Verzeichnis außerhalb der Projekt-Root (PP:84-88), gilt derselbe Test gegen die Segmente der Vorfahrkette der Root, nur bei destruktiven Wörtern.
- Der Test kann den Vorfilter nur **verengen**, nie erweitern. Alles, was er als Treffer wertet, war schon vorher ein Treffer.

**Beispiele**

| Token (Wort) | heute | neu | warum |
|---|---|---|---|
| `.[0].message` (`gh api`) | deny | allow | Segment `.[0].message`: nach `.` muss `0` kommen, `.monarch` hat `m` |
| `.[]` (jq) | deny | allow | `[]` ohne Inhalt ist literal; ein Name mit `[` `]` ist kein Zonenname |
| `s/.*x//`, `s/.*?foo//g` (sed/perl) | deny | allow | erstes Segment `s` ≠ `.monarch`; `.*x` endet auf `x` |
| `.monarch/ledger/*` (`rm`) | deny | deny | Segmente `.monarch`, `ledger` passen, `*` liegt unter `**` |
| `.*` (`rm -rf`) | deny | deny | `.*` kann `.monarch` und `.claude` benennen |
| `.m*` | deny | deny | trifft `.monarch` |
| `.mon[a]rch` (`mv`) | deny | deny | Klasse `[a]` wird zu `a` ausgewertet: `.monarch` |
| `.[a-z]*` (`rm -rf`) | deny | deny | `m` liegt in `[a-z]`, `*` deckt den Rest |
| `'.monarch/**'` (`git rm`) | deny | deny | Pathspec, `**` unter der Zone |

## Fehlalarm 2 — #14: Interpreter-Prüfung über das ganze Kommando

**Befehl:** `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF' … EOF` → deny, ohne dass der Code einen Schutzpfad erwähnt.

**Ursache (verifiziert):** `BashProtect` (PP:145) findet ein Inline-Interpreter-Segment (`inline`, PP:148) und läuft dann `Compress(cmd)` (BA:149-159) über das **ganze** Kommando (PP:151). `Compress` entfernt Quotes, `\`, `+`, `,` und Whitespace und schreibt klein, damit `'.mon' + 'arch'` zu `.monarch` wird. Dann folgt `Contains(".monarch"|".claude")` (PP:152-153). Das gilt auch für das vorangehende `cd …`, und `.monarch-worktrees` enthält den Teilstring `.monarch`. Zusätzlich prüft die Zeile nur das **erste** Inline-Interpreter-Segment (`FirstOrDefault`, PP:148).

### Entscheidung 2 — Teilstring nur im Code, außerhalb des Codes nur echte Komponenten

**„Code“** sind genau diese Teile:
1. Die Argumente des Interpreter-Segments (`-c`, `-e`, `-pe`, `--eval`, `eval`, `-` usw.).
2. Heredoc- und Here-String-Bodies des Interpreter-Segments.
3. **Alle Segmente derselben Pipeline vor dem Interpreter** samt ihren Heredoc-Bodies, weil deren Ausgabe als Code auf stdin ankommen kann (`echo "…" | python3 -`, `cat <<EOF | python3`).

**Im Code** bleibt alles unverändert: `Compress` über den Code-Text und `Contains(".monarch"|".claude")`, auch zerlegt wie `'.mon' + 'arch'`, `.MONARCH`, `os.path.join('.mon','arch')`. Die Tests `BashFailClosedTests.cs:35-41` bleiben unverändert.

**Außerhalb des Codes** (alles andere, z. B. ein `cd …` vor `&&`, `;`, `||`) zählt nur eine echte Pfadkomponente:
- Jedes Token wird lexikalisch normalisiert (`.` entfällt, `..` hebt die vorige Komponente auf). Danach zählt es, wenn eine Komponente genau `.monarch` oder `.claude` ist. Nicht gezählt werden `.monarch-worktrees`, `.claudex`, `x.monarch`.
- **Ausnahme `.claude/worktrees`:** Eine Komponente `.claude`, deren **nächste normalisierte** Komponente `worktrees` ist, zählt nicht (Gate-Worktrees sind Projektcode). Geschützt bleiben `.claude/settings*.json` und `.claude/plugins/**`, und jede Komponente `.monarch` oder ein weiteres `.claude` darunter zählt weiter.
- Wegen der Normalisierung gilt: `.claude/worktrees/../settings.json` wird zu `.claude/settings.json` und zählt. `.claude/worktrees/G-0104/.monarch/local/x` zählt (Komponente `.monarch`). `.claude/worktrees/G/../..` wird zu `.claude` und zählt.
- Zusätzlich gilt die Prüfung für **alle** Inline-Interpreter-Segmente, nicht nur das erste (schließt die `FirstOrDefault`-Lücke). Das ist eine Verschärfung, keine Lockerung.

**Beispiele**

| Befehl | heute | neu |
|---|---|---|
| `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF'` mit Body `print(1)` | deny | allow |
| `cd .claude/worktrees/G-0104 && python3 -c "print(1)"` | deny | allow |
| `cd .monarch/local && python3 -c "print(1)"` | deny | deny (Komponente `.monarch`, außerhalb des Codes) |
| `cd .claude && python3 -c "print(1)"` | deny | deny |
| `cd .claude/worktrees/../ && python3 -c "print(1)"` | deny | deny (normalisiert zu `.claude`) |
| `python3 -c "os.remove('.mon' + 'arch/local/x')"` | deny | deny (Code) |
| `echo "os.remove('.monarch/local/x')" \| python3 -` | deny | deny (Pipeline vor dem Interpreter) |
| `cd .monarch-worktrees/x && python3 - <<'EOF'` mit Body `os.remove('.monarch/local/x')` | deny | deny (Heredoc ist Code) |

**Restrisiko (Klasse 2, bekannt):** Code kann den Namen aus Ordnernamen oder der Umgebung ableiten (`os.listdir`, `os.getcwd()`, ENV). Das geht schon heute per `chr(46) + 'monarch'`. Die Teilstring-Suche im Code ist eine Heuristik gegen Fehlgriffe (G1), keine Garantie gegen gezielte Umgehung (G4). Die Lockerung betrifft nur Text **außerhalb** des Codes, in dem der Name nicht zur Laufzeit gebildet werden kann.

## Fehlalarm 3 — #47: `git restore --staged .monarch`

**Befehl:** `git restore --staged .monarch` → deny (Regel 2).

**Ursache (verifiziert):** `restore` und `reset` stehen in `MutatingGitSubcommands` (PP:27). `Classify` (PP:694-700) nimmt für Git alle Positionsargumente hinter dem Subkommando als destruktive Ziele und wertet **Flags nicht aus**. `git restore --staged .monarch` ist damit nicht von `git restore .monarch` (überschreibt die Datei auf der Platte) unterscheidbar.

### Entscheidung 3 — eng erlauben: nur der Index wird auf HEAD zurückgesetzt

Auf Schutzpfaden sind **genau** diese beiden Formen erlaubt (Flags jeweils getrennte Tokens, in beliebiger Reihenfolge und Wiederholung):

1. `git restore --staged|-S [-q|--quiet] [--] <pfade>`: erlaubte Flags sind ausschließlich `--staged`/`-S` (mindestens einmal), `-q`/`--quiet` und `--`.
2. `git reset [-q|--quiet] [HEAD] -- <pfade>`: `--` ist **Pflicht**, vor `--` steht nichts außer `-q`/`--quiet` und optional dem Wort `HEAD`.

In beiden Formen muss mindestens ein Pfad folgen. Die Pfade selbst werden nicht als destruktiv gewertet, weil beide Formen sie nicht verändern können (siehe unten).

**Bleibt bei der heutigen Klassifikation (deny):** `git restore` ohne `--staged`; `--worktree`/`-W`, auch gebündelt (`-SW`; auch `-qS` wird nicht zerlegt, Bündel sind fail-closed); `--source`/`-s` in jeder Schreibweise (`--source=HEAD~1`, `--source HEAD`, `-sHEAD`); `-p`/`--patch`; `--pathspec-from-file`; `--overlay`/`--no-overlay` und jedes andere Flag; `git reset <commit> -- …`, `git reset HEAD~1 -- …`, `git reset @ -- …`; `git reset <pfad>` ohne `--` (das bestehende `git reset HEAD .monarch/ledger/x` bleibt deny); `--soft/--mixed/--hard/--merge/--keep`; `git rm --cached`; `git checkout -- .monarch`. Globale Git-Optionen vor dem Subkommando (`git -C <dir> …`) werden wie heute behandelt: `GitSubcommand` überspringt sie, ihre Tokens laufen durch die normalen Regeln (ein `-C`-Ziel in einer Schutzzone bleibt Regel 3).

**Warum Ledger-Datei und Historie unberührt bleiben:** `git restore --staged` und `git reset -- <pfade>` (Mixed-Modus auf Pfaden) schreiben nur die Index-Datei (`.git/index`). Der Arbeitsbaum, also die Ledger-Datei auf der Platte mit ihrer Hash-Kette, wird nicht angefasst (dafür gäbe es `--worktree`, das gesperrt bleibt). `HEAD`, Refs und alle bereits committeten Objekte bleiben unverändert (ein anderer Quellcommit via `--source` oder `reset <commit>` ist gesperrt, `HEAD` ist die einzige zulässige Quelle). Das Ergebnis ist „vorgemerkt“ → „nicht vorgemerkt“; ein neues `git add` stellt den Zustand wieder her. Für G3 relevant: Die Kette im Ledger wird weder gekürzt noch überschrieben, und gepushte Commits sind der Anker (ADR-0010). Es kann höchstens ein verwaister Blob im Objektspeicher zurückbleiben.

**Beispiele**

| Befehl | heute | neu |
|---|---|---|
| `git restore --staged .monarch` | deny | allow |
| `git restore -S -q -- .monarch/ledger` | deny | allow |
| `git reset -q HEAD -- .monarch/ledger/x` | deny | allow |
| `git reset -- .monarch/ledger/x` | deny | allow |
| `git restore .monarch` | deny | deny |
| `git restore --staged --worktree .monarch` / `-SW` | deny | deny |
| `git restore --source=HEAD~1 --staged .monarch` | deny | deny |
| `git reset HEAD .monarch/ledger/x` (ohne `--`) | deny | deny |
| `git reset HEAD~1 -- .monarch/ledger/x` | deny | deny |
| `git reset --hard -- .monarch` | deny | deny |
| `git checkout -- .monarch` | deny | deny |

## Warum keine Schutzregel fällt (Maßstab ADR-0011)

**Klasse 1 (Literal → Blocker).** Jede Klasse-1-Nennung bleibt Blocker:
- *Pfad nach Normalisierung im Kommandotext:* Die Normalisierung (Regel 1) ändert sich nicht. Literale Pfade laufen unverändert durch `Hit`. Bei Globs bleibt jedes Token Treffer, das einen Schutzpfad benennen kann (`.*`, `.m*`, `.mon[a]rch`, `.[a-z]*`, `.monarch/ledger/*`, `'.monarch/**'`), weil der neue Test nur Tokens aussortiert, die das **beweisbar** nicht können. Unsicherheit (`*` über `/`, fehlerhafte Klasse) wird als Treffer gewertet. Der Wert von `--jq` kann keinen Pfad benennen, und die Umleitung bleibt separat deny (`gh api … --jq . > .monarch/ledger/x`).
- *Wirksames cwd in einer Schutzzone und pfadloses Schreiben (Regel 3):* unverändert. Neu ist nur, dass `cd ~/Projects/.monarch-worktrees/x` nicht mehr als Nennung zählt, denn `.monarch-worktrees` ist weder Komponente `.monarch` noch cwd in einer Zone. `cd .monarch/local`, `cd .claude` und `cd .claude/worktrees/..` zählen weiter.
- *Link auf Vorfahr/Teil einer Zone (Regel 4), pfadlose Arbeitsbaum-Mutation (Regel 5):* unverändert. `git restore --staged` fasst den Arbeitsbaum nicht an und fällt nicht unter Regel 5; `-W`, `--source`, `--hard`, `--merge`, `--keep` und `git checkout -f` bleiben deny bzw. ask.

**Regel 2 (fail-closed bei Nennung):** Sie bleibt. Wir ändern, **was als Nennung gilt**, nicht, wie auf eine Nennung reagiert wird. Treffer, die nur durch Teilstring-Zufall (`.monarch-worktrees`), durch ein Zeichen-Muster ohne Zonenbezug (`.[0]`) oder durch Daten-Flags (`--jq`) entstehen, sind keine Nennungen eines Schutzpfads. Nicht als lesend bekannte Befehle bleiben mutierend; `gh` und `git fetch` kommen **nicht** in die Lesend-Liste (`git fetch --upload-pack=<cmd>` führt aus, `gh run download -D <dir>` schreibt). Sie sind erlaubt, weil kein Argument einen Schutzpfad benennt, nicht, weil sie als lesend gelten.

**Interpreter (Klasse 2):** Im Code ändert sich nichts. Die Heuristik wird sogar breiter abgedeckt (alle Inline-Interpreter statt nur dem ersten, Pipeline-Segmente davor). Klasse 2 bleibt Follow-up mit Beleg: Die Grenze (Code leitet den Namen zur Laufzeit ab) steht in `plugin/README.md`, Abschnitt „Grenzen“, und die Folge für das Ledger ist unter G3 erkennbar. Die Lockerung betrifft nur Text außerhalb des Codes.

**Gegenbeispiele (bleiben deny):** `rm -rf .*`, `rm -rf .m*`, `mv .mon[a]rch x`, `rm .monarch/ledger/*`, `git rm '.monarch/**'`, `rm -rf .[a-z]*`, `gh api … --jq . > .monarch/ledger/x`, `python3 -c "os.remove('.mon' + 'arch/local/x')"`, `echo "…'.monarch/…'" | python3 -`, `cd .monarch/local && python3 -c …`, `git restore .monarch`, `git restore --staged --worktree .monarch`, `git restore --source=HEAD~1 --staged .monarch`, `git reset HEAD~1 -- .monarch`, `git checkout -- .monarch`.

**G1–G3:** G1 (plausible Fehlgriffe) bleibt vollständig, weil jede Lockerung nur Befehle freigibt, die keinen Schutzpfad benennen oder nur den Index berühren. G2 (Policy nicht still abschaltbar): `.claude/settings*.json`, `.claude/plugins/**` und `.monarch/system.yaml` bleiben geschützt, auch unter `.claude/worktrees/../`. G3 (Ledger-Manipulation erkennbar): Datei und Historie bleiben bei `restore --staged` unberührt (Begründung oben).

## Teststrategie (TDD: rot vor grün, je Issue mit dem echten Kommando)

Dateien: `tests/Monarch.Policy.Tests/Adr0011Tests.cs` (Regel 2, Globs, git) und `tests/Monarch.Policy.Tests/BashFailClosedTests.cs` (Interpreter). Kein bestehender Test wird gelöscht oder gelockert; `BashFailClosedTests.cs:35-41` und `64-68`, `ToolCallPolicyTests.cs:96` und `Adr0011Tests.cs:127-128` laufen unverändert grün.

**Allow (vorher rot):**
- `Issue28_gh_jq_value_is_data_allowed`: `gh api repos/o/r/commits --jq '.[0].message'`; ebenso `-q '.[0]'` und `--template '{{.x}}'`.
- `Issue28_regex_dot_star_in_sed_perl_is_not_a_glob_hit`: `sed 's/.*x//' f.txt`, `perl -pe 's/.*?foo//g' f.txt`, `jq '.[]' f.json`.
- `Issue14_cd_into_dotted_sibling_before_heredoc_python_allowed`: `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF'` mit Body `print(1)`; `cd .claude/worktrees/G-0104 && python3 -c "print(1)"` (BashFailClosedTests).
- `Issue47_git_restore_staged_index_only_allowed`: `git restore --staged .monarch`, `git restore -S -q -- .monarch/ledger`, `git reset -q HEAD -- .monarch/ledger/x`, `git reset -- .monarch/ledger/x`.
- `Read_only_commands_with_dot_tokens_allowed` (Theory, realistische Formen): `gh pr checks 29`, `gh pr view 29 --json statusCheckRollup --jq '.statusCheckRollup[].conclusion'`, `gh run list --branch develop --limit 5`, `gh run view 41038797796 --log-failed`, `git fetch origin`, `git rev-parse HEAD`, `git log --oneline -5`, `git diff origin/develop...HEAD`, `jq '.[] | .name' file.json`, `jq '.[0]' datei`.

**Deny (bleibt bzw. neu, muss grün sein):**
- `Glob_tokens_that_can_hit_protected_paths_stay_denied`: `rm -rf .*`, `rm -rf .m*`, `mv .mon[a]rch x`, `rm .monarch/ledger/*`, `git rm '.monarch/**'`, `rm -rf .[a-z]*`, `rm -rf [.]monarch`, `rm -rf .[m]onarch`, `rm -rf .claude/sett*`.
- `Gh_jq_redirect_to_protected_path_denied`: `gh api repos/o/r/commits --jq '.[0]' > .monarch/ledger/x`.
- `Gh_jq_flag_drops_only_its_own_value`: `gh api --jq '.x' .monarch/ledger/x` bleibt deny (der Pfad ist ein weiteres Argument).
- `Interpreter_mention_inside_code_still_denied_after_safe_cd`: `cd ~/Projects/.monarch-worktrees/x && python3 -c "os.remove('.mon' + 'arch/local/x')"`; Heredoc-Variante; `echo "…" | python3 -` mit Nennung im echo; zwei Interpreter-Segmente, das zweite mit Nennung.
- `Interpreter_cd_into_real_protected_component_denied`: `cd .monarch/local && python3 -c "print(1)"`, `cd .claude && …`, `cd .claude/worktrees/../ && …`, `cd .claude/worktrees/G/.monarch && …`.
- `Git_restore_reset_non_index_forms_denied`: `git restore .monarch`, `git restore --staged --worktree .monarch`, `-SW`, `-qS`, `git restore --source=HEAD~1 --staged .monarch` (auch `-s HEAD`, `-sHEAD`), `-p`, `--pathspec-from-file=x`, `git reset HEAD~1 -- .monarch`, `git reset HEAD .monarch/ledger/x`, `git reset --hard -- .monarch`, `git rm --cached .monarch/ledger/x`, `git checkout -- .monarch`, `git -C .monarch restore --staged x`.

**Einheitentest des Matchers** (neu, in `Adr0011Tests.cs` oder neben dem Matcher): Klassen, Bereiche, Negation, fehlerhafte Klassen (`[`, `[]`, `[z-a]`), `*` über `/`, Case-Faltung (Faltungstabelle).

**Latenz:** unverändert p99 ≤ 20 ms (Bench-Grenze unangetastet). Der Matcher ist allokationsarm und läuft nur nach positivem Vorfilter.

## Folgen und Grenzen

- Code-Änderungen: `src/Monarch.Policy/ProtectPathsRule.cs` (`DropDataValues`, `BashProtect`, `Classify`, `Find`), `src/Monarch.Policy/GlobMatcher.cs`, ggf. `src/Monarch.Policy/BashAnalysis.cs` (Code-Bereiche je Pipeline). `plugin/README.md`, Abschnitt „Grenzen“, wird nachgezogen (Fehlalarme mit Ausweg, Restrisiko Klasse 2 unverändert).
- ADR-0011 bekommt bei Regel 2 einen Verweis auf dieses ADR.
- Grenze: Tokens, die mit `*` beginnen oder `.*` enthalten und in einem mutierenden Befehl vorkommen (z. B. `sed -i '.*x' f`), bleiben konservativ Treffer, wenn sie einen Zonennamen benennen können.
- **Nicht Teil dieser Quest:** Der Fehlalarm bei `for f in .monarch/quests/*.md; do sed -n 1p "$f"; done` (Schleife über Schutzpfade mit lesendem Rumpf) wird ein eigenes Issue.
- Restrisiko Klasse 2 (Code leitet den Namen zur Laufzeit ab) bleibt wie in ADR-0011 dokumentiert.
