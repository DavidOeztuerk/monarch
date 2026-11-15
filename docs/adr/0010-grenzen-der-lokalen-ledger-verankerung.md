# ADR-0010 — Grenzen der lokalen Ledger-Verankerung

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0120 (ergänzt Q-0108)

## Kontext

Die Hash-Kette des Ledgers ist schlüssellos, der Anker `.monarch/local/ledger-heads.json`
liegt lokal. Wer Ledger-Datei und Anker konsistent neu schreibt, erzeugt ein in sich
stimmiges Ledger. Ein unabhängiger Warden hat gezeigt, dass Löschen oder Kürzen plus
Anpassen des Ankers „ledger ok“ ergab, dass `seq: 0` im Anker `verify` abstürzen ließ und
dass ein korrupter Anker beim nächsten Append still neu aufgebaut wurde.

## Entscheidung

1. **Anker wird nie still repariert.** Ist er korrupt (kein JSON, falsche Form, ungültiger
   Eintrag, unlesbar) oder fehlt er trotz vorhandener Historie, schreibt der Append weiter,
   legt den alten Anker als `ledger-heads.json.corrupt-<zeit>` ab, verankert die vorhandenen
   Dateien neu und schreibt das Event `LedgerAnchorReset` (Grund, Zeit, Archivdatei,
   neu verankerte Dateien). `ledger verify` meldet jedes nicht quittierte Reset (Exit 1).
   Quittiert wird nur per `monarch ledger ack-reset <event-hash>` (Event
   `LedgerAnchorResetAcknowledged`, Actor `cli`). Ein frischer Klon (kein `writer-id`) gilt
   nicht als Reset. Liegt der Anker einer Datei **vor** deren Inhalt (weniger Zeilen oder
   anderer Hash an der verankerten `seq`, z. B. nach `git stash`), schreibt der Append den
   Anker nicht fort, sondern beginnt eine neue Datei (neue writerId-Generation) und
   hinterlässt ein `LedgerAnchorReset` („Anchor ahead of file“) als klebenden Befund bis zur
   Quittierung; die alte Datei wird dabei auf ihren Rest verankert.
   **Rückkehr des Originalinhalts (Q-0125, N2):** Das `LedgerAnchorReset` führt den alten Head
   (`oldHead`: Datei, seq, hash). Steht in dieser Datei danach genau dieser Hash an dieser `seq`
   (z. B. nach `git stash pop` oder einer Branch-Rundreise), die Kette ist intakt und es gibt
   keine Zeilen dahinter, gilt der Stand wieder als verankert: `verify` meldet für die Datei
   keinen Befund, und der nächste Append setzt den Anker auf den alten Head zurück und schreibt
   das sichtbare Event `LedgerAnchorRestored` (kein stilles Heilen). Jeder andere Inhalt (anderer
   Hash, zusätzliche Zeilen, gebrochene Kette) bleibt ein Befund. **Das Restore quittiert den Reset
   nicht:** das `LedgerAnchorReset` bleibt Befund, bis der Owner es per `ack-reset` quittiert, auch
   wenn der Originalinhalt zurück ist (der Owner soll wissen, dass der Anker zwischenzeitlich vor der
   Datei lag). Offen: wird die Reset-Datei selbst durch einen Branchwechsel entfernt (auf dem anderen Branch
   committet), bleibt sie als „missing but anchored“ ein Befund.
2. **`verify` stürzt bei keinem Anker-Inhalt ab.** Jeder defekte Anker oder Eintrag ist ein
   Befund `AnchorInvalid`.
