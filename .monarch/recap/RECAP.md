# RECAP

## Stand
- Zeitpunkt: 2026-11-06T17:10:21Z
- Plan: Pro
- Mana 5h: 56% (Limit 90%, Reset 2026-11-06T20:40:00Z)
- Mana 7d: 96% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 90%, 7d 96% (adaptive)

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
G-0107 abschliessen nach dem Wochenreset (Mi 11.11. 12:00). Session in ~/Projects/monarch starten, dann EnterWorktree mit path .claude/worktrees/G-0107. Stand: PR 77, Head 522f605, Verdict approve eingetragen, Q-0144 bis Q-0147 in_review. (1) Mana pruefen. (2) gh pr checks 77: Ein aot (linux-x64) ist noch rot, Laeufe 42256365085 und 42256352454, also gh run rerun <id> --failed. Das Latenz-Gate streut unabhaengig vom Code (a8b59a6, nur Q-0144: PR-Lauf rot, Push-Lauf gruen; 709d712 Push gruen); Grenze 20 ms nie aendern. Bleibt es nach mehreren Versuchen rot: stoppen, Owner melden. (3) Vor gate merge NICHTS committen oder pushen, sonst verfaellt das Verdict; uncommittete Hook-Ledger-Zeilen liegen lassen. (4) gate merge G-0107; das installierte Binary ist noch alt, die Commit-Nachrichten sind auf Skip-Marker geprueft. (5) evidence sync, Quests clearen, Aufraeumen nach Standardregel 2. (6) Issues 64, 60, 65, 75 schliessen; 68 und 69 bleiben offen (sed), 66 und 67 ebenso. (7) Kurzbericht und Owner-Aktion install-local und Plugin.

## Offene Entscheidungen
- Owner 06.11.: sed (Issues 67, 68 Punkt 1, 69) und grep (Issue 66) sind verschoben, Vorarbeit in .monarch/knowledge/G-0107-sed-folgegate.md und G-0107-grep-folgegate.md. Der Entscheid vom 30.10. (STRENG) gilt weiter, bis der Owner ihn aufhebt. Punkt 5 aus 3a (Issues 46, 44, 36, 42, 72) kommt ebenfalls in ein Folge-Gate.
