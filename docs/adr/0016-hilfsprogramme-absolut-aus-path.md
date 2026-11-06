# ADR-0016 — Hilfsprogramme nur absolut aus PATH (#60)

**Datum:** 06.11.2026 · **Status:** vorgeschlagen, wartet auf das Warden-Verdict vor dem ersten Code-Commit · **Quest:** Q-0145 (G-0107)

## Kurz in einfacher Sprache

- Monarch startet `git` und `gh`. Heute kann dabei eine Datei namens `git` aus dem Repo laufen, wenn sie im Arbeitsordner liegt. Wer so eine Datei in ein Repo bringt, führt Code mit den Rechten der Sitzung aus und kann die Schutzregeln aushebeln.
- Künftig sucht Monarch beide Programme selbst, und zwar nur in den absoluten Einträgen von `PATH`. Arbeitsordner und Programmordner zählen nicht mehr.
- Findet Monarch das Programm nicht, verhält es sich so, als wäre es nicht installiert. Es probiert nichts anderes aus.

## Kontext

`.NET` löst bei `Process.Start` einen nackten Dateinamen zuerst über den Ordner der ausführbaren Datei und das aktuelle Arbeitsverzeichnis auf, erst danach über `PATH`. Mit einem Stub `./git` gemessen (#60).

| Stelle | Heute | Hook-Pfad |
|---|---|---|
| `src/Monarch.Cli/Git.cs:13` | `"git"` | nein laut Kommentar, wird geprüft |
| `src/Monarch.Cli/MergeMethodResolver.cs:32,64` | `MONARCH_GIT_BIN` absolut, sonst `"git"` | nein |
| `src/Monarch.Infrastructure.Ledger/GitAnchor.cs:45` | `"git"` | ja, das Ledger läuft in jedem Hook |
| `src/Monarch.Application/Net/GitHubTokenProvider.cs:55` | `"gh"` | nein |
| `src/Monarch.Cli/WakeCommands.cs:42,63` | `/usr/bin/id`, absolut | nicht betroffen |
| `src/Monarch.Cli/SecretScanHook.cs` | `/bin/test`, absolut | nicht betroffen |
| `src/Monarch.Cli/Statusline.cs` | `/bin/sh -c <Kommando des Nutzers>`, absolut | nicht betroffen; das Kommando stammt aus der Konfiguration des Nutzers |

## Entscheidung

1. **Ein Helfer:** `Monarch.Application.ToolLocator.Find(string name)` gibt den absoluten Pfad zurück oder `null`. Application ist das tiefste Projekt, das Cli, Ledger und `GitHubTokenProvider` gemeinsam sehen.
2. **Suche:** `PATH` am `:` zerlegen. Leere und nicht absolute Einträge werden übersprungen (`.`, `bin`, ein führender oder doppelter `:`). Ein Kandidat zählt nur, wenn er eine reguläre Datei ist (Symlinks werden gefolgt) und ein Ausführ-Bit trägt. Der erste Treffer gewinnt. Ohne `PATH` ist das Ergebnis `null`.
3. **Kein Rückfall:** An keiner Stelle bleibt ein nackter Name in `ProcessStartInfo`. `null` führt in den vorhandenen Fehlerpfad derselben Stelle, also in denselben Zustand wie heute, wenn `git` fehlt oder scheitert: `Git.Run` gibt `null` zurück, `GitAnchor.Execute` `false`, `RunGhAuthToken` `null`, `MergeMethodResolver` die vorhandene Warnung „Historie nicht lesbar“. So entsteht kein neuer Zustand im Ledger oder im Hook.
4. **Klarer Fehler:** `Git.Run` und `RunGhAuthToken` schreiben im Fehlerfall einmal je Prozess eine Zeile nach stderr: `monarch: <name> nicht in PATH gefunden (nur absolute Einträge zählen)`. Stdout bleibt unberührt, das Hook-Protokoll ändert sich nicht.
5. **`MONARCH_GIT_BIN`** bleibt die Test-Naht und gilt wie bisher nur absolut. Relativ wird der Wert ignoriert, dann gilt die Suche aus Punkt 2.

## Tests

- Stub `git` und `gh` im Arbeitsordner: Der Stub läuft nicht. Der Stub schreibt eine Marker-Datei; geprüft wird, dass sie fehlt.
- Dasselbe mit `PATH`, das `.`, einen relativen Eintrag und einen leeren Eintrag enthält.
- Dasselbe für den Ordner des Binaries und für den Hook-Pfad (`hook pre-tool-use` mit `GitAnchor`).
- `Find`: nicht ausführbare Datei und Ordner mit dem Namen werden übersprungen, der nächste absolute Eintrag gewinnt; ohne Treffer und ohne `PATH` kommt `null`.
- Eine statische Prüfung im Test: In `src/` gibt es kein `ProcessStartInfo("git")` und kein `ProcessStartInfo("gh")`.

## Grenzen

- Ein **absoluter** `PATH`-Eintrag ist die Entscheidung des Nutzers, auch wenn er ins Repo zeigt. Ein Repo kann `PATH` nicht von selbst setzen; eine Sandbox dagegen ist G-0102.
- Die Suche ändert nichts an Programmen, die `git` selbst startet, etwa Hooks und Filter aus der Repo-Konfiguration. Das ist ein eigenes Thema und nicht Teil von #60.
- Die Kosten sind wenige `stat`-Aufrufe je Prozessstart. Die Latenz im Hook wird in Gate 3c gemessen.
