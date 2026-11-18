---
id: Q-0167
gate: G-0118
kind: main
rank: E
state: in_progress
dod:
  - Befund 1 der Schlussrunde (major): Ursache des macOS-Fehlers im Integrationsfall Real_ps_finds_the_hull_of_a_real_child_and_the_hook_leaves_it_running (KeyNotFoundException pid, CI-Lauf 44316981093) zuerst belegen: Läuft die ps-Frist ab, schreibt BackgroundShells.cs eine Zeile nach hook-errors.log ohne Inhalt einer Befehlszeile (Befund 2); der Test nennt bei fehlender PID den Inhalt von hook-errors.log und die Dauer in der Assert-Meldung; ein CI-Lauf auf macOS mit diesem Stand belegt die Ursache (Run-ID im PR)
  - Danach die belegte Ursache beheben, ohne die 2-s-Frist und ohne eine andere Schwelle zu ändern (eine Friständerung wäre eine Designänderung und geht an den Owner); kein Test wird aufgeweicht: der Integrationsfall prüft weiter PID der Hülle, Dauer unter 5 s und dass das Kind noch läuft
  - DoD-Lücken aus der Schlussrunde: Quellen 3 und 4 (ProtectBranchesRule.cs Push ohne Refspec bei unlesbarer Config, Push bei detached HEAD) je ein Main- und ein Subagent-Fall in HookSubagentAskTests.cs mit nachgebautem .git wie ProtectBranchesTests.cs:20-22 (Befund 5); ledger verify bleibt ok nach einer umgewandelten Rückfrage und nach BackgroundShellsLeft (Befund 6); die Property prüft für Subagent plus Ask auch Kind Deny (Befund 7)
  - CI grün auf macOS und Linux inklusive aot und Latenz-Gate; dotnet test -c Release gesamt grün; bestehende Testzeilen nicht geändert oder entfernt außer in den beiden in Q-0164 bis Q-0166 neu angelegten Testdateien
scope:
  include: ["src/Monarch.Cli/BackgroundShells.cs", "tests/Monarch.Cli.Tests/HookSubagentStopShellsTests.cs", "tests/Monarch.Cli.Tests/HookSubagentAskTests.cs"]
  exclude: ["src/Monarch.Policy/**", "src/Monarch.Cli/HookRunner.cs", ".github/**", "bench/**", "plugin/**", "docs/**"]
estimate: { session_pct: 12, weekly_pct: 2, gold_usd: 0 }
boss: "CI-Lauf auf macOS belegt die Ursache; danach CI grün auf macOS und Linux inklusive aot und Latenz-Gate; neue Fälle vorher rot oder als DoD-Lücke belegt, danach grün"
---
# Nacharbeit Schlussrunde G-0118: macOS-Integrationsfall und DoD-Lücken der Tests
