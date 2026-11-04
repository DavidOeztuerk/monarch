---
id: Q-0142
gate: G-0105
kind: main
rank: A
state: proposed
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0012-mana-nach-modell-und-effort.md mit belegter Ursache: clamp aus system.yaml wird nie gelesen (SystemConfig.cs:76-77), die adaptive Untergrenze ist fest Start minus 15 (Support.cs:216, :242-243), fünf Recaps mit Kosten 0 und p90 der Wochenschritte 8 ergeben 100 - (1,5 * 0 + 8) = 92; Briefing, status und quest accept nutzen config.Limits statt Resolve (HookRunner.cs:383, StatusCommand.cs:42/61/122/124, QuestCommands.cs:89); eine Warden-Runde, höchstens eine Delta-Runde, Verdict vor dem ersten Code-Commit
  - Fix: SystemConfigReader liest clamp je Fenster; die adaptive Grenze und die Plan-Grenze fallen nie unter das clamp-Minimum; AdaptiveFloorBelowStart entfällt; fehlt clamp oder ist es ungültig, ist work_until_pct die Untergrenze; nach oben bleibt die Grenze bei work_until_pct (kein automatisches Anheben, das wäre eine Owner-Entscheidung); Limits aus ConfigTests.cs bleibt unverändert (Rohwert)
  - Ein Wert für alle: mana, rest, recap, status (Text und --json), Briefing und quest accept nutzen dieselbe Auflösung ManaSupport.Resolve
  - Tests vorher rot, danach grün: clamp [96, 99] mit 5 Recaps Kosten 0 und Schritt 8 ergibt 7d 96 statt 92; hohe Recap-Kosten ergeben weiter 96; 5h clamp [90, 97] fällt nie unter 90; rest, mana, status und quest accept nennen in demselben Zustand denselben Wert; work_until_pct unter clamp-Minimum ergibt das clamp-Minimum; fehlendes clamp ergibt work_until_pct
  - Kein Test gelöscht oder gelockert; dotnet build und test -c Release grün; Hook-Latenz unverändert (das Briefing läuft nur bei SessionStart); plugin/README.md nachgezogen, falls es Grenzen nennt; Warden-Runde über den Code mit Verdict am Head-SHA
  - Abhängigkeit: keine (erste Quest des Gates, Owner-Reihenfolge)
scope:
  include: ["src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Cli/Support.cs", "src/Monarch.Cli/QuestCommands.cs", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/ManaCommands.cs", "tests/Monarch.Cli.Tests/ConfigTests.cs", "tests/Monarch.Cli.Tests/Part2Tests.cs", "tests/Monarch.Cli.Tests/CommandTests.cs", "tests/Monarch.Cli.Tests/ManaLimit*.cs", "docs/adr/0012-mana-nach-modell-und-effort.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 10, weekly_pct: 1.5, gold_usd: 0 }
boss: "Test: clamp [96, 99], 5 Recaps mit Kosten 0, p90 Schritt 8 -> Limits in use 7d 96 %; rest, mana, status und quest accept nennen denselben Wert"
---
# Mana: Wochengrenze fällt nie unter clamp, ein Wert für alle Befehle
