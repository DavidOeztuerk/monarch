---
id: Q-0003
gate: G-0001
kind: main
rank: A
severity: medium
risk_tags: [infra, auth]
state: proposed
findings: [D5, N-1, WT-5]
scope:
  repo: workertransfer
  include: ["docker-compose.yml", "scripts/control-plane.sh", "scripts/control-plane-fleet.json", "Makefile"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 1, gold_usd: 0 }
dod:
  - Dienstports 8001–8015, Postgres 5432 und Mailpit 1025/8025 nur an 127.0.0.1 gebunden
  - Host-Control-Plane sammelt weiterhin 15/15 (make control-plane)
  - Login-Bremse kann nicht mehr über Dienstports umgangen werden (nur Gateway von außen)
boss: "make control-plane zeigt 15/15 + Skript-Probe: Dienstport von Nicht-Loopback-IP nicht erreichbar"
evidence: ["docker-compose.yml:136,176-177,245,256,297,…,686", "src/shared/…/ServiceFoundation.cs:280-284", "07-warden-workertransfer.md D5, N-1"]
---
# Compose: Dienst-, Postgres- und Mailpit-Ports nur auf Loopback

## Objective
Im Dev-LAN ist nur das Gateway erreichbar; `/internal/*`, `/noelia`, `/auth/login` am Gateway vorbei und DB/Mailpit sind von außen zu.

## Definition of Done
- [ ] Review (Warden) + Freigabe (Infra, Rank A) – kleine Änderung, aber Infra
- [ ] `"127.0.0.1:80xx:80xx"` für alle Dienste; Postgres/Mailpit ebenso
- [ ] Boss: Control Plane 15/15 und Probe von Nicht-Loopback-Adresse schlägt fehl
