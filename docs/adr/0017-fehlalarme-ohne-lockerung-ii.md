# ADR-0017 — Fehlalarme ohne Lockerung II: grep, tr, Issue-Texte (#66, #75, #68 Punkt 2)

**Datum:** 06.11.2026 · **Status:** vorgeschlagen, Fassung 2 nach Warden-Design-Runde; wartet auf das Delta-Verdict vor dem ersten Code-Commit · **Quest:** Q-0147 (G-0107) · **Baut auf:** ADR-0011, ADR-0014

## Kurz in einfacher Sprache

- Monarch verweigert harmlose Befehle, weil es Text für einen Pfad hält: das Suchwort `\.env` bei `grep` und das Argument von `tr`.
- In einer Worktree gibt es außerdem keinen Weg, einen Issue-Text aus `.monarch/knowledge/` zu übergeben.
- Künftig erkennt Monarch in drei eng beschriebenen Fällen sicher, dass kein Schutz berührt wird. In jedem anderen Fall bleibt alles genau wie heute.
- Kein vorhandener Test ändert sich. Wer eine Credential-Datei liest oder in einen Schutzpfad schreibt, wird weiter verweigert.
- **sed gehört nicht dazu** (#67, #68 Punkt 1, #69): Owner-Entscheid vom 06.11. Der Fix verlangt, den Entscheid vom 30.10. (ADR-0014, STRENG) teilweise aufzuheben. Das ist verschoben, sed kommt in ein Folge-Gate, und der Entscheid gilt bis dahin weiter.

## Grundsatz

Jede Ausnahme ist ein Beweis, keine Vermutung. Sie greift nur, wenn **alle** Bedingungen gelten. Sonst läuft der heutige Code unverändert (Rückfall). Gemeinsame Bedingungen für alle drei Ausnahmen, geprüft am Aufrufer auf `seg`, nicht auf dem Ergebnis von `Effective()`:

- **Rohtext:** `HasSubstitution(cmd)` und `HasDefinition(cmd)` sind falsch (wie `ProtectPathsRule.cs:245-247`). Der Lexer nimmt `$(…)`, Backticks und `<(…)` aus den Wörtern, darum genügt die Prüfung der Tokens nicht. Der Rohtext enthält außerdem kein `{` und kein `}`, weil der Lexer Braces expandiert.
- **Literal:** Kein Token enthält `$` oder eines der Zeichen `*?[]`.
- **Direkt:** `seg.RawWords` ist gleich `seg.Tokens`, `seg.Assignments` ist leer, und `RawWords[0]` ist genau das Kommandowort, ohne Pfad. Damit fallen `env`, `sudo`, `VAR=x grep`, `xargs grep` und `./grep` aus der Ausnahme.

Eine Ausnahme nimmt nur die bewiesenen Argumente aus der Prüfung. Alle übrigen Tokens, Umleitungen und Segmente werden weiter geprüft. `IsMutating`, `ReadOnlyWords` und `SecretReaders` ändern sich nicht, also auch nicht Rank S und der Bypass-Modus.

## Entscheidung 1 — grep: Das Suchmuster ist kein Dateioperand (#66)

**Ursache:** `ProtectSecrets` (`ToolCallPolicy.cs:365`) prüft jedes Token, auch das Muster.

**Neu:** Eine Funktion liefert die Token-Indizes der Muster. Nur diese überspringt die Schleife in `ProtectSecrets`, und nur für `grep`, nicht für `rg`. Regel 2 ändert sich nicht.

- Jedes Optionstoken im **ganzen** Segment muss bekannt sein, denn grep erlaubt Optionen auch nach den Operanden. Erlaubt sind Bündel aus `n E F G P i l L v c H h o q s w x I` sowie `-e WERT`, `--regexp=WERT` und `--regexp WERT`. Alles andere führt in den Rückfall, auch `-f`, `--file`, `-A 3`, `--exclude-from`, `--`, `-r` und `-R`.
- `-r` und `-R` fehlen bewusst: `grep -rn '\.env' .` liest `./.env`. Das bleibt deny und damit ein bekannter Fehlalarm.
- Mit `-e` oder `--regexp` sind nur deren Werte Muster, jeder Positionsoperand bleibt Datei. Ohne sie ist der erste Positionsoperand das Muster.
- Im Kommando steht keine Zuweisung eines Namens mit dem Präfix `GREP_`.

## Entscheidung 2 — tr als letzte Pipe-Stufe (#75)

**Neu:** Die Kandidaten-Schleife von Regel 2 überspringt das Segment (wie `ProtectPathsRule.cs:390`), wenn alle Bedingungen gelten. Das geht bewusst nicht über `IsReading`, sonst fiele die Zuweisungsregel weg.

- `RawWords[0]` ist `tr`.
- Das vorige Segment mündet per `|` in dieses, und dieses mündet in keines mehr (letzte Stufe).
- Das Segment hat weder Aus- noch Eingabe-Umleitung noch Heredoc.
- Optionen sind nur Bündel aus `c C d s`, und es gibt höchstens zwei Operanden.

`tr` nimmt keine Dateiargumente, liest nur stdin und schreibt nur stdout.

## Entscheidung 3 — Issue-Text aus .monarch/knowledge (#68 Punkt 2, Ergänzung in #69)

**Ursache:** Im Projekt-Root ist `gh issue create --body-file .monarch/knowledge/x.md` heute schon erlaubt. Die Verweigerung aus #69 entsteht nur, wenn der Arbeitsordner nicht die Wurzel des ausgewerteten Projekts ist: In einer Worktree wertet der Hook auch das umgebende Projekt aus (`HookRunner.cs:178-181`), und dort gilt die Datei als weiteres `.monarch` (`ProtectPathsRule.cs:405`).

**Neu:** Bei `gh issue create` entfällt in Regel 2 genau der Wert von `--body-file`, `--body-file=` oder `-F`, wenn alle Bedingungen gelten:

- Die ersten Tokens sind genau `gh issue create`.
- Der Wert ist relativ und beginnt, von `CwdAt(si)` aus gesehen, mit `.monarch/knowledge/`. Das gilt in beiden Auswertungen gleich, im Projekt der Worktree wie im umgebenden Projekt. Der Wert enthält kein `..`.
- Der kanonische Pfad, mit aufgelösten Symlinks, liegt unter demselben `.monarch/knowledge/`.
- Der Pfad trifft keinen Secret-Glob. Sonst greift der Rückfall.

Andere `gh`-Befehle wie `gh issue edit`, `gh pr create` und `gh api -F` bleiben wie heute. `cp` aus `.monarch/knowledge/` bleibt verweigert: `--body-file` macht das Kopieren unnötig.

**Bekannte Lücke, nicht von dieser Änderung:** `gh` steht nicht in `SecretReaders`. `gh issue create --body-file .env` ist heute im Root erlaubt, also auch `.monarch/knowledge/.env`. Das wird ein eigenes Issue. Hier gilt nur: Die neue Ausnahme verbreitert die Lücke nicht, weil sie Secret-Globs ausnimmt.

## Tests (Pflicht je Fehlalarm: weg und Gegentest)

| Issue | Muss erlaubt sein | Muss verweigert bleiben |
|---|---|---|
| #66 | `grep -n -E 'make env\|\.env\|NOTIFY\|SECRET' .github/workflows/ci.yml`, `grep -E 'a\|\.env' ci.yml`, `grep -e '\.env' ci.yml` | `grep foo .env`, `grep -f .env ci.yml`, `grep foo ci.yml .env`, `grep .env* ci.yml`, `grep -A 3 foo .env`, `grep --file=.env ci.yml`, `VAR=x grep foo .env`, `env grep '\.env' .env`, `grep '\.env' $(find . -name '.env*')`, `` grep '\.env' `ls -a` ``, `grep '\.env' .e*`, `grep '\.env' <(cat .env)`, `grep -rn '\.env' .` |
| #75 | `cd .monarch/quests && grep -l 'gate: G-0001' *.md \| tr '\n' ' '` | `tr a b > .monarch/ledger/x`, `tr a b .monarch/ledger/x` ohne Pipe, `cat f \| tr a b > .monarch/quests/n`, in `.monarch/quests`: `cat f \| tr a b \| sh`, `cat f \| tr "$X" b`, `cat f \| tr a b c` |
| #68.2 | aus einer verschachtelten Worktree mit beiden Auswertungen: `gh issue create --title t --body-file .monarch/knowledge/x.md` | aus derselben Worktree: `--body-file .monarch/ledger/x`, `--body-file .monarch/knowledge/../ledger/x`, `--body-file .monarch/knowledge/.env`, ein Symlink in knowledge auf das Ledger, `gh issue edit 1 --body-file .monarch/knowledge/x.md` |

Jeder Allow-Test muss vor dem Fix rot sein. Vorhandene Tests werden nicht geändert. Wechselt einer, ist das ein Stopp und geht an den Owner.

## Bekannte Fehlalarme, die bleiben

Bewusst nicht behoben, weil sie den Beweis schwächen würden: grep mit `-r`, mit Klammern oder `*` im Muster, `rg`, `tr` mitten in einer Pipe. Sie werden nur verweigert, wenn ein Teil davon wie ein Schutzpfad oder eine Credential-Datei aussieht.
