# ADR-0016 — Hilfsprogramme nur absolut aus PATH (#60)

**Datum:** 06.11.2026 · **Status:** vorgeschlagen, Fassung 2 nach Warden-Design-Runde; wartet auf das Delta-Verdict vor dem ersten Code-Commit · **Quest:** Q-0145 (G-0107)

## Kurz in einfacher Sprache

- Monarch startet `git` und `gh`. Heute kann dabei eine Datei namens `git` aus dem Repo laufen, wenn sie im Arbeitsordner liegt. Wer so eine Datei in ein Repo bringt, führt Code mit den Rechten der Sitzung aus und kann die Schutzregeln aushebeln.
- Künftig sucht Monarch beide Programme selbst, und zwar nur in den absoluten Einträgen von `PATH`. Arbeitsordner und Programmordner zählen nicht mehr.
- Findet Monarch das Programm nicht, verhält es sich so, als wäre es nicht installiert. Es probiert nichts anderes aus.

## Kontext

`.NET` löst bei `Process.Start` einen nackten Dateinamen zuerst über den Ordner der ausführbaren Datei und das aktuelle Arbeitsverzeichnis auf, erst danach über `PATH`. Mit einem Stub `./git` gemessen (#60).

| Stelle | Heute | Hook-Pfad |
|---|---|---|
| `src/Monarch.Cli/Git.cs:13` | `"git"` | nein |
| `src/Monarch.Cli/MergeMethodResolver.cs:32,36,64` | `MONARCH_GIT_BIN` absolut, sonst `"git"`; dazu der Default `string git = "git"` in `Derive`, den `AwakenCommand.cs:45` nutzt | nein |
| `src/Monarch.Infrastructure.Ledger/GitAnchor.cs:45` | `"git"` | ja, aber nur wenn die Ledger-Datei existiert und keinen Anker hat (`JsonlLedgerStore.cs:118-120`); `pre-tool-use` schreibt nur bei deny, ask oder Signal (`HookRunner.cs:199-205`) |
| `src/Monarch.Application/Net/GitHubTokenProvider.cs:55` | `"gh"` | nein |
| `src/Monarch.Cli/WakeCommands.cs:42,47,63` | `/usr/bin/id`, absolut; `launchctl`, `systemd-run`, `systemctl` und `claude` über `WakeProcess.Which`, das nur absolute `PATH`-Einträge nimmt | nicht betroffen |
| `src/Monarch.Cli/SecretScanHook.cs` | `/bin/test`, absolut | nicht betroffen |
| `src/Monarch.Cli/Statusline.cs` | `/bin/sh -c <Kommando des Nutzers>`, absolut | nicht betroffen; das Kommando stammt aus der Konfiguration des Nutzers |

## Entscheidung

1. **Ein Helfer:** `Monarch.Application.ToolLocator.Find(string name)` gibt den absoluten Pfad zurück oder `null`. Application ist das tiefste Projekt, das Cli, Ledger und `GitHubTokenProvider` gemeinsam sehen.
2. **Suche:** `PATH` am `:` zerlegen. Leere und nicht absolute Einträge werden übersprungen (`.`, `bin`, ein führender oder doppelter `:`). Ein Kandidat zählt nur, wenn er eine reguläre Datei ist (Symlinks werden gefolgt) und ein Ausführ-Bit trägt. Der erste Treffer gewinnt. Ohne `PATH` ist das Ergebnis `null`.
3. **Kein Rückfall:** An keiner Stelle bleibt ein nackter Name als Programm. Der Default `string git = "git"` in `MergeMethodResolver.Derive` entfällt, die Auflösung sitzt in `Derive`, damit auch `monarch awaken` sie nutzt. `null` führt in den vorhandenen Fehlerpfad derselben Stelle, also in denselben Zustand wie heute, wenn `git` fehlt oder scheitert: `Git.Run` gibt `null` zurück, `GitAnchor.Execute` `false`, `RunGhAuthToken` `null`, `MergeMethodResolver` die vorhandene Warnung „Historie nicht lesbar“. So entsteht kein neuer Zustand im Ledger oder im Hook.
4. **Klarer Fehler:** `ToolLocator` selbst schreibt im Fehlerfall einmal je Prozess und Programm eine Zeile nach stderr: `monarch: <name> nicht in PATH gefunden (nur absolute Einträge zählen)`. So bleibt keine Stelle stumm, auch `GitAnchor` nicht. Stdout bleibt unberührt, das Hook-Protokoll ändert sich nicht.
5. **Folge ohne `git`, wie heute:** Ohne `git` überspringt `LedgerVerifier.cs:145,149` die Prüfung gegen die zweite Quelle, und `JsonlLedgerStore.cs:120` hängt an eine schon committete Datei an. Das ist kein neuer Zustand, wird aber häufiger sichtbar, wenn jemand `git` nur über einen relativen `PATH`-Eintrag hatte. Die stderr-Zeile aus Punkt 4 macht es erkennbar.
6. **`MONARCH_GIT_BIN`** bleibt die Test-Naht und gilt wie bisher nur absolut. Relativ wird der Wert ignoriert, dann gilt die Suche aus Punkt 2.
7. **`WakeProcess.Which`** bleibt unverändert: Es nimmt schon nur absolute Einträge. Es prüft kein Ausführ-Bit, das ist hier kein Sicherheitsthema.

## Tests

- Stub `git` und `gh` im Arbeitsordner: Der Stub läuft nicht. Der Stub schreibt eine Marker-Datei; geprüft wird, dass sie fehlt.
- Dasselbe mit `PATH`, das `.`, einen relativen Eintrag und einen leeren Eintrag enthält.
- Dasselbe für den Ordner des Binaries und für `monarch awaken` (`MergeMethodResolver.Derive`).
- Hook-Pfad: `hook pre-tool-use` mit einem Aufruf, der verweigert wird, und einer vorhandenen Ledger-Datei ohne Anker. Nur dann läuft `GitAnchor`. Dieser Test muss vor dem Fix rot sein; der Rot-Beleg gehört in den Bericht.
- `Find`: nicht ausführbare Datei und Ordner mit dem Namen werden übersprungen, der nächste absolute Eintrag gewinnt; ohne Treffer und ohne `PATH` kommt `null`, und die stderr-Zeile erscheint einmal.
- Eine statische Prüfung im Test: In `src/` steht `"git"` oder `"gh"` nirgends mehr als Programmname, weder in `ProcessStartInfo` noch als Default oder als Variable, die dorthin führt. Die Namen dürfen nur als Argument von `ToolLocator.Find` vorkommen.
- Der Kommentar in `GitAnchor.cs:9` wird zur Bedingung aus der Tabelle passend korrigiert.

## Grenzen

- Ein **absoluter** `PATH`-Eintrag ist die Entscheidung des Nutzers, auch wenn er ins Repo zeigt. Ein Repo kann `PATH` nicht von selbst setzen; eine Sandbox dagegen ist G-0102.
- Die Suche ändert nichts an Programmen, die `git` selbst startet, etwa Hooks und Filter aus der Repo-Konfiguration. Das ist ein eigenes Thema und nicht Teil von #60.
- Die Kosten sind wenige `stat`-Aufrufe je Prozessstart. Die Latenz im Hook wird in Gate 3c gemessen.
