---
id: Q-0134
gate: G-0104
kind: main
rank: A
state: abandoned
dod:
  - Design zuerst: Nachtrag in docs/adr/0007-merge-policy-nach-zielbranch.md (Fix-PR-Kriterium, cancelled = unbelegt, Laufzeit und Nachprüfung, git.merge_method mit Default aus der Historie) -> eine Warden-Runde (Design) -> Code
  - Issue 34, roter develop: gate merge merged nur einen belegten Fix-PR, d. h. der PR-Head enthält den aktuellen develop-Head, alle Checks am PR-Head sind grün und genau die am develop-Head roten Checks sind dort grün; GateMerged hält die überstimmten Checks fest. Fehlt ein Beleg, bleibt die Verweigerung ohne PUT; kein Flag und keine Selbstauskunft schaltet das frei
  - Issue 34, abgebrochen: cancelled ist nicht rot; vor dem Merge Verweigerung als unbelegt mit Rerun-Hinweis (Lauf-ID), nach dem Merge IntegrationRunChecked mit result cancelled statt red; CheckEvidence.IsSuccess bleibt strikt (cancelled zählt nie als grün)
  - Issue 33: gate merge endet im Default unter 120 s (Default-Wartezeit <= 90 s, Test); pending nach dem Merge ist Exit 0 mit Hinweis auf einen kurzen Nachprüf-Befehl, der IntegrationRunChecked nachträgt und wie gate merge nur dem Main erlaubt ist
  - Issue 48: git.merge_method squash|merge je Repo (andere Werte -> Config-Fehler); awaken schreibt den Default aus der Historie des Integrations-Branches (Merge-Commits mit zwei Eltern auf der first-parent-Linie -> merge, sonst squash); PUT, GateMerged.method und Ausgabe nennen die tatsächliche Methode; GateMergeTests.cs:64-82 (squash) unverändert grün, neuer Test für merge
  - TDD (Tests vorher rot, danach grün), Warden-Runde über den Code mit Verdict am Head-SHA; plugin/skills/gate/SKILL.md, plugin/README.md:68 und der Hilfetext in MonarchApp.cs:143 nachgezogen; kein Test abgeschwächt
  - Abhängigkeit: Start nach Q-0133 (Owner-Reihenfolge)
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/EvidenceCommands.cs", "src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/MonarchApp.cs", "src/Monarch.Infrastructure.Evidence/**", "src/Monarch.Application/Ports/IEvidenceSource.cs", "src/Monarch.Policy/ProtectBranchesRule.cs", "tests/Monarch.Cli.Tests/**", "tests/Monarch.Infrastructure.Tests/**", "tests/Monarch.Policy.Tests/ProtectBranchesTests.cs", "docs/adr/0007-merge-policy-nach-zielbranch.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "develop rot und PR auf aktuellem develop mit genau diesen Checks grün -> Merge; develop rot ohne diese Belege -> verweigert ohne PUT; Merge-Commit-Lauf cancelled -> cancelled statt red; git.merge_method merge -> PUT mit merge_method merge"
---
# gate merge: belegter Fix-PR, abgebrochen ist unbelegt, kurze Laufzeit, Merge-Methode je Repo (#34, #33, #48)
