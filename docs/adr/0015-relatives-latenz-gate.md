# ADR-0015 — Relatives Latenz-Gate in der CI

**Datum:** 11.11.2026 · **Status:** vorgeschlagen (Design-Review Warden G-0108 steht aus) · **Quest:** Q-0148 (G-0108) · **Issue:** #8

## Kurz in einfacher Sprache

- Der Hook läuft vor jedem Tool-Aufruf. Ein Test in der CI soll verhindern, dass er unbemerkt langsamer wird.
- Bisher prüfte die CI eine feste Zahl: 99 von 100 Starts müssen unter 20 ms bleiben. Auf den geteilten Rechnern von GitHub hängt diese Zahl vor allem davon ab, wie schnell der Rechner gerade ist. Der Test wurde oft rot, obwohl am Code nichts geändert war.
- Künftig misst die CI auf demselben Rechner zusätzlich, wie lange das Programm zum bloßen Starten braucht, und prüft, **um welchen Faktor der Hook langsamer ist als der bloße Start**. Ein langsamer Rechner macht beides langsamer, der Faktor bleibt gleich.
- **Das ist eine Lockerung, und sie ist gewollt:** Die feste Grenze von 20 ms macht die CI nicht mehr rot. Sie gilt weiter auf dem Mac des Owners (MVP-Kriterium C1, Issue #70) und ist dort heute nicht erfüllt. Die CI zeigt die absoluten Werte weiter an.
- Der Owner hat das am 11.11.2026 entschieden.

## Problem (belegt)

1. **Jeder rote CI-Lauf kam von diesem Gate.** Von 87 CI-Versuchen zwischen dem 27.10. und dem 11.11. waren 24 rot. In allen 24 scheiterten nur Latenz-Schritte, nie Build oder Tests (Aufstellung in Issue #8, Kommentar vom 11.11.).
2. **Gleicher Code, verschiedenes Ergebnis.** Lauf 41038797796 (Commit ohne Code-Änderung): erster Versuch rot, Wiederholung grün. Lauf 43026276365 auf `develop` (`2b3c86aa`): dreimal rot mit p99 94,55, 20,22 und 20,72 ms, später grün.
3. **Die Runner sind verschieden schnell.** Der bloße Programmstart lag auf Linux-Runnern zwischen 4,49 und 7,71 ms (Lauf 43060173567, Versuche 3 bis 5), also bis zum 1,7-Fachen auseinander. Eine feste Grenze in Millisekunden misst damit den Runner.
4. **Das eigentliche Kriterium erfasst die CI nicht.** p99 ≤ 20 ms über den Wrapper auf dem Mac des Owners ist verfehlt (Issue #70). Die CI war dabei grün.

## Entscheidung

### Was gemessen wird

Je Szenario fünf Blöcke. In jedem Block misst `bench/hook-latency.sh` erst den **Bezug**, dann das **Szenario**, je 60 Läufe mit hyperfine:

- Bezug: `monarch --version`, im selben Modus (direkt das Binary, oder über `plugin/scripts/monarch`), im selben Ordner, mit derselben Eingabe.
- Szenario: `monarch hook pre-tool-use` wie bisher.

Für jeden Block gilt `Verhältnis = Median(Szenario) / Median(Bezug)`. Der Wert des Szenarios ist der **Median der fünf Block-Verhältnisse**. Bezug und Szenario laufen abwechselnd, damit beide denselben Zustand des Rechners sehen.

Ein Szenario besteht, wenn sein Wert in einem von höchstens zwei Versuchen die Schwelle nicht überschreitet. Das ist dieselbe Regel wie bisher (`ATTEMPTS`).

### Warum `monarch --version` als Bezug

- Der Befehl startet dasselbe Binary, liest keine Projektdateien und schreibt nichts. Er kostet also Prozessstart und Laufzeit-Initialisierung.
- Er ist nicht identisch mit dem Startpfad des Hooks: `--version` baut den Befehlsbaum von System.CommandLine auf, der Hook umgeht ihn (`src/Monarch.Cli/MonarchApp.cs:12-28`). Gemessen ist der Unterschied klein: Das Szenario `non-awakened` (Hook in einem Ordner ohne Monarch, also fast nur Start) liegt bei 1,02 bis 1,16 des Bezugs.
- Folge: Der Bezug ist etwas zu groß, alle Verhältnisse sind etwas zu klein. Für das Gate ist das unerheblich, weil die Schwellen aus Messungen mit genau diesem Bezug stammen.
- Ein anderer Bezug (etwa ein eigener Leerlauf-Befehl im Binary) bräuchte Code in `src` und einen Owner-Entscheid.

### Schwellen

Regel: **Schwelle = größter gemessener Wert der Klasse + 0,25**. Gemessen im Lauf 43060173567 auf dem Commit `e09b614`, Linux in den Versuchen 3 bis 5, macOS in den Versuchen 1 bis 5, jeweils direkt und über den Wrapper.

| Klasse | Szenarien | Linux gemessen | macOS gemessen | Schwelle Linux | Schwelle macOS |
|---|---|---|---|---|---|
| ohne Ledger | in-scope, non-awakened, bash-targets, abs-target, nested-projects, long-heredoc | 1,02–1,30 | 1,01–1,22 | 1,55 | 1,55 |
| mit Ledger | out-of-scope, denied-bash | 1,69–2,15 | 1,14–1,45 | 2,40 | 1,70 |

- Die Klasse „mit Ledger“ schreibt nach der Entscheidung eine Zeile ins Ledger (`src/Monarch.Cli/HookRunner.cs:199-223`). Das kostet auf Linux 4,5 bis 6,2 ms, auf macOS 1,2 bis 2,7 ms. Deshalb hat sie eigene Schwellen je Betriebssystem. Warum Linux so viel teurer ist, ist nicht belegt und wird ein eigenes Issue.
- Die Schwellen stehen im Skript. Wer sie ändert, trägt die Messwerte und die Run-ID hier nach.
- Die Schwellen gelten für direkt und Wrapper gleich. Über den Wrapper ist der Bezug größer, die Verhältnisse sind dort kleiner oder gleich.

### Was das Gate erkennt und was nicht

- **Erkennt:** eine Verlangsamung des Hooks um rund 0,25 bis 0,5 des Programmstarts gegenüber heute, also etwa 1,5 bis 3,5 ms. Dazu gehört ein zusätzlicher Prozessstart auf dem heißen Pfad, etwa ein `git`-Aufruf. Nachweis: Fehler-Injektion, siehe Teststrategie.
- **Erkennt nicht:** eine Verlangsamung, die Bezug und Hook gleich trifft, etwa ein größeres Binary oder eine langsamere Laufzeit. Dafür bleiben die absoluten Werte im Log und die Messung auf dem Mac.
- **Erkennt nicht:** Ausreißer (p99). Auf geteilten Runnern kommen sie vom Runner.
- **Erkennt nicht:** schleichende Verlangsamung unterhalb der Schwelle.

### Was mit der Grenze von 20 ms geschieht

- `LIMIT_MS` bleibt 20, `GATE_PCT` bleibt 99. Ohne weitere Angabe verhält sich `bench/hook-latency.sh` wie bisher (`GATE_MODE=absolute`). So wird auf dem Mac des Owners gemessen.
- Die CI ruft das Skript mit `GATE_MODE=relative` auf. Dann entscheidet nur das Verhältnis über rot oder grün. Die absoluten Werte p50, p95 und p99 stehen weiter im Log und in der Job-Summary, mit dem Vermerk, ob 20 ms eingehalten wären.
- **Lockerung, offen benannt:** Die Schritte „Hook latency (direct)“ und „Hook latency (through plugin/scripts/monarch)“ blockierten bisher auf p99 ≤ 20 ms (der Wrapper-Schritt auf macOS stand in Quarantäne). Das entfällt in der CI. `docs/lessons.md` hält fest, dass dieses Gate schon einmal still aufgeweicht wurde (`5abdd63`); diesmal geschieht es mit ADR, Owner-Entscheid und Warden.

### CI

- Der relative Schritt blockiert auf Linux und macOS, direkt und über den Wrapper.
- Die macOS-Quarantäne (`continue-on-error`) entfällt, weil der relative Schritt den Wrapper-Modus blockierend abdeckt.
- Der Selbsttest `bench/test-gate.sh` bleibt blockierend.
- `permissions` bleiben `contents: read`. Es wird kein Basis-Binary gebaut und kein Artefakt aus fremden Läufen geladen.

## Verworfen

- **A/B gegen ein Basis-Binary** (Q-0136, Q-0141): robuster gegen den zu großen Bezug, braucht aber einen zweiten Build je Lauf und eine Regel, was die Basis ist. Zweimal geplant, zweimal nicht begonnen.
- **Gate ganz streichen:** Dann fiele ein `git`-Aufruf auf dem heißen Pfad erst auf dem Mac auf.
- **Grenze anheben oder p95 statt p99:** ändert nichts daran, dass eine feste Zahl den Runner misst.

## Teststrategie

- `bench/test-gate.sh`, deterministisch über `BENCH_RESULTS_DIR` mit Bezugs-Werten, vorher rot, danach grün:
  - Regression nur auf den Hook-Werten (Verhältnis über der Schwelle) → rot.
  - Langsamerer Runner: Bezug und Hook beide mal 1,7 → grün.
  - Einzelausreißer in Bezug und Hook, auch ein ganzer gestörter Block von fünf → grün.
  - Im relativen Modus macht ein p99 über 20 ms allein nicht rot; im absoluten Modus weiterhin schon.
  - Fehlen die Bezugs-Werte im relativen Modus, bricht das Skript mit Exit 2 ab, statt grün zu melden.
  - Die bestehenden Prüfungen (p95 besteht, p99 fällt, p95-Spalte) bleiben unverändert.
- Echte Fehler-Injektion in der CI: `BENCH_INJECT=git` lässt das Bench vor jedem Hook-Aufruf einen `git`-Prozess starten (nur im Bench, kein Code in `src`). Der relative Schritt muss auf beiden Betriebssystemen rot werden. Run-ID: wird nachgetragen. Im finalen Stand ist die Injektion nicht aktiv.
- Beleg: grüner CI-Lauf auf dem Gate-Branch mit dem relativen Gate auf beiden Betriebssystemen. Run-ID: wird nachgetragen.

## Folgen

- Die CI wird nicht mehr rot, nur weil ein Runner langsam ist.
- C1 bleibt offen (Issue #70). Dieses ADR ändert daran nichts; die Arbeit liegt beim Wrapper und beim Programmstart (#70, #31).
- Neue Szenarien brauchen eine Zuordnung zu einer Klasse. Ein unbekanntes Szenario bekommt die strengere Schwelle 1,55.
- Wird der Hook absichtlich teurer, muss die Schwelle mit Messwerten angepasst werden. Das ist beabsichtigt.
- Bekannte zweite Störquelle, hier nicht behoben: `apt-get update` in der CI scheiterte am 11.11. sechsmal an einer DNS-Störung des Runners.
