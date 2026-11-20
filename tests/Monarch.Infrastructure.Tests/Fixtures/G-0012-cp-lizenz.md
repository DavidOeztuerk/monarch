---
id: G-0012
type: standard
title: Control Plane – Lizenzdurchsetzung und Lizenz-Token-Hygiene
rank: S
risk_tags: [secrets]
repo: [NoeliaControlPlane, workertransfer]
state: proposed
quests: [Q-0040, Q-0038, Q-0039]
boss: "Lizenzbudget wirkt identisch in UI, Recorder, History und Alarmen; Lizenzstatus wird zur Laufzeit ausgewertet; kein Lizenz-Token in Dateien unter src/ oder bin/"
estimate: { session_pct: 35, weekly_pct: 5, gold_usd: 0 }
---
# Control Plane – Lizenz

**Warden-Stand:** CP-1, CP-2, CP-3 bestätigt, D3 teilweise – **kein Zahlungsfluss im Code, daher nicht `payment` und nicht Rank S** (Warden: D). Gate-Rank S nur wegen **D1** (Lizenz-Token in ignorierter Config + bin/-Kopien, Inhalt nicht geöffnet).
**Policy:** Q-0040 (S): jede mutierende Aktion einzeln freigeben; Q-0038/Q-0039 (D): Standard-Shadow + 1 Verifier.

| Quest | Titel | Rank | Schwere |
|---|---|---|---|
| Q-0040 | Lizenz-Token aus Dateien in Env/Secret-Store, bin-Kopien entfernen, CI-Scan | S | low |
| Q-0038 | Ein Lizenzbudget für alle Pfade; PRICING abgleichen | D | medium |
| Q-0039 | Lizenzstatus zur Laufzeit; gebundene Lizenz vor dem Start | D | low |
