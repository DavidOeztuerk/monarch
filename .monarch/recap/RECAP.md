# RECAP

## Stand
- Zeitpunkt: 2026-11-06T12:36:49Z
- Plan: Pro
- Mana 5h: 44% (Limit 90%, Reset 2026-11-06T15:40:00Z)
- Mana 7d: 82% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 90%, 7d 96% (adaptive)

## Aktiv
- keine aktive Quest

## Erledigt
- Q-0113 [A] cleared: Merge-Policy nach Zielbranch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0127 [C] cleared: Mana-Schätzung je (Modell, Effort) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0128 [B] cleared: Weckruf, der einen Session-Neustart überlebt - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0129 [A] cleared: Wake nur durch den Main (Hook-Regel) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0130 [A] cleared: gate merge nie in einen geschützten Branch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0143 [D] cleared: docs/lessons.md: Lehren aus dem Beweis (Lauf 2) - https://github.com/DavidOeztuerk/monarch/pull/76 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/42217800392/job/127892565197, https://github.com/DavidOeztuerk/monarch/actions/runs/42217800392/job/127892565021, https://github.com/DavidOeztuerk/monarch/actions/runs/42217800392/job/127892063068, https://github.com/DavidOeztuerk/monarch/actions/runs/42217800392/job/127892063134, https://github.com/DavidOeztuerk/monarch/actions/runs/42217800392/job/127892062783, https://github.com/DavidOeztuerk/monarch/pull/76

## Offen
- keine offenen DoD-Punkte

## Nächster konkreter Schritt
Teil C ist abgeschlossen, MVP-Gate nicht bestanden (Hook-p99 über den Wrapper über 20 ms, Issue 70). Bericht: monarch-proof/09-proof-report.md, Lehren: docs/lessons.md (develop 4d20074a). Phase 1 (Prioliste Punkt 17) startet nicht; die Punkte 3a bis 16 laufen weiter. Nächster Schritt nach der Entscheidung des Owners.

## Offene Entscheidungen
- Owner: Latenz zuerst beheben und nur C1 neu messen (Empfehlung: eigenes kleines Gate vor Punkt 3a, mit Ende der Quarantäne) oder die Grenze von 20 ms neu festlegen.
- Owner: Reicht beim Ledger-Kriterium die Erkennung an nicht committeten Ständen, oder gehört die Lücke mit Commit (Issue 71) dazu?
