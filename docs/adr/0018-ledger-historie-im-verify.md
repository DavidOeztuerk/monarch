# ADR-0018 — Ledger-Historie im verify: Fälschung per normalem Commit (#71)

**Datum:** 13.11.2026 · **Status:** Fassung 2 nach Warden-Designrunde (REQUEST_CHANGES, major 1 und 2 eingearbeitet), Delta-Runde offen · **Quest:** Q-0153 (Design), Q-0154 (Umsetzung), G-0111 · **Baut auf:** ADR-0010, ADR-0011 (G3)

## Kurz in einfacher Sprache

- Heute vergleicht `ledger verify` eine getrackte Ledger-Datei nur mit ihrer Fassung im letzten Commit (`HEAD`). Wer eine fremde Ledger-Datei fälscht und das **normal committet**, macht die Fälschung selbst zum Maßstab. Zwei Fälle aus #71 bleiben so unerkannt: die letzte Zeile gelöscht (K-delete-last) und eine kettenkorrekte Zeile angehängt (K-append-chained).
- Neu merkt sich eine frische Worktree beim ersten Schreiben, auf welchem Commit sie angefangen hat (**Basis**). `verify` schaut sich alle Commits an, die danach dazugekommen und noch nicht auf dem Remote-Stand von `develop` oder `main` angekommen sind. In diesen Commits darf sich eine Ledger-Datei nur ändern, wenn **diese Worktree** sie geschrieben hat. Ein Merge darf eine fremde Datei nur übernehmen, nie kürzen. Alles andere ist ein Befund.
- Damit sind beide Fälle aus #71 erkannt, auch nach einem `git push` auf den Gate-Branch.
- Die Basis wird nie neu angelegt. Fehlt sie, prüft `verify` strenger, nicht lockerer.
- Was bleibt: Eine Worktree oder ein Klon, die erst **nach** der Fälschung anfangen, sehen die Fälschung als Vorgeschichte (Klon ohne Anker). Wer die Git-Historie umschreibt oder die Remote-Stände lokal versetzt, ist ebenfalls nicht erkennbar (History-Rewrite).
- Keine Schutzregel wird gelockert. `verify` wird nur strenger. Git läuft im Schreibpfad höchstens einmal je Worktree.

## Was verify heute prüft

1. **Hash-Kette je Datei:** Hash, `prevHash`, `seq`. Ändern oder Löschen in der Mitte bricht die Kette (`HashMismatch`, `SeqGap`, `PrevHashMismatch`).
2. **Lokaler Anker** (`.monarch/local/ledger-heads.json`): Für jede Datei, die diese Worktree geschrieben hat, stehen `seq` und Hash des letzten Eintrags darin. Fehlende, gekürzte oder verlängerte Dateien fallen auf (`Truncated`, `HeadMismatch`).
3. **Git gegen `HEAD`** (ADR-0010, Punkt 3): Eine getrackte Datei muss existieren und ihre `HEAD`-Fassung byte-genau als Anfang enthalten. Eine Datei ohne lokalen Anker gilt als „von Git verankert“, wenn sie getrackt und gegenüber `HEAD` unverändert ist.

## Die Lücke

Fremde Ledger-Dateien (aus anderen Worktrees, per Merge gekommen) haben keinen lokalen Anker. Ihr einziger Maßstab ist `HEAD`. Ein neuer Commit verschiebt `HEAD`. Danach ist die gefälschte Datei „getrackt und unverändert“ und damit verankert. Ältere Commits schaut `verify` nicht an.

