# ADR-0014 — Policy: Fehlalarme ohne Lockerung (#28, #14, #47)

**Datum:** 30.10.2026 · **Status:** vorgeschlagen, überarbeitet nach Warden-Runde 1 (Design); wartet auf Delta-Verdict vor dem ersten Code-Commit · **Quest:** Q-0133 (G-0104)

## Kontext

ADR-0011 legt fest, was die Bash-Analyse im Agent-Prozess leisten muss: Klasse 1 (Literal) ist Blocker, Regel 2 ist fail-closed bei Nennung, Interpreter-Code ist Klasse 2 (Heuristik, Follow-up mit Beleg). Im Einsatz sind drei Fehlalarme aufgefallen, bei denen die Policy harmlose Befehle blockiert (deny), obwohl **kein Schutzpfad genannt wird**: #28, #14 und #47. Dieses ADR belegt je Fehlalarm die Ursache, legt die engste Korrektur fest und zeigt am Maßstab von ADR-0011, dass **ADR-0014 nichts lockert**: Jede Korrektur macht die Treffermenge nur dort kleiner, wo Token oder Text einen Schutzpfad beweisbar nicht benennen können, und im Zweifel gilt Treffer. Vorbestehende Lücken (heute allow) gehören nicht hierher, siehe „Folgen“ (Issue #52).

Kürzel: PP = `src/Monarch.Policy/ProtectPathsRule.cs`, TCP = `src/Monarch.Policy/ToolCallPolicy.cs`, BA = `src/Monarch.Policy/BashAnalysis.cs`.

## Kurz in einfacher Sprache

