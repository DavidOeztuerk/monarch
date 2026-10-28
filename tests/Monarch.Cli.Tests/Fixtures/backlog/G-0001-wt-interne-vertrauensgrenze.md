---
id: G-0001
type: standard
title: Workertransfer – interne Vertrauensgrenze
rank: A
risk_tags: [auth, pii, infra]
repo: workertransfer
state: proposed
quests: [Q-0001, Q-0002, Q-0003, Q-0004]
boss: "Ein kompromittierter Fachdienst (z. B. notifications) kann weder Personendaten noch KI-Schlüssel anderer Konten lesen – belegt durch Q-0001- und Q-0002-Bosse grün in CI"
estimate: { session_pct: 70, weekly_pct: 9, gold_usd: 0 }
---
# Workertransfer – interne Vertrauensgrenze

Heute bilden alle 15 Dienste eine einzige Vertrauensdomäne: ein Notify-Secret öffnet alle `/internal/*`-Türen von identity (WT-1), alle Dienste teilen DB-Superuser und Master-Key (N-3, WT-5, WT-10), Dienstports liegen in Compose auf allen Interfaces (D5, N-1), und ein Operator-Secret gilt für alle (D4).

**Warden-Stand:** WT-1, WT-5, D4, D5 bestätigt; N-1, N-3 vom Warden neu entdeckt (`07-warden-workertransfer.md`).
**Kontext:** Plattform laut ADR-0045 nicht live → kein aktueller Schaden; Gate muss vor dem ersten echten Konto geschlossen sein.
**Policy (Rank A):** Security-Warden Pflicht, Freigabe vor jedem Merge, Ablauf Design → Review → Freigabe → Implementierung.

| Quest | Titel | Rank | Schwere |
|---|---|---|---|
| Q-0001 | `/internal/*` je Tür eigenes Recht statt Notify-Secret | A | high |
| Q-0002 | DB-Rolle je Dienst, Master-Key nur wo nötig | A | high |
| Q-0003 | Compose: Dienst-, Postgres- und Mailpit-Ports nur auf Loopback | A | medium |
| Q-0004 | Operator-Secret je Dienst | A | low |
