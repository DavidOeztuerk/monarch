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
   `verify` nicht mehr Git, sondern vergleicht die Bytes selbst mit dem Blob an `HEAD`. Quelle
   sind der Baum von `HEAD` und zusätzlich `git ls-files`; der Index darf nur Befunde
   hinzufügen, nie einen ersparen (siehe Nachtrag am Ende).
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
- Neu: `verify` liest die Blobs an `HEAD` und vergleicht die Bytes jeder Datei selbst. Index-Kennzeichen und Stat-Daten ersparen keinen Vergleich mehr. Der Index bleibt nur als zusätzliche Quelle, die Befunde hinzufügen kann (Warden-Designrunde G-0114, Befunde 1 und 2).

### Befund (nachgestellt)

- `GitAnchor.IsAnchoredByGit` (`git ls-files --error-unmatch` und `git diff --quiet HEAD`) entscheidet, ob eine fremde Datei ohne lokalen Anker „anchored by git“ ist (`LedgerVerifier.cs:65`).
- `GitAnchor.ChangedAgainstHead` (`git diff --name-only HEAD`) wählt aus, welche getrackten Dateien gegen den HEAD-Blob geprüft werden (`LedgerVerifier.cs:166`, :175). Was Git nicht als geändert meldet, wird nie verglichen.
- Mit `assume-unchanged` oder `skip-worktree` liefern beide „unverändert“. `LedgerIndexTrustTests` hält das fest: Vor der Umsetzung sind 6 Fälle rot (K-append-chained und K-delete-last, je mit beiden Kennzeichen, dazu ein frischer Klon).
- Eine zweite Lücke derselben Art: Nach `git rm --cached <datei>` und Löschen der Datei listet `git ls-files` sie nicht mehr; da sie nicht im lokalen Anker steht, gab es keinen Befund (Warden-Designrunde G-0114, Befund 3; ebenfalls ein roter Fall in `LedgerIndexTrustTests`).
- Dieselbe Klasse, nicht einzeln getestet: `core.ignoreStat` (setzt `assume-unchanged` beim Aktualisieren) und `core.fsmonitor` (ein Hook, der „nichts geändert“ meldet). Abgeleitet, nicht ausgeführt: der Stat-Vergleich mit `core.trustctime=false`, gleicher Dateigröße und zurückgesetzter Änderungszeit (`core.checkStat=minimal` allein genügt nicht). Das sind Index-Kennzeichen oder Einstellungen in `.git/config`; `.git` ist kein Schutzpfad.

### Entscheidung

