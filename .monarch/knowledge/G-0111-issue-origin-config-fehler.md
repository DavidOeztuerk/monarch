## Was passiert

`LedgerHistory.Check` (src/Monarch.Infrastructure.Ledger/LedgerHistory.cs, Abfrage `git config --get remote.origin.url`) wertet jeden Ausgang außer Exit 0 mit nicht leerer URL still als „kein origin eingerichtet“: einen Exit-Code ungleich 1, „git nicht verfügbar“ und eine leere URL. Dann entfällt die Historienprüfung, wenn es auch keine Basis gibt.

Praktisch ist das abgedeckt: Eine kaputte Konfiguration scheitert schon vorher bei `rev-parse`. Es weicht aber vom Grundsatz aus ADR-0018 Punkt 5 ab: „Steht das Repository fest, ist jede Git-Störung ein Befund.“

## Vorschlag

Nur Exit 1 (Schlüssel fehlt) heißt „nicht eingerichtet“. Jeder andere Ausgang ruft `Fail` und wird damit ein Befund. Test dazu.

Gefunden in der Warden-Schlussrunde zu PR 97 (G-0111, Q-0154), Befund 5 (minor).
