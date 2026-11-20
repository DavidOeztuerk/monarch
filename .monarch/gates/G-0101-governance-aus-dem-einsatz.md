---
id: G-0101
type: standard
title: Monarch – Governance aus dem Einsatz
rank: A
risk_tags: [policy, infra]
repo: monarch
state: proposed
quests: [Q-0113, Q-0126, Q-0114]
boss: "Agent kann main/master nicht per git push, gh pr merge oder gh api mergen; Mana-Schätzung je (Modell, Effort) kalibriert; ein Weckruf überlebt einen Session-Neustart – je belegt durch Tests rot→grün"
estimate: { session_pct: 45, weekly_pct: 10, gold_usd: 0 }
---
# Monarch – Governance aus dem Einsatz

Aus Lauf 2 (27./28.10.): Claude merged selbst nur nach `develop`, `main` bleibt dem Owner (Q-0113); Mana-Kosten hängen von Modell und Effort ab (Q-0126); der In-Session-Weckruf (`CronCreate`) verfiel beim Session-Neustart (Q-0114).

**Grenzen (projects-e9, 28.10.):** 3 Quests, ≤ 10 pp Woche, 1 Warden-Runde (Design- und Code-Phase durch denselben Warden). Maßstab ADR-0011.

| Quest | Titel | Rank |
|---|---|---|
| Q-0113 | Merge-Policy nach Zielbranch (ADR-0007) | A |
| Q-0126 | Mana-Schätzung je (Modell, Effort) (ADR-0012) | C |
| Q-0114 | Weckruf, der einen Session-Neustart überlebt (ADR-0008) | B |
