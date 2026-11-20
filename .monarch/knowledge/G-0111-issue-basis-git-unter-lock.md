## Was passiert

Seit ADR-0018 (G-0111) legt `JsonlLedgerStore.AppendCore` beim „Anfang bei null“ einer Worktree die Ledger-Basis an. `CreateBaseOnce` ruft dafür `GitAnchor.HeadCommit` auf (`git rev-parse`, Zeitlimit 10 s), **während der exklusive Lock der Sitzungsdatei gehalten wird**. Parallele PostToolUse-Hooks derselben Sitzung warten in `LedgerFiles.OpenExclusive` nur 200 ms.

Das passiert nur einmal je Worktree. Unter Last kann daraus aber ein Hook-Fehler werden.

## Vorschlag

HEAD vor `OpenExclusive` oder nach `fs.Dispose` ermitteln und die Basis danach mit `CreateNew` schreiben (bleibt atomar). Test mit einem parallelen Append während eines langsamen Git-Aufrufs.

Gefunden in der Warden-Schlussrunde zu PR 97 (G-0111, Q-0154), Befund 4 (minor).