1. **Stand einmal festlegen.** Wie die Historienprüfung (`LedgerHistory.cs:28-71`) stellt `verify` einmal isoliert fest, ob ein Repository da ist und auf welchen Commit `HEAD` zeigt. Mit genau dieser Commit-ID laufen der Vergleich und die Historienprüfung, damit sich `HEAD` dazwischen nicht ändern kann.
2. **Baum des Commits.** `git ls-tree -r -z <commit> -- .monarch/ledger` liefert Modus, Typ, Blob-ID und Pfad jeder committeten Ledger-Datei, `git cat-file --batch` die Inhalte. Verglichen werden alle regulären Dateien unter dem Ledger-Ordner (Modus 100644 oder 100755, Typ `blob`), mit jeder Endung, auch `.JSONL` (wie ADR-0018, Entscheidung Punkt 3); der bisherige Filter auf `.jsonl` entfällt. Jeder andere Eintrag unter dem Ledger-Ordner (Symlink 120000, Gitlink 160000) ist ein Befund `HeadMismatch` („kein reguläres File im Baum“). Den Kopf jeder Antwort von `cat-file` prüft `verify` streng (ID, Typ `blob`, Größe, wie `LedgerHistory.cs:317-339`); „missing“ ist ein Befund. Alle Aufrufe laufen mit der Isolation aus ADR-0018, Entscheidung Punkt 6 (ohne geerbte `GIT_*`, ohne Replace-Objekte, ohne Grafts).
3. **Der Index bleibt zusätzliche Quelle, die nur Befunde hinzufügt.** `git ls-files -z -- .monarch/ledger` (isoliert) liefert wie bisher die getrackten Dateien. Ein Eintrag im Index erspart nie einen Vergleich und macht allein keine Datei zu „anchored by git“.
4. **Fehlende Dateien:** Liegt eine Datei im Baum des Commits **oder** im Index und fehlt sie auf der Platte, ist das `Truncated` („tracked by git but missing“). Über den Baum deckt das neu `git rm --cached` plus Löschen ab; über den Index bleibt der Fall nach `git checkout --orphan` erhalten (Gegenprobe).
5. **Fremde Datei ohne lokalen Anker:** gilt nur als „anchored by git“, wenn sie im Index **und** im Baum des Commits liegt und ihre Bytes dem Blob genau gleichen. Sonst `UnknownFile` wie bisher; eine Datei, die nicht im Index steht, bleibt also `UnknownFile` (`GitAnchoredLedgerTests.Untracked_unanchored_file_is_unknown`).
6. **Jede Datei aus dem Baum des Commits, die auf der Platte liegt,** muss den Blob byte-genau als Anfang enthalten; weniger Zeilen sind `Truncated`, anderer Inhalt `HeadMismatch`. Das gilt für jede Datei, nicht mehr nur für die, die Git als geändert meldet.
7. **Neu angelegte, noch nicht committete Dateien** liegen nicht im Baum. Für sie gilt wie bisher nur der lokale Anker (eigene Datei) oder `UnknownFile` (fremde Datei).
8. **Ohne Baum, Git-Fehler:** Ohne Git oder außerhalb eines Repositories entfällt die zweite Quelle still wie bisher. In einem Repository ohne Commit (auch nach `git checkout --orphan`) gibt es keinen Baum: Dann ist keine fremde Datei „anchored by git“ (jede ist `UnknownFile`), der Abgleich mit dem Index aus Punkt 4 gilt weiter. Steht fest, dass ein Repository da ist, ist jeder Fehler und jede Zeitüberschreitung von `ls-files`, `ls-tree` oder `cat-file` ein Befund, nie „unverändert“ (wie ADR-0018, Entscheidung Punkt 5).
9. **Kosten:** bis zu sechs isolierte Git-Aufrufe je `verify` (Repository und Commit feststellen, `ls-files`, `ls-tree`, `cat-file`, dazu die eigene Repository-Prüfung der Historienprüfung), unabhängig von der Zahl der Dateien; den Commit teilt sich der Vergleich mit der Historienprüfung (`LedgerHeadView.cs`, `LedgerHistory.Check` mit `pinnedHead`). Ein Git-Fehler beim Vergleich ist ein Befund `HeadMismatch` auf dem Pfad oder dem Ledger-Ordner. `cat-file --batch` liest dabei alle committeten Ledger-Bytes, bei jedem `verify` und damit auch bei `monarch status` (heute rund 1,2 MB, Warden-Designrunde G-0114). Schreibpfad und Hook bleiben unberührt; die Latenz-Gates messen nur `hook pre-tool-use` (`bench/hook-latency.sh:2`).
10. `GitAnchor.IsAnchoredByGit`, `GitAnchor.ChangedAgainstHead` und `GitAnchor.HeadBlob` entfallen, `TrackedLedgerFiles` läuft isoliert; einen anderen Aufrufer gibt es nicht (`git grep` in `src`). Der Klassenkommentar von `GitAnchor` („hook path“) wird berichtigt.

### Was ok bleibt

- Index-Kennzeichen ohne Änderung an der Datei: kein Befund (Gegenprobe in `LedgerIndexTrustTests`).
- Eine unveränderte, committete fremde Datei in einem frischen Klon bleibt „anchored by git“ (`GitAnchoredLedgerTests.Committed_unmodified_file_without_local_anchor_is_anchored_by_git`).
- Eigene Appends an eine committete Datei verlängern den Blob und bleiben ok.
- `git stash`, Merge-Konflikt, Rebase in Arbeit, gestagte eigene Datei und Sparse Checkout verhalten sich wie bisher (Warden-Designrunde G-0114).

### Falsch-Positive und Ausweg

