# ADR-0007 — Grenzen der lokalen Ledger-Verankerung

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
   nicht als Reset.
2. **`verify` stürzt bei keinem Anker-Inhalt ab.** Jeder defekte Anker oder Eintrag ist ein
   Befund `AnchorInvalid`.
3. **Zweite, unabhängige Quelle: Git.** `verify` gleicht die Ledger-Dateien mit
   `git ls-files` ab. Eine getrackte, aber fehlende Datei ist ein Befund; eine gegenüber
   `HEAD` geänderte Datei muss den HEAD-Blob byte-genau als Präfix enthalten (weniger Zeilen
   = `Truncated`, anderer Inhalt = `HeadMismatch`). Git läuft nur im Verify-Pfad, nie im
   Append im Normalfall.
4. Derselbe Schreiber-Dateiname in zwei Monatsordnern ist ein Befund
   (`DuplicateGeneration`). Weitergehende Lückenfreiheit über Dateigenerationen ist aus den
   vorhandenen Daten nicht ableitbar: es gibt keinen Generationszähler, `seq` zählt je Datei.

## Grenzen der lokalen Verankerung

Nicht erkennbar bleibt: Datei **und** Anker einer **nicht von Git getrackten** (bzw. noch
nicht committeten) Ledger-Datei werden konsistent neu geschrieben, auch gekürzt oder gelöscht
samt Ankereintrag. Der Test
`Consistent_forgery_of_untracked_file_and_anchor_is_not_detectable_locally` hält das als
erwartetes „nicht erkennbar“ fest. Ebenso nicht erkennbar: Fälschung zusammen mit einem
Rewrite der Git-Historie. Ohne Git (kein Repository, kein `git` im Pfad) entfällt die zweite
Quelle still. Schutz dagegen sind nur externe Anker: committete Ledger-Dateien (Q-0108),
Branch-Schutz/Review und später die Forge.
