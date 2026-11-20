# Monarch – Hinweise für Claude

## Prüfen und Freigeben (Regeln des Owners, 14.11.2026; Regel 3 eingegrenzt am 15.11.2026)

1. Diffs im Chat zeigen, mit ENTFERNT: und NEU:. Keine Diff-Dateien im Repo.
2. Duplikate nur mit grep -n auf die fertige Datei belegen (Zeilennummern und Text),
   nie aus einem Diff-Auszug ableiten.
3. Gilt nur für Freigaben des Owners von Dokumenten mit Owner-Entscheiden (ADRs):
   Jede Änderung nach einer solchen Freigabe braucht eine neue Freigabe, auch wenn
   sie klein ist. Das gilt auch für Zeitstempel und Fundstellen.
4. Owner-Entscheide im Dokument: Zitate nur wörtlich, und nur die Antworten, die
   David im Chat tatsächlich gegeben hat. Alles andere als "Zusammenfassung" markieren.
5. Keine Behauptung ohne Beleg: Fundstellen nennen Datei, Zeile und Befehl.

Sonst gilt Standardregel 3, ohne Einzelfreigaben.
