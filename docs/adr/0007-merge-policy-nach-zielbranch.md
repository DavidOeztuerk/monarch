# ADR-0007 — Merge-Policy nach Zielbranch: Integration ja, geschützte Branches nie

**Datum:** 28.10.2026 · **Status:** vorgeschlagen (Design-Phase Warden G-0101) · **Quest:** Q-0113 (G-0101)
**Anlass:** Merge-Regel des Owners vom 27.10.2026: Claude merged selbst in den
Integrations-Branch (`develop`), wenn CI grün ist, ein unabhängiger Warden
freigibt und die eigene Prüfung keine Ungereimtheiten findet (Rank E–A).
`main`/`master` merged nur der Owner, nach Test auf `develop`. Rank S: jede
mutierende Aktion einzeln mit dem Owner.

## Problem

Branch-Protection auf GitHub hilft hier nicht. Claude arbeitet mit dem Account
des Owners, und GitHub kann die beiden nicht unterscheiden. Mit
`enforce_admins: false` darf der Owner (und damit der Agent) die Protection
umgehen. Auf privaten Repos im Free-Plan gibt es sie gar nicht (gemessen am
27.10. für `monarch` und `NoeliaControlPlane`). Die Sperre gegen den Agenten
muss also in Monarch liegen.

## Entscheidung

1. **Konfiguration** in `system.yaml` unter `git:`
   - `protected_branches`: Default `[main, master]`. Die Datei kann nur
     ergänzen, nicht entfernen (wie bei `deny_bash`).
   - `integration_branch`: Default `develop`.
2. **Neue Regel `protect_branches` im PreToolUse-Hook** (Bash). Sie gilt für
   jeden Akteur, Main und Shadows. Sie verweigert (`deny`):
   - `git push`, dessen Ziel ein geschützter Branch ist, in jeder Schreibweise:
     `main`, `HEAD:main`, `x:refs/heads/main`, `+main`, `:main`,
     `--delete main`, `-d main`, `--all`, `--mirror` sowie Force-Varianten.
     Ohne Refspec (`git push`, `git push origin`) zählt der aktuelle Branch. Er
     wird aus `HEAD` des Git-Verzeichnisses gelesen, ohne Prozess-Fork; Worktrees
     mit `.git`-Datei werden aufgelöst. Ist der Zielbranch unbekannt (kein
     lesbares `HEAD`), entscheidet die Regel `ask`, nicht `allow`.
   - `gh pr merge` in jeder Form. Gemerged wird nur über `monarch gate merge`.
   - `gh api` auf `…/pulls/<n>/merge` und `…/merges` sowie GraphQL mit
     `mergePullRequest`, `enablePullRequestAutoMerge` oder `mergeBranch`.
   - `git push` mit `--no-verify` gegen geschützte Branches ist schon durch den
     ersten Punkt abgedeckt. Lokale `git merge`/`git rebase` bleiben erlaubt,
     denn ohne Push ändern sie nichts auf der Forge.
3. **`monarch gate merge <G>`** ist der einzige Merge-Weg. Er prüft in dieser
   Reihenfolge und bricht beim ersten Fehler mit Begründung ab:
   1. Das Gate ist offen und hat einen verlinkten PR (aus `quest pr`).
   2. Die Base des PR ist `integration_branch`, nie ein geschützter Branch.
   3. Rank-Regel: Rank S verweigert immer (Owner-Aktion). Rank E–A ist erlaubt.
   4. Die CI ist grün: Alle Check-Runs des Head-SHA sind `completed`, und jede
      Conclusion ist `success`, `neutral` oder `skipped`. Läuft noch ein Check
      oder fehlen Check-Runs, wird verweigert.
   5. Ein Warden-Verdict liegt als Evidence vor. `monarch gate verdict <G>
      --verdict approve|changes --source <url>` schreibt `WardenVerdictRecorded`
      mit Head-SHA. Gültig ist nur das jüngste Verdict zum aktuellen Head-SHA,
      und es muss `approve` sein. Zusätzlich muss das Ledger seit Gate-Öffnung
      ein `ShadowRunCompleted` mit `agent_type` eines Wardens enthalten
      (SubagentStop). Ein Push nach dem Verdict macht es damit ungültig.
   6. Erst dann ruft Monarch selbst `PUT /repos/{o}/{r}/pulls/{n}/merge` auf,
      mit `merge_method: squash` und `sha` = geprüfter Head-SHA. GitHub weist
      den Merge ab, falls sich der Head inzwischen bewegt hat. Monarch schreibt
      `GateMerged` mit dem Merge-Commit; dieser ist die Evidence für `Clear`.
4. Der Hook verweigert `gh api …/merge`, Monarchs eigener HTTP-Aufruf ist davon
   nicht betroffen. Der Agent kann den Merge also nur über die Prüfkette
   auslösen.

## Grenzen (bewusst)

- Ein Agent mit Bash kann die GitHub-API auch direkt per `curl`, `python` oder
  Ähnlichem ansprechen. Der Hook erkennt nur bekannte Formen, eine
  Netzwerk-Policy außerhalb des Agent-Prozesses schließt diese Lücke
  (ADR-0011, G4; OpenShell-Vorschlag folgt nach dem MVP-Gate).
- Das Warden-Verdict wird vom Main eingetragen. Der Ledger-Nachweis des
  Warden-Laufs (SubagentStop) macht ein frei erfundenes Verdict sichtbar, aber
  nicht unmöglich. Die Prüfung des Owners beim Merge nach `main` bleibt die
  letzte Instanz.
- Die Regel greift nur, wenn der Hook das Projekt findet (seit Q-0117/Q-0122 auch
  über Zielpfade, relative Pfade und `cd`/`git -C` im Kommando). Ohne erwecktes
  Projekt gibt es keine Policy (ADR-0011).
- Maßstab für Umgehungen ist ADR-0011: plausible Schreibweisen von `git push`,
  `gh pr merge` und `gh api` sind Klasse 1 (Blocker), Verschleierung ist Klasse 2.

## Folgen

- `deny_bash` behält `git push --force*` als allgemeine Sperre.
  `protect_branches` ist eine eigene Regel mit eigener Begründung im Ledger
  (`PolicyDecided rule=protect_branches`).
- Latenz: Es kommt höchstens ein zusätzlicher Dateizugriff (`HEAD`) für
  `git push` ohne Refspec hinzu. Das p99-Gate (≤ 20 ms) bleibt.
