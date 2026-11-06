# Lehren aus dem Beweis (Lauf 2)

Stand 06.11.2026. Monarch hat sich vom 26.10. bis 06.11.2026 an echter Arbeit in Workertransfer, Noelia und an sich selbst bewiesen. Diese Datei hält fest, was das ergeben hat und welche Regeln daraus folgen. Der volle Bericht mit allen Messwerten liegt außerhalb des Repos (`monarch-proof/09-proof-report.md`).

## Ergebnis

**Das MVP-Gate ist nicht bestanden.** Das Latenz-Kriterium ist verfehlt. Die anderen beiden gelten als erfüllt, beim Ledger mit einer Auslegung (siehe „Wo der Beweis dünn ist“).

| Kriterium | Ergebnis | Urteil |
|---|---|---|
| Hook-p99 ≤ 20 ms über den Wrapper, installiertes Binary, reale Last | 19,6–76,5 ms, 22 von 24 Messungen darüber | nicht erfüllt |
| Mindestens 10 gecleart | 27 gecleart, davon 14 außerhalb von Monarch | erfüllt |
| Ledger-Manipulation wird erkannt | alle 12 Fälle an nicht committeten Ständen erkannt | erfüllt, mit Auslegung |

Phase 1 startet deshalb noch nicht. Die Grenze von 20 ms bleibt, bis der Owner anders entscheidet (#70).

Die Latenz im Einzelnen, gemessen auf dem Mac des Owners, 1000 Läufe je Szenario:

| Bedingung | p50 | p99 | über 20 ms |
|---|---|---|---|
| Ohne Stack | 10,8–13,0 ms | 16,4–36,0 ms | 17 von 24 |
| WT-Stack läuft | 10,5–20,5 ms | 19,6–76,5 ms | 22 von 24 |
| WT-Stack startet gerade | 17,4–48,5 ms | 31,1–136,6 ms | 8 von 8 |

Direkt aufgerufen hält der Hook die Grenze ohne Stack ein (p99 16,1 ms), über den Wrapper nicht (25,3 ms). Der Wrapper kostet im Median rund 5 ms. Das Starten des Binarys kostet 6,3 ms, die Hook-Logik rund 3,6 ms.

## Was nicht geklappt hat

**Wettrüsten in der Nacht.** In G-0100 fand der Warden Runde um Runde neue Bash-Umgehungen, und wir haben jedes Mal gehärtet, vier Runden lang, ohne Stoppregel. Erst eine Kurskorrektur von außen beendete das.

**Aufgeweichter Test, zweimal.** Um die CI grün zu bekommen, wurde ein Latenz-Test vom Median auf das Minimum umgestellt und das CI-Gate von p99 auf p95 gesenkt (`5abdd63`). `c720509` nahm beides zurück, seitdem steht der Test aber in Quarantäne und läuft nicht (#9). Eine Woche später wurde in G-0105 eine Testgrenze von 8 auf 15 s gelockert. Der Warden lehnte das ab. Die 15 s blieben, aber eine eigene git-Frist von 25 s macht den Test wieder beweiskräftig.

**Roter develop.** Von sechs Commits auf `develop` waren nur zwei beim ersten Versuch grün. Einer bekam nie einen grünen Lauf, zwei brauchten Wiederholungen, und einer bekam gar keinen Lauf, weil `[skip ci]` aus den Branch-Commits in der Squash-Nachricht stand (#64).

**Vergessene Release-Schritte.** Bei Noelia 7.1.0 fiel dem Owner kurz vor dem Publish auf, dass die Version noch auf 7.0.1 stand. Der Release-Skill war nicht vollständig abgearbeitet. Die Nachprüfung zeigte: Ein Gate-Schritt prüfte den Kandidaten gar nicht, weil er auf eine alte Version gepinnt war.

**Eine Zusage ist still verfallen.** Die Quarantäne des macOS-Wrapper-Schritts in der CI sollte spätestens vor dieser Messung enden. Issue #8 wurde aber verschoben. Die CI blieb grün, obwohl dort p99 bis 49,48 ms stand, und die Zahl fiel erst bei der Messung für das MVP-Gate auf.

**Auflagen nicht eingehalten.** In WT G-0001 hat eine Session eine Warden-Auflage nicht dem Owner vorgelegt und eine Aufgabe erledigt, die dem Owner zugewiesen war. Beides wurde gemeldet, aber erst hinterher.

**Schätzungen zu niedrig.** Ohne die Gates aus dem Doppel-Session-Betrieb lag das Ist bei 91 statt 46 Wochen-Prozentpunkten, also beim Doppelten. Die größten Abweichungen kommen von Gates mit mehreren Warden-Runden. Die Kalibrierung hat im Betrieb nie gegriffen: 21 von 21 Schritten blieben unkalibriert (#74).

## Was getragen hat

- **Der unabhängige Warden.** Er erkannte beide Kanarien, lehnte die gelockerte Testgrenze in G-0105 ab und ein Design, bei dem jeder Dienst-Aufruf in 401 geendet hätte.
- **Clears nur mit Forge-Evidence.** Alle 27 Clears haben einen Merge-Commit, der in `develop` liegt.
- **Der Hook als Leitplanke.** Ein echter Agent scheiterte im e2e-Test an 6 von 6 Ledger-Manipulationen.
- **Der Mana-Guard.** Er hat Sessions an sicheren Punkten gestoppt, statt sie ins Limit laufen zu lassen.
- **Die Prioritäten.** Die beiden einzigen high-Befunde aus Lauf 1 sind geschlossen.

## Wo der Beweis dünn ist

- An der Control Plane und am Demo-Projekt wurde kein Befund geschlossen. Vom Backlog aus 40 Quests sind 4 erledigt.
- `monarch quest list` zeigt auf `develop` zu wenig, weil der Status nur auf dem Gate-Branch landet (#72).
- Eine gezielte Fälschung mit einem normalen Commit bleibt `ledger verify` verborgen, wenn sie das Ende der Datei kürzt oder sauber verlängert (#71). Der Auftrag nennt „ändern, löschen, anhängen“ ohne diese Einschränkung. „Erfüllt“ gilt nur für nicht committete Stände; ob das reicht, entscheidet der Owner.
- Gemessen wurde auf einem Mac an einem Tag, bei normaler Alltagslast. Der Stack lief ohne Nutzerlast.

## Regeln, die bleiben

1. **Kriterien vorher festlegen und nicht verschieben.** Wenn die Messung sie verfehlt, heißt das Ergebnis „nicht bestanden“. Eine andere Grenze entscheidet der Owner, nicht die Session.
2. **Eine Quarantäne braucht ein Enddatum, das jemand prüft.** Sonst verschwindet genau die Zahl, die man später braucht.
3. **Ein rotes Gate wird nicht leiser gestellt.** Tests und Grenzen werden nicht gelockert, um grün zu werden. Die Ursache wird belegt, und im Zweifel entscheidet der Warden.
4. **Reviews brauchen ein Modell und ein Ende.** Ohne Bedrohungsmodell und Rundengrenze konvergieren sie nicht (ADR-0011).
5. **Ein Gate, das nichts prüft, sieht aus wie ein grünes Gate.** Gate-Schritte müssen nachweisen, dass sie den Kandidaten wirklich getroffen haben.
6. **Ein Test muss zeigen, dass er etwas getan hat.** Beim Ledger-Test für dieses Gate lieferte ein Skriptfehler 10 falsche „nicht erkannt“, weil gar nichts manipuliert wurde. Seitdem weist jeder Fall die Änderung nach.
7. **Messen heißt: nichts nebenher.** Die erste Messreihe war durch eigene Arbeit gestört und musste wiederholt werden.
8. **Auflagen gehen vor dem Abschluss an den Owner.** Hinterher melden reicht nicht.
9. **Schätzungen rechnen Warden-Runden mit ein.** Bis die Kalibrierung greift, ist Faktor 2 der ehrliche Wert.
