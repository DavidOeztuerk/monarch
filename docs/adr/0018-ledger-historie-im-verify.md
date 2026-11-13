# ADR-0018 — Ledger-Historie im verify: Fälschung per normalem Commit (#71)

**Datum:** 13.11.2026 · **Status:** Entwurf, Warden-Designrunde offen · **Quest:** Q-0153 (Design), Q-0154 (Umsetzung), G-0111 · **Baut auf:** ADR-0010, ADR-0011 (G3)

## Kurz in einfacher Sprache

- Heute vergleicht `ledger verify` eine getrackte Ledger-Datei nur mit ihrer Fassung im letzten Commit (`HEAD`). Wer eine fremde Ledger-Datei fälscht und das **normal committet**, macht die Fälschung selbst zum Maßstab. Zwei Fälle aus #71 bleiben so unerkannt: die letzte Zeile gelöscht (K-delete-last) und eine kettenkorrekte Zeile angehängt (K-append-chained).
- Neu merkt sich jede Worktree beim ersten Schreiben, auf welchem Commit sie angefangen hat (**Basis**). `verify` schaut sich alle Commits an, die danach dazugekommen und noch nicht auf `develop` oder `main` angekommen sind. In diesen Commits darf sich eine Ledger-Datei nur ändern, wenn **diese Worktree** sie geschrieben hat. Jede andere Änderung ist ein Befund.
- Damit sind beide Fälle aus #71 erkannt, auch nach einem `git push` auf den Gate-Branch.
- Was bleibt: Eine Worktree oder ein Klon, die erst **nach** der Fälschung anfangen, sehen die Fälschung als Vorgeschichte (Klon ohne Anker). Wer die Git-Historie oder die Remote-Stände umschreibt, ist ebenfalls nicht erkennbar (History-Rewrite).
- Keine Schutzregel wird gelockert. `verify` wird nur strenger. Git läuft im Schreibpfad höchstens einmal je Worktree.

## Was verify heute prüft

1. **Hash-Kette je Datei:** Hash, `prevHash`, `seq`. Ändern oder Löschen in der Mitte bricht die Kette (`HashMismatch`, `SeqGap`, `PrevHashMismatch`).
2. **Lokaler Anker** (`.monarch/local/ledger-heads.json`): Für jede Datei, die diese Worktree geschrieben hat, stehen `seq` und Hash des letzten Eintrags darin. Fehlende, gekürzte oder verlängerte Dateien fallen auf (`Truncated`, `HeadMismatch`).
3. **Git gegen `HEAD`** (ADR-0010, Punkt 3): Eine getrackte Datei muss existieren und ihre `HEAD`-Fassung byte-genau als Anfang enthalten. Eine Datei ohne lokalen Anker gilt als „von Git verankert“, wenn sie getrackt und gegenüber `HEAD` unverändert ist.

## Die Lücke

Fremde Ledger-Dateien (aus anderen Worktrees, per Merge gekommen) haben keinen lokalen Anker. Ihr einziger Maßstab ist `HEAD`. Ein neuer Commit verschiebt `HEAD`. Danach ist die gefälschte Datei „getrackt und unverändert“ und damit verankert. Ältere Commits schaut `verify` nicht an.

