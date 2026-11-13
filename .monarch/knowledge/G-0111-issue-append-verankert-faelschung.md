## Was passiert

Ein kettenkorrekt gefälschtes Ende der **eigenen** Ledger-Datei wird beim nächsten Append mitverankert und ist danach nicht mehr erkennbar.

- `JsonlLedgerStore.AppendCore` (src/Monarch.Infrastructure.Ledger/JsonlLedgerStore.cs:135-136) prüft den Anker nur, wenn die Datei kürzer ist als verankert oder gleich lang mit anderem Hash. Ist sie **länger**, wird der Hash an der verankerten `seq` nicht geprüft.
- `LedgerHeadStore.Update` (LedgerHeadStore.cs, Zweig `current.Seq < seq`) setzt den Anker dann auf das neue Ende.
- PostToolUse hängt nach jedem Tool ein `ToolUsed` an (HookRunner.cs:104). Eine Fälschung am Ende der aktuellen Session-Datei wird so noch im selben Tool-Aufruf verankert.

## Folge

Der Fall U-append-chained aus #71 wurde nur erkannt, weil zwischen Fälschung und `verify` kein Append lag. G3 (ADR-0011) gilt für eigene, nicht committete Dateien damit nur, solange kein Append dazwischen fällt.

## Vorschlag

Vor dem Fortschreiben prüfen, dass an der verankerten `seq` genau der verankerte Hash steht und die Zeilen dahinter vom Store selbst stammen können. Sonst wie beim Fall „Anker vor der Datei“ eine neue Generation und ein `LedgerAnchorReset` als klebender Befund.

Gefunden in der Warden-Designrunde zu ADR-0018 (G-0111, Q-0153).
