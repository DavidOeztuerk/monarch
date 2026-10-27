---
id: Q-0008
gate: G-0002
kind: main
rank: A
severity: medium
risk_tags: [auth]
state: proposed
findings: [USER-2026-10-27, 02-N5, WT-7, 02a-R10]
blocked_by: [Q-0016]
scope:
  repo: workertransfer
  include: ["src/identity-service/**/Api/AuthEndpoints.cs", "src/identity-service/**/Application/SignIn/**", "src/identity-service/**/Application/Companies/RemoveMember.cs", "src/shared/WorkerTransfer.ServiceDefaults/Revocation.cs", "web/src/**/session/**", "tests/**"]
  exclude: []
estimate: { session_pct: 18, weekly_pct: 2, gold_usd: 0 }
dod:
  - "Abmelden beendet alle Sitzungen der Person: Stichtag-Widerruf (RevokeSubjectBefore) + alle Refresh-Sessions geschlossen, in allen 15 Diensten wirksam"
  - Cross-Tab-Logout bleibt (BroadcastChannel), zusätzlich andere Browser/Geräte
  - Entfernen eines Firmenmitglieds widerruft dessen Tokens für diese Firma sofort (WT-7)
  - ADR zur Sitzungspolitik ersetzt die Entscheidung vom 2026-10-07 (mehrere Tabs/Geräte bleiben möglich, Abmelden wirkt überall)
boss: "SignOut_EndsEverySessionOfThePerson_AcrossServicesAndDevices grün in CI"
evidence: ["User 2026-10-27 (diese Session): „am liebsten alle abmelden“", "02a R10 (User 2026-10-06T21:36:59Z), R15 (compact-summary 2026-10-07T02:42:31Z)", "workertransfer …/AuthEndpoints.cs:252-262, SignOut.cs:36-40 (02 N5)", "…/Companies/RemoveMember.cs:55-67 (WT-7)"]
---
# Abmelden beendet alle Sitzungen der Person

## Objective
Umsetzung der User-Entscheidung: Wer sich abmeldet, ist überall abgemeldet (alle Tabs, Browser, Geräte), in allen Diensten sofort.

**Interpretation (bitte bestätigen):** „alle abmelden“ = Abmelden wirkt auf alle Sitzungen der Person. Gleichzeitige Anmeldung in mehreren Tabs/Geräten bleibt möglich. Falls stattdessen „nur eine Sitzung gleichzeitig“ gemeint ist (neue Anmeldung wirft alte raus), wird die DoD um Single-Session-Enforcement ergänzt.

## Definition of Done
- [ ] Design → Review → Freigabe (auth, Rank A); **blockiert durch Q-0016** (Noelia: Grace-Fenster nach Sitzungsende), sonst bleibt ein Refresh-Geschwister nutzbar
- [ ] Sign-out ruft Stichtag-Widerruf + `CloseAllSessions`; Firmen-Entfernung widerruft betroffene Tokens
- [ ] ADR „Sitzungspolitik“ geschrieben
- [ ] Boss: `SignOut_EndsEverySessionOfThePerson_AcrossServicesAndDevices` grün in CI