Eigene Dateien sind nicht betroffen: Ihr Anker kennt `seq` und Hash, ein Commit ändert daran nichts (Fälle U-… in #71).

**Warum „jede Fassung muss die vorige als Anfang enthalten“ allein nicht reicht:** Damit wäre K-delete-last erkannt. K-append-chained ist aber genau ein Anhängen. Kettenkorrekt angehängt sieht eine Fälschung aus wie eine echte Zeile; die Kette ist schlüssellos. Unterscheiden lässt sich nur, **wo** angehängt wurde: Eine fremde Datei wächst nie in dieser Worktree, sondern kommt fertig über `develop` oder `main`.

## Entscheidung

### 1. Basis der Worktree

- Neue Datei `.monarch/local/ledger-base` (ungetrackt, liegt unter dem Schutzpfad `.monarch/local/**`, G2).
- Inhalt: die volle Commit-ID von `HEAD` beim **ersten Schreiben** dieser Worktree, oder `none`, wenn es keinen Commit gibt (leeres Repository, kein Git).
- Geschrieben wird sie genau einmal:
  - beim Anlegen der `writer-id` (frische Worktree, frischer Klon), und
  - als Migration beim nächsten Append einer Worktree, die eine `writer-id` von vor dieser Version hat, aber noch keine Basis.
- Danach wird sie nie geändert, auch nicht bei einer neuen `writer-id`-Generation oder einem Anker-Reset. Fehlt sie, gilt das als erstes Schreiben (siehe Grenzen).
- Ist der Inhalt kein gültiger Commit im Repository, zählt die Basis nicht. Das macht `verify` strenger, nicht lockerer.

### 2. Vertrauenswürdige Stände

Als schon geprüft gelten nur Commits, die erreichbar sind von

- der Basis dieser Worktree, und
- den **Remote-Ständen** des Integrations-Branches und der geschützten Branches (`git.integration_branch` und `git.protected_branches` aus `system.yaml`, also im Default `develop`, `main`, `master`), jeweils als `refs/remotes/<remote>/<branch>`.

Lokale Branches (`refs/heads/develop`) zählen nicht: Sie lassen sich mit einem gewöhnlichen `git branch -f` versetzen. Ein ungeschützter Remote-Branch (etwa der Gate-Branch auf `origin`) zählt auch nicht, denn der Agent darf dorthin pushen.

### 3. Regel für branch-lokale Commits

Branch-lokal sind alle Commits, die von `HEAD` erreichbar sind, aber von keinem vertrauenswürdigen Stand. Für sie gilt:

- **Normaler Commit:** Jede Ledger-Datei (`.monarch/ledger/**/*.jsonl`), die er anlegt, ändert oder löscht, muss im lokalen Anker dieser Worktree stehen. Sonst ist es ein Befund.
- **Merge-Commit:** Eine Ledger-Datei darf im Ergebnis nur eine Fassung haben, die schon in einem der Eltern steht. Eine Fassung, die von **allen** Eltern abweicht, ist ein Befund. Was ein Merge aus `develop` mitbringt, stammt aus einem vertrauenswürdigen Elternteil; was aus einem anderen branch-lokalen Elternteil kommt, prüft die Regel dort.

Befunde zu Dateien der eigenen Worktree gibt es hier nicht; dafür sorgt weiter der Anker (Kürzen, Ersetzen, Verlängern hinter seinem Rücken).

### 4. Befund

- Neue Art `UnanchoredCommit`, je Datei höchstens einmal (der älteste betroffene Commit), Zeile 0.
- Text: Datei, Kurz-ID des Commits und der Hinweis, dass dieser Commit weder auf dem Remote-Stand von `develop`/`main` liegt noch die Datei von dieser Worktree stammt.
- Exit 1 wie jeder andere Befund. `quest clear`, `evidence sync` und `gate merge` verweigern dann wie heute.

### 5. Ohne Git, ohne vertrauenswürdigen Stand

- Ohne Git oder außerhalb eines Repositories entfällt die Prüfung still, wie heute die ganze zweite Quelle (ADR-0010).
- Gibt es weder eine gültige Basis noch einen Remote-Stand der genannten Branches, entfällt die Prüfung ebenfalls. Sonst wäre jede fremde Datei der ganzen Historie ein Befund.

### 6. Aufruf und Kosten

- `LedgerVerifier.Verify(root, trustedBranches)`. Die CLI-Aufrufer (`ledger verify`, `quest clear`, `evidence sync`, `status`, `gate merge`, `ack-reset`) geben Integrations-Branch und geschützte Branches aus `system.yaml` mit. Ohne Liste zählt nur die Basis.
- `verify`: Ein `git rev-list --parents` über die branch-lokalen Commits und ein `git diff-tree --stdin` für deren Ledger-Pfade, dazu je vertrauenswürdigem Stand eine Auflösung. Das sind wenige zusätzliche Git-Aufrufe, unabhängig von der Anzahl der Dateien.
- Schreibpfad: einmal `git rev-parse --verify HEAD` je Worktree (Basis), danach nur ein Dateitest je Append. Die Latenz-Gates bleiben unverändert und müssen grün bleiben.

## Was ok bleibt (legitime Abläufe)

| Ablauf | Warum kein Befund |
|---|---|
| Eigene Commits der Gate-Worktree (Hook-Zeilen, Quest-Status, Aufräum-Commit nach dem Merge) | Dateien stehen im lokalen Anker |
| `git merge origin/develop` in den Gate-Branch | fremde Dateien haben die Fassung des `develop`-Elternteils, und der ist vertrauenswürdig |
| `git rebase origin/develop`, `git pull --rebase` | die neu geschriebenen Commits ändern nur eigene Dateien |
| Neue Worktree auf einem bestehenden Gate-Branch (Standardregel 2, ohne `-b`) | die Basis ist der Branch-Stand beim ersten Schreiben; ältere Commits gelten als geprüft |
| Haupt-Checkout auf `develop` nach `git pull` | keine branch-lokalen Commits |
| Frischer Klon | die Basis ist `HEAD` beim ersten Schreiben |
| Squash-Merge auf GitHub, danach Aufräum-Commit | der Squash liegt auf `origin/develop`; der Aufräum-Commit ändert nur eigene Dateien |

Strenger als heute, mit Absicht: Wer in einer Worktree einen **anderen, noch nicht gemergten** Gate-Branch hineinmerged oder einen Commit daraus cherry-pickt, bekommt für dessen Ledger-Dateien einen Befund. Von hier aus ist nicht unterscheidbar, ob sie echt sind. Ausweg: den anderen Gate-Branch zuerst nach `develop` mergen. Eine Worktree von vor dieser Version, die beim Update schon auf einem solchen Stand steht, nimmt ihn mit der Migration als Basis.

## Grenzen (bleiben)

1. **Klon ohne Anker:** Eine Worktree oder ein Klon, die erst **nach** der Fälschung zum ersten Mal schreiben, nehmen die Fälschung als Basis. Dasselbe gilt, wenn jemand `.monarch/local` löscht (Anker, `writer-id`, Basis) oder die Basis-Datei allein löscht oder neu schreibt; das ist eine Fälschung des lokalen Ankers (ADR-0010, Schutzpfad `.monarch/local/**`). Die ursprüngliche Worktree meldet die Fälschung weiter. Ebenso bei der einmaligen Migration: Was vor dem ersten Schreiben mit dieser Version schon branch-lokal committet war, gilt als Basis.
2. **History-Rewrite:** Wer die Fälschung in die Basis oder in einen vertrauenswürdigen Stand umschreibt, wird nicht erkannt. Dazu gehören `commit --amend`, `rebase` oder `reset` über die Basis hinweg sowie das Versetzen von Remote-Ständen (`git update-ref refs/remotes/…`, ein Remote auf eine lokale Kopie). Das Löschen der Remote-Stände allein reicht nicht, solange die Basis gilt.
3. **Ungetrackt** (unverändert aus ADR-0010): Datei und Anker gemeinsam gefälscht, und die Fälschung „frischer Klon“.
4. **Gepusht auf `develop` oder `main`:** Was dort liegt, gilt als vertrauenswürdig. Schutz ist wie bisher der Branch-Schutz mit PR, CI und Warden-Verdict (`gate merge`), nicht `verify`.
5. **Ohne Git oder ohne vertrauenswürdigen Stand** (Punkt 5 oben) entfällt die Prüfung.

G3 in ADR-0011 wird präzisiert: „lokal committet nur ohne History-Rewrite“ heißt künftig „lokal committet, auch nach Push auf einen ungeschützten Branch, ohne History-Rewrite, geprüft in einer Worktree, die vor der Fälschung schon geschrieben hat“.

## Tests (Q-0154, TDD)

Rot vor der Umsetzung, grün danach (in einem Test-Repository mit Bare-Remote, `develop` auf dem Remote, einer Worktree mit Basis):

- **K-delete-last:** In einer fremden, getrackten Datei die letzte Zeile löschen, normal committen → `UnanchoredCommit`, `Ok` falsch.
- **K-append-chained:** An eine fremde, getrackte Datei eine kettenkorrekte Zeile (`QuestCleared`) anhängen, normal committen → `UnanchoredCommit`.
- Beide auch nach `git push` auf einen ungeschützten Remote-Branch → weiter Befund.
- Neue gefälschte Datei mit fremder `writer-id`, normal committet → `UnanchoredCommit`.
- Merge-Commit, der eine fremde Datei anders zusammensetzt als beide Eltern → `UnanchoredCommit`.

Gegenproben, die schon heute stimmen und so bleiben:

- K-change und K-delete-mid committet → weiter `HashMismatch` bzw. `SeqGap`/`PrevHashMismatch`.
- Nicht committete Änderungen an getrackten und an eigenen Dateien, gelöschte Datei → unverändert ihre heutigen Befunde.
- Legitime Abläufe aus der Tabelle oben → `Ok`: eigene Commits, Merge von `origin/develop`, neue Worktree auf bestehendem Branch, frischer Klon, Repository ohne Remote und ohne Basis.
- **Grenze als Test:** Klon ohne Anker, nach der Fälschung angelegt → `Ok` (erwartet „nicht erkennbar“), wie `Fresh_clone_forgery_is_not_detectable_locally_and_creates_no_reset`.
- Basis: genau einmal geschrieben, bei neuer `writer-id`-Generation und Anker-Reset unverändert; ungültige Basis zählt nicht (strenger).

Bestehende Tests ändern sich nicht.

## Folgen

- ADR-0010 bekommt einen Nachtrag zu Punkt 3 und zu den Grenzen; ADR-0011 die präzisierte G3-Zeile; `plugin/README.md` im Abschnitt „Grenzen“ dieselben Grenzen in denselben Worten (Maßstab, Klasse 3).
- `docs/lessons.md`: kurzer Nachtrag, warum „nur anhängen“ allein nicht reicht.
- Nach dem Merge: Owner-Aktion `install-local`, damit die Basis in den Worktrees entsteht.
