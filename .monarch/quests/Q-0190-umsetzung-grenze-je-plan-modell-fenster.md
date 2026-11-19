---
id: Q-0190
gate: G-0129
kind: main
rank: A
state: proposed
dod:
  - "Ein Schritt läuft, wenn aktuell + Schritt <= Arbeitsgrenze im 5h- und 7d-Fenster; die Reserve wird nicht noch einmal abgezogen (der Fall vom 19.11.: 80 + 8 bei Grenze 90 ergibt Proceed)"
  - "Grenzen je Plan, Modell und Fenster wie im Owner-Entscheid; Fable wie Opus, unbekanntes Modell Opus, unbekannter Plan Pro; Plan-Aliase max5 = Max, max20 = Ultra"
  - "Ultra ohne 5h-Fenster: nur das Wochenfenster wird geprüft; Pro und Max ohne 5h bleiben veralteter Sensor"
  - "Messung senkt die Grenze, wenn Recap plus Überlauf nicht in die Reserve passt, hebt sie nie über den eingestellten Wert"
  - "mana --estimate zeigt Grenze, Reserve und Herkunft"
  - "Tests je Punkt vorher rot, danach grün; alte Tests mit der alten Semantik werden einzeln auf den Owner-Entscheid angepasst und im PR aufgelistet"
  - "Schlussrunde des Wardens mit Verdict"
scope:
  include: ["src/Monarch.Domain/Mana.cs", "src/Monarch.Policy/ManaPolicy.cs", "src/Monarch.Cli/Support.cs", "src/Monarch.Cli/SystemConfig.cs", "src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/ManaCommands.cs", "src/Monarch.Cli/QuestCommands.cs", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/State.cs", "tests/**"]
  exclude: []
estimate: { session_pct: 14, weekly_pct: 3, gold_usd: 0 }
boss: "dotnet test grün, gate check grün"
---
# Umsetzung: Grenze je (Plan, Modell, Fenster), kein doppelter Abzug, Plan-Aliase, Ultra ohne 5h
