# RECAP

## Stand
- Zeitpunkt: 2026-10-30T18:49:58Z
- Plan: Pro
- Mana 5h: 90% (Limit 90%, Reset 2026-10-30T21:20:00Z)
- Mana 7d: 88% (Limit 96%, Reset 2026-11-04T11:00:00Z)
- Limits: 5h 90%, 7d 96% (plan default)

## Aktiv
- keine aktive Quest

## Erledigt
- Q-0113 [A] cleared: Merge-Policy nach Zielbranch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0127 [C] cleared: Mana-Schätzung je (Modell, Effort) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0128 [B] cleared: Weckruf, der einen Session-Neustart überlebt - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0129 [A] cleared: Wake nur durch den Main (Hook-Regel) - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0130 [A] cleared: gate merge nie in einen geschützten Branch - https://github.com/DavidOeztuerk/monarch/pull/15 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762702, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122946762600, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943422084, https://github.com/DavidOeztuerk/monarch/actions/runs/40721744761/job/122943425111, https://github.com/DavidOeztuerk/monarch/actions/runs/40721758194/job/122934234801, https://github.com/DavidOeztuerk/monarch/pull/15
- Q-0131 [A] cleared: MCP: Projekt je Aufruf - https://github.com/DavidOeztuerk/monarch/pull/29 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201559922, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201559975, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163798, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163737, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163446, https://github.com/DavidOeztuerk/monarch/pull/29
- Q-0132 [A] cleared: Regeln dort, wo sie gebraucht werden - https://github.com/DavidOeztuerk/monarch/pull/29 - Evidence: https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201559922, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201559975, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163798, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163737, https://github.com/DavidOeztuerk/monarch/actions/runs/40798654599/job/123201163446, https://github.com/DavidOeztuerk/monarch/pull/29

## Offen
- keine offenen DoD-Punkte

## Nächster konkreter Schritt
G-0104 abschließen: (1) gh pr checks 54 – macOS-Job aus Lauf 41107110442 wurde wegen 'Runner nicht verfügbar' neu gestartet; erst weiter, wenn ALLE Checks grün (aot rot: Ursache prüfen, nur bei Latenz-Rauschen #8 einmal neu starten). (2) Q-0134, Q-0135, Q-0136: quest accept + quest abandon (proposed→abandoned ist unzulässig, #16), Begründung 'in Folge-Gate verschoben' (steht im PR-Kommentar #54). (3) MONARCH_MERGE_WAIT_SECONDS=90 ~/.monarch/bin/monarch gate merge G-0104 (Verdict approve @ c5f83e9 ist eingetragen). (4) develop-Lauf nach dem Merge abwarten, bis ALLE Jobs grün (gh run watch); dann evidence sync (Q-0133 verify/clear). (5) Aufräumen nach Standardregel 2 ([skip ci]-Commit .monarch auf den Gate-Branch, ExitWorktree keep, git worktree remove ohne --force). (6) Memory 'policy-exceptions-strict-not-trick-hunting' schreiben (vorher Scope-Deny #17). (7) Owner-Aktion install-local + Plugin melden; Folge-Gate für #34/#33/#48, gitleaks-Hinweis, #8 ab 04.11. planen.

## Offene Entscheidungen
- keine
