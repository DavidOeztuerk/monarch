# RECAP

## Stand
- Zeitpunkt: 2026-10-30T23:09:44Z
- Plan: Pro
- Mana 5h: 40% (Limit 90%, Reset 2026-10-31T02:20:00Z)
- Mana 7d: 94% (Limit 92%, Reset 2026-11-04T11:00:00Z)
- Limits: 5h 90%, 7d 92% (adaptive)

## Aktiv
- keine aktive Quest

## Erledigt
- Q-0113 [A] cleared: Merge-Policy nach Zielbranch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0127 [C] cleared: Mana-Schätzung je (Modell, Effort) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0128 [B] cleared: Weckruf, der einen Session-Neustart überlebt - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0129 [A] cleared: Wake nur durch den Main (Hook-Regel) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0130 [A] cleared: gate merge nie in einen geschützten Branch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15

## Offen
- keine offenen DoD-Punkte

## Nächster konkreter Schritt
Nach Reset (Mi 04.11. 12:00): in Worktree .claude/worktrees/G-0105 (EnterWorktree path). (1) Q-0137-DoD ist in der Datei unvollständig (1. Punkt abgeschnitten vor dem Rautezeichen, 3 Punkte leer; Issue-Text für den Owner liegt vor) – zuerst Q-0137 per gate_plan neu schreiben (strategist, ganzes G-0105 mit Q-0137–Q-0141, DoD ohne Rautezeichen, Variante V1 und Q-0138 streng einarbeiten). (2) quest accept/start Q-0137, Knight: ADR-0007-Nachtrag (V1, cancelled, Laufzeit unter 120 s, Nachprüf-Befehl), eine Warden-Design-Runde, dann TDD. (3) Reihenfolge danach: Q-0138 + Q-0139, Q-0140, Q-0141 (ADR-0015; offen: bleibt das absolute p99-20-ms-Gate in CI blockierend – Owner fragen, falls das Design es ändern will).

## Offene Entscheidungen
- G-0105/Q-0138 (#55): Owner wählt STRENG – jede bei d45e6b9 gesperrte Variante bleibt gesperrt; frei werden nur sed 's/.*/new/' f und sed -e 's/.*/e/' f. awk '/.*/ {print | "sort"}' f und rm -rf dist/.* bleiben deny und stehen im ADR-0014-Nachtrag als bekannte Fehlalarme. Grund: Owner-Regel aus G-0104, kein Schutz schrumpft.
