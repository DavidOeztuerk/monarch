# ADR-0015 — Relatives Latenz-Gate in der CI

**Datum:** 11.11.2026 · **Status:** vorgeschlagen (Design-Runde Warden G-0108: REQUEST_CHANGES am Stand `a25689b`, sechs major-Befunde hier eingearbeitet, Delta-Runde steht aus) · **Quest:** Q-0148 (G-0108) · **Issue:** #8

## Kurz in einfacher Sprache

- Der Hook läuft vor jedem Tool-Aufruf. Ein Test in der CI soll verhindern, dass er unbemerkt langsamer wird.
- Bisher prüfte die CI eine feste Zahl: 99 von 100 Starts müssen unter 20 ms bleiben. Auf den geteilten Rechnern von GitHub hängt diese Zahl vor allem davon ab, wie schnell der Rechner gerade ist. Der Test wurde oft rot, obwohl am Code nichts geändert war.
- Künftig misst die CI auf demselben Rechner zusätzlich, wie lange das Programm zum bloßen Starten braucht, und prüft, **um welchen Faktor der Hook langsamer ist als der bloße Start**. Ein langsamer Rechner macht beides langsamer. Der Faktor schwankt deshalb viel weniger als die Millisekunden, ganz gleich bleibt er nicht (siehe „Restrisiko“).
- **Das ist eine Lockerung, und sie ist gewollt:** Die feste Grenze von 20 ms macht die CI nicht mehr rot. Sie gilt weiter auf dem Mac des Owners (MVP-Kriterium C1, Issue #70) und ist dort heute nicht erfüllt. Die CI zeigt die absoluten Werte weiter an.
- Der Owner hat das am 11.11.2026 entschieden.

## Problem (belegt)

1. **Bis zur Auswertung am 11.11., 16:25 UTC, kam jeder rote CI-Versuch von diesem Gate.** Von 87 Versuchen seit dem 27.10. waren 24 rot. In allen 24 scheiterten nur Latenz-Schritte, nie Build oder Tests (Aufstellung in Issue #8, Kommentar vom 11.11.). Danach kam eine zweite Störquelle dazu, siehe „Folgen“.
2. **Gleicher Code, verschiedenes Ergebnis.** Lauf 41038797796 (Commit ohne Code-Änderung): erster Versuch rot, Wiederholung grün. Lauf 43026276365 auf `develop` (`2b3c86aa`): dreimal rot mit p99 94,55, 20,22 und 20,72 ms, später grün.
3. **Die Runner sind verschieden schnell.** Der bloße Programmstart lag auf Linux-Runnern im Direkt-Modus zwischen 4,49 und 7,71 ms (Lauf 43060173567, Versuche 3 bis 5), also bis zum 1,7-Fachen auseinander. Eine feste Grenze in Millisekunden misst damit den Runner.
4. **Das eigentliche Kriterium erfasst die CI nicht.** p99 ≤ 20 ms über den Wrapper auf dem Mac des Owners ist verfehlt (Issue #70). Die CI war dabei grün.

## Entscheidung

### Was gemessen wird

Je Szenario fünf Blöcke. In jedem Block misst `bench/hook-latency.sh` erst den **Bezug**, dann das **Szenario**, je 60 Läufe mit hyperfine:

- Bezug: `monarch --version`, im selben Modus (direkt das Binary, oder über `plugin/scripts/monarch`), im selben Ordner, mit derselben Eingabe.
- Szenario: `monarch hook pre-tool-use` wie bisher.

Für jeden Block gilt `Verhältnis = Median(Szenario) / Median(Bezug)`. Der Wert des Szenarios ist der **Median der fünf Block-Verhältnisse**. Bezug und Szenario laufen abwechselnd, damit beide denselben Zustand des Rechners sehen.

Ein Szenario besteht, wenn sein Wert in einem von höchstens zwei Versuchen die Schwelle nicht überschreitet. Das ist dieselbe Regel wie bisher.

### Warum `monarch --version` als Bezug

- Der Befehl startet dasselbe Binary, liest keine Projektdateien und schreibt nichts. Er kostet also Prozessstart und Laufzeit-Initialisierung.
- Er ist nicht identisch mit dem Startpfad des Hooks: `--version` baut den Befehlsbaum von System.CommandLine auf, der Hook umgeht ihn (`src/Monarch.Cli/MonarchApp.cs:12-28`).
- **Der Unterschied ist gemessen.** Der kleinste Hook-Pfad ohne System.CommandLine ist `monarch hook <unbekanntes Ereignis>` mit leerer Eingabe in einem Ordner ohne Monarch; er endet ohne Arbeit (`HookRunner.cs:69-73`). Lokal auf dem Mac, 300 Läufe (Messung des Wardens in der Design-Runde): `--version` 7,3 ms, dieser Leerlauf-Hook 7,2 ms. Der Anteil von System.CommandLine liegt also bei rund 2 %.
- Das Bench misst diesen Vergleich künftig in jedem Lauf mit und gibt ihn aus (`Bezugsprüfung`). Wächst der Befehlsbaum, fällt das dort auf.
- Der Leerlauf-Hook wäre der genauere Bezug. Die DoD legt `--version` fest; ein Wechsel ist ein Owner-Entscheid.

### Schwellen

Regel: **Schwelle = größter gemessener Wert je Betriebssystem und Klasse (über beide Modi) + 0,25**.

Gemessen im Lauf 43060173567 auf dem Commit `e09b614`. Werte sind die Mediane der Block-Verhältnisse je Szenario.

| Betriebssystem, Modus | Versuche | ohne Ledger | mit Ledger |
|---|---|---|---|
| Linux, direkt | 3, 4, 5 | 1,05–1,30 | 1,82–2,15 |
| Linux, Wrapper | 3, 4, 5 | 1,02–1,27 | 1,69–2,08 |
| macOS, direkt | 1, 2, 3, 4, 5 | 1,06–1,22 | 1,23–1,45 |
| macOS, Wrapper | 1, 2, 4, 5 | 1,01–1,18 | 1,14–1,29 |

- Linux lieferte in den Versuchen 1 und 2 keine Werte (`apt-get` scheiterte). Auf macOS lief in Versuch 3 der Wrapper-Schritt nicht, weil der Direkt-Schritt am alten p99-Gate rot war.
- Job-IDs: Linux 130668581171, 130673816226, 130677650068; macOS 130661004863, 130664873940, 130668581225, 130673815592, 130677649846.
- „ohne Ledger“: in-scope, non-awakened, bash-targets, abs-target, nested-projects, long-heredoc. „mit Ledger“: out-of-scope, denied-bash.

| Klasse | Schwelle Linux | Schwelle macOS |
|---|---|---|
| ohne Ledger | 1,55 | 1,47 |
| mit Ledger | 2,40 (vorläufig) | 1,70 |

- Die Klasse „mit Ledger“ schreibt nach der Entscheidung eine Zeile ins Ledger (`src/Monarch.Cli/HookRunner.cs:199-223`). Der ganze Aufschlag dieser Szenarien auf den Bezug lag auf Linux bei 4,50 bis 7,66 ms, auf macOS bei 1,13 bis 2,72 ms. Der Anteil des Ledgers daran (Aufschlag minus in-scope) liegt auf Linux bei etwa 3,7 bis 6,5 ms, auf macOS bei etwa 0 bis 1,9 ms. Warum Linux so viel teurer ist, ist nicht belegt und wird ein eigenes Issue.
- **Die Linux-Schwelle „mit Ledger“ ist vorläufig.** Sie stützt sich auf drei Versuche. Sie wird erst mit den weiteren Linux-Läufen dieser Quest (Injektion und grüner Beleg) festgeschrieben; die Werte werden hier nachgetragen.
- Die Schwellen stehen fest im Skript. Sie lassen sich **nicht** über Umgebungsvariablen ändern. Wer sie ändert, trägt Messwerte und Run-ID hier nach.
- Die Schwellen gelten je Klasse, nicht je Szenario. Szenarien mit kleinem Verhältnis (non-awakened 1,02 bis 1,16) sind dadurch weniger empfindlich als solche nah an der Schwelle.

### Restrisiko: Der Faktor ist nicht ganz unabhängig vom Runner

Die Ledger-Kosten wachsen nicht im selben Maß wie der Programmstart. Auf dem schnellsten Linux-Runner (Bezug 4,5 bis 5,7 ms, Versuch 3) lag die Klasse „mit Ledger“ bei 2,01 bis 2,15, auf den langsameren (Bezug 7,1 bis 8,5 ms) bei 1,69 bis 1,96. Auf einem noch schnelleren Runner kann das Verhältnis also über 2,15 steigen. Der Abstand von 0,25 und der zweite Versuch fangen das ab, ausgeschlossen ist ein Fehlalarm dort nicht.

### Was das Gate erkennt und was nicht

Ab welchem Aufschlag das Gate rot wird, folgt aus `(Schwelle − Verhältnis) × Bezug`. Nachgerechnet an den Messwerten oben (Rechnung des Wardens in der Design-Runde, mit 1,55 statt 1,47 für macOS):

| Der Aufschlag trifft … | Gate wird rot ab |
|---|---|
| alle Szenarien gleich (etwa ein Prozessstart ganz am Anfang des Hooks) | 1,2 bis 3,7 ms |
| nur die Szenarien mit Ledger | bis zu 4,2 ms |
| nur ein einzelnes Szenario | bis zu 6,0 ms |

- **Erkennt nicht:** eine Verlangsamung, die Bezug und Hook gleich trifft, etwa ein größeres Binary oder eine langsamere Laufzeit. Dafür bleiben die absoluten Werte im Log und die Messung auf dem Mac.
- **Erkennt nicht:** Ausreißer (p99). Auf geteilten Runnern kommen sie vom Runner.
- **Erkennt nicht:** schleichende Verlangsamung unterhalb der Schwelle.

### Was mit der Grenze von 20 ms geschieht

- `LIMIT_MS` bleibt 20, `GATE_PCT` bleibt 99. Ohne weitere Angabe verhält sich `bench/hook-latency.sh` wie bisher (`GATE_MODE=absolute`). So wird auf dem Mac des Owners gemessen.
- Die CI ruft das Skript je Modus **einmal** mit `GATE_MODE=relative` auf. Dann entscheidet nur das Verhältnis über rot oder grün. Die absoluten Werte p50, p95 und p99 werden aus denselben Messwerten berechnet und stehen weiter im Log und in der Job-Summary, mit dem Vermerk, ob 20 ms eingehalten wären.
- Der Berichtsschalter `REL_REPORT` aus `e09b614` entfällt.
- **Lockerung, offen benannt:** Die Schritte „Hook latency (direct)“ und „Hook latency (through plugin/scripts/monarch)“ blockierten bisher auf p99 ≤ 20 ms (der Wrapper-Schritt auf macOS stand in Quarantäne). Das entfällt in der CI. `docs/lessons.md` hält fest, dass dieses Gate schon einmal still aufgeweicht wurde (`5abdd63`); diesmal geschieht es mit ADR, Owner-Entscheid und Warden.

### Das Gate lässt sich nicht still abschalten

Das Skript prüft seine Eingaben und bricht sonst mit Exit 2 ab, in beiden Modi:

- `ATTEMPTS` nur 1 oder 2. (Befund der Design-Runde: Mit `ATTEMPTS=0` meldete das Skript bisher „OK“ ohne eine Messung.)
- `GATE_MODE` nur `absolute` oder `relative`.
- `BENCH_INJECT` nur leer oder `git`; der Wert wird nie als Befehl ausgeführt.
- Im relativen Modus sind fünf Blöcke und 60 Läufe fest, nicht einstellbar.
- Fehlen Bezugs-Werte, ist ein Bezugs-Median 0 oder leer, oder fehlt ein Block, gilt das als Fehler, nicht als bestanden.
- Ein unbekanntes Szenario bekommt die strengere Schwelle „ohne Ledger“.

### CI

- Der relative Schritt blockiert auf Linux und macOS, direkt und über den Wrapper. Er trägt nie `continue-on-error`.
- Die macOS-Quarantäne (`ci.yml:84-91`, Summary-Zeile `:97`) entfällt, weil der relative Schritt den Wrapper-Modus blockierend abdeckt.
- Der Selbsttest `bench/test-gate.sh` bleibt blockierend.
- `permissions` bleiben `contents: read`. Es wird kein Basis-Binary gebaut und kein Artefakt aus fremden Läufen geladen.

## Verworfen

- **A/B gegen ein Basis-Binary** (Q-0136, Q-0141): robuster gegen einen driftenden Bezug, braucht aber einen zweiten Build je Lauf und eine Regel, was die Basis ist. Zweimal geplant, zweimal nicht begonnen.
- **Gate ganz streichen:** Dann fiele ein `git`-Aufruf auf dem heißen Pfad erst auf dem Mac auf.
- **Grenze anheben oder p95 statt p99:** ändert nichts daran, dass eine feste Zahl den Runner misst.

## Teststrategie

`bench/test-gate.sh`, deterministisch über `BENCH_RESULTS_DIR` mit Bezugs-Werten, vorher rot, danach grün:

- Regression nur auf den Hook-Werten (Verhältnis über der Schwelle) → rot.
- Grenzfall je Klasse für das Betriebssystem, auf dem der Test läuft: knapp unter der Schwelle grün, knapp darüber rot.
- Langsamerer Runner: Bezug und Hook beide mal 1,7 → grün.
- Einzelausreißer in Bezug und Hook, auch zwei gestörte Blöcke von fünf → grün.
- Im relativen Modus macht ein p99 über 20 ms allein nicht rot; im absoluten Modus weiterhin schon.
- Je ein Fall mit Exit 2: `ATTEMPTS=0`, `ATTEMPTS=3`, unbekannter `GATE_MODE`, unbekannter `BENCH_INJECT`, fehlende Bezugs-Datei, Bezugs-Median 0, zu wenige Blöcke.
- Eine gesetzte Schwellen-Variable aus der Umgebung ändert das Ergebnis nicht.
- Die bestehenden Prüfungen (p95 besteht, p99 fällt, p95-Spalte) bleiben unverändert.

Echte Fehler-Injektion in der CI, `BENCH_INJECT=git`:

- Das Bench legt in seinem Arbeitsordner ein Skript an, das erst `git --version` startet (Eingabe und Ausgabe nach `/dev/null`) und dann per `exec` das Programm aufruft. Die Eingabe des Hooks bleibt unberührt.
- Die Injektion liegt im gemessenen Befehl, nicht in einem Vorbereitungsschritt, und trifft **nur die Hook-Seite**. Der Bezug läuft ohne sie.
- Das Bench misst die Kosten der Injektion selbst (Bezug mit gegen Bezug ohne Injektion) und gibt sie in ms aus. So ist belegt, wie groß der Regress war, den das Gate erkannt hat.
- Erwartung: Der relative Schritt wird auf beiden Betriebssystemen rot. Run-ID und gemessene Kosten: werden nachgetragen.
- Im finalen Stand ist die Injektion nicht aktiv; `ci.yml` setzt `BENCH_INJECT` nicht.

Beleg: grüner CI-Lauf auf dem Gate-Branch mit dem relativen Gate auf beiden Betriebssystemen. Run-ID und Zahlen: werden nachgetragen.

## Folgen

- Die CI wird nicht mehr rot, nur weil ein Runner langsam ist.
- C1 bleibt offen (Issue #70). Dieses ADR ändert daran nichts; die Arbeit liegt beim Wrapper und beim Programmstart (#70, #31).
- ADR-0011 Regel 9 und der Kommentar in `ci.yml` verweisen auf dieses ADR; `docs/lessons.md` bekommt einen Nachtrag.
- Neue Szenarien brauchen eine Zuordnung zu einer Klasse.
- Wird der Hook absichtlich teurer, muss die Schwelle mit Messwerten angepasst werden. Das ist beabsichtigt.
- Zweite Störquelle, hier nicht behoben: `apt-get update` in der CI scheiterte am 11.11. sechsmal an einer DNS-Störung des Runners (Lauf 43026276365, Versuche 4 bis 7; Lauf 43060173567, Versuche 1 und 2).
- Offen als Issues (minor aus der Design-Runde): eine Untergrenze gegen ein Gate, das nichts prüft, und eine einmalige Prüfung, dass jedes Szenario seine erwartete Entscheidung liefert.
