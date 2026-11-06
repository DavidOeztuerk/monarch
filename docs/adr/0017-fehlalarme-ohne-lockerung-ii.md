# ADR-0017 — Fehlalarme ohne Lockerung II: tr und Issue-Texte (#75, #68 Punkt 2)

**Datum:** 06.11.2026 · **Status:** Fassung 4. Warden: Alle Befunde zu tr und Issue-Texten sind geschlossen; der einzige offene Befund betraf grep, das nach Owner-Entscheid nicht mehr Teil dieses ADR ist · **Quest:** Q-0147 (G-0107) · **Baut auf:** ADR-0011, ADR-0014

## Kurz in einfacher Sprache

- Monarch verweigert eine harmlose Pipeline, weil es das Argument von `tr` für einen Pfad hält.
- In einer Worktree gibt es außerdem keinen Weg, einen Issue-Text aus `.monarch/knowledge/` zu übergeben.
- Künftig erkennt Monarch in diesen zwei eng beschriebenen Fällen sicher, dass kein Schutz berührt wird. In jedem anderen Fall bleibt alles genau wie heute.
- Kein vorhandener Test ändert sich.
- **Nicht in diesem ADR**, beides nach Owner-Entscheid vom 06.11. in einem Folge-Gate:
  - **sed** (#67, #68 Punkt 1, #69): Der Fix verlangt, den Entscheid vom 30.10. (ADR-0014, STRENG) teilweise aufzuheben. Der Entscheid gilt bis dahin weiter.
  - **grep** (#66): Der Lexer verwirft leere Wörter, darum sähe in `grep '' .env` die Datei wie das Suchmuster aus. Die Warden-Runden endeten hier ohne approve.

Die Vorarbeit steht in `.monarch/knowledge/G-0107-sed-folgegate.md` und `.monarch/knowledge/G-0107-grep-folgegate.md`.

## Grundsatz

Jede Ausnahme ist ein Beweis, keine Vermutung. Sie greift nur, wenn **alle** Bedingungen gelten. Sonst läuft der heutige Code unverändert (Rückfall). Gemeinsame Bedingungen für beide Ausnahmen, geprüft am Aufrufer auf `seg`, nicht auf dem Ergebnis von `Effective()`:

- **Rohtext:** `HasSubstitution(cmd)` und `HasDefinition(cmd)` sind falsch (Vorlage: `NonCodeAllowed`, `ProtectPathsRule.cs:198`). Der Lexer nimmt `$(…)`, Backticks und `<(…)` aus den Wörtern, darum genügt die Prüfung der Tokens nicht. Neu ist die Prüfung, dass der Rohtext `cmd` weder `$` noch `{` noch `}` enthält: Der Lexer löst `$'…'` auf und expandiert Braces.
- **Literal:** Kein Token des Segments enthält eines der Zeichen `*?[]`.
- **Direkt:** `seg.RawWords` ist gleich `seg.Tokens`, `seg.Assignments` ist leer, und `RawWords[0]` ist genau das Kommandowort, ohne Pfad. Damit fallen `env`, `sudo`, `VAR=x tr`, `xargs tr` und `./tr` aus der Ausnahme.

Eine Ausnahme nimmt nur die bewiesenen Argumente aus der Prüfung. Alle übrigen Tokens, Umleitungen und Segmente werden weiter geprüft. `IsMutating`, `ReadOnlyWords` und `SecretReaders` ändern sich nicht, also auch nicht Rank S und der Bypass-Modus.

Leere Wörter, die der Lexer verwirft, sind hier unschädlich: `tr` nimmt keine Dateien, und bei `gh` entfällt nur ein Wert, den `gh` selbst als Datei liest.

## Entscheidung 1 — tr als letzte Pipe-Stufe (#75)

**Neu:** Die Kandidaten-Schleife von Regel 2 überspringt das Segment (wie `ProtectPathsRule.cs:390`), wenn alle Bedingungen gelten. Das geht bewusst nicht über `IsReading`, sonst fiele die Zuweisungsregel weg.

- `RawWords[0]` ist `tr`.
- Das vorige Segment mündet per `|` in dieses, und dieses mündet in keines mehr (letzte Stufe).
- Das Segment hat weder Aus- noch Eingabe-Umleitung noch Heredoc.
- Optionen sind nur Bündel aus `c C d s`, und es gibt höchstens zwei Operanden.

`tr` nimmt keine Dateiargumente, liest nur stdin und schreibt nur stdout.

## Entscheidung 2 — Issue-Text aus .monarch/knowledge (#68 Punkt 2, Ergänzung in #69)

**Ursache:** Im Projekt-Root ist `gh issue create --body-file .monarch/knowledge/x.md` heute schon erlaubt. Die Verweigerung aus #69 entsteht nur, wenn der Arbeitsordner nicht die Wurzel des ausgewerteten Projekts ist: In einer Worktree wertet der Hook auch das umgebende Projekt aus (`HookRunner.cs:178-181`), und dort gilt die Datei als weiteres `.monarch` (`ProtectPathsRule.cs:405`).

**Neu:** Bei `gh issue create` entfällt in Regel 2 genau der Wert von `--body-file`, `--body-file=` oder `-F`, wenn alle Bedingungen gelten:

- Die ersten Tokens sind genau `gh issue create`.
- Der Wert ist relativ, beginnt mit `.monarch/knowledge/` und enthält kein `..`.
- `CwdAt(si)` ist ein Array aus logischem und physischem Arbeitsordner. Es ist nicht leer, und für **jeden** Eintrag liegt **jede** Variante aus `AbsFrom(wert, eintrag)`, also auch die mit aufgelösten Symlinks, unter `<eintrag>/.monarch/knowledge/`. Für jeden Eintrag muss `<eintrag>/.monarch/knowledge/` außerdem als Ordner existieren. Ohne diese Prüfung wäre die Bedingung bei einem relativen Wert immer wahr. Zusätzlich darf vor dem Segment kein `cd`, `pushd` oder `popd` stehen, weil das cwd-Modell `||` und `&&` nicht kennt und jedes `cd` nacheinander anwendet (`cd . || cd ../../..` bleibt in der Shell am ersten Ort, das Modell landet im zweiten). Sonst greift der Rückfall. Das gilt in beiden Auswertungen gleich, im Projekt der Worktree wie im umgebenden Projekt. Ist der Arbeitsordner selbst ein Symlink, greift der Rückfall; der Allow-Test braucht darum einen Pfad ohne Symlink.
- Der Pfad trifft keinen Secret-Glob. Sonst greift der Rückfall.

Andere `gh`-Befehle wie `gh issue edit`, `gh pr create` und `gh api -F` bleiben wie heute. `cp` aus `.monarch/knowledge/` bleibt verweigert: `--body-file` macht das Kopieren unnötig.

**Bekannte Lücke, nicht von dieser Änderung:** `gh` steht nicht in `SecretReaders`. `gh issue create --body-file .env` ist heute im Root erlaubt, also auch `.monarch/knowledge/.env`. Das wird ein eigenes Issue. Hier gilt nur: Die neue Ausnahme verbreitert die Lücke nicht, weil sie Secret-Globs ausnimmt.

## Tests (Pflicht je Fehlalarm: weg und Gegentest)

| Issue | Muss erlaubt sein | Muss verweigert bleiben |
|---|---|---|
| #75 | `cd .monarch/quests && grep -l 'gate: G-0001' *.md \| tr '\n' ' '` | `tr a b > .monarch/ledger/x`, `tr a b .monarch/ledger/x` ohne Pipe, `cat f \| tr a b > .monarch/quests/n`, in `.monarch/quests`: `cat f \| tr a b \| sh`, `cat f \| tr "$X" b`, `cat f \| tr a b c` |
| #68.2 | aus einer verschachtelten Worktree mit beiden Auswertungen: `gh issue create --title t --body-file .monarch/knowledge/x.md` | aus derselben Worktree: `--body-file .monarch/ledger/x`, `--body-file .monarch/knowledge/../ledger/x`, `--body-file .monarch/knowledge/.env`, ein Symlink in knowledge auf das Ledger, `gh issue edit 1 --body-file .monarch/knowledge/x.md`, nach einem mehrdeutigen `cd` (`cd a \|\| cd b; gh issue create --body-file .monarch/knowledge/x.md`) |

Jeder Allow-Test muss vor dem Fix rot sein. Vorhandene Tests werden nicht geändert. Wechselt einer, ist das ein Stopp und geht an den Owner.

## Bekannte Fehlalarme, die bleiben

Bewusst nicht behoben, weil sie den Beweis schwächen würden: `tr` mitten in einer Pipe oder ohne Pipe. Sie werden nur verweigert, wenn ein Argument wie ein Schutzpfad aussieht.
