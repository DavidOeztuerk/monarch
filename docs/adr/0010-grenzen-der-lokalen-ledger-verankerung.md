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
4. Derselbe Schreiber-Dateiname in zwei Monatsordnern ist ein Befund
   (`DuplicateGeneration`). Weitergehende Lückenfreiheit über Dateigenerationen ist aus den
   vorhandenen Daten nicht ableitbar: es gibt keinen Generationszähler, `seq` zählt je Datei.

## Grenzen der lokalen Verankerung

Nicht erkennbar bleibt: Datei **und** Anker einer **nicht von Git getrackten** (bzw. noch
nicht committeten) Ledger-Datei werden konsistent neu geschrieben, auch gekürzt oder gelöscht
samt Ankereintrag. Der Test
`Consistent_forgery_of_untracked_file_and_anchor_is_not_detectable_locally` hält das als
erwartetes „nicht erkennbar“ fest. Ebenso nicht erkennbar: Fälschung zusammen mit einem
Rewrite der Git-Historie; dazu zählt nach Owner-Entscheid vom 13.11.2026 auch das lokale
Versetzen der Remote-Stände von `origin` (ADR-0018, Grenze 2; Härtung #94). Committete
Fälschungen erkennt `verify` seit ADR-0018 nur in einer Worktree, die vor der Fälschung schon
geschrieben hat; eine danach angelegte Worktree oder ein Klon nimmt sie als Basis, ebenso eine
frische Worktree, die die Fälschung vor ihrem ersten Ledger-Eintrag committet (Klon ohne Anker,
ADR-0018, Grenze 1). Zu „Datei und Anker gemeinsam gefälscht“ gehören auch: die Basis-Datei mit
anderer Commit-ID neu schreiben, den Gate-Branch lokal in `git.protected_branches` eintragen
(#94), und ein quittierter Anker-Reset, der fremde Dateien als eigene neu verankert (#96;
ADR-0018, Grenze 3). Ohne Git, oder ohne Remote `origin` und ohne Basis, entfällt die
Historienprüfung (ADR-0018, Grenze 5). Ohne Git (kein Repository, kein `git` im Pfad) entfällt die zweite
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