- Umwandlungen, die die Zeilen selbst ändern, sind für Ledger-Dateien schon heute ein Befund: Bei `core.autocrlf=true` und `eol=crlf` lässt das `\r` am Zeilenende `LedgerCanonical.cs:58` scheitern, die Zeile ist `Unparseable` (`LedgerVerifier.cs:269-275`). `working-tree-encoding` ergibt `Unparseable` oder `HashMismatch`; `ident` wirkt nur bei `$Id$` im Inhalt und ergibt dann `HashMismatch`. Daran ändert der Nachtrag nichts.
- Neu ein Befund sind nur Filter, die den Inhalt beim Auschecken wiederherstellen (Git LFS, eigene Clean-/Smudge-Filter). Bisher glich `git diff` das aus; jetzt weichen die Bytes vom Blob ab (bei LFS ist der Blob nur ein Zeiger).
- Ausweg: in `.gitattributes` als **letzte** passende Zeile `.monarch/ledger/** -text -filter -ident -working-tree-encoding` (in einem Projekt im Unterordner mit dessen Pfad davor). Bei LFS reicht neues Auschecken nicht, es schriebe nur den Zeiger in den Arbeitsbaum; nötig ist zuerst `git add --renormalize` mit Commit, erst danach neu auschecken. Das ändert fremde Dateien und wäre auf einem Gate-Branch ein `UnanchoredCommit` (ADR-0018); es gehört darum auf `develop`, durch den Owner.
- Die Umwandlung selbst nachzubilden scheidet aus, denn Filter sind Befehle aus der Repo-Konfiguration und damit fälschbar.
- Monarch läuft heute auf macOS und Linux (Release-Builds `osx-arm64`, `linux-x64`, `ci.yml:48-49`); dort ist `core.autocrlf` ohne eigene Einstellung nicht gesetzt.

### Grenzen (bleiben)

- Datei und Anker einer nicht committeten Ledger-Datei gemeinsam gefälscht, „frischer Klon“, History-Rewrite: unverändert wie oben beschrieben.
- Die Prüfung kennt nur `HEAD`. Wer die Fälschung committet, landet bei der Historienprüfung aus ADR-0018.
- **Ohne Commit und ohne Index-Eintrag:** Nach `git checkout --orphan`, `git rm --cached` und Löschen kennen weder Baum noch Index die Datei; es gibt keinen Befund. **Vorschlag, kein Owner-Entscheid:** `HEAD` ohne Commit als Befund werten, wenn eine gültige Basis oder ein auflösbarer Remote-Stand existiert.
- **HEAD zurücksetzen (#108):** `git reset --soft` auf einen älteren Commit, der von einem vertrauenswürdigen Stand erreichbar ist, und eine fremde Datei auf dessen Fassung gekürzt: gleiche Bytes wie der Blob, keine branch-lokalen Commits, also kein Befund. Das bestand schon vorher (aus dem Code abgeleitet, Warden-Designrunde G-0114, Befund 8).

### Tests (Q-0159, TDD)

- Rot vor der Umsetzung, grün danach: die 7 Fälle aus `LedgerIndexTrustTests` (6 mit Index-Kennzeichen, dazu `git rm --cached` plus Löschen). Sie prüfen auch die Befundart (`UnknownFile` beim Anhängen, `Truncated` beim Kürzen und Löschen).
- Gegenproben grün: Index-Kennzeichen ohne Änderung; dieselbe Fälschung ohne Kennzeichen; `git rm --cached` plus Kürzen bleibt `UnknownFile`; `git checkout --orphan` nach dem Löschen einer fremden Datei bleibt `Truncated`; alle Tests in `GitAnchoredLedgerTests` und `LedgerHistoryTests` unverändert.
- Neu in Q-0159: ein Projekt in einem Unterordner des Repositories (Pfade aus `ls-tree` passen zu den Anker-Schlüsseln); ein Symlink unter dem Ledger-Ordner im Baum ist ein Befund; in einem Repository ohne Commit ist eine fremde Datei `UnknownFile`; ein Git-Fehler bei vorhandenem Repository ist ein Befund.

### Folgen

- ADR-0018, Entscheidung Punkt 5, sagt, die Prüfung gegen `HEAD` „bleibt davon unberührt“ (mit veraltetem Verweis `LedgerVerifier.cs:154`); dort kommt ein Verweis auf diesen Nachtrag hin.
- `plugin/README.md`, Abschnitt „Ledger“: der neue Fehlalarm (Filter, LFS) und die Grenze „ohne Commit und ohne Index-Eintrag“.
- Beides liegt außerhalb der Quest-Scopes und kommt nach `quest submit` von Q-0159 als eigener Doku-Commit in dasselbe Gate, vor der Schlussrunde des Wardens.
- ADR-0018, Grenze 2 („reset über die Basis hinweg reicht dafür nicht“): Verweis auf die Grenze „HEAD zurücksetzen“ oben und #108.
- Folge-Issue #108 (HEAD zurücksetzen).
