---
id: Q-0178
gate: G-0125
kind: side
rank: A
state: cleared
dod:
  - "Veralteter Stand mit vergangenem resets_at: Meldung nennt 'veraltet' und den Weg (interaktive Session öffnen), nicht PauseWithoutRecap allein"
  - "Entscheidung bleibt verweigern; die Frage annehmen-mit-0 vs. verweigern bleibt Owner-Entscheid (in PR benannt)"
  - "Test rot vorher, grün nachher; Test für Stand in der Zukunft unverändert"
scope:
  include: ["src/Monarch.Policy/ManaPolicy.cs", "src/Monarch.Cli/QuestCommands.cs", "tests/Monarch.Policy.Tests/**", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 3, weekly_pct: 1, gold_usd: 0 }
boss: "Klare Meldung statt falscher Mana-Grenze, ohne neue Annahme"
board: { pr: 146 }
---
# Mana-Guard meldet veraltete Sensor-Werte nach Reset klar (#87, ohne Lockerung)
