---
id: Q-0137
gate: G-0105
kind: main
rank: A
state: proposed
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0007-merge-policy-nach-zielbranch.md (Abschnitt :138-175) zu (a) rotem develop mit drei Varianten – V0 nur Ausweg dokumentieren (Owner-Merge oder Re-run, wie
  - Nur bei Owner-Wahl V1/V2, belegter Fix-PR: Merge trotz rotem develop nur, wenn (1) der develop-Head Vorfahr des PR-Heads ist (Forge-Compare: neue Methode in GitHubPulls.cs, Compare-Route im FakeForge Part2Support.cs), (2) alle Checks am PR-Head abgeschlossen und grün sind (CheckCi wie heute, GateMergeCommands.cs:134) und (3) jeder am develop-Head nicht grüne Check gleichen Namens am PR-Head existiert und grün ist; GateMerged hält überstimmte Checks und develop-SHA fest; je fehlendem Beleg ein Refused-Test ohne PUT (develop nicht Vorfahr, Check am PR-Head fehlt, Check am PR-Head rot, bei V2 Schalter fehlt); 'develop läuft noch' bleibt verweigert (GateMergeTests.cs:505-518 unverändert). Bei V0: Verweigerungstext nennt den Ausweg (Owner-Merge oder Re-run), Test
  - 
  - 
  - 
  - Nachprüf-Befehl (Name im ADR): liest GateMerged des Gates, prüft den Lauf auf merge_commit_sha (Wartezeit <= 90 s) und hängt IntegrationRunChecked (green/red/cancelled/pending) an; ohne GateMerged Fehler ohne Ledger-Eintrag; nur Main: Hook-Regel ProtectBranchesRule.cs:49-50 und Binary-Sperre bei MONARCH_AGENT_TYPE, Tests analog GateMergeTests.cs:426-433 und ProtectBranchesTests.cs
  - TDD: neue Tests vorher rot, danach grün; dotnet build/test -c Release grün; kein Test gelöscht oder gelockert; Doku nachgezogen: plugin/skills/gate/SKILL.md:13, plugin/README.md:68, Hilfetext MonarchApp.cs:143; Warden-Runde über den Code mit Verdict am Head-SHA
  - Abhängigkeit: keine (erste Quest des Gates)
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/EvidenceCommands.cs", "src/Monarch.Cli/MonarchApp.cs", "src/Monarch.Infrastructure.Evidence/GitHubPulls.cs", "src/Monarch.Infrastructure.Evidence/GitHubChecksEvidence.cs", "src/Monarch.Infrastructure.Evidence/GitHubJson.cs", "src/Monarch.Application/Ports/IEvidenceSource.cs", "src/Monarch.Policy/ProtectBranchesRule.cs", "tests/Monarch.Cli.Tests/GateMerge*.cs", "tests/Monarch.Cli.Tests/Part2Support.cs", "tests/Monarch.Infrastructure.Tests/FakeGitHub.cs", "tests/Monarch.Infrastructure.Tests/GitHub*Tests.cs", "tests/Monarch.Policy.Tests/ProtectBranchesTests.cs", "docs/adr/0007-merge-policy-nach-zielbranch.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: ["src/Monarch.Infrastructure.Ledger/**", "src/Monarch.Domain/LedgerEvents.cs"]
estimate: { session_pct: 28, weekly_pct: 4, gold_usd: 0 }
boss: "GateMergeTests: develop-Head mit rotem 'test', PR-Head enthält den develop-Head und 'test' ist dort grün -> Merge mit sha-Bindung und überstimmten Checks im GateMerged (bei Owner-Wahl V0: Verweigerung, die den Ausweg nennt); develop rot ohne diese Belege -> kein PUT; Merge-Commit-Lauf cancelled -> IntegrationRunChecked cancelled und Exit ungleich 0; pending -> Exit ungleich 0, danach trägt der Nachprüf-Befehl green nach"
---
# gate merge: roter develop, abgebrochen ist unbelegt, Laufzeit unter 2 min mit Nachprüfung (#34, #33)
