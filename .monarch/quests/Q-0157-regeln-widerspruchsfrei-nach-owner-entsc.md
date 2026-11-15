---
id: Q-0157
gate: G-0113
kind: main
rank: B
state: in_review
dod:
  - plugin/skills/regeln/SKILL.md Regel 3, Punkt Rank S: an ADR-0007 angeglichen (jede mutierende Aktion einzeln mit dem Owner)
  - plugin/agents/strategist.md: Planfreigabe durch den Nutzer nur bei Rank S, Gate B zu Rank korrigiert
  - plugin/skills/regeln/SKILL.md Regel 6: Ein Deny stoppt die Aktion, keine Varianten, keine Umwege; die Session arbeitet an anderer Stelle weiter und meldet das Deny gebündelt im Kurzbericht; den User fragt sie nur, wenn ohne die Aktion nichts mehr weitergeht
  - plugin/skills/regeln/SKILL.md Regel 10: Kurzbericht mit gebündelten Fragen, je mit Empfehlung und Default; ein Default ist kein Owner-Entscheid und steht in Dokumenten als Vorschlag; Weiterarbeiten mit Default nur, wenn umkehrbar
  - CLAUDE.md Regel 3 gilt nur für Freigaben des Owners von Dokumenten mit Owner-Entscheiden (ADRs); Regeln 1, 2, 4, 5 bleiben; dazu der Satz: Sonst gilt Standardregel 3, ohne Einzelfreigaben
  - Kein weiterer Widerspruch zu ADR-0007 in plugin/skills und plugin/agents (grep-Beleg im PR)
scope:
  include: ["plugin/skills/regeln/SKILL.md", "plugin/agents/strategist.md", "CLAUDE.md"]
  exclude: ["src/**", "tests/**", ".github/**", "bench/**", "plugin/hooks/**"]
estimate: { session_pct: 8, weekly_pct: 2, gold_usd: 0 }
boss: "Warden-Schlussrunde approve; grep auf den fertigen Dateien belegt, dass Rank S, Planfreigabe, Regel 6, Regel 10 und CLAUDE.md Regel 3 dem Owner-Entscheid vom 15.11.2026 entsprechen"
---
# Regeln widerspruchsfrei nach Owner-Entscheid vom 15.11.2026
