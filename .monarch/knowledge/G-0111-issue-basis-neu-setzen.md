## Was fehlt

ADR-0018 legt die Ledger-Basis (`.monarch/local/ledger-base`) nur an, wenn eine Worktree bei null anfängt, und setzt sie nie neu. In einigen legitimen, seltenen Abläufen meldet `ledger verify` danach `UnanchoredCommit`, ohne dass der Agent das beheben kann:

- einen anderen, noch nicht gemergten Gate-Branch hineinmergen oder daraus cherry-picken;
- einen Squash-Commit aus `develop` cherry-picken;
- einen bestehenden Branch mit Commits einer anderen Worktree per `rebase` über die Basis hinweg neu schreiben;
- im Repository ohne Remote einen lokalen Merge.

`quest clear`, `evidence sync` und `gate merge` bleiben dann in dieser Worktree verweigert.

## Vorschlag

Ein Owner-Befehl, der die Basis sichtbar neu setzt, etwa `monarch ledger rebase-base <commit>`: verlangt ein TTY wie `ack-reset`, schreibt ein Event mit alter und neuer Basis, ist für den Agenten per Hook verboten (ADR-0011, Regel 7). Das berührt die Hook-Regeln und ist damit eine heikle Quest.

Gefunden in der Warden-Designrunde zu ADR-0018 (G-0111, Q-0153).