- Monarch blockiert manchmal Befehle, die gar nichts Geschütztes anfassen. Drei Fälle sind bekannt.
- Fall 1: Ein Filter wie `.[0].message` in `gh api … --jq` sieht für die Prüfung wie ein Platzhalter-Pfad aus, der `.monarch` treffen könnte. Künftig sind Filter-Werte bei `gh` Daten, und ein Platzhalter zählt nur, wenn er wirklich `.monarch`, `.claude/settings…` oder `.claude/plugins` treffen kann. Muster in `sed`-Skripten (z. B. `s/^(.*)x/\1/`) sind Daten, solange das Skript kein `w`, `W` oder `e` enthält. Bei `perl`/`python`-Inline-Code bleibt es streng; Ausweg: Skriptdatei oder `sed`.
- Fall 2: Ein Python-Block nach `cd ~/Projects/.monarch-worktrees/…` wurde blockiert, weil `.monarch-worktrees` den Text `.monarch` enthält. Künftig zählt in reinen `cd`/`ls`-Teilen nur der echte Ordnername `.monarch` bzw. `.claude`. Alles, was Code sein könnte, wird weiter streng nach dem Text durchsucht.
- Fall 3: `git restore --staged .monarch` nimmt Dateien nur aus der Vormerkung (Index) zurück und ändert weder die Datei auf der Platte noch die Historie. Genau diese enge Form (und `git reset -- <pfad>`) wird erlaubt, alles andere bleibt gesperrt.
- Nichts wird lockerer. Es gibt aber bekannte, schon heute offene Lücken (Issue #52); die löst diese Quest nicht.
- Die Tests kommen erst nach der Freigabe dieses Designs.

## Fehlalarm 1 — #28: `gh api … --jq '.[0].message'` und Muster mit `.*`

**Befehl:** `gh api repos/o/r/commits --jq '.[0].message'` → deny (Regel 2).

**Ursache (verifiziert, mit Sonde gemessen):**
1. `gh` gilt nicht als lesend (TCP:46-52, `ReadOnlyWords` enthält kein `gh`), also werden alle Argumente als Pfade geprüft.
2. `DropDataValues` nimmt bei `gh` nur `--body/--title/-b/-t` heraus (PP:576). Der Wert von `--jq` bleibt Pfad-Kandidat, in jeder Schreibweise: `--jq X`, `--jq=X` und `-q'X'` sind heute deny, weil `PathTokens` an `=` trennt (BA:16) und `PathCandidates` angehängte Werte zieht (PP:229-237).
3. `Find` (PP:658) sieht `IsGlob` (BA:143) und zerlegt das Token mit `GlobParts` (TCP:276-284) am ersten Wildcard in Verzeichnis und Namensanfang. Für `.[0].message` ist der Namensanfang `.`.
4. `Zone.HitGlob` (PP:80-110) prüft nur, ob dieser Präfix ein Anfang von `.monarch/...` ist (`lit.StartsWith(prefix)`, Zeile 100). Der Rest des Tokens wird nie ausgewertet.

**Korrektur der ersten Fassung:** `sed 's/.*x//' f.txt`, `perl -pe 's/.*?foo//g' f.txt` und `jq '.[]' f.json` sind heute **allow** (gemessen), weil dort vor dem ersten Wildcard ein anderer Präfix steht (`s/`). Die echten Auslöser in der sed/perl-Familie sind Muster, die `PathTokens` zerlegt: Es trennt an `(`, `=`, `,` und Leerzeichen (BA:16, `PathSeparators`), und das Reststück beginnt dann mit `.`. Heute deny: `sed -i -E 's/^(.*)x/\1/' f.txt` (Stück `.*)x/\1/`), `sed -i 's/,.*//' f.txt` (Stück `.*//`), `perl -pe 's/(.*?)foo//g'`.

### Entscheidung 1a — Werte von `--jq`/`-q` (und `--template` bei `gh api`) sind Daten

`DropDataValues` (PP:576-596) bekommt für `gh` zusätzlich:
- `--jq` und `-q`;
- `--template` nur, wenn das Subkommando `api` ist (bei `gh pr|issue create` ist `--template` ein Dateiname und bleibt Pfad-Kandidat).

Der Wert wird in **allen** Schreibweisen entfernt: `--jq X`, `--jq=X`, `-q X`, `-qX`, `-q=X`. Die Mechanik (lange Flags mit `=`, Short-Cluster, Wert als nächstes Wort) besteht bereits (PP:582-596). Gedroppt wird nur der Wert dieses Flags; weitere Argumente bleiben Pfad-Kandidaten, Umleitungen werden separat geprüft. Nach einem blanken `--` wird nichts gedroppt.

Begründung: Der Wert ist ein jq-Ausdruck bzw. Go-Template, der intern ausgewertet wird und keine Datei schreiben kann.

### Entscheidung 1b — `sed`-Skripte sind Daten, solange sie nichts schreiben oder ausführen können

`ScriptWriterHit` (PP:412-460) prüft `sed`-Skripte nur, wenn sie einen der Buchstaben `w`, `W`, `e` enthalten (PP:446), und wertet dann die Pfad-Tokens des Skripts aus. Das deckt die schreibenden/ausführenden sed-Befehle `w`, `W`, `e`, `s///w`, `s///e` auf Buchstabenebene vollständig ab (`r`/`R` lesen nur). Die Erkennung des **Pfades** ist aber tokenbasiert und lückenhaft (z. B. angehängt wie `w.monarch/x` oder `s/x/y/w.monarch/x`; vorbestehend). Deshalb gilt die engere Regel:

- Das Skript (die Werte von `-e`/`--expression`, `--expression=X`, sonst das erste Nicht-Options-Argument) ist Daten **nur wenn**
  1. es keinen der Buchstaben `w`, `W`, `e` enthält und
  2. alle Optionen aus der Positivliste stammen: `-n -E -r -s -u -z --quiet --silent --regexp-extended --separate --unbuffered --null-data --posix --debug`, `-i`, `-i<Suffix>`, `--in-place[=Suffix]`, `-e X`, `--expression X`, `--expression=X`. Jede andere Option (`-f`, `-l N`, `--line-length`, Bündel wie `-ie`) macht das Skript-Argument nicht eindeutig; dann gilt die heutige Prüfung.
- Enthält das Skript `w`, `W` oder `e` (auch nur als Buchstabe in einem Wort wie `message`), bleibt die heutige Prüfung, fail-closed. Das ist gröber als nötig, aber beweisbar.
- Die Dateioperanden bleiben Pfad-Kandidaten: `sed -i 's/,.*//' .monarch/ledger/x` ist deny.
- **Optionstokens bleiben Pfad-Kandidaten.** Nur das Skript-Argument wird gedroppt, nicht die Optionen. GNU `-i'<dir>/*'` und `--in-place=<dir>/*` schreiben Backups woanders hin und werden deshalb weiter geprüft (`sed -i'.monarch/ledger/*' 's/a/b/' f`, `sed --in-place='.monarch/ledger/*' 's/a/b/' f` bleiben deny). BSD-sed (macOS) liest bei `-i` das **nächste** Argument als Suffix; die dadurch mögliche Fehlzuordnung (das Skript wird als Suffix gelesen) wirkt nur strenger, weil ein Skript mit `w`/`e` ohnehin geprüft wird und sonst nur ein Suffix-Token mehr geprüft wird.

**perl/python-Inline-Code bleibt unter der Pfadprüfung.** Sonst fiele `perl -e 'system("rm -rf .m*")'`. `perl -pi -e 's/(.*?)foo//g' f` bleibt deshalb deny; Ausweg: Skriptdatei oder `sed`. Das steht unter „Folgen und Grenzen“ und im README.

### Entscheidung 1c — Glob-Präzision: ein Glob-Token trifft nur, wenn es einen Schutzpfad treffen kann

`HitGlob` bleibt der billige Vorfilter. Wird er positiv, kommt ein zweiter Test dazu, der den **ganzen** Token auswertet. Der Treffer entfällt nur, wenn der Test **beweist**, dass das Token weder einen Pfad in der Zone noch einen Vorfahren der Zone benennen kann (Maßstab unten).

**Exakte Regel (`GlobMatcher.CanMatchZone`, neu):**
- **Flaches Wildcard-Match.** Das Token (ab dem kanonischen Verzeichnis aus `GlobParts`) wird als *ein* Muster gegen den Zonenpfad gelesen, nicht segmentweise. **Jeder** Wildcard (`*`, `?`, `[…]`) darf `/` überspannen, denn git-Pathspecs matchen `/` auch mit `?` und `[/]` (`git rm '.monarch?ledger?x'`).
- **Maßstab (Owner-Entscheid, Regression ausgeschlossen):** Ein Glob-Token trifft, wenn das Muster einen String s erzeugen kann, für den `Zone.Hit(s, destructive, exact:false)` trifft. Das sind bei **allen** Befehlen (nicht nur bei destruktiven Wörtern) Pfade in der Zone **und deren Vorfahren** (`.monarch`, `.claude`), genau wie heute über `IsMatchOrAncestor` (PP:68-77). Nur die Projekt-Root selbst als Vorfahr bleibt destruktiv-only, wie heute. Der Schutz bei Platzhaltern wie `.monarc?` oder `.mon[a]rch` gilt damit für alle Befehle unverändert (`cp -R x/ledger .monarc?`, `chmod 000 .monarc?`). Technisch: Präfix-Match gegen den literalen Anfang L der Zone (`.monarch/`, `.claude/settings`, `.claude/plugins/`, `~/.monarch/` usw.; der Rest der Zone ist `**` bzw. `*.json` und wird übernähert: im Zweifel Treffer) oder vollständiges Erzeugen eines Vorfahren von L (`.monarch`, `.claude`; außerhalb der Projekt-Root die Vorfahrkette der Root, PP:84-88, dort nur destruktiv wie heute). Der Matcher darf die heutige Treffermenge nur dort verkleinern, wo er beweist, dass kein solches s existiert.
- **Zeichenklassen (Auswertung nur für positive Mengen):** `[abc]` mit ausschließlich literalen Mitgliedern wird ausgewertet, **jedes Mitglied einzeln** gefaltet (`PathFold`). Ein Mitglied, dessen Faltung nicht genau ein Zeichen ergibt (`ß`), macht die Klasse zu „beliebiges Zeichen“. POSIX-Regel: `]` direkt nach `[` ist Mitglied. Ein unterminiertes `[` (keine schließende `]`) ist ein literales `[`. **Alles andere ist „beliebiges Zeichen“:** Negation (`[!x]`, `[^x]`), Bereiche (`[a-z]`; locale-abhängig, bash 3.2 de_DE rechnet `m` zu `[A-Z]`), `[:name:]`, `[=c=]`, `[.c.]`. Das ganze Token wird **nicht** vorab gefaltet, denn Falten plus Klassenauswertung öffnete `.[!M]onarch`.
- **`..` hinter einem Wildcard:** Enthält das Token nach dem ersten Wildcard ein Segment `..`, zählt es als Treffer (`rm -rf ./s*/../.monarch/ledger`; ein Wildcard-Segment kann auf ein Verzeichnis zeigen, das `..` wieder aufhebt). Vor dem ersten Wildcard löst `GlobParts` das schon kanonisch auf.
- Quoting wird **nicht** ausgewertet (Regel 1 entfernt Quotes, das Werkzeug liest Pathspecs selbst).
- Der Test kann den Vorfilter nur **verengen**, nie erweitern.

**Beispiele**

| Token (Wort) | heute | neu | warum |
|---|---|---|---|
| `.[0].message` (`gh api`) | deny | allow | `.` dann `[0]`: zweites Zeichen `0`, Zone verlangt `m` |
| `.[]` (jq) | deny | allow | `[]` ohne Abschluss ist literal, `.[]` beginnt nicht mit L |
| `s/.*x//`, `s/.*?foo//g` (sed/perl) | allow | allow | Präfix `s/` (Regressionsschutz) |
| `.*`, `.m*`, `.mon[a]rch`, `.monarch/ledger/*` | deny | deny | erzeugen Strings mit L bzw. `.monarch` |
| `.[a-z]*`, `.[!M]onarch`, `.[]m]onarch`, `.[![:upper:]]onarch`, `.[[=m=]]onarch` | deny | deny | Bereich/Negation/Klasse = beliebiges Zeichen; `]m` sind Mitglieder |
| `'.monarch/**'` (`git rm`) | deny | deny | Pathspec, `**` hinter L |
| `'.monarch?ledger?x'`, `'.monarch[/]ledger'` (`git rm`) | deny | deny | `?` und `[/]` dürfen `/` überspannen |
| `./s*/../.monarch/ledger` (`rm -rf`) | allow | deny | `..` hinter Wildcard (schließt eine vorbestehende Lücke) |
| `.claude/sett*` (`rm -rf`) | deny | deny | Präfix `.claude/settings` |

## Fehlalarm 2 — #14: Interpreter-Prüfung über das ganze Kommando

**Befehl:** `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF' … EOF` → deny, ohne dass der Code einen Schutzpfad erwähnt.

**Ursache (verifiziert):** `BashProtect` (PP:145) findet ein Inline-Interpreter-Segment (`inline`, PP:148) und läuft dann `Compress(cmd)` (BA:149-159) über das **ganze** Kommando (PP:151). `Compress` entfernt Quotes, `\`, `+`, `,` und Whitespace und schreibt klein, damit `'.mon' + 'arch'` zu `.monarch` wird. Dann folgt `Contains(".monarch"|".claude")` (PP:152-153), auch für das vorangehende `cd …`; `.monarch-worktrees` enthält den Teilstring `.monarch`. Die Prüfung gilt nur dem **ersten** Inline-Interpreter-Segment (`FirstOrDefault`, PP:148).

### Entscheidung 2 — Default ist „Code“; nur nachweislich harmlose Segmente sind es nicht

Es gibt zwei Wege, „Code“ abzugrenzen. Eine **Aufzählung** der Code-Quellen (Argumente, Heredoc, Pipeline davor, Substitutionen, Gruppen, Zuweisungen, …) ist nicht lückenlos beweisbar: Der Lexer stellt `$(…)`, Backticks und `<(…)` als eigene Segmente davor (ShellLexer.cs:278-284, 369-373), `{ …; } | python3` und `( … ) | python3` markieren das Producer-Segment nicht als „pipet in den Interpreter“ (ShellLexer.cs:163-173), und Zuweisungen landen in `Assignments` (BA:279-285). Jede vergessene Quelle wäre eine Lockerung (`python3 -c "$(echo "…'.mon'+'arch/local/x'…")"`, `python3 <(echo …)`, `{ echo …; } | python3 -`, `C="…'.mon'+'arch…" python3 -c 'exec(os.environ["C"])'`). Darum gilt die **Negativliste**: Alles ist Code, außer ein Segment erfüllt **alle** Bedingungen für „nachweislich kein Code“. Was der Lexer nicht kennt, bleibt Code. Das ist fail-closed und beweisbar, weil der Default die strenge Seite ist.

**Nicht-Code-Segment** (alle Bedingungen):
1. Das Kommandowort ist `cd`, `pushd`, `popd`, `ls`, `pwd`, `true`, `false`, `test` oder `[`. Nicht `export`/`declare` (tragen Zuweisungen), nicht `echo`/`cat`/`printf` (liefern Text, der als Code ankommen kann).
2. Das Segment ist ein Segment der obersten Ebene, verbunden über `&&`, `||`, `;` oder Zeilenumbruch; es **pipet nicht** in ein anderes Segment.
3. Es hat keine Zuweisungen/env-Präfixe, keine Umleitung, keinen Heredoc/Here-String, und sein Rohtext enthält weder `$(`, Backtick, `<(` noch `>(`. Substitutionen in Argumenten (`cd "$(echo …)"`) erscheinen ohnehin als eigene, nicht gelistete Segmente und sind damit Code.
4. Das Kommando enthält **keine Funktions- oder `alias`-Definition** (`cd() { …; }`, `function cd`, `alias cd=…`). Sonst gibt es im ganzen Kommando keine Nicht-Code-Segmente (ein umdefiniertes `cd` wäre Code: `cd() { python3 -c "…os.remove(sys.argv[1]+sys.argv[2])" "$@"; }; cd .mon arch/local/x` bleibt deny).

**Im Code** (alles übrige: Text, Heredoc-Bodies, Zuweisungen, Umleitungsziele und Substitutions-Segmente aller nicht freigestellten Segmente) bleibt alles unverändert: `Compress` und `Contains(".monarch"|".claude")`, auch zerlegt wie `'.mon' + 'arch'`, `.MONARCH`, `os.path.join('.mon','arch')`. Die Tests `BashFailClosedTests.cs:35-41` bleiben unverändert. Zusätzlich gilt die Prüfung für **alle** Inline-Interpreter-Segmente, nicht nur das erste (Verschärfung).

**Außerhalb des Codes** (nur die Nicht-Code-Segmente) werden die Argumente mit `BashAnalysis.PathTokens` zerlegt, mit `PathFold` gefaltet und lexikalisch normalisiert (`.` entfällt, `..` hebt die vorige Komponente auf). Es zählt eine Komponente genau `.monarch` oder `.claude`; nicht `.monarch-worktrees`, `.claudex`, `x.monarch`. Gefaltet heißt: `cd .MONARCH/local && python3 …` bleibt deny.
- **Ausnahme `.claude/worktrees`:** Eine Komponente `.claude`, deren **nächste normalisierte** Komponente `worktrees` ist, zählt nicht (Gate-Worktrees sind Projektcode). Geschützt bleiben `.claude/settings*.json` und `.claude/plugins/**`; jede Komponente `.monarch` oder ein weiteres `.claude` darunter zählt.
- Wegen der Normalisierung: `.claude/worktrees/../settings.json` → `.claude/settings.json` zählt; `.claude/worktrees/G-0104/.monarch/local/x` zählt; `.claude/worktrees/G/../..` → `.claude` zählt.

**Beispiele**

| Befehl | heute | neu |
|---|---|---|
| `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF'` (Body `print(1)`) | deny | allow |
| `cd /work/proj/.claude/worktrees/G-0104 && python3 - <<EOF` (**ungequotet**, Body `print(1)`) | deny | allow |
| `cd .claude/worktrees/G-0104 && python3 -c "print(1)"` | deny | allow |
| `cd .monarch/local && python3 -c "print(1)"` | deny | deny |
| `cd .MONARCH/local && python3 -c "print(1)"` | deny | deny |
| `cd .claude && python3 -c "print(1)"`; `cd .claude/worktrees/../ && python3 -c …` | deny | deny |
| `cd .claude/worktrees/G/.monarch && python3 -c …` | deny | deny |
| `python3 -c "os.remove('.mon' + 'arch/local/x')"` | deny | deny |
| `cd ~/Projects/.monarch-worktrees/x && python3 -c "os.remove('.mon' + 'arch/local/x')"` | deny | deny |
| `echo "os.remove('.monarch/local/x')" \| python3 -` | deny | deny |
| `python3 -c "$(echo "import os; os.remove('.monarch/local/x')")"` | deny | deny |
| `python3 -c "$(echo "…'.mon'+'arch/local/x'…")"` | deny | deny |
| `python3 <(echo "…'.mon'+'arch/local/x'…")` | deny | deny |
| `{ echo "…'.mon'+'arch…"; } \| python3 -`; `( echo "…" ) \| python3 -` | deny | deny |
| `C="…'.mon'+'arch…" python3 -c 'exec(os.environ["C"])'` | deny | deny |
| `cd .monarch-worktrees/x && python3 - <<'EOF'` (Body `os.remove('.monarch/local/x')`) | deny | deny |
| `python3 -c 'print(1)'; python3 -c "…'.monarch/x'…"` (zweiter Interpreter) | allow | deny |

**Restrisiko (Klasse 2, bekannt):** Code kann den Namen aus Ordnernamen oder der Umgebung ableiten (`os.listdir`, `os.getcwd()`, ENV, `chr(46) + 'monarch'`). Das geht schon heute. Neu und bewusst: `cd .claude/worktrees/G && python3 -c "open('../../settings.json','w')"` wird allow, weil der Code den Namen nicht nennt. Über das Session-cwd (`cd` in `.claude`-Nachbarn ohne Nennung, relative Pfade im Code) ist dasselbe schon heute möglich. Die Teilstring-Suche im Code ist eine Heuristik gegen Fehlgriffe (G1), keine Garantie gegen gezielte Umgehung (G4). Die Grenze kommt in `plugin/README.md`, „Grenzen“ (TDD-Phase).

## Fehlalarm 3 — #47: `git restore --staged .monarch`

**Befehl:** `git restore --staged .monarch` → deny (Regel 2).

**Ursache (verifiziert):** `restore` und `reset` stehen in `MutatingGitSubcommands` (PP:27). `Classify` (PP:694-700) nimmt für Git alle Positionsargumente hinter dem Subkommando als destruktive Ziele und wertet **Flags nicht aus**. `git restore --staged .monarch` ist damit nicht von `git restore .monarch` (überschreibt die Datei auf der Platte) unterscheidbar.

### Entscheidung 3 — eng erlauben: nur der Index wird auf HEAD zurückgesetzt

Auf Schutzpfaden sind **genau** diese beiden Formen erlaubt (Flags als getrennte Tokens, beliebige Reihenfolge, Wiederholung erlaubt):

1. `git restore --staged|-S [-q|--quiet] [--] <pfade>`: erlaubt sind ausschließlich `--staged`/`-S` (mindestens einmal), `-q`/`--quiet`, `--`.
2. `git reset [-q|--quiet] [HEAD] -- <pfade>`: `--` ist **Pflicht**; vor `--` steht nichts außer `-q`/`--quiet` und optional `HEAD`.

Mindestens ein Pfad muss folgen.

**Mechanik (wichtig):** Die Ausnahme wird **nicht** über `IsPositive` (PP:556-561) gebaut, denn das macht das ganze Segment unmutierend und schaltet die Zuweisungsregel (PP:179, 255-262) ab. Gemessen: `GIT_INDEX_FILE=.monarch/ledger/x git add src/a` ist schon heute allow (vorbestehend, eigenes Issue, nicht hier lösen). Mit einer `IsPositive`-Ausnahme würde `GIT_INDEX_FILE=<zone>/new git restore --staged <pfad>` allow. Deshalb:
- Ausgenommen werden **nur die Pathspec-Operanden** (sie werden aus der Nennungs- und Destruktiv-Prüfung genommen). Das Segment bleibt mutierend (`SegmentMutates`), die Zuweisungsregel greift weiter.
- Die Ausnahme gilt **nicht**, wenn im Aufruf irgendwo eine `GIT_*`-Zuweisung, ein `export GIT_…` oder ein env-Präfix (`env GIT_…=… git …`) steht, oder wenn zwischen `git` und dem Subkommando **irgendein** Token steht (`-C`, `-c`, `--git-dir`, `--work-tree`, `--exec-path`, `--namespace` und alles andere). Dann gilt die heutige Klassifikation (deny).
- **Nur wörtliche Argumente (Owner-Entscheid):** Die Ausnahme gilt nur, wenn alle Argumente wörtlich dastehen. Sie entfällt (heutige Klassifikation, deny), sobald eines davon zutrifft: das Segment hat Substitution oder Kind-Segmente (`$(`, Backtick, `<(`, `>(`); irgendein Token enthält `$`; ein Operand enthält Glob- oder Brace-Zeichen (`*?[]{}`); das Segment läuft über `xargs`, `env`, `command`, `exec` o. ä. (Wrapper); `Tokens[0]` ist nicht exakt `git` (also `./git` oder `/pfad/git`). Weitere Verschleierungen (Variablen, die Flags liefern, Wrapper-Tricks) sind keine Jagd wert, sondern nach ADR-0011 eine Grenze (G4), die unter „Folgen und Grenzen“ steht.
- `git -C .monarch restore --staged x` ist damit deny durch **Regel 2** (das Token `.monarch` ist eine Nennung), nicht durch Regel 3.

**Bleibt bei der heutigen Klassifikation (deny):** `git restore` ohne `--staged`; `--worktree`/`-W`, auch gebündelt (`-SW`; auch `-qS` wird nicht zerlegt, Bündel sind fail-closed); `--source`/`-s` in jeder Schreibweise (`--source=HEAD~1`, `--source HEAD`, `-sHEAD`); `-p`/`--patch`; `--pathspec-from-file`; jedes andere Flag; `git reset <commit> -- …`, `git reset HEAD~1 -- …`, `git reset @ -- …`; `git reset <pfad>` ohne `--` (auch `git reset HEAD .monarch/ledger/x`); `--soft/--mixed/--hard/--merge/--keep`; `git rm --cached`; `git checkout -- .monarch`.

**Warum Ledger-Datei und Historie unberührt bleiben:** `git restore --staged` und `git reset -- <pfade>` schreiben nur die Index-Datei (`.git/index`). Der Arbeitsbaum, also die Ledger-Datei mit ihrer Hash-Kette, wird nicht angefasst (dafür gäbe es `--worktree`, das gesperrt bleibt). `HEAD`, Refs und committete Objekte bleiben unverändert (`HEAD` ist die einzige zulässige Quelle). Das Ergebnis ist „vorgemerkt“ → „nicht vorgemerkt“; ein neues `git add` stellt den Zustand wieder her. Für G3: Die Kette wird weder gekürzt noch überschrieben, und gepushte Commits sind der Anker (ADR-0010). Es kann höchstens ein verwaister Blob im Objektspeicher bleiben. Dass der Index selbst nicht per Umgebungsvariable umgebogen wird, sichert die Mechanik oben.

**Beispiele**

| Befehl | heute | neu |
|---|---|---|
| `git restore --staged .monarch` | deny | allow |
| `git restore -S -q -- .monarch/ledger` | deny | allow |
| `git reset -q HEAD -- .monarch/ledger/x`; `git reset -- .monarch/ledger/x` | deny | allow |
| `git restore .monarch` | deny | deny |
| `git restore --staged --worktree .monarch`; `-SW`; `-qS` | deny | deny |
| `git restore --source=HEAD~1 --staged .monarch`; `-s HEAD`; `-sHEAD` | deny | deny |
| `git restore -p .monarch`; `--pathspec-from-file=x` | deny | deny |
| `git reset HEAD .monarch/ledger/x` (ohne `--`) | deny | deny |
| `git reset HEAD~1 -- .monarch/ledger/x`; `git reset --hard -- .monarch` | deny | deny |
| `git rm --cached .monarch/ledger/x`; `git checkout -- .monarch` | deny | deny |
| `GIT_INDEX_FILE=.monarch/ledger/new git restore --staged src/a` | deny | deny |
| `GIT_INDEX_FILE=.monarch/ledger/new git reset -q -- src/a`; `export GIT_DIR=x; git reset -- a` | deny | deny |
| `git -C .monarch restore --staged x`; `git -c core.x=y restore --staged .monarch`; `git --git-dir=d restore --staged .monarch` | deny | deny |

## Warum keine Schutzregel fällt (Maßstab ADR-0011)

**Klasse 1 (Literal → Blocker).**
- *Pfad nach Normalisierung im Kommandotext:* Die Normalisierung (Regel 1) ändert sich nicht. Literale Pfade laufen unverändert durch `Hit`. Bei Globs bleibt jedes Token Treffer, das einen Schutzpfad benennen kann (flaches Match, jeder Wildcard darf `/` überspannen, Negation/Bereiche/Klassen sind „beliebiges Zeichen“, `..` hinter dem Wildcard zählt); der Test sortiert nur beweisbar nicht treffende Tokens aus. Der Wert von `--jq` kann keinen Pfad benennen, und die Umleitung bleibt separat deny (`gh api … --jq . > .monarch/ledger/x`). Ein `sed`-Skript ist nur Daten ohne `w`/`W`/`e`; Dateioperanden bleiben Pfade.
- *Wirksames cwd in einer Schutzzone und pfadloses Schreiben (Regel 3):* unverändert. Neu ist nur, dass in reinen `cd`/`ls`-Segmenten `.monarch-worktrees` nicht mehr als `.monarch` zählt. `cd .monarch/local`, `cd .MONARCH/local`, `cd .claude` und `cd .claude/worktrees/..` zählen weiter.
- *Link auf Vorfahr/Teil einer Zone (Regel 4), pfadlose Arbeitsbaum-Mutation (Regel 5):* unverändert. `git restore --staged` fasst den Arbeitsbaum nicht an; `-W`, `--source`, `--hard`, `--merge`, `--keep` und `git checkout -f` bleiben deny bzw. ask. Die bestehenden Tests `Adr0011Tests.cs:127-128` sind **Ask**-Tests (`Rule5_pathless_worktree_mutations_ask`) und bleiben Ask.

**Regel 2 (fail-closed bei Nennung):** Sie bleibt. Wir ändern, **was als Nennung gilt**, nicht, wie auf eine Nennung reagiert wird. `gh` und `git fetch` kommen **nicht** in die Lesend-Liste (`git fetch --upload-pack=<cmd>` führt aus, `gh run download -D <dir>` schreibt). Sie sind erlaubt, weil kein Argument einen Schutzpfad benennt.

**Interpreter (Klasse 2):** Im Code ändert sich nichts, die Abdeckung wird breiter (alle Inline-Interpreter; Default „Code“ statt Aufzählung). Klasse 2 bleibt Follow-up mit Beleg: Die Grenze steht in `plugin/README.md`, „Grenzen“, und die Folge für das Ledger ist unter G3 erkennbar. Die Lockerung betrifft nur Text in nachweislich harmlosen `cd`/`ls`-Segmenten.

**Gegenbeispiele (bleiben deny):** siehe die Tabellen oben; zusätzlich `rm -rf .*`, `rm -rf .m*`, `mv .mon[a]rch x`, `rm .monarch/ledger/*`, `git rm '.monarch/**'`, `rm -rf .[a-z]*`, `gh api … --jq . > .monarch/ledger/x`, `sed -i 's/x/y/w .monarch/ledger/x' f.txt`, `sed -i 's/,.*//' .monarch/ledger/x`.

**G1–G3:** G1 bleibt vollständig. G2: `.claude/settings*.json`, `.claude/plugins/**` und `.monarch/system.yaml` bleiben geschützt, auch unter `.claude/worktrees/../`; eine `GIT_*`-Umbiegung des Index schließt die #47-Ausnahme aus. G3: Datei und Historie bleiben bei `restore --staged` unberührt.

## Teststrategie (TDD: rot vor grün, je Issue mit dem echten Kommando)

Dateien: `tests/Monarch.Policy.Tests/Adr0011Tests.cs` (Regel 2, Globs, sed, gh, git) und `tests/Monarch.Policy.Tests/BashFailClosedTests.cs` (Interpreter). Kein bestehender Test wird gelöscht oder gelockert; `BashFailClosedTests.cs:35-41` und `64-68`, `ToolCallPolicyTests.cs:96` laufen unverändert grün; `Adr0011Tests.cs:127-128` sind Ask-Tests und bleiben Ask.

**Allow, vorher rot (muss vor der Implementierung scheitern):**
- `Issue28_gh_jq_value_is_data_allowed`: `gh api repos/o/r/commits --jq '.[0].message'`, `--jq='.[0].message'`, `-q '.[0].message'`, `-q'.[0].message'`, `-q='.[0].message'`, `gh api … --template '{{.[0]}}'`.
- `Issue28_sed_script_is_data_allowed`: `sed -i -E 's/^(.*)x/\1/' f.txt`, `sed -i 's/,.*//' f.txt`.
- `Issue14_*`: `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF'` (Body `print(1)`), ungequotet `cd /work/proj/.claude/worktrees/G-0104 && python3 - <<EOF`, `cd .claude/worktrees/G-0104 && python3 -c "print(1)"`.
- `Issue47_git_restore_staged_index_only_allowed`: `git restore --staged .monarch`, `git restore -S -q -- .monarch/ledger`, `git reset -q HEAD -- .monarch/ledger/x`, `git reset -- .monarch/ledger/x`.
- `Dot_wildcard_tokens_not_matching_zones_allowed`: `.[0]`, `.[]` in `jq`/`gh`-Argumenten.

**Allow, schon grün (Regressionsschutz, nicht „vorher rot“):** `gh pr checks 29`, `gh pr view 29 --json statusCheckRollup --jq '.statusCheckRollup[].conclusion'`, `gh run list --branch develop --limit 5`, `gh run view 41038797796 --log-failed`, `git fetch origin`, `git rev-parse HEAD`, `git log --oneline -5`, `git diff origin/develop...HEAD`, `jq '.[] | .name' file.json`, `jq '.[0]' datei`, `jq '.[]' f.json`, `sed 's/.*x//' f.txt`, `perl -pe 's/.*?foo//g' f.txt`. Ob ein Fall heute rot oder grün ist, wird in der TDD-Phase gemessen, nicht angenommen.

**Deny (muss grün bleiben bzw. neu grün sein):**
- Globs: `rm -rf .*`, `.m*`, `mv .mon[a]rch x`, `rm .monarch/ledger/*`, `git rm '.monarch/**'`, `rm -rf .[a-z]*`, `.[!M]onarch`, `.[]m]onarch`, `.[![:upper:]]onarch`, `.[[=m=]]onarch`, `git rm '.monarch?ledger?x'`, `git rm '.monarch[/]ledger'`, `rm -rf ./s*/../.monarch/ledger` (neu), `rm -rf .claude/sett*`, sowie die Vorfahr-Platzhalter bei **nicht**-destruktiven Wörtern: `cp -R x/ledger .monarc?`, `cp -R x/ledger .mon[a]rch`, `chmod 000 .monarc?` (je Form ein Testfall).
- gh: `gh api … --jq '.[0]' > .monarch/ledger/x`; `gh api --jq '.x' .monarch/ledger/x`.
- sed/perl: `sed -i 's/x/y/w .monarch/ledger/x' f.txt`, `sed -i 's/,.*//' .monarch/ledger/x`, `sed -i -f s.sed -E 's/(.*)//' f`, `perl -pi -e 's/(.*?)foo//g' f` (bleibt deny, dokumentierte Grenze), `perl -e 'system("rm -rf .m*")'`, `sed -i'.monarch/ledger/*' 's/a/b/' f`, `sed --in-place='.monarch/ledger/*' 's/a/b/' f`.
- Interpreter: alle deny-Zeilen der Tabelle in Fehlalarm 2, zusätzlich `cd() { python3 -c "…os.remove(sys.argv[1]+sys.argv[2])" "$@"; }; cd .mon arch/local/x`.
- git: alle deny-Zeilen der Tabelle in Fehlalarm 3, zusätzlich `W=--worktree; git restore --staged .monarch $W`, `git restore --staged .monarch $(cat f)`, `printf -- --worktree | xargs git restore --staged .monarch`, `git restore --staged .monarch *`, `./git restore --staged .monarch/ledger`.

**Matcher-Einheitentests:** Klassen (positive Mengen, `]` als erstes Mitglied, unterminiert), Negation, Bereiche, `[:…:]`, `[=…=]`, Wildcards über `/`, `..` hinter dem Wildcard, Faltung je Mitglied.

**Latenz:** unverändert p99 ≤ 20 ms (Bench-Grenze unangetastet); der Matcher läuft nur nach positivem Vorfilter.

## Folgen und Grenzen

- Code-Änderungen: `src/Monarch.Policy/ProtectPathsRule.cs` (`DropDataValues`, `BashProtect`, `Classify`, `ScriptWriterHit`-Umfeld, `Find`), `src/Monarch.Policy/GlobMatcher.cs`, ggf. `src/Monarch.Policy/BashAnalysis.cs`. `plugin/README.md`, „Grenzen“, wird in der TDD-Phase nachgezogen (Fehlalarme mit Ausweg; perl/python-Inline-Code mit Muster `(.*`; `cd .claude/worktrees/G && python3 -c "open('../../settings.json','w')"` als Klasse 2).
- ADR-0011 bekommt bei Regel 2 einen Verweis auf dieses ADR (erledigt).
- **Vorbestehende Lücken, nicht Teil dieser Quest** (heute allow, ADR-0014 verschlechtert nichts): `git rm -r ':/.monarch/ledger/*'`, `git rm -r ':^src'`, zsh `rm -rf .m(o|x)narch/ledger` (Issue #52); `GIT_INDEX_FILE=.monarch/ledger/x git add src/a` (eigenes Issue; `IsPositive` macht `git add` unmutierend und schaltet die Zuweisungsregel ab); Pfad-Erkennung in sed-Skripten ist tokenbasiert (`w.monarch/x` angehängt).
- **Grenze (ADR-0011, kein Blocker):** Weitere Verschleierungen der #47-Argumente über Variablen oder Wrapper werden nicht gejagt; die Ausnahme verlangt wörtliche Argumente, alles andere bleibt deny wie heute.
- **Nicht Teil dieser Quest:** Der Fehlalarm bei `for f in .monarch/quests/*.md; do sed -n 1p "$f"; done` wird ein eigenes Issue.
- Grenze: Tokens in mutierenden Befehlen, die wegen `*` über `/` einen Zonennamen benennen können (z. B. `.*)x` aus einem perl-Muster), bleiben konservativ Treffer.
- Restrisiko Klasse 2 (Code leitet den Namen zur Laufzeit ab) bleibt wie in ADR-0011 dokumentiert.

## Nachtrag Q-0138 (G-0105): `sed 's/.*/new/' f` wieder frei, sonst nichts

**Datum:** 04.11.2026 · **Status:** Entwurf (Design-Review Warden ausstehend) · **Issue:** 55

### Kurz in einfacher Sprache

- Der Fix aus der Code-Runde von Q-0133 sperrt seitdem auch harmlose Lesebefehle wie `sed 's/.*/new/' f`. Das Wort `new` enthält die Buchstaben `e` und `w`, deshalb wird das Skript als möglicher Schreiber geprüft, und `.*` zählt dann als Treffer.
- Owner-Entscheid vom 30.10.: streng. Frei wird nur diese eine Form. Alles andere, was bei `d45e6b9` gesperrt ist, bleibt gesperrt.
- Dafür ändert sich der Matcher nicht. Die Regel für `.*` bleibt, wie sie ist; es gibt nur eine eng begrenzte Ausnahme davor.

### Ursache

`ScriptWriterHit` nimmt ein sed-Skript in die Pfadprüfung, sobald es irgendwo die Buchstaben `w`, `W` oder `e` enthält (`ProtectPathsRule.cs:612`). Das trifft auch den Ersatztext eines `s`-Befehls. In der Pfadprüfung zählt ein Segment wie `.*` sofort als Treffer, weil es `.` oder `..` erzeugen kann (`NormalizeTail`, `:877`, `Find`, `:892`, aus Warden-Befund 1 zu Q-0133).

### Entscheidung

`ScriptWriterHit` überspringt ein sed-Skript nur, wenn **alle** Bedingungen gelten. Sonst läuft die Prüfung wie bei `d45e6b9`.

1. **Exakte Tokenform.** Das Segment hat genau die Tokens `sed <Skript> <Datei>` (drei) oder `sed -e <Skript> <Datei>` (vier). Damit gibt es genau ein Skript und genau einen Operanden. Keine weitere Option an irgendeiner Stelle, auch nicht hinter dem Operanden, wo GNU-sed sie noch liest (`f -i`, `f --in-place`, `f -n`, `f -e …`).
2. **Das Skript** ist genau `s/.*/R/`: Trennzeichen `/`, Regex genau `.*`, keine Flags, `R` besteht aus mindestens einem Buchstaben oder einer Ziffer (`[A-Za-z0-9]+`).
3. **Der Operand** beginnt nicht mit `-` (auch nicht `-` allein oder `--`), enthält kein `$` und kein Glob- oder Brace-Zeichen, und die Operandenprüfung wie in `ProtectPathsRule.cs:374-381` (`PathCandidates`, `Find`, `IsDecoyMonarch`) findet nichts. Damit bleiben auch `sub/.monarch/x` und Vorfahren gesperrt.
4. **Nichts außer stdout.** Das Segment hat keine `OutRedirects`, `InRedirects`, keinen `Heredoc`, kein `PipesToNext`, und es bekommt keine Pipe-Eingabe. Wie bei `ProtectPathsRule.cs:244` zählt die Umleitung also nicht als harmlos.
5. **Wörtlich wie bei #47.** Geprüft wird am Aufrufer (`ProtectPathsRule.cs:362`) auf `seg`, nicht auf dem Ergebnis von `Effective()`, das bei `xargs` auch `sed` liefert: `seg.Tokens[0]` und `RawWords` sind genau `sed` (kein `env`, `command`, `xargs`, `./sed`), und im Kommando steht keine Substitution.

`NormalizeTail`, `Find`, `FlatPattern` und `DropSedScript` bleiben unverändert. Die Ausnahme sitzt ausschließlich vor der Skriptprüfung. Weitere Verschleierungen sind nach ADR-0011 eine Grenze, kein Blocker.

**Lesart des Owner-Entscheids.** `f` und `new` sind Platzhalter: Ein Fix nur für die Datei `f` wäre keiner. Deshalb gilt die Ausnahme für diese eine Form mit einem beliebigen Dateinamen und einem einfachen Ersatzwort, nicht für zwei exakte Zeichenketten. Enger geht es ohne Sonderfälle für einzelne Wörter nicht. Weiter geht sie nicht: Der Regex bleibt genau `.*`, und es gibt genau einen Operanden.

### Wechsel gegenüber `d45e6b9`

| Kommando | `d45e6b9` | neu | Warum unmöglich zu schreiben |
|---|---|---|---|
| `sed 's/.*/new/' f` | deny | **allow** | Ohne Flags ist `s` kein Schreiber; ohne `-i` und ohne Umleitung oder Pipe geht die Ausgabe nur nach stdout |
| `sed -e 's/.*/e/' f` | deny | **allow** | wie oben |

Das sind die einzigen Wechsel, abgesehen von anderen Dateinamen und Ersatzwörtern derselben Form. Ein `s`-Befehl schreibt nur mit dem Flag `w` und führt nur mit dem Flag `e` aus; beide stehen hinter dem letzten Trennzeichen, und dort lässt die Form nichts zu. `R` kann das Trennzeichen nicht enthalten, also keinen zweiten Befehl anhängen.

### Bleibt deny (bekannte Fehlalarme, Owner-Entscheid)

- `awk '/.*/ {print | "sort"}' f`
- `rm -rf dist/.*` (`dist/..` ist die Projekt-Root)
- `sed -i 's/.*/new/' f`, `sed -n 's/.*/new/p' f`, `sed -E 's/.*/new/' f`, `sed 's/.*/new/' a b`, `sed 's/.*/new/' f > out`, `sed 's/.*/new/' f | sort`
- Skripte mit einem Segment `.*` oder einem Token, das in die Zone reichen kann, sobald darin `w`, `W` oder `e` vorkommt, auch nur als Buchstabe: `sed -n '/private static .*Foo/,/^}/p' f`. Diese Form kam beim Lesen von Quelltext am 04.11. zweimal vor. Ausweg: `grep -n` und das Read-Werkzeug.

### Bleibt deny (Schutz)

Unverändert: alle Fälle in `Adr0014Tests.cs:55-82` und `:148-159`, darunter schon `rm -rf .?/*` (`:154`). Neu als Test: `rm -rf dist/.?/.monarch/ledger`, `rm -rf dist/.*/.monarch`, `cp -R x/ledger dist/.?/.monarc?`, `chmod -R 000 dist/.*`, `sed 's/x/y/w s/.?/.monarch/ledger/x' f`, `sed -i 's/a/b/' dist/.?/.monarch/ledger/x`. Zusätzlich gegen die Ausnahme:

- Operand: `sed 's/.*/new/' .monarch/ledger/x`, `sed 's/.*/new/' sub/.monarch/x`, `sed 's/.*/new/'` ohne Operand
- Skript: `sed 's/.*/new/w .monarch/ledger/x' f`, `sed 's/.*/new/e' f`, `sed 's/.*/new/;w .monarch/ledger/x' f`
- Optionen hinter dem Operanden: `sed 's/.*/new/' f -i`, `… f --in-place`, `… f -n`, `sed 's/.*/new/' f -e 'w .monarch/ledger/x'`, `sed 's/.*/new/' -- f`, `sed 's/.*/new/' -`, `sed 's/.*/new/' {f,-i}`
- Umleitung und Pipe: `sed 's/.*/new/' f > out`, `… > $T`, `… f | sh`, `… f < g`, `cat g | sed 's/.*/new/' f`
- Nicht wörtlich: `sed 's/.*/$NEW/' f`, `sed "s/.*/$(cat g)new/" f`, `sed 's/.*/new/' $F`, `sed 's/.*/new/' *`
- Wrapper: `env sed 's/.*/new/' f`, `./sed 's/.*/new/' f`, `printf f | xargs sed 's/.*/new/'`
- Zwei Skripte: `sed -e 's/.*/new/' -e 'w .monarch/ledger/x' f`

`ProtectPathsRule.cs` ist seit `d45e6b9` unverändert, der Lauf vor dem Fix misst also genau diesen Stand. Jeder Deny-Fall wird dort gegengeprüft. Ein dort schon erlaubter Fall ist ein eigener Befund und nicht Teil dieser Quest; er bleibt, wie er ist, und wird gemeldet.

**Eigener Befund, nicht Teil dieser Quest:** `sed -e's/x/y/w .monarch/ledger/x' f` (Skript direkt an `-e`) übergeht `ScriptWriterHit` (`:607`). Siehe Issue 59.

### Teststrategie

`Adr0014Tests.cs`: Die zwei Allow-Fälle vorher rot, danach grün. Die Deny-Listen oben sind Regressionswächter und vorher grün; sie sichern, dass die Ausnahme nicht weiter reicht. Bestehende Tests bleiben unverändert. Latenz: Die Prüfung läuft nur für `sed` und nur vor der Skriptprüfung.
