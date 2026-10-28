---
id: Q-0040
gate: G-0012
kind: main
rank: S
severity: low
risk_tags: [secrets]
state: proposed
findings: [D1]
scope:
  repo: NoeliaControlPlane
  include: [".github/workflows/ci.yml", "README.md", ".gitignore"]
  exclude: ["src/Noelia.ControlPlane/appsettings.WorkerTransfer.json (Inhalt nur durch den User)"]
estimate: { session_pct: 5, weekly_pct: 0.5, gold_usd: 0 }
dod:
  - User prüft selbst, ob src/Noelia.ControlPlane/appsettings.WorkerTransfer.json ein Lizenz-Token enthält, und verschiebt es in die WT-.env (WORKERTRANSFER_NOELIA_LICENCE)
  - Kopien unter bin/Debug, bin/Release, tests/**/bin werden gelöscht (einzeln freigeben)
  - CI-Secret-Scan ergänzt um das Präfix noelia-lic.v1.
  - README - Lizenz nur über Env/Secret-Store, nie über appsettings.*.json
boss: "CI secret-scan findet ein Test-Token noelia-lic.v1.TEST in einer Fixture-Datei (Gegenprobe) und bleibt sonst grün"
evidence: ["CP .gitignore:15", ".dockerignore", "ls -l: 2283 B, 0644, + Kopien in bin/Debug, bin/Release/net10.0, tests/…/bin/Release/net10.0", "ci.yml:44-58", "Licence.cs:204", "07-warden-controlplane.md D1"]
---
# Lizenz-Token-Hygiene

## Objective
Ein Lizenz-Token liegt nirgends als Datei im Repo-Baum oder in Build-Ausgaben, und die CI würde ein versehentlich hinzugefügtes Token erkennen.

## Definition of Done
- [ ] **Rank S:** jede mutierende Aktion (Datei verschieben, Kopien löschen) einzeln vom User freigeben; den Inhalt öffnet nur der User
- [ ] CI-Regex + README
- [ ] Boss: Gegenprobe im Secret-Scan
