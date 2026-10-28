---
id: Q-0001
gate: G-0001
kind: main
rank: A
severity: high
risk_tags: [auth, pii]
state: proposed
findings: [WT-1]
scope:
  repo: workertransfer
  include: ["src/identity-service/**/Api/InternalEndpoints.cs", "src/identity-service/**/Api/NotifyEndpoints.cs", "src/identity-service/**/Application/Account/Settings.cs", "src/shared/**", "docker-compose.yml", "deploy/helm/**", "tests/WorkerTransfer.Identity*.Tests/**"]
  exclude: ["**/Migrations/**"]
estimate: { session_pct: 22, weekly_pct: 3, gold_usd: 0 }
dod:
  - Design-Notiz (Tür → berechtigte Aufrufer → Credential) vom User freigegeben
  - Jede /internal-Tür prüft ein eigenes Recht; Notify-Secret öffnet nur Notify
  - Endpunkt mit entschlüsseltem KI-Schlüssel nur für den einen Dienst, der ihn braucht
  - Erzwingen der „Ledger-Prüfung vorher“ (InternalEndpoints.cs:131-137) in identity statt Konvention
  - Negativtests je Tür
boss: "InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey grün in CI"
evidence: ["workertransfer/src/identity-service/…/Api/InternalEndpoints.cs:22,141-232", "…/Application/Account/Settings.cs:193", "docker-compose.yml (12 Dienste mit dem Secret)", "07-warden-workertransfer.md WT-1"]
---
# `/internal/*` je Tür eigenes Recht statt Notify-Secret

## Objective
Ein Dienst, der nur Mails auslösen darf, kann nicht mehr Name/E-Mail beliebiger Konten oder entschlüsselte KI-Schlüssel abrufen.

## Definition of Done
- [ ] Design (Strategist, Heavy) → Review (Warden) → Freigabe (User) – vor jeder Code-Änderung
- [ ] Rechte je Tür getrennt (eigene Secrets oder M2M-Token mit Scope), Konfiguration in Compose und Helm
- [ ] KI-Schlüssel-Tür nur für den berechtigten Dienst; alle anderen erhalten 404
- [ ] Negativtest pro Tür (falsches/fremdes Secret → 404)
- [ ] Boss: `InternalEndpoints_NotifySecret_CannotReadIdentityOrAiKey` grün in CI
