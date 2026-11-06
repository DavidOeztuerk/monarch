# grep-Fehlalarm (#66): für das Folge-Gate

Stand 06.11.2026, G-0107. Owner-Entscheid: #66 wird verschoben, zusammen mit sed (siehe G-0107-sed-folgegate.md). Grund: Die Warden-Runden waren ausgeschöpft, die schmale Prüfung gab kein approve.

## Ursache
`ProtectSecrets` (`ToolCallPolicy.cs:365`) prüft jedes Token, auch das Suchmuster. `BashAnalysis.PathTokens` macht aus `\.env` zusätzlich `.env`, das trifft `**/.env`.

## Entwurf (Stand ADR-0017 Fassung 3, Commit a8a3503)
Nur für `grep`, nicht für `rg`. Die Schleife überspringt nur die Muster-Tokens.
- Rohtext: `HasSubstitution(cmd)` und `HasDefinition(cmd)` falsch (Vorlage `NonCodeAllowed`, `ProtectPathsRule.cs:198`), kein `$`, `{`, `}` in `cmd`.
- Kein Token enthält `*?[]`. Sonst wird `grep .env* ci.yml` zu `.env .env.local ci.yml` und liest die Credential-Datei.
- `RawWords` gleich `Tokens`, keine Zuweisung, `RawWords[0]` genau `grep`.
- Kein `GREP_`-Name in den `AssignmentNames` **aller** Segmente (wie `ProtectPathsRule.cs:246`); `export GREP_OPTIONS=-r; grep '\.env' .` steht im `export`-Segment.
- Jedes Optionstoken im ganzen Segment ist bekannt: Bündel aus `n E F G P i l L v c H h o q s w x I`, dazu `-e WERT`, `--regexp=WERT`, `--regexp WERT`. Alles andere ist Rückfall, auch `-f`, `-A 3`, `--`.
- `-r` und `-R` fehlen bewusst: `grep -rn '\.env' .` liest `./.env`.
- Mit `-e` sind nur deren Werte Muster, sonst der erste Positionsoperand.

## Offen: der Grund für die Verschiebung
Der Lexer verwirft leere Wörter. `grep '' .env` hat die Tokens `grep .env`, und `.env` sähe wie das Muster aus, obwohl die Datei gelesen wird.
- Flag in `RawSegment`/`Segment` bei `AddWord` (`ShellLexer.cs:183-187`), bei `BashAnalysis.cs:80-90` mitführen, über alle Segmente prüfen (Re-Lex von `eval`, `BashAnalysis.cs:94`).
- Das reicht nicht: `grep ''2>&1 .env` und `grep 2''>x .env`. `''` hinterlässt nichts in `Quoted`, `2` gilt als Dateideskriptor und fällt ohne `AddWord` weg (`ShellLexer.cs:141,158`). `Word` müsste sich merken, ob Quotes im Wort standen.
- Vorschlag an den Owner, nicht angenommen: Die Ausnahme greift nicht bei `<`, `>` oder leeren Anführungszeichen im Rohtext. Folge: `grep '\.env' ci.yml > out` bliebe ein Fehlalarm.
- Ungeklärt: Ob die Form `''2>&1` nach ADR-0011 als Verschleierung gilt. Dann wäre sie kein Blocker. Der Warden hat ADR-0011 dazu nicht gelesen.

## Tests
Erlaubt: `grep -n -E 'make env|\.env|NOTIFY|SECRET' .github/workflows/ci.yml`, `grep -E 'a|\.env' ci.yml`, `grep -e '\.env' ci.yml`.
Verweigert: `grep foo .env`, `grep -f .env ci.yml`, `grep foo ci.yml .env`, `grep .env* ci.yml`, `grep -A 3 foo .env`, `grep --file=.env ci.yml`, `VAR=x grep foo .env`, `env grep '\.env' .env`, `grep '\.env' $(find . -name '.env*')`, `` grep '\.env' `ls -a` ``, `grep '\.env' .e*`, `grep '\.env' <(cat .env)`, `grep -rn '\.env' .`, `grep '' .env`, `grep "" .env`, `grep -n '' .env`, `grep -e '' .env`, `grep ''2>&1 .env`, `grep 2''>x .env`, `export GREP_OPTIONS=-r; grep '\.env' .`.
