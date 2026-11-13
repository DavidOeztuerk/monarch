## Was passiert

Ein Anker-Reset (`LedgerHeadStore.Update`, src/Monarch.Infrastructure.Ledger/LedgerHeadStore.cs:36-43) verankert alle vorhandenen Ledger-Dateien neu, auch fremde aus anderen Worktrees. Quittiert der Owner den Reset per `monarch ledger ack-reset`, gelten diese Dateien danach als eigene.

Seit ADR-0018 prüft `ledger verify` branch-lokale Commits nur für Dateien, die **nicht** im Anker stehen. Eine fremde Datei, die vor dem Reset gefälscht war, bleibt nach der Quittung dauerhaft von dieser Prüfung ausgenommen.

## Vorschlag

`ack-reset` listet vor der Quittung die neu verankerten Dateien auf, die nicht von dieser Worktree stammen (andere writer-id im Dateinamen), damit der Owner weiß, was er mit quittiert.

Gefunden in der Warden-Delta-Runde zu ADR-0018 (G-0111, Q-0153), dort Grenze 3.
