---
id: Q-0140
gate: G-0105
kind: main
rank: B
state: in_review
dod:
  - Design zuerst (einfache Sprache, im PR): kurzer Nachtrag in docs/adr/0007-merge-policy-nach-zielbranch.md (:86-87 merge_method: squash): git.merge_method je Repo, nur squash oder merge (Owner-Vorgabe, kein rebase), Default aus der Historie, sha-Bindung bleibt, develop nach main bleibt Owner-Merge-Commit (nicht Monarchs Sache)
  - Config: git.merge_method squash|merge in SystemConfig.cs (Record :9-13, Reader :72-92, Validierung wie :96-98); anderer Wert -> Config-Fehler; Tests in ConfigTests.cs
  - Default aus der Historie: die first-parent-Linie des Integrations-Branches (origin, sonst lokal; letzte 50 Commits, überwiegend Merge-Commits mit zwei Eltern -> merge, sonst squash; Branch fehlt oder Historie nicht lesbar -> squash; nie rebase). awaken schreibt das Ergebnis als git-Sektion ins Template (ab AwakenCommand.cs:100) und gibt Ableitung plus vom Owner bestätigen aus; fehlt der Schlüssel in einem bestehenden Repo, leitet gate merge die Methode zur Merge-Zeit genauso ab und nennt die Quelle; Tests mit echtem git-Repo: --no-ff-Merges -> merge, linear -> squash, ohne develop -> squash
  - gate merge: PUT .../merge mit der aufgelösten merge_method und sha = geprüfter Head (GitHubPulls.cs:78-95, Methode als Parameter statt fest squash); GateMerged.method (GateMergeCommands.cs:167) und Ausgabe (:179) nennen die tatsächliche Methode; Squash-Test GateMergeTests.cs:59-93 unverändert grün; neuer Test für merge: PUT-Body und GateMerged.method
  - Doku: plugin/skills/gate/SKILL.md:13 (dann Squash), plugin/README.md:68, Hilfetext MonarchApp.cs:143 (Squash-merge ...); TDD vorher rot, danach grün; dotnet build/test -c Release grün; kein Test gelockert; Warden-Runde über ADR-Nachtrag und Code mit Verdict am Head-SHA
  - Abhängigkeit: nach Q-0137 (GateMergeCommands.cs, GateMergeTests.cs, ADR-0007) und Q-0139 (AwakenCommand.cs)
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/MonarchApp.cs", "src/Monarch.Cli/MergeMethod*.cs", "src/Monarch.Infrastructure.Evidence/GitHubPulls.cs", "tests/Monarch.Cli.Tests/GateMerge*.cs", "tests/Monarch.Cli.Tests/Part2Support.cs", "tests/Monarch.Cli.Tests/ConfigTests.cs", "tests/Monarch.Cli.Tests/CommandTests.cs", "tests/Monarch.Cli.Tests/MergeMethod*.cs", "tests/Monarch.Cli.Tests/TestSupport.cs", "docs/adr/0007-merge-policy-nach-zielbranch.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: ["src/Monarch.Infrastructure.Ledger/**", "src/Monarch.Domain/LedgerEvents.cs"]
estimate: { session_pct: 14, weekly_pct: 2, gold_usd: 0 }
boss: "GateMergeTests: git.merge_method merge -> PUT mit merge_method merge und sha = geprüfter Head, GateMerged.method = merge; ohne Schlüssel und mit --no-ff-Historie ebenfalls merge, mit linearer Historie squash; awaken in einem Repo mit --no-ff-Merges auf develop schreibt merge_method: merge"
board: { pr: 57 }
---
# gate merge: Merge-Methode je Repo, Default aus der Historie (#48)
