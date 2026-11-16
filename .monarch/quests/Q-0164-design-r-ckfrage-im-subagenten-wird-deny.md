---
id: Q-0164
gate: G-0118
kind: main
rank: E
state: in_progress
dod:
  - Nachtrag zu ADR-0003 in einfacher Sprache (Kopf, Entscheidung 1-3 und Folgen bleiben stehen): Liste aller Ask-Quellen mit Fundstelle (ToolCallPolicy.cs:496 rank_s_approval; ProtectBranchesRule.cs:33, :142, :189; ProtectPathsRule.cs:545 Regel 5; HookRunner.cs:64, :263, :330 hook_error) und je Quelle: Main bleibt ask mit unverändertem Text, Subagent deny mit Ausweg-Satz (was der Subagent stattdessen tut, z. B. stoppen und der Hauptsession melden) oder begründet ask
  - Der Nachtrag beantwortet: Subagent-Kriterium agent_id (ADR-0001:28-29) oder agent_type, inklusive Main mit --agent (agent_type ohne agent_id, ADR-0001:22-23) und agent_id ohne agent_type; ob Hook-Fehler-Asks im Subagenten deny werden; unlesbarer Input ohne Identität (HookTests.cs:172-179) bleibt ask; Ort der Umwandlung (Policy oder HookRunner) und Verhalten bei mehreren Projekten (Combine ToolCallPolicy.cs:298-310, HookRunner.cs:188-197); Feldname und Werte in PolicyDecided (HookRunner.cs:207-215, :334-335), die eine umgewandelte Rückfrage kennzeichnen; Verhältnis zu ADR-0003 Entscheidung 1 (Zeilen 24-26, kein eigener Headless-Erkenner)
  - Keine Lockerung, im Nachtrag ausdrücklich: ask wird nur im Subagenten zu deny (Verschärfung), im Main bleibt ask, kein Pfad wird allow; Hook-Fehler bleiben nie stilles allow (ADR-0003:34-35). Owner-Entscheide nur wörtlich (CLAUDE.md Regel 4), alles Offene als OFFEN oder Vorschlag markiert
  - Warden-Designrunde über den Nachtrag (eine Runde, höchstens eine Delta-Runde), Verdict im PR verlinkt; Tests erst danach
  - tests/Monarch.Cli.Tests/HookSubagentAskTests.cs (Hook pre-tool-use mit TempProject) nach dem freigegebenen Nachtrag: je Ask-Quelle ein Fall Main (ask, Text unverändert) und ein Fall Subagent (Erwartung laut Nachtrag: deny mit Ausweg-Satz im Grund, PolicyDecided mit kind deny und dem Kennzeichen-Feld); Grenzfälle agent_id ohne agent_type und agent_type ohne agent_id; Policy-Fälle in tests/Monarch.Policy.Tests/SubagentAskTests.cs nur, wenn der Nachtrag die Umwandlung in die Policy legt. Fälle mit neuer Erwartung sind rot, Main-Fälle grün; dotnet test-Ausgabe im PR
  - tests/Monarch.Policy.Tests/CloneIntoMonarchDirTests.cs (ToolCallPolicy.Evaluate, Root /work/proj, Rank A wie TestRepoWaysTests.cs) stellt Klon mit Ziel unter .monarch/ nach: die Fälle aus Issue 119 (Wortlaut im PR zitiert), mindestens ein relatives und ein absolutes Ziel unter .monarch/ außerhalb der protect_paths-Liste (.monarch/system.yaml:24); Assert auf das heutige Verhalten, grün ohne src-Änderung, kein Fix; Klassenkommentar beschreibt nur das heutige Verhalten und nennt Issue 119; PR sagt, ob sich die Lücke bestätigt (Label Schutzlücke setzt die Hauptsession)
  - Policy-Fälle nur als Tests im Repo im Quest-Scope; kein src-, Hook-, Skript-, Bench- oder CI-Diff: git diff origin/develop --stat -- src .github bench plugin ist leer; keine bestehende Testdatei geändert
scope:
  include: ["docs/adr/0003-ask-im-headless-modus.md", "tests/Monarch.Cli.Tests/HookSubagentAskTests.cs", "tests/Monarch.Policy.Tests/SubagentAskTests.cs", "tests/Monarch.Policy.Tests/CloneIntoMonarchDirTests.cs"]
  exclude: ["src/**", ".github/**", "bench/**", "plugin/**"]
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "Warden-Designrunde approve über den Nachtrag; Subagent-Fälle mit neuer Erwartung rot, Main-Fälle und Klon-Test grün (dotnet test-Ausgabe im PR)"
---
# Design: Rückfrage im Subagenten wird Deny mit Ausweg (Nachtrag ADR-0003), rote Tests, Klon unter .monarch nachgestellt (Issue 119)
