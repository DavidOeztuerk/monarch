---
id: Q-0147
gate: G-0107
kind: main
rank: A
state: proposed
dod:
  - Je Fehlalarm ein Test, dass er weg ist, und ein Test, dass der echte Zugriff weiter verweigert wird; die Tabelle in ADR-0017 ist vollständig umgesetzt.
  - Neue Ausnahmen gelten nur für rein literale Argumente; alles andere bleibt bei der heutigen Einordnung. Kein vorhandener Test ändert sich.
  - Issue-Texte aus .monarch/knowledge gehen auch aus einer verschachtelten Worktree durch, mit beiden Auswertungen getestet.
  - sed bleibt unverändert (Issues 67, 69 und 68 Punkt 1 kommen in ein Folge-Gate).
  - Heikle Quest: Design-ADR im PR, vom Warden freigegeben vor dem Code.
scope:
  include: ["src/Monarch.Policy/**", "tests/Monarch.Policy.Tests/**", "docs/adr/**"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
boss: "Die Allow-Tests aus ADR-0017 sind vor dem Fix rot und danach grün, die Deny-Tests bleiben durchgehend grün."
---
# Fehlalarme grep, sed, tr und Issue-Texte ohne Lockerung (#66, #67, #68, #69, #75)
