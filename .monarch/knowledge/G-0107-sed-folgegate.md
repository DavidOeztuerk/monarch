# sed-Fehlalarme (#67, #68 Punkt 1, #69): für das Folge-Gate

Stand 06.11.2026, G-0107. Owner-Entscheid: sed verschoben. Der Entscheid vom 30.10. (STRENG, Q-0138) gilt weiter, bis der Owner ihn selbst aufhebt. Ohne diese Aufhebung ist kein Fix möglich: ADR-0014 führt `sed -n 's/.*/new/p' f` und `sed 's/.*/new/' f | sort` als „bleibt deny, Owner-Entscheid“, und `Adr0014Tests.cs` (`Issue55_sed_exception_does_not_reach_further`) schreibt das fest.

## Ursache
`DropSedScript` und `ScriptWriterHit` in `ProtectPathsRule.cs` werten ein Skript als möglichen Schreiber, sobald irgendwo `w`, `W` oder `e` steht.

## Entwurf
`SedScript.ProvablyNoWriter(string)`: wahr nur, wenn das ganze Skript in diese Grammatik passt, sonst Verhalten von heute.

    skript    := befehl (trenner befehl)*
    trenner   := leer* (';' | zeilenumbruch) leer*
    befehl    := adresse? ( 's' ersetzung | einfach )
    adresse   := zahl (',' zahl)? | '/' regex '/'
    einfach   := 'd' | 'p' | 'q' | '='
    ersetzung := D regex D text D flags
    flags     := ( 'g' | 'p' | 'i' | 'I' | 'm' | 'M' | ziffer )*

D ist ein ASCII-Satzzeichen, nicht `\`, kein Leerraum, kein Buchstabe, keine Ziffer. Nach `\` wird ein Zeichen übersprungen. Unmaskierter Zeilenumbruch: Rückfall. Klammerausdrücke: Rückfall, wenn sie das Trennzeichen enthalten, offen bleiben, mit `[]` oder `[^]` beginnen oder ein weiteres `[` enthalten. Die Klammerregel ist nicht gemessen. Gegen die Grammatik selbst hatte der Warden keinen Einwand.

## Warden-Befunde aus der Design-Runde, die das Folge-Gate lösen muss
- Ausgabe als Code: `sed 's#x#rm .monarch/ledger/y#' f | sh` ist heute deny (`ProtectPathsRule.cs:640-642`, `Adr0014Tests.cs:261,266`). Ebenso `| xargs rm` (eigener Fund, nicht vom Warden). Folge-Stufen nur per Positivliste: lesende Wörter oder eine Shell mit Skriptoperand (`bash scripts/x.sh` aus #69). `ReadsCodeFromStdin` allein reicht nicht.
- Optionen: `sed -e's/x/y/w .monarch/ledger/x' 's/.*/new/' f` ist heute deny und wäre frei. `ScriptWriterHit`, `DropSedScript` und `DropDataValues` sehen nur `Effective()`, ohne `seg`. Prüfung am Aufrufer auf `seg` (wie ADR-0014), strenge Optionsliste auch für `ScriptWriterHit`. Sonst werden `env sed …` und `xargs sed …` frei (`Adr0014Tests.cs:275,277`).
- Rohtext prüfen wie `ProtectPathsRule.cs:245-247` (`HasSubstitution`, `HasDefinition`); der Lexer nimmt `$(…)` und Backticks aus den Wörtern.
- Test für #68 Punkt 1 mit Ersatztext wie `new`: `-e '62s/.*/x/'` enthält kein `w`/`e` und ist heute schon allow.

## Enger Zuschnitt, damit nur vier Testzeilen wechseln
Rückfall, wenn: Operand trifft eine Zone oder ein weiteres .monarch; Option hinter dem Operanden; mehr als ein Operand; `<` oder `>` im Rohtext; Events nicht leer; das Kommando ist nicht genau eine Pipeline (also `cd x && sed …` bleibt deny); sed steht hinter einer Pipe und hat trotzdem einen Operanden; kein Operand und keine Pipe davor.
Wechseln würden dann nur `sed -i 's/.*/new/' f`, `sed -n 's/.*/new/p' f`, `sed -E 's/.*/new/' f` und `sed 's/.*/new/' f | sort`. Diese Liste ist nicht am Code geprüft.

Kosten als Richtwert: Die Design-Runde über beide ADRs lag bei rund 11 Punkten 5h und 2 pp Woche.
