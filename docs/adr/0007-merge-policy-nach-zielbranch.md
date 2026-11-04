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
   - `integration_branch`: Default `develop`. Liegt er in `protected_branches`
     (Defaults oder Datei, ohne Groß-/Kleinschreibung), ist die Config ungültig
     (Q-0130); `gate merge` verweigert dann mit Begründung.
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
   2. Die Base des PR ist `integration_branch`, nie ein geschützter Branch
      (geprüft vor dem Merge und nach dem Merge gegen die frisch gelesene Base).
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

## Q-0132: develop vorher und nachher grün

Ein Merge auf einen roten Integrations-Branch verdeckt, wer ihn gebrochen hat.
`monarch gate merge` prüft deshalb den Integrations-Branch selbst.

- **Vorher.** Nach der PR-CI (Schritt 5) liest Monarch den Head des
  Integrations-Branches über die GitHub-API (`GET …/git/ref/heads/{branch}`,
  `GitHubPulls.GetBranchHeadAsync`) und wendet auf diesen SHA dieselbe
  Funktion an wie `quest verify` (`EvidenceCommands.CheckCi`). Nur `success`
  zählt. Rot, abgebrochen, ausstehend oder fehlend gilt als nicht grün und
  verweigert den Merge mit Begründung, bevor das PUT passiert. Läuft noch ein
  Check oder fehlt der Lauf, lautet die Begründung „develop läuft noch“.
- **Nachher.** Nach dem erfolgreichen Merge fragt Monarch dieselbe Funktion
  für den Merge-Commit ab, bis sie entscheidet oder die Wartezeit abläuft
  (Default 90 s seit Q-0137, Polling 30 s; injizierbar über
  `MONARCH_MERGE_WAIT_SECONDS` und `MONARCH_MERGE_POLL_SECONDS`, damit Tests
  nicht warten). Ergebnis als Ledger-Event `IntegrationRunChecked`
  (`gate`, `sha`, `branch`, `result`):
  - `green`: Exit 0.
  - `red` (rot; abgebrochen siehe Q-0137): Ausgabe „develop ist ROT nach dem Merge –
    zuerst klären“, Exit ≠ 0.
  - `pending` (Timeout): Hinweis, die GitHub-Checks des Merge-Commits
    anzusehen, Exit ≠ 0.
  Der Merge selbst bleibt in jedem Fall bestehen und `GateMerged` ist
  geschrieben; der Exit-Code ist nur das Signal, dass jemand hinsehen muss.
- **Grenze.** Die Prüfung kennt nur die Checks, die GitHub am SHA zeigt
  (`evidence.required` aus `system.yaml` schränkt sie ein, wie bei
  `quest verify`). Ein Branch ganz ohne CI wird als „nicht grün“ behandelt;
  wer das will, braucht eine CI auf dem Integrations-Branch.

## Q-0137 (G-0105): Roter develop, abgebrochene Läufe, Laufzeit

**Datum:** 04.11.2026 · **Status:** Entwurf (Design-Review Warden ausstehend) · **Issues:** 34, 33

Dieser Nachtrag ändert den Abschnitt Q-0132 an den drei unten genannten Stellen.

### Kurz in einfacher Sprache

- **Roter develop:** Bisher blockierte ein roter develop jeden Merge, auch den PR, der ihn repariert. Künftig geht ein Fix-PR automatisch durch, wenn die Forge belegt, dass er den Fehler behebt: Er baut auf dem aktuellen develop auf, alle seine Checks sind grün, und genau die auf develop roten Checks sind bei ihm grün. Ein Schalter oder eine eigene Aussage reicht dafür nicht.
- **Abgebrochener Lauf:** Abgebrochen ist nicht rot, sondern „kein Beleg“. Vor dem Merge bleibt das ein Stopp, aber die Meldung sagt jetzt, welcher Lauf abgebrochen ist und wie man ihn neu startet. Nach dem Merge steht „abgebrochen“ statt „ROT“.
- **Laufzeit:** `gate merge` wartet höchstens 90 Sekunden auf den develop-Lauf und endet damit innerhalb des Bash-Timeouts von 2 Minuten. Läuft die CI noch, meldet der Befehl das und nennt `monarch gate check <G-xxxx>`. Damit prüft man später kurz nach.

### 1. Vorher: Entscheidung am develop-Head (ändert Schritt „Vorher“)

Schritt 5 bleibt: Die PR-CI muss grün sein. Danach wertet Schritt 5b `CheckCi` am develop-Head so aus:

| Ergebnis am develop-Head | Entscheidung |
|---|---|
| Alle relevanten Checks `success` | weiter wie bisher |
| Ein Check läuft oder fehlt | Verweigerung „develop läuft noch“, unverändert |
| Mindestens ein nicht grüner Check ist `cancelled` | Stopp, kein PUT; Meldung nennt Check und Lauf-ID und den Befehl `gh run rerun <Lauf-ID>` |
| Sonst (nicht grün, nicht abgebrochen) | Prüfung „belegter Fix-PR“ |

`cancelled` hat Vorrang vor rot: Ohne Beleg lässt sich nicht sagen, ob der PR alles behebt. `failure`, `timed_out`, `skipped`, `neutral` und `action_required` zählen hier als rot. `IsSuccess` bleibt strikt (`IEvidenceSource.cs:19`); neu ist nur `IsCancelled` für die Unterscheidung.

