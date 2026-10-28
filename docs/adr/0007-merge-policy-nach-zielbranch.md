# ADR-0007 — Merge-Policy nach Zielbranch: Integration ja, geschützte Branches nie

**Datum:** 28.10.2026 · **Status:** angenommen (Design-Review Warden G-0101, Auflagen eingearbeitet) · **Quest:** Q-0113 (G-0101)
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
     mit `.git`-Datei werden aufgelöst (`gitdir:`, `commondir`). Zusätzlich liest
     die Regel per Dateizugriff aus der Config: den Upstream
     (`branch.<cur>.merge`), `remote.<r>.push`, `remote.<r>.mirror` und
     `push.default`. Ist das Ziel geschützt, gilt `deny`. Ist das Git-Verzeichnis
     vorhanden, aber `HEAD` (detached) oder die Config nicht lesbar, entscheidet
     die Regel `ask`, nicht `allow`. Wo gar kein Git-Verzeichnis zu finden ist,
     scheitert `git push` selbst; dort greift die Regel nicht.
   - **Refspec-Semantik (B1):** Ein Refspec ohne `:` gilt als `<src>:<src>`.
     `HEAD` und `@` stehen für den aktuellen Branch, wie bei einem Push ohne
     Refspec. Das Ziel wird nach Entfernen von `+` und `refs/heads/` exakt und
     case-insensitiv gegen die geschützten Branches verglichen, nie per
     Substring oder Glob über den Kommandotext. Wildcard-Refspecs
     (`refs/heads/*:refs/heads/*`) zählen als Treffer, wenn ihr Ziel-Muster einen
     geschützten Branch trifft. `--all`, `--branches` und `--mirror` verweigert
     die Regel immer. `git -C <repo>`, `--git-dir`, `GIT_DIR=` und Worktrees
     bestimmen, welches Git-Verzeichnis gelesen wird.
   - **Integrations-Branch (T4):** Ein direkter `git push` auf den
     `integration_branch` führt zu `ask`. Gemerged wird über `monarch gate merge`.
   - `gh pr merge` in jeder Form. Gemerged wird nur über `monarch gate merge`.
   - `gh api` auf `…/pulls/<n>/merge`, `…/merges` und `…/git/refs/heads/<geschützt>`
     (außer explizitem GET), auch mit `{owner}`-Platzhaltern, als volle URL und
     mit `--method`/`-X`, sowie GraphQL mit `mergePullRequest`,
     `enablePullRequestAutoMerge`, `enqueuePullRequest` oder `mergeBranch`, auch
     in Heredoc und `$(…)`.
   - `monarch gate merge` und `monarch gate verdict` verweigert die Regel jedem
     Shadow. Beide führt nur der Main aus.
   - `git push` mit `--no-verify` gegen geschützte Branches ist schon durch den
     ersten Punkt abgedeckt. Lokale `git merge`/`git rebase` bleiben erlaubt,
     denn ohne Push ändern sie nichts auf der Forge.
3. **`monarch gate merge <G>`** ist der einzige Merge-Weg. Er prüft in dieser
   Reihenfolge und bricht beim ersten Fehler mit Begründung ab:
   0. Vorher läuft `ledger verify`; Befunde brechen ab.
   1. Das Gate ist offen und hat genau einen verlinkten PR (aus `quest pr` der
      Quests des Gates). Kein PR oder mehrere verschiedene PRs verweigern.
   2. Die Base des PR ist `integration_branch`, nie ein geschützter Branch.
   3. Rank-Regel: Rank = Maximum aus Gate und seinen Quests. Rank S verweigert
      immer (Owner-Aktion). Rank E–A ist erlaubt (T2).
   4. Die CI ist grün (B2). Die Prüfung nutzt dieselbe Funktion wie
      `quest verify`: Nur `success` zählt, je Check-Name zählt der jüngste Lauf,
      `evidence.required` aus `system.yaml` wird berücksichtigt (Port
      `IEvidenceSource`, `EvidenceCommands`). `neutral` und `skipped` sind nicht
      grün. Läuft noch ein Check oder fehlen Check-Runs, wird verweigert.
   5. Ein Warden-Verdict liegt als Evidence vor. `monarch gate verdict <G>
      --verdict approve|changes --source <url>` schreibt `WardenVerdictRecorded`
      mit Head-SHA. Gültig ist nur das jüngste Verdict zum aktuellen Head-SHA,
      und es muss `approve` sein. Zusätzlich braucht es ein `ShadowRunCompleted`
      (SubagentStop) mit `agent_type` exakt `monarch:warden` oder `warden` (T1).
      Der Lauf liegt nach der Commit-Zeit des Head-SHA und nach dem vorigen
      Verdict desselben Gates, und er liegt vor dem Verdict, das er deckt. Ein
      Lauf deckt genau ein Verdict. Ein Push nach dem Verdict macht es damit
      ungültig.
   6. Erst dann ruft Monarch selbst `PUT /repos/{o}/{r}/pulls/{n}/merge` auf,
      mit `merge_method: squash` und `sha` = geprüfter Head-SHA. GitHub weist
      den Merge ab, falls sich der Head inzwischen bewegt hat. Danach liest
      Monarch den PR erneut (Base, `merged`, `merge_commit_sha`) und schreibt
      `GateMerged` mit Merge-Commit und Base. Liegt die Base nicht auf dem
      Integrations-Branch, meldet Monarch das als Fehler.
   7. **Clear-Evidence (T3)** bleibt der Forge-Merge (`GetMergeAsync`).
      `GateMerged` dient dem Abgleich von `merge_commit_sha` und Base. Ein Merge
      ohne `GateMerged` (oder mit abweichendem Merge-Commit) wird als Signal
      gemeldet: `quest clear` und `ledger verify` geben einen Hinweis aus.
4. Der Hook verweigert `gh api …/merge`, Monarchs eigener HTTP-Aufruf ist davon
   nicht betroffen. Der Agent kann den Merge also nur über die Prüfkette
   auslösen.

## Löst ab (T2)

- `src/Monarch.Policy/RankPolicy.cs` (`RankGovernance.For`): `OwnerApprovalRequired`
  für Rank B–A entfällt. Ein Merge in den Integrations-Branch ist für E–A
  erlaubt, nur Rank S bleibt beim Owner. Merge nach `main`/`master` bleibt in
  jedem Rank dem Owner vorbehalten.
- Die Grundregel „B–A: PR öffnen, niemals selbst mergen“ in den Skills
  (`plugin/skills/**`). Neuer Text: „Merge nur nach `develop` über
  `monarch gate merge`, nie nach main/master; Rank S fragt jede mutierende
  Aktion einzeln.“

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
- `gh api graphql -F query=@datei` liest die Mutation aus einer Datei; der Hook
  sieht sie nicht (Klasse 2). Das gilt auch für andere Wege (`curl`, Skripte).
  Die Grenze steht in `plugin/README.md` unter „Grenzen“.
- Maßstab für Umgehungen ist ADR-0011: plausible Schreibweisen von `git push`,
  `gh pr merge` und `gh api` sind Klasse 1 (Blocker), Verschleierung ist Klasse 2.

## Folgen

- `deny_bash` behält `git push --force*` als allgemeine Sperre.
  `protect_branches` ist eine eigene Regel mit eigener Begründung im Ledger
  (`PolicyDecided rule=protect_branches`).
- Latenz: Es kommt höchstens ein zusätzlicher Dateizugriff (`HEAD`) für
  `git push` ohne Refspec hinzu. Das p99-Gate (≤ 20 ms) bleibt.
