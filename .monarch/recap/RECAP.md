# RECAP

## Stand
- Zeitpunkt: 2026-11-04T22:03:40Z
- Plan: Pro
- Mana 5h: 99% (Limit 88%, Reset 2026-11-04T23:10:00Z)
- Mana 7d: 25% (Limit 96%, Reset 2026-11-11T11:00:00Z)
- Limits: 5h 88%, 7d 96% (adaptive)

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
G-0105 abschließen nach Owner-Entscheid vom 04.11. Head 0e6af2d ist gepusht, Q-0142 und Q-0137 bis Q-0140 sind eingereicht und an PR 57 gelinkt. (1) Q-0141 kommt aus dem Gate und wird nicht angefangen: quest accept + quest abandon; Issue 8 bleibt offen, Prioliste Punkt 17. (2) PR-Text 57 fortschreiben (Q-0137 bis Q-0140 in einfacher Sprache). (3) Monarch-Stand committen OHNE [skip ci], pushen: das ist der endgültige Head. (4) Genau eine Warden-Runde über den ganzen PR-Head; sie ersetzt die Kurzprüfung und ist die Runde für gate merge. Ausdrücklich prüfen: die von 8 auf 15 s gelockerte Testgrenze (MergeMethodTests) und A_relative_git_bin_is_ignored (kein Rotlauf). Offen aus dem Knight-Bericht: bei 8 parallelen Suiten griff die Phase-A-Frist zu spät, Ursache nur teilweise belegt. (5) Nur major beheben, minor als Issue per --body-file. (6) macOS per gh run rerun --failed wiederholen, CI nicht aufweichen; kommt macOS nach mehreren Versuchen nicht durch: stoppen und Owner melden. (7) gate verdict, dann gate merge nach develop, vorher und nachher grün. Das installierte Binary ist noch alt: MONARCH_MERGE_WAIT_SECONDS=90 setzen, gate check gibt es dort nicht, also gh run watch. Dann evidence sync, Quests clearen, Aufräumen nach Standardregel 2 (letzter Commit dort mit [skip ci]), Owner-Aktion melden: install-local und Plugin. Issue 60 (git aus cwd) und die sed-Grenze im Bericht nennen.

## Offene Entscheidungen
- ENTSCHIEDEN vom Owner am 30.10.: (1) Q-0137/Issue 34: V1 – belegter Fix-PR wird trotz rotem develop automatisch gemergt (develop-Head Vorfahr des PR-Heads, alle Checks am PR-Head grün, jeder am develop-Head rote Check gleichen Namens dort grün; GateMerged hält überstimmte Checks und develop-SHA fest); cancelled vor dem Merge bleibt Stopp (Lauf-ID, Rerun-Hinweis), danach Ergebnis cancelled mit Exit ungleich 0; pending bleibt Exit ungleich 0. (2) Q-0138/Issue 55: STRENG – jede bei d45e6b9 gesperrte Variante bleibt gesperrt; frei werden nur sed 's/.*/new/' f und sed -e 's/.*/e/' f; awk-Beispiel und rm -rf dist/.* bleiben deny, im ADR-0014-Nachtrag als bekannte Fehlalarme.