Eigene Dateien schützt der Anker, solange zwischen Fälschung und `verify` kein Append in dieselbe Datei fällt (Fälle U-… in #71). Fällt einer dazwischen, verankert er heute ein kettenkorrekt gefälschtes Ende mit; das ist ein eigener, älterer Befund (Issue, nicht dieses Gate).

**Warum „jede Fassung muss die vorige als Anfang enthalten“ allein nicht reicht:** Damit wäre K-delete-last erkannt. K-append-chained ist aber genau ein Anhängen. Kettenkorrekt angehängt sieht eine Fälschung aus wie eine echte Zeile; die Kette ist schlüssellos. Unterscheiden lässt sich nur, **wo** angehängt wurde: Eine fremde Datei wächst nie in dieser Worktree, sondern kommt fertig über `develop` oder `main`.

## Entscheidung

### 1. Basis der Worktree

- Neue Datei `.monarch/local/ledger-base` (ungetrackt, unter dem Schutzpfad `.monarch/local/**`, G2). Inhalt: die volle Commit-ID von `HEAD`.
- **Angelegt wird sie genau einmal: wenn eine Worktree bei null anfängt.** Das heißt: Der Anker hat vor diesem Append keinen Eintrag (weder `files` noch `restore`), es gibt noch keine Basis-Datei, und der Append schreibt kein `LedgerAnchorReset`. Das sind der frische Klon und die neue Worktree.
- Nur im Schreibpfad (`AppendCore`), nie im Lesepfad (`ReadSession` legt heute auch die `writer-id` an; das ist kein Auslöser).
- Nie sonst: nicht bei einer neuen `writer-id`-Generation, nicht nach einem Anker-Reset, nicht als Migration für Worktrees von vor dieser Version.
- Liefert Git keine eindeutige Commit-ID (kein Repository, noch kein Commit, Fehler, Zeitüberschreitung), entsteht keine Basis.
- **Fehlt die Basis, prüft `verify` strenger:** Dann zählen nur die Remote-Stände (Punkt 2). Löschen der Basis-Datei kann eine Fälschung also nie durchlassen, höchstens zusätzliche Befunde erzeugen. Ist der Inhalt kein gültiger Commit, zählt die Basis ebenso nicht.
- Die Basis-Datei mit einer anderen Commit-ID neu zu schreiben, ist eine Fälschung des lokalen Ankers (ADR-0010, Grenze „Datei und Anker gemeinsam gefälscht“). Alles in `.monarch/local` zu löschen, ist „Klon ohne Anker“.

### 2. Vertrauenswürdige Stände

Als schon geprüft gelten nur Commits, die erreichbar sind von

- der Basis dieser Worktree (falls gültig), und
- den **Remote-Ständen** des Integrations-Branches und der geschützten Branches (`git.integration_branch` und `git.protected_branches` aus `system.yaml`, im Default `develop`, `main`, `master`), jeweils als `refs/remotes/<remote>/<branch>`. An `git rev-list --not` gehen nur Refs, die sich auflösen ließen.

Lokale Branches (`refs/heads/develop`) zählen nicht: Sie lassen sich mit einem gewöhnlichen `git branch -f` versetzen. Ein ungeschützter Remote-Branch (etwa der Gate-Branch auf `origin`) zählt auch nicht, denn der Agent darf dorthin pushen.

### 3. Regel für branch-lokale Commits

Branch-lokal sind alle Commits, die von `HEAD` erreichbar sind, aber von keinem vertrauenswürdigen Stand. Geprüft werden nur Ledger-Dateien (`.monarch/ledger/**/*.jsonl`), die **nicht** im lokalen Anker stehen (weder unter `files` noch unter `restore`). Für sie gilt:

- **Normaler Commit (auch Root-Commit):** Jedes Anlegen, Ändern oder Löschen ist ein Befund.
- **Merge-Commit:** Für jede solche Datei, die im Ergebnis von mindestens einem Elternteil abweicht,
  - muss das Ergebnis byte-genau der Fassung eines Elternteils gleichen, **und**
  - muss die Fassung **jedes** Elternteils byte-genau der Anfang des Ergebnisses sein. Fehlt die Datei in einem Elternteil, zählt das als leerer Anfang. Fehlt sie im Ergebnis, obwohl ein Elternteil sie hat, ist das ein Befund.

  Ein Merge kann eine fremde Datei also übernehmen, aber nie kürzen, löschen oder neu zusammensetzen. Was er aus einem branch-lokalen Elternteil übernimmt, prüft die Regel an dessen Commits.

Befunde zu Dateien der eigenen Worktree gibt es hier nicht; die schützt weiter der Anker.

### 4. Befund

- Neue Art `UnanchoredCommit`, je Datei höchstens einmal (der älteste betroffene Commit), Zeile 0.
- Text: Datei, Kurz-ID des Commits, ob normaler Commit oder Merge, und der Hinweis: Der Commit liegt weder auf dem Remote-Stand von `develop`/`main` noch stammt die Datei von dieser Worktree. **Zuerst `git fetch` ausführen**, denn ein veralteter Remote-Stand ist die häufigste harmlose Ursache.
- Exit 1 wie jeder andere Befund. `quest clear`, `evidence sync` und `gate merge` verweigern dann wie heute.

### 5. Ohne Git, ohne vertrauenswürdigen Stand

- Ohne Git oder außerhalb eines Repositories entfällt die Prüfung still, wie heute die ganze zweite Quelle (ADR-0010).
- Gibt es weder eine gültige Basis noch einen Remote-Stand der genannten Branches, entfällt die Prüfung ebenfalls. Sonst wäre jede fremde Datei der ganzen Historie ein Befund. Monarch-Projekte haben einen Remote; ohne Remote hängt die Prüfung allein an der Basis.

### 6. Aufruf, Git-Details und Kosten

- `LedgerVerifier.Verify(root, trustedBranches)`. Die CLI-Aufrufer (`ledger verify`, `quest clear`, `evidence sync`, `status`, `gate merge`, `ack-reset`) geben Integrations-Branch und geschützte Branches aus `system.yaml` mit. Ohne Liste zählt nur die Basis.
- Git läuft mit `GIT_NO_REPLACE_OBJECTS=1` und ohne Grafts, damit `git replace` die Historie nicht mit einem Befehl umbiegt.
- Commits: `git rev-list --parents HEAD --not <aufgelöste Refs>`.
- Pfade: `git diff-tree --stdin -r --root --no-renames -z --relative` mit dem Ledger-Pfad, für Merges zusätzlich je Elternteil (`-m`) mit Blob-IDs, damit Gleichheit und Anfang geprüft werden können (Inhalte per `git cat-file --batch`). `--root` deckt Root- und Orphan-Commits ab, `--relative` ein Projekt in einem Unterordner des Repositories, damit Pfade und Anker-Schlüssel zusammenpassen.
- Für `--stdin` braucht es eine Variante von `GitAnchor.Execute`, die stdin beschreibt, statt es sofort zu schließen.
- Das sind wenige zusätzliche Git-Aufrufe je `verify`, unabhängig von der Zahl der Dateien.
- Schreibpfad: einmal `git rev-parse --verify HEAD` je Worktree (beim Anfang bei null), danach nur ein Dateitest je Append. Das Bench-Projekt ist kein Git-Repository; die Latenz-Gates bleiben unverändert und müssen grün bleiben.

## Was ok bleibt (legitime Abläufe)

| Ablauf | Warum kein Befund |
|---|---|
| Eigene Commits der Gate-Worktree (Hook-Zeilen, Quest-Status, Aufräum-Commit nach dem Merge) | Dateien stehen im lokalen Anker |
| `git merge origin/develop` in den Gate-Branch | fremde Dateien haben die Fassung des `develop`-Elternteils, die ältere Fassung der Gate-Seite ist ihr Anfang |
| `git rebase origin/develop`, `git pull --rebase` über **eigene** Commits | die neu geschriebenen Commits ändern nur eigene Dateien |
| Neue Worktree auf einem bestehenden Gate-Branch (Standardregel 2, ohne `-b`) | die Basis ist der Branch-Stand beim ersten Schreiben; ältere Commits gelten als geprüft |
| Haupt-Checkout auf `develop` nach `git pull` | keine branch-lokalen Commits |
| Frischer Klon | die Basis ist `HEAD` beim ersten Schreiben |
| Squash-Merge auf GitHub, danach Aufräum-Commit | der Squash liegt auf `origin/develop`; der Aufräum-Commit ändert nur eigene Dateien |
| Worktree von vor dieser Version | keine Basis, also nur Remote-Stände; ihre branch-lokalen Commits ändern nur eigene Dateien |

**Strenger als heute, mit Absicht.** Diese Abläufe ergeben einen Befund, weil von hier aus nicht unterscheidbar ist, ob die fremden Zeilen echt sind:

- einen **anderen, noch nicht gemergten** Gate-Branch hineinmergen oder einen Commit daraus cherry-picken;
- einen Squash-Commit aus `develop` cherry-picken, statt `develop` zu mergen;
- einen bestehenden Branch, dessen ältere Commits aus einer anderen Worktree stammen, per `rebase` über die Basis hinweg neu schreiben;
- nach „Update branch“ auf GitHub `git pull` ohne vorheriges `git fetch` von `develop` (verschwindet nach `git fetch`);
- im Repository ohne Remote einen lokalen Merge eines anderen Branches.

Ausweg: `git fetch`, den anderen Branch zuerst nach `develop` bringen, `develop` mergen statt cherry-picken. Einen Owner-Befehl, der die Basis sichtbar neu setzt, gibt es in diesem Gate nicht; er wäre ein neuer Owner-Befehl mit eigener Hook-Regel (ADR-0011, Regel 7) und kommt als Folge-Issue.

## Grenzen (bleiben)

1. **Klon ohne Anker:** Eine Worktree oder ein Klon, die erst **nach** der Fälschung zum ersten Mal schreiben, nehmen die Fälschung als Basis. Dasselbe gilt, wenn jemand ganz `.monarch/local` löscht (Anker, `writer-id`, Basis). Die ursprüngliche Worktree meldet die Fälschung weiter.
2. **History-Rewrite:** Wer die Fälschung in die Basis oder in einen vertrauenswürdigen Stand umschreibt, wird nicht erkannt. Dazu gehören `commit --amend`, `rebase` oder `reset` über die Basis hinweg sowie das **lokale Versetzen der Remote-Stände** (`git update-ref refs/remotes/…`, `git fetch . HEAD:refs/remotes/origin/develop`, ein Remote auf eine lokale Kopie). Das geht mit einem Befehl und ohne Schutzpfad; es ist gezielte Umgehung (G4). Härtung als Folge-Issue: `gate merge`, `quest clear` und `evidence sync` nehmen den `develop`-Stand der Forge statt des lokalen, und `gate merge` bindet den geprüften `HEAD` an den PR-Head.
3. **Lokaler Anker gefälscht** (ADR-0010): Datei und Anker gemeinsam gefälscht, die Basis-Datei mit anderer Commit-ID neu geschrieben, die Fälschung „frischer Klon“ bei ungetrackten Dateien.
4. **Gepusht auf `develop` oder `main`:** Was dort liegt, gilt als vertrauenswürdig. Schutz ist wie bisher der Branch-Schutz mit PR, CI und Warden-Verdict (`gate merge`), nicht `verify`.
5. **Ohne Git oder ohne vertrauenswürdigen Stand** (Punkt 5 oben) entfällt die Prüfung.

G3 in ADR-0011 wird präzisiert: „lokal committet nur ohne History-Rewrite“ heißt künftig „lokal committet, auch nach Push auf einen ungeschützten Branch, ohne History-Rewrite (dazu zählt das lokale Versetzen der Remote-Stände), geprüft in einer Worktree, die vor der Fälschung schon geschrieben hat“.

## Tests (Q-0154, TDD)

Aufbau: Test-Repository mit Bare-Remote, `develop` auf dem Remote mit einer fremden Ledger-Datei, eine Worktree mit Basis.

Rot vor der Umsetzung, grün danach:

- **K-delete-last:** In einer fremden, getrackten Datei die letzte Zeile löschen, normal committen → `UnanchoredCommit`, `Ok` falsch.
- **K-append-chained:** An eine fremde, getrackte Datei eine kettenkorrekte Zeile (`QuestCleared`) anhängen, normal committen → `UnanchoredCommit`.
- Beide auch nach `git push` auf einen ungeschützten Remote-Branch → weiter Befund.
- Neue gefälschte Datei mit fremder `writer-id`, normal committet → `UnanchoredCommit`.
- Merge mit einem älteren Elternteil, der die fremde Datei kürzer hat oder gar nicht, Ergebnis aus diesem Elternteil übernommen (Kürzen bzw. Löschen) → `UnanchoredCommit`.
- Merge, der eine fremde Datei anders zusammensetzt als alle Eltern → `UnanchoredCommit`.
- Orphan-Commit mit gefälschter Datei, per `merge --allow-unrelated-histories` eingebracht → `UnanchoredCommit`.
- Basis-Datei gelöscht, danach Append und Fälschung → Basis bleibt weg, Befund weiter da (strenger).
- Projekt in einem Unterordner des Repositories: eigene Commits ok, Fälschung erkannt.
- CLI: `ledger verify` Exit 1, `quest clear`, `evidence sync` und `gate merge` verweigern bei `UnanchoredCommit`; die Branches kommen aus `system.yaml`.

Gegenproben, die schon heute stimmen und so bleiben:

- K-change und K-delete-mid committet → weiter `HashMismatch` bzw. `SeqGap`/`PrevHashMismatch`.
- Nicht committete Änderungen an getrackten und an eigenen Dateien, gelöschte Datei → unverändert ihre heutigen Befunde.
- Legitime Abläufe aus der Tabelle → `Ok`: eigene Commits, Merge von `origin/develop` (auch wenn die Gate-Seite eine ältere Fassung hat), Rebase eigener Commits, neue Worktree auf bestehendem Branch, frischer Klon, Worktree ohne Basis, Repository ohne Remote und ohne Basis.
- Basis: genau einmal beim Anfang bei null; nicht durch den Lesepfad, nicht bei neuer `writer-id`-Generation, nicht nach Anker-Reset; ungültige Basis zählt nicht.
- **Grenzen als Tests** (erwartet „nicht erkennbar“): Klon ohne Anker nach der Fälschung angelegt; versetzter Remote-Stand; `commit --amend` über die Basis hinweg.

Bestehende Tests ändern sich nicht.

Abnahme: `ledger-tamper.sh` aus #71 mit dem gebauten Binary erneut laufen lassen. Erwartet: K-delete-last und K-append-chained erkannt, B0 sauber, alle übrigen Fälle wie bisher.

## Folgen

- ADR-0010 bekommt einen Nachtrag zu Punkt 3 und zu den Grenzen; ADR-0011 die präzisierte G3-Zeile; `plugin/README.md` im Abschnitt „Grenzen“ dieselben Grenzen in denselben Worten (Maßstab, Klasse 3).
- `docs/lessons.md`: kurzer Nachtrag, warum „nur anhängen“ allein nicht reicht.
- Folge-Issues: (a) Owner-Befehl zum sichtbaren Neusetzen der Basis; (b) Forge-Stand statt lokalem Remote-Stand in `gate merge`, `quest clear`, `evidence sync` und Bindung von `HEAD` an den PR-Head; (c) Append verankert ein kettenkorrekt gefälschtes Ende der eigenen Datei mit (Hash an der verankerten `seq` wird nicht geprüft).
- Nach dem Merge: Owner-Aktion `install-local`. Bestehende Worktrees bekommen keine Basis und prüfen nur gegen die Remote-Stände.
