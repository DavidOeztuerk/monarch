---
id: Q-0137
gate: G-0105
kind: main
rank: A
state: cleared
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0007-merge-policy-nach-zielbranch.md (Abschnitt :138-175) zu (a) rotem develop nach Owner-Entscheid V1: ein belegter Fix-PR wird automatisch gemergt, (b) cancelled ist nicht rot, sondern unbelegt, (c) Laufzeit unter 120 s und Nachprüf-Befehl; eine Warden-Runde, höchstens eine Delta-Runde, Verdict vor dem ersten Code-Commit
  - Belegter Fix-PR (Owner-Entscheid V1, automatisch, kein Schalter, keine Selbstauskunft): Merge trotz rotem develop nur, wenn (1) der develop-Head Vorfahr des PR-Heads ist (Forge-Compare: neue Methode in GitHubPulls.cs, Compare-Route im FakeForge Part2Support.cs), (2) alle Checks am PR-Head abgeschlossen und grün sind (CheckCi wie heute, GateMergeCommands.cs:134) und (3) jeder am develop-Head rote Check gleichen Namens am PR-Head existiert und grün ist; GateMerged hält überstimmte Checks und develop-SHA fest; je fehlendem Beleg ein Refused-Test ohne PUT (develop nicht Vorfahr, Check am PR-Head fehlt, Check am PR-Head rot); develop läuft noch bleibt verweigert (GateMergeTests.cs:505-518 unverändert)
  - cancelled vor dem Merge: Ein abgebrochener Lauf am develop-Head ist nicht rot, sondern unbelegt, und bleibt ein Stopp; die Meldung nennt Lauf-ID und Rerun-Hinweis statt der Rot-Meldung; IsSuccess bleibt strikt (IEvidenceSource.cs:19); Test ohne PUT
  - cancelled nach dem Merge: IntegrationRunChecked trägt result cancelled statt red, die Ausgabe sagt abgebrochen statt ROT, Exit ungleich 0, der Merge bleibt; Test
  - Laufzeit: Default-Wartezeit nach dem Merge höchstens 90 s, gate merge endet im Default unter 120 s; pending bleibt Exit ungleich 0 und nennt den Nachprüf-Befehl; die Env-Variablen MONARCH_MERGE_WAIT_SECONDS und MONARCH_MERGE_POLL_SECONDS bleiben; Test
  - Nachprüf-Befehl (Name im ADR): liest GateMerged des Gates, prüft den Lauf auf merge_commit_sha (Wartezeit <= 90 s) und hängt IntegrationRunChecked (green/red/cancelled/pending) an; ohne GateMerged Fehler ohne Ledger-Eintrag; nur Main: Hook-Regel ProtectBranchesRule.cs:49-50 und Binary-Sperre bei MONARCH_AGENT_TYPE, Tests analog GateMergeTests.cs:426-433 und ProtectBranchesTests.cs
  - TDD: neue Tests vorher rot, danach grün; dotnet build/test -c Release grün; kein Test gelöscht oder gelockert; Doku nachgezogen: plugin/skills/gate/SKILL.md:13, plugin/README.md:68, Hilfetext MonarchApp.cs:143; Warden-Runde über den Code mit Verdict am Head-SHA
  - Abhängigkeit: nach Q-0142 (Owner-Reihenfolge)
scope:
  include: ["src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/EvidenceCommands.cs", "src/Monarch.Cli/MonarchApp.cs", "src/Monarch.Infrastructure.Evidence/GitHubPulls.cs", "src/Monarch.Infrastructure.Evidence/GitHubChecksEvidence.cs", "src/Monarch.Infrastructure.Evidence/GitHubJson.cs", "src/Monarch.Application/Ports/IEvidenceSource.cs", "src/Monarch.Policy/ProtectBranchesRule.cs", "tests/Monarch.Cli.Tests/GateMerge*.cs", "tests/Monarch.Cli.Tests/Part2Support.cs", "tests/Monarch.Infrastructure.Tests/FakeGitHub.cs", "tests/Monarch.Infrastructure.Tests/GitHub*Tests.cs", "tests/Monarch.Policy.Tests/ProtectBranchesTests.cs", "docs/adr/0007-merge-policy-nach-zielbranch.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: ["src/Monarch.Infrastructure.Ledger/**", "src/Monarch.Domain/LedgerEvents.cs"]
estimate: { session_pct: 28, weekly_pct: 4, gold_usd: 0 }
boss: "GateMergeTests: develop-Head mit rotem 'test', PR-Head enthält den develop-Head und 'test' ist dort grün -> Merge mit sha-Bindung und überstimmten Checks im GateMerged; develop rot ohne diese Belege -> kein PUT; develop-Head cancelled -> Stopp mit Lauf-ID und Rerun-Hinweis, kein PUT; Merge-Commit-Lauf cancelled -> IntegrationRunChecked cancelled und Exit ungleich 0; pending -> Exit ungleich 0, danach trägt der Nachprüf-Befehl green nach"
board: { pr: 57 }
---
# gate merge: roter develop, abgebrochen ist unbelegt, Laufzeit unter 2 min mit Nachprüfung (#34, #33)
