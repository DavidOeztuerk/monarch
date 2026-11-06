# ADR-0017 — Fehlalarme ohne Lockerung II: grep, sed, tr, Issue-Texte (#66, #67, #68, #69, #75)

**Datum:** 06.11.2026 · **Status:** vorgeschlagen, wartet auf das Warden-Verdict vor dem ersten Code-Commit · **Quest:** Q-0147 (G-0107) · **Baut auf:** ADR-0011, ADR-0014

## Kurz in einfacher Sprache

- Monarch verweigert harmlose Befehle, weil es Text für einen Pfad hält: das Suchwort `\.env` bei `grep`, Ersetzungen in `sed`, das Argument von `tr`.
- Außerdem gibt es während einer Quest keinen Weg, einen Issue-Text aus `.monarch/knowledge/` zu übergeben.
- Künftig erkennt Monarch in vier eng beschriebenen Fällen sicher, dass kein Pfad gemeint ist. In jedem anderen Fall bleibt alles genau wie heute.
- Kein Schutz fällt: Wer eine Credential-Datei liest oder in einen Schutzpfad schreibt, wird weiter verweigert. Dazu gehört je Fehlalarm ein Gegentest.
- Der Fall `gh issue create --body-file` ist die einzige neue Erlaubnis, die eine `.monarch`-Datei berührt. Sie liest nur aus `.monarch/knowledge/`, das Agenten ohnehin schreiben und lesen dürfen.

## Grundsatz

Jede Ausnahme ist ein Beweis, keine Vermutung. Sie greift nur, wenn **alle** Bedingungen gelten. Sonst läuft der heutige Code unverändert (Rückfall). Gemeinsame Bedingungen für alle vier Ausnahmen (ADR-0014, Entscheid des Owners vom 30.10.):

- **Literal:** Kein betrachtetes Argument enthält `$` oder einen Backtick.
- **Direkt:** `seg.RawWords` ist gleich `seg.Tokens` und `seg.Assignments` ist leer. Damit fallen Wrapper (`env`, `sudo`, `xargs`, `./grep`) und `VAR=x grep` aus der Ausnahme.
- `RawWords[0]` ist genau das Kommandowort, ohne Pfad.

Eine Ausnahme nimmt nur die bewiesenen Argumente aus der Prüfung. Alle übrigen Tokens, Umleitungen und Segmente werden weiter geprüft. `IsMutating` ändert sich nicht, also auch nicht Rank S und der Bypass-Modus.

## Entscheidung 1 — sed: Skript parsen statt Zeichen suchen (#67, #68 Punkt 1, #69)

**Ursache:** `DropSedScript` (`ProtectPathsRule.cs:810`) und `ScriptWriterHit` (`:620`) werten ein Skript als möglichen Schreiber, sobald irgendwo die Zeichen `w`, `W` oder `e` stehen. Fast jeder Text enthält sie.

**Neu:** `SedScript.ProvablyNoWriter(string)` gibt nur dann `true` zurück, wenn das ganze Skript in diese enge Grammatik passt. Beide Stellen fragen zusätzlich zur Zeichensuche diese Funktion. Alles außerhalb der Grammatik ergibt `false` und damit das Verhalten von heute.

```
skript  := befehl (trenner befehl)*
trenner := leer* (';' | zeilenumbruch) leer*
befehl  := adresse? ( 's' ersetzung | einfach )
adresse := zahl (',' zahl)? | '/' regex '/'
einfach := 'd' | 'p' | 'q' | '='
ersetzung := D regex D text D flags
flags   := ( 'g' | 'p' | 'i' | 'I' | 'm' | 'M' | ziffer )*
```

