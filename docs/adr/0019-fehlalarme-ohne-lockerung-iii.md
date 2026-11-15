# ADR-0019 — Fehlalarme ohne Lockerung III: `git -C` mit festem Ordner außerhalb des Projekts

**Datum:** 15.11.2026 · **Status:** Entwurf (Warden-Designrunde offen) · **Gate:** G-0115 · **Quests:** Q-0160 (Design), Q-0161 (Umsetzung) · **Issue:** #105

## Kurz in einfacher Sprache

- Der Hook prüft relative Pfade in einem Bash-Befehl gegen jedes Verzeichnis, in dem der Befehl laufen könnte, darunter immer das Session-Verzeichnis (das Projekt). So wird `git -C /scratch/repo rm --cached .monarch/ledger/x.jsonl` verweigert, obwohl git den Pfad gegen `/scratch/repo` auflöst und das Projekt gar nicht berührt.
- Ebenso fragt der Hook bei `git -C /scratch/repo checkout -f` nach (Regel 5), obwohl der Arbeitsbaum außerhalb des Projekts liegt. In einem Hintergrund-Agenten hat diese Rückfrage am 15.11.2026 eine Warden-Runde rund 5 Stunden blockiert (#105).
- Neu: Steht in einem `git`-Segment genau **ein** `-C` mit einem **literalen absoluten** Ordner, der nach dem Auflösen von Symlinks **außerhalb jeder Schutzzone** liegt, und gibt es keine weiteren Angaben, die den Arbeitsbaum oder das Repository umbiegen, dann löst der Hook die relativen Operanden dieses Segments nur gegen diesen Ordner auf. Regel 5 greift dort nicht.
- Alles, was nicht nachweislich außerhalb liegt, bleibt wie heute deny bzw. ask.

## Kontext

- `Canon.AbsAll` löst ein Token gegen jedes Basisverzeichnis auf, „konservativ: jedes zählt“ (`ToolCallPolicy.cs:255-266`). Das cwd-Modell vereinigt alle möglichen Verzeichnisse, wo es unsicher ist (`ToolCallPolicy.cs:108-110`).
- Für ein `git`-Segment mit `-C <dir>` ist das Modell nicht unsicher: git wechselt vor allem anderen in `<dir>` und löst Pfad-Operanden dort auf. Die Optionen `-C`, `--git-dir`, `--work-tree` werden heute schon erkannt (`BashAnalysis.cs:250-259`), aber nur als Pfad-Kandidaten, nicht als Arbeitsverzeichnis.
- Regel 5 (`ProtectPathsRule.cs:523-549`, `WorktreeAsk`) prüft pfadloses `checkout -f`, `switch -f`, `reset --keep/--merge`, `stash -u` ohne Blick auf `-C`.
- Belege (#105): `PolicyDecided` deny für `git hash-object` mit `.monarch/ledger/2026-10/s7.deadbeef.jsonl` (15.11. 00:34:30) und ask für `git -C <scratchpad>/warden-g0114/e1 checkout -q -f develop` (15.11. 03:57:01).

## Entscheidung

1. **Fester Ordner.** Ein `git`-Segment hat einen festen Ordner, wenn alle Bedingungen gelten:
   - genau ein `-C`, mit einem Wert, der mit `/` beginnt und weder `$`, Backtick, `~`, Glob- noch Brace-Zeichen enthält;
   - kein `--git-dir`, `--work-tree` (auch nicht in der Form `--x=`), kein `-c` mit `core.worktree`, keine Zuweisung oder `env`-Angabe `GIT_DIR`, `GIT_WORK_TREE`, `GIT_INDEX_FILE`, `GIT_COMMON_DIR` im Segment;
   - das Kommando enthält keine Funktions- oder `alias`-Definition (wie ADR-0014, Entscheidung 2, Bedingung 4);
   - der Ordner liegt nach lexikalischer Normalisierung **und** nach dem Auflösen von Symlinks (`Canon.Canonical`) außerhalb jeder Schutzzone und ist kein Vorfahr einer Schutzzone (Projekt-Root, `~` mit den globalen Schutzpfaden, `$MONARCH_HOME`).
2. **Operanden.** In einem Segment mit festem Ordner werden relative Operanden nur gegen diesen Ordner aufgelöst, nicht gegen die übrigen Basisverzeichnisse. Absolute Operanden und solche, die über `..` zurück in eine Zone führen, treffen wie bisher (Regel 2 bleibt: fail-closed bei Nennung).
3. **Regel 5.** Ein Segment mit festem Ordner fragt nicht nach, denn sein Arbeitsbaum liegt außerhalb jeder Zone.
4. **Sonst unverändert.** Fehlt eine Bedingung, gilt das heutige Verhalten. Andere Segmente desselben Befehls behalten ihre Basisverzeichnisse (`git -C /x rm a && rm .monarch/ledger/x` bleibt deny).

## Warum keine Schutzregel fällt (Maßstab ADR-0011, ADR-0014)

- **Klasse 1 (Literal):** Es ändert sich, **was als Nennung gilt**, nicht wie auf eine Nennung reagiert wird (wie ADR-0014). Ein relativer Operand eines Segments mit festem Ordner bezeichnet für git einen Pfad unter diesem Ordner; liegt der außerhalb jeder Zone, nennt er keinen Schutzpfad.
- **Symlinks und `..`:** Der Ordner wird lexikalisch und physisch geprüft; ein Ordner, der über einen Symlink ins Projekt führt, ist kein fester Ordner (Gegenprobe). Operanden mit `..` werden wie bisher normalisiert.
- **Umbiegen des Repositories:** `--git-dir`, `--work-tree`, `core.worktree` und `GIT_*`-Angaben schließen die Ausnahme aus; dann bleibt ask/deny.
- **G1–G3:** G1 bleibt (Fehlgriffe im Projekt unverändert gesperrt), G2 (Konfigurationsdateien) und G3 (Ledger des Projekts) sind nicht berührt, weil nur Ordner außerhalb jeder Zone in Frage kommen.
- **Restrisiko (Klasse 2, bekannt):** Ein Symlink, der **nach** der Prüfung in den festen Ordner gelegt wird (TOCTOU), oder git-Hooks und Konfiguration des fremden Repositories, die ins Projekt schreiben. Das gilt heute schon für jeden Befehl in einem fremden Ordner (ADR-0011, „Code, den der Agent ausführt“).

## Grenzen

- Nur `git -C` mit literalem absolutem Ordner. `cd /x && git …`, relative `-C`-Werte, mehrere `-C` und andere Programme (`dotnet`, `mkdir` mit Pfaden außerhalb, Fälle aus #105) bleiben unverändert und sind Folge-Kandidaten.
- `ask` in Hintergrund-Agenten allgemein (Pfad im Projekt) ist nicht Gegenstand; der Auftrag des Owners betrifft Pfade außerhalb des Projekts, und die fallen mit dieser Entscheidung für `git -C` weg.

## Tests (Q-0160 rot, Q-0161 grün)

- `tests/Monarch.Policy.Tests/GitDirOutsideProjectTests.cs`: rot vor der Umsetzung `git -C <außen> checkout -q -f develop` (auch mit `..` im Ordner), `git -C <außen> hash-object -w .monarch/ledger/…`, `git -C <außen> rm -q --cached -- .monarch/ledger/…`.
- Gegenproben (bleiben ask bzw. deny): `-C` auf Projekt-Root, Unterordner, über `..` zurück ins Projekt, mit `$D`, `~`, Glob, relativ; mit `--work-tree`, `--git-dir`, `GIT_WORK_TREE=`; zwei `-C`; mit Funktionsdefinition; absoluter Operand ins Projekt; `..`-Operand zurück ins Projekt; zweites Segment ohne `-C`; `-C` auf das Home-Verzeichnis mit `.monarch/bin/monarch`; `-C` über einen Symlink ins Projekt.
- Bestehende Tests unverändert; Hook-Latenz-Gates unverändert grün.

## Folgen

- `plugin/README.md`, Abschnitt „Grenzen“: Ausnahme für `git -C` mit festem Ordner außerhalb (Q-0161).
- #105 bleibt offen für `cd`, `dotnet`, `mkdir` und relative `-C`.