**Belegter Fix-PR (Owner-Entscheid V1 vom 30.10.).** Der Merge läuft trotz rotem develop nur, wenn alle drei Belege vorliegen:

1. Der develop-Head ist Vorfahr des PR-Heads: `GET repos/{o}/{r}/compare/{develop-Head}...{PR-Head}` meldet `ahead` oder `identical`. Ein Fehler der Forge ist kein Beleg und verweigert.
2. Alle Checks am PR-Head sind abgeschlossen und grün (Schritt 5).
3. Jeder am develop-Head nicht grüne Check existiert am PR-Head unter demselben Namen und ist dort grün. Ein Check, der nur beim Push läuft, fehlt am PR-Head und verweigert deshalb.

**Warum das reicht.** Enthält der PR-Head den develop-Head, ist der Baum des Merges der getestete Baum des PR-Heads. Die Checks am PR-Head belegen also den Stand, den develop nach dem Merge hat. Das gilt für Squash und für Merge-Commit.

**develop darf sich nicht bewegen.** Die `sha`-Bindung sichert nur den PR-Head. Direkt vor dem PUT liest Monarch im Fix-PR-Pfad den develop-Head erneut; hat er sich geändert, verweigert der Befehl ohne PUT.

**Nachweis.** `GateMerged` erhält in diesem Pfad zwei zusätzliche Felder: `overridden` (Namen der überstimmten Checks, kommagetrennt) und `integration_head` (der rote develop-SHA). Die Ausgabe nennt beide. Der grüne Pfad schreibt `GateMerged` unverändert.

### 2. Nachher: Ergebnis `cancelled` (ändert „red (rot oder abgebrochen)“)

`IntegrationRunChecked.result` kennt jetzt `green`, `red`, `cancelled`, `pending`. `cancelled` gilt, wenn alle nicht grünen Checks abgebrochen sind; ist einer davon wirklich rot, bleibt es `red`. Die Ausgabe sagt „abgebrochen, kein Beleg“ mit Lauf-ID und Rerun-Befehl. `cancelled` und `pending` enden mit Exit ≠ 0, wie `red`; der Merge bleibt bestehen.

### 3. Laufzeit und Nachprüfung (ändert „Default 15 min“)

- `DefaultWaitSeconds` ist 90, Polling bleibt 30 s. `MONARCH_MERGE_WAIT_SECONDS` und `MONARCH_MERGE_POLL_SECONDS` bleiben.
- Neuer Befehl `monarch gate check <G-xxxx>`: liest das letzte `GateMerged` des Gates, prüft den Lauf auf `merge_commit_sha` mit derselben Funktion und Wartezeit und hängt `IntegrationRunChecked` an. Exit wie bei `gate merge`. Ohne `GateMerged`: Fehler, kein Ledger-Eintrag. Der Befehl braucht das Ledger, in dem `GateMerged` steht; er läuft also vor dem Aufräumen der Worktree.
- Nur der Main: Die Hook-Regel (`ProtectBranchesRule.cs:49`) und die Sperre im Binary bei `MONARCH_AGENT_TYPE` gelten auch für `gate check`, weil er ins Ledger schreibt.

### Warum keine Schutzregel fällt

Der Zweck der Regel ist, dass kein Merge einen Bruch verdeckt. Bei einem belegten Fix-PR wird nichts verdeckt: Der Stand nach dem Merge ist getestet, und `GateMerged` hält fest, was überstimmt wurde. Die Lockerung hat der Owner entschieden; sie gilt nur mit allen drei Belegen. Verdict, Rank-Prüfung, `sha`-Bindung und `ledger verify` bleiben unverändert.

**Grenze.** Ein PR könnte einen Check gleichen Namens inhaltlich aushöhlen. Das fängt der Fix-PR-Pfad nicht ab. Dagegen stehen der Warden und der Risk-Tag `infra` für `.github/workflows/**`.

### Teststrategie

`GateMergeTests.cs`, vorher rot:

- Fix-PR gemergt: PUT mit `sha`, `overridden` und `integration_head` in `GateMerged`.
- Kein PUT je fehlendem Beleg: develop nicht Vorfahr (`diverged`, `behind`), Compare-Fehler, Check am PR-Head fehlt, Check am PR-Head rot, develop bewegt sich vor dem PUT.
- `cancelled` am develop-Head, auch gemischt mit rot: kein PUT, Meldung mit Lauf-ID.
- Nach dem Merge: `cancelled`, gemischt `red`, Exit ≠ 0.
- Default-Wartezeit 90 s; `pending` nennt `gate check`.
- `gate check`: trägt `green` nach, ohne `GateMerged` kein Eintrag, Shadow verweigert (Binary und `ProtectBranchesTests.cs`).

Bestehende Tests bleiben unverändert, mit genau einer Ausnahme:

- `GateMergeTests.cs:539` (`[InlineData("cancelled")]` in `A_red_integration_branch_afterwards_is_loud_and_keeps_the_merge`) schreibt das Verhalten fest, das der Owner geändert hat: `result red` und „ROT“ bei einem abgebrochenen Lauf. Diese eine Zeile entfällt, ein neuer Test `A_cancelled_integration_run_afterwards_…` übernimmt sie mit derselben Strenge: Exit ≠ 0, genau ein `GateMerged`, genau ein PUT, dazu `result cancelled`.
- Die Zeile mit `failure` bleibt unverändert. Vor dem Merge behält die Meldung den Text `not green`, damit `GateMergeTests.cs:487` unverändert gilt.