- `D` ist ein ASCII-Satzzeichen, nicht `\`, kein Leerraum, kein Buchstabe, keine Ziffer. Damit fällt `swxwyw` in den Rückfall.
- Nach `\` wird das nächste Zeichen übersprungen. Ein unmaskierter Zeilenumbruch in Regex oder Text führt in den Rückfall.
- **Klammerausdrücke:** Ob ein Trennzeichen innerhalb von `[...]` die Regex beendet (`s/[/]/x/`), kann je nach sed-Variante verschieden sein; gemessen ist das hier nicht. Die Grammatik hängt nicht davon ab: Sie lehnt jede Regex ab, in der ein Klammerausdruck das Trennzeichen enthält, nicht geschlossen ist, mit `[]` oder `[^]` beginnt oder ein weiteres `[` enthält. In allen übrigen Fällen führen beide Lesarten zum selben Ende der Regex.
- Die Befehle `w`, `W`, `e`, `r`, `R`, `a`, `i`, `c`, `b`, `t`, `T`, `:`, `{`, `}`, `y`, `#`, `!` stehen bewusst nicht in der Grammatik. Sie sind Schreiber, oder GNU und BSD trennen ihren Text unterschiedlich.
- Die Flags `w` und `e` stehen nicht in `flags`, also führt `s#a#b#w .monarch/ledger/x` in den Rückfall und bleibt deny.
- Optionen: `DropSedScript` bleibt unverändert, ein unbekanntes Optionstoken (`-f`, Bündel wie `-nE`) lässt alle Tokens stehen.
- `IsPlainSedSubstitute` (Q-0138) bleibt unverändert.

## Entscheidung 2 — grep: Das Suchmuster ist kein Dateioperand (#66)

**Ursache:** `ProtectSecrets` (`ToolCallPolicy.cs:365`) prüft jedes Token, auch das Muster.

**Neu:** `GrepPattern.Indices(seg)` liefert die Token-Indizes der Muster, sonst nichts. Nur diese überspringt die Schleife, und nur für `grep`, nicht für `rg`.

- Jedes Optionstoken im **ganzen** Segment muss bekannt sein. Grep erlaubt Optionen auch nach den Operanden, darum zählt das ganze Segment. Erlaubt sind Bündel aus `n E F G P i l L v c H h o q r R s w x I` sowie `-e WERT`, `--regexp=WERT` und `--regexp WERT`. Alles andere, auch `-f`, `--file`, `-A 3`, `--exclude-from` und `--`, führt in den Rückfall.
- Mit `-e` oder `--regexp` sind nur deren Werte Muster, jeder Positionsoperand bleibt Datei. Ohne sie ist der erste Positionsoperand das Muster.
- Das Muster enthält keines der Zeichen `$`, Backtick, `*`, `?`, `[`, `]`, `{`, `}`. Der Lexer entfernt Quotes, ein Glob im Muster ist also nicht von einem ungequoteten zu unterscheiden: `grep .env* ci.yml` würde zu `.env .env.local ci.yml` und läse eine Credential-Datei. Darum bleibt das deny.

## Entscheidung 3 — tr als Pipe-Stufe (#75)

**Neu:** Regel 2 behandelt ein Segment als lesend ohne Pfad-Kandidaten, wenn alle Bedingungen gelten:

- `RawWords[0]` ist `tr`.
- Das vorige Segment mündet per `|` in dieses (`PipesToNext`).
- Das Segment hat weder Aus- noch Eingabe-Umleitung noch Heredoc.
- Optionen sind nur Bündel aus `c C d s`, und es gibt höchstens zwei Operanden.

`tr` nimmt keine Dateiargumente, liest nur stdin und schreibt nur stdout. `ReadOnlyWords` bleibt unverändert, weil die Liste an vielen Stellen wirkt, etwa bei `find -exec` und in `IsMutating`.

## Entscheidung 4 — Issue-Text aus .monarch/knowledge (#68 Punkt 2, #69)

**Neu:** Bei `gh issue create` entfällt in `DropDataValues` genau der Wert von `--body-file`, `--body-file=` oder `-F`, wenn alle Bedingungen gelten:

- Die ersten Tokens sind genau `gh issue create`.
- Der Wert ist relativ, liegt nach Normalisierung unter `.monarch/knowledge/` im Projekt, enthält kein `..`, keine Glob-Zeichen und kein `$`.
- Der kanonische Pfad, mit aufgelösten Symlinks, liegt ebenfalls unter `.monarch/knowledge/`.
- Der Pfad trifft keinen Secret-Glob. So bleibt `.monarch/knowledge/.env` verweigert, und `gh` wird nicht zum Lesekanal für Credentials.

Andere `gh`-Befehle wie `gh issue edit`, `gh pr create` und `gh api -F` bleiben wie heute. `cp` aus `.monarch/knowledge/` bleibt verweigert (#69): `--body-file` macht das Kopieren unnötig.

## Tests (Pflicht je Fehlalarm: weg und Gegentest)

| Issue | Muss erlaubt sein (wörtlich aus dem Issue) | Muss verweigert bleiben |
|---|---|---|
| #66 | `grep -n -E 'make env\|\.env\|NOTIFY\|SECRET' .github/workflows/ci.yml`, `grep -E 'a\|\.env' ci.yml`, `grep -e '\.env' ci.yml` | `grep foo .env`, `grep -f .env ci.yml`, `grep foo ci.yml .env`, `grep .env* ci.yml`, `grep -A 3 foo .env`, `grep --file=.env ci.yml`, `VAR=x grep foo .env` |
| #67 | der `s#…#…#`-Filter aus dem Issue | `sed 's#a#b#w .monarch/ledger/x'`, `sed 's/[/]/x/w .monarch/ledger/x'` |
| #68.1 | `sed -i '' -e '62s/.*/x/' -e '65s/.*/y/' src/a.cs` | `sed -i '' -e 's/a/b/' .monarch/ledger/x`, `sed -e '1w .monarch/ledger/x' f` |
| #69 | beide Kommandos aus dem Issue | `sed 's/a/b/;w .monarch/ledger/x' f`, `sed "s/a/$X/w .monarch/ledger/x" f` |
| #75 | die Pipeline aus dem Issue | `tr a b > .monarch/ledger/x`, `tr a b .monarch/ledger/x` ohne Pipe, `cat f \| tr a b > .monarch/quests/n` |
| #68.2 | `gh issue create --title t --body-file .monarch/knowledge/x.md` | `gh issue create --body-file .monarch/ledger/x`, `--body-file .monarch/knowledge/../ledger/x`, `--body-file .monarch/knowledge/.env`, ein Symlink in knowledge auf das Ledger, `gh issue edit 1 --body-file .monarch/ledger/x` |

Dazu ein Korpus-Test: Eine Liste heutiger deny-Kommandos aus `Adr0011Tests`, `Adr0014Tests` und `BashFailClosedTests` bleibt deny; die vorhandenen Tests werden nicht geändert.

## Bekannte Fehlalarme, die bleiben

Bewusst nicht behoben, weil sie den Beweis schwächen würden: sed-Skripte mit `$` oder mit `w`/`e` außerhalb der Grammatik, sed-Optionsbündel wie `-nE`, Muster mit Klammern oder `*` bei grep, `rg`. Sie werden nur verweigert, wenn ein Teil davon wie ein Schutzpfad aussieht. Trifft das im Alltag, kommt ein neues Issue.