3. **Zweite, unabhängige Quelle: Git.** `verify` gleicht die Ledger-Dateien mit
   `git ls-files` ab. Eine getrackte, aber fehlende Datei ist ein Befund; eine gegenüber
   `HEAD` geänderte Datei muss den HEAD-Blob byte-genau als Präfix enthalten (weniger Zeilen
   = `Truncated`, anderer Inhalt = `HeadMismatch`). Git läuft nur im Verify-Pfad, nie im
   Append im Normalfall. **Nachtrag 13.11.2026 (ADR-0018, Q-0154):** Der Vergleich mit `HEAD`
   allein macht eine normal committete Fälschung zum Maßstab (#71). `verify` prüft darum
   zusätzlich alle branch-lokalen Commits (weder von der Basis der Worktree
   `.monarch/local/ledger-base` noch vom Remote-Stand `origin/<develop|main|…>` erreichbar):
   Dort darf sich eine Ledger-Datei nur ändern, wenn sie im lokalen Anker steht; ein Merge darf
   eine fremde Datei nur übernehmen, nie kürzen oder löschen (`UnanchoredCommit`). Im Append
   läuft Git dafür zusätzlich höchstens einmal je Worktree (Basis beim Anfang bei null).
   **Nachtrag 15.11.2026 (#103, G-0114):** Ob eine Datei gegenüber `HEAD` geändert ist, fragt
   `verify` nicht mehr Git, sondern vergleicht die Bytes selbst mit dem Blob an `HEAD` (siehe
   Nachtrag am Ende).
4. Derselbe Schreiber-Dateiname in zwei Monatsordnern ist ein Befund
   (`DuplicateGeneration`). Weitergehende Lückenfreiheit über Dateigenerationen ist aus den
   vorhandenen Daten nicht ableitbar: es gibt keinen Generationszähler, `seq` zählt je Datei.

## Grenzen der lokalen Verankerung

Nicht erkennbar bleibt: Datei **und** Anker einer **nicht von Git getrackten** (bzw. noch
nicht committeten) Ledger-Datei werden konsistent neu geschrieben, auch gekürzt oder gelöscht
samt Ankereintrag. Der Test
`Consistent_forgery_of_untracked_file_and_anchor_is_not_detectable_locally` hält das als
erwartetes „nicht erkennbar“ fest. Ebenso nicht erkennbar: Fälschung zusammen mit einem
Rewrite der Git-Historie. Welche Befehle dazu zählen (Owner-Entscheide und ein Vorschlag),
steht in ADR-0018, Nachtrag vom 14.11.2026; Härtung #94.
Committete Fälschungen erkennt `verify` seit ADR-0018 nur in einer Worktree, die vor der Fälschung schon
geschrieben hat; eine danach angelegte Worktree oder ein Klon nimmt sie als Basis, ebenso eine
frische Worktree, die die Fälschung vor ihrem ersten Ledger-Eintrag committet (Klon ohne Anker,
ADR-0018, Grenze 1). Zu „Datei und Anker gemeinsam gefälscht“ gehören auch: die Basis-Datei mit
anderer Commit-ID neu schreiben, den Gate-Branch lokal in `git.protected_branches` eintragen
(#94), und ein quittierter Anker-Reset, der fremde Dateien als eigene neu verankert (#96;
ADR-0018, Grenze 3). Ohne Remote `origin` und ohne Basis entfällt die Historienprüfung nur in
den zwei Fällen aus ADR-0018, Entscheidung Punkt 5; sonst ist es ein Befund. Ohne Git (kein Repository, kein `git` im Pfad) entfällt die zweite
Quelle still. Schutz dagegen sind nur externe Anker: committete Ledger-Dateien (Q-0108),
Branch-Schutz/Review und später die Forge.

**Fälschung „frischer Klon“ (ADR-0011, Q-0122):** Anker `.monarch/local/ledger-heads.json` und
`.monarch/local/writer-id` werden gelöscht, die Ledger-Dateien werden auf `HEAD` zurückgesetzt
(`git checkout`, ungetrackte Dateien entfernt). Das Ergebnis ist von einem echten frischen Klon
nicht zu unterscheiden. Es entsteht kein `LedgerAnchorReset`, `ledger verify` meldet „ok“, und
Ereignisse, die nicht committet waren, sind spurlos weg. Das ist **nicht erkennbar** (siehe
`GitAnchoredLedgerTests.Fresh_clone_forgery_is_not_detectable_locally_and_creates_no_reset`).
Erkennbar sind nur gepushte Commits auf einem geschützten Branch und die Forge-Evidence;
deshalb sind Ledger-Dateien zu committen und zu pushen (Q-0108), und der Owner trägt dieses
Restrisiko gemäß ADR-0011 (G3) bewusst.

## Nachtrag 15.11.2026: Vergleich mit HEAD ohne Index (#103, G-0114, Q-0158/Q-0159)

### Kurzfassung

- Bisher fragte `verify` Git, ob eine Ledger-Datei gegenüber `HEAD` geändert ist (`git diff HEAD`). Git beantwortet das aus dem Index. Ein Befehl, `git update-index --assume-unchanged <datei>` (oder `--skip-worktree`), genügt, damit Git eine geänderte Datei als unverändert meldet.
- Folge: Eine nicht committete Fälschung einer fremden, getrackten Ledger-Datei (letzte Zeile gelöscht oder kettenkorrekte Zeile angehängt) kam ohne Befund durch `verify`, auch in einem Checkout mit `origin`.
- Neu: `verify` liest die Blobs an `HEAD` und vergleicht die Bytes jeder Datei selbst. Index-Kennzeichen und Stat-Daten spielen keine Rolle mehr.

### Befund (nachgestellt)

- `GitAnchor.IsAnchoredByGit` (`git ls-files --error-unmatch` und `git diff --quiet HEAD`) entscheidet, ob eine fremde Datei ohne lokalen Anker „anchored by git“ ist (`LedgerVerifier.cs:65`).
- `GitAnchor.ChangedAgainstHead` (`git diff --name-only HEAD`) wählt aus, welche getrackten Dateien gegen den HEAD-Blob geprüft werden (`LedgerVerifier.cs:166`, :175). Was Git nicht als geändert meldet, wird nie verglichen.
- Mit `assume-unchanged` oder `skip-worktree` liefern beide „unverändert“. `LedgerIndexTrustTests` hält das fest: Vor der Umsetzung sind 6 Fälle rot (K-append-chained und K-delete-last, je mit beiden Kennzeichen, dazu ein frischer Klon), die 3 Gegenproben grün.
- Dieselbe Klasse, nicht einzeln getestet: `core.ignoreStat` (setzt `assume-unchanged` beim Aktualisieren), `core.fsmonitor` (ein Hook, der „nichts geändert“ meldet) und der Stat-Vergleich mit `core.checkStat=minimal` oder `core.trustctime=false` samt zurückgesetzter Änderungszeit. Das sind Index-Kennzeichen oder Einstellungen in `.git/config`; `.git` ist kein Schutzpfad.

### Entscheidung

1. **Quelle ist der Baum von `HEAD`, nicht der Index.** `git ls-tree -r -z HEAD -- .monarch/ledger` liefert Pfad und Blob-ID jeder committeten Ledger-Datei, `git cat-file --batch` die Inhalte. Das sind zwei Git-Aufrufe je `verify`, unabhängig von der Zahl der Dateien. Beide laufen mit derselben Isolation wie die Historienprüfung (ADR-0018, Entscheidung Punkt 6: ohne geerbte `GIT_*`, ohne Replace-Objekte, ohne Grafts).
2. **Fremde Datei ohne lokalen Anker:** gilt nur als „anchored by git“, wenn sie im Baum von `HEAD` liegt und ihre Bytes dem Blob genau gleichen. Sonst `UnknownFile` wie bisher.
3. **Jede Datei aus dem Baum von `HEAD`:** fehlt sie auf der Platte, ist das wie bisher `Truncated` („tracked by git but missing“). Sonst muss sie den Blob byte-genau als Anfang enthalten; weniger Zeilen sind `Truncated`, anderer Inhalt `HeadMismatch`. Das gilt für jede Datei, nicht mehr nur für die, die Git als geändert meldet.
4. **Neu angelegte, noch nicht committete Dateien** liegen nicht im Baum von `HEAD`. Für sie gilt wie bisher nur der lokale Anker (eigene Datei) oder `UnknownFile` (fremde Datei).
5. **Git-Fehler:** Ohne Git, außerhalb eines Repositories oder ohne Commit entfällt der Vergleich still wie bisher. Steht fest, dass ein Repository mit Commit da ist, ist jeder Fehler und jede Zeitüberschreitung von `ls-tree` oder `cat-file` ein Befund, nie „unverändert“ (wie ADR-0018, Entscheidung Punkt 5).
6. `GitAnchor.IsAnchoredByGit` und `GitAnchor.ChangedAgainstHead` entfallen; einen anderen Aufrufer gibt es nicht (`git grep` in `src`).

### Was ok bleibt

- Index-Kennzeichen ohne Änderung an der Datei: kein Befund (Gegenprobe in `LedgerIndexTrustTests`).
- Eine unveränderte, committete fremde Datei in einem frischen Klon bleibt „anchored by git“ (`GitAnchoredLedgerTests.Committed_unmodified_file_without_local_anchor_is_anchored_by_git`).
- Eigene Appends an eine committete Datei verlängern den Blob und bleiben ok.

### Falsch-Positive und Ausweg

- Wandelt Git Ledger-Dateien beim Auschecken um, weichen die Bytes vom Blob ab, und `verify` meldet einen Befund. Das betrifft `core.autocrlf=true`, eine `text`- oder `eol`-Regel in `.gitattributes` und Clean-/Smudge-Filter (etwa Git LFS) für `.monarch/ledger`. Bisher glich `git diff` das aus.
- Ausweg: in `.gitattributes` die Zeile `.monarch/ledger/** -text -filter` und die Dateien neu auschecken. Die Umwandlung selbst nachzubilden scheidet aus, denn Filter sind Befehle aus der Repo-Konfiguration und damit fälschbar.
- Monarch läuft heute auf macOS und Linux (Release-Builds `osx-arm64`, `linux-x64`); dort ist `core.autocrlf` ohne eigene Einstellung nicht gesetzt.

### Grenzen (bleiben)

- Datei und Anker einer nicht committeten Ledger-Datei gemeinsam gefälscht, „frischer Klon“, History-Rewrite: unverändert wie oben beschrieben.
- Die Prüfung kennt nur `HEAD`. Wer die Fälschung committet, landet bei der Historienprüfung aus ADR-0018.

### Tests (Q-0159, TDD)

- Rot vor der Umsetzung, grün danach: die 6 Fälle aus `LedgerIndexTrustTests`.
- Gegenproben grün: Index-Kennzeichen ohne Änderung; dieselbe Fälschung ohne Kennzeichen; alle Tests in `GitAnchoredLedgerTests` und `LedgerHistoryTests` unverändert.
- Dazu: ein Projekt in einem Unterordner des Repositories (Pfade aus `ls-tree` passen zu den Anker-Schlüsseln) und ein Git-Fehler bei vorhandenem Commit ist ein Befund.
