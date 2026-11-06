---
id: Q-0147
gate: G-0107
kind: main
rank: A
state: proposed
dod:
  - Je Fehlalarm ein Test, dass er weg ist, und ein Test, dass der echte Zugriff weiter verweigert wird.
  - Die Beispiele aus
  - Neue Ausnahmen gelten nur für rein literale Argumente; alles andere bleibt bei der heutigen Einordnung. Kein deny von heute wird allow, außer er ist beweisbar ein Fehlalarm.
  - Heikle Quest: Design-ADR im PR, vom Warden freigegeben vor dem Code.
scope:
  include: ["src/Monarch.Policy/**", "tests/Monarch.Policy.Tests/**", "docs/adr/**"]
  exclude: []
estimate: { session_pct: 25, weekly_pct: 5, gold_usd: 0 }
boss: "Harmlose grep-, sed-, tr- und Issue-Text-Aufrufe lösen Fehlalarme aus, ohne dass dafür echter Zugriff erlaubt wird."
---
# Fehlalarme grep, sed, tr und Issue-Texte ohne Lockerung (#66, #67, #68, #69, #75)
