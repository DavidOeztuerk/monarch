## Was fehlt

Nach ADR-0018 gelten im `ledger verify` Commits als geprüft, die vom lokalen Remote-Stand von `develop`/`main` (`refs/remotes/<remote>/<branch>`) erreichbar sind. Diesen Stand kann ein Agent mit einem Befehl und ohne Schutzpfad versetzen, etwa `git update-ref refs/remotes/origin/develop HEAD` oder `git fetch . HEAD:refs/remotes/origin/develop`. Danach ist eine Fälschung per normalem Commit wieder unsichtbar.

Der Owner hat am 13.11.2026 entschieden: Das zählt zur Grenze History-Rewrite (G4) und wird später gehärtet.

Außerdem: `gate merge` prüft das Ledger am lokalen `HEAD` des aktuellen Ordners (GateMergeCommands.cs:182), gemergt wird aber `pull.HeadSha`. Beide sind nicht aneinander gebunden.

## Vorschlag

- `gate merge`, `quest clear` und `evidence sync` nehmen als vertrauenswürdigen Stand den `develop`-SHA, den die Forge meldet, nicht den lokalen Remote-Stand.
- `gate merge` verweigert, wenn der geprüfte `HEAD` nicht der PR-Head ist.

Gefunden in der Warden-Designrunde zu ADR-0018 (G-0111, Q-0153).
