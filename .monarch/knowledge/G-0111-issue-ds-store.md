## Was passieren kann

Seit ADR-0018 (G-0111) meldet `ledger verify` jeden nicht verankerten Pfad unter `.monarch/ledger`, der in einem branch-lokalen Commit auftaucht, als `UnanchoredCommit`, unabhängig von der Endung. Das ist Absicht (macOS liest auch `x.JSONL`).

`.DS_Store` ist weder in `.gitignore` noch global ignoriert. Der übliche Aufräum-Schritt `git add .monarch` (Standardregel 2) würde eine Finder-Datei im Ledger-Ordner mitnehmen. Danach meldet `verify` auf dem Gate-Branch einen Befund, und `quest clear` und `gate merge` verweigern.

Bisher kam das nie vor.

## Vorschlag

`.DS_Store` in die `.gitignore` des Repos aufnehmen (und in das Template von `monarch awaken`). Die Prüfung selbst bleibt streng.

Gefunden in der Warden-Delta-Runde zu PR 97 (G-0111), Nit.
