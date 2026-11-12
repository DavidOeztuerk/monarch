# ADR-0015 — Relatives Latenz-Gate in der CI

**Datum:** 11.11.2026 · **Status:** angenommen (Design-Runde Warden G-0108: REQUEST_CHANGES am Stand `a25689b`, sechs major-Befunde eingearbeitet, Delta-Runde APPROVE am Stand `70106a0`) · **Quest:** Q-0148 (G-0108) · **Issue:** #8

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
- **Der Unterschied ist gemessen.** Der kleinste Hook-Pfad ohne System.CommandLine ist `monarch hook <unbekanntes Ereignis>` mit leerer Eingabe in einem Ordner ohne Monarch; er endet ohne Arbeit (`HookRunner.cs:69-73`). Lokal auf dem Mac, 300 Läufe (Messung des Wardens in der Design-Runde, Mittelwerte): `--version` 7,3 ms, dieser Leerlauf-Hook 7,2 ms.
- Das Bench misst diesen Vergleich in jedem Lauf mit und gibt ihn aus (Zeile `reference check`). In der CI (Läufe 43069811876 und 43071057248): macOS `--version` 0,15 bis 0,16 ms über dem Leerlauf-Hook (2 bis 3 %), Linux 0,45 bis 0,61 ms (8 bis 11 %). Der Bezug ist auf Linux also spürbar größer als der reine Start. Wächst der Befehlsbaum, fällt das in dieser Zeile auf.
- Der Leerlauf-Hook wäre der genauere Bezug. Die DoD legt `--version` fest; ein Wechsel ist ein Owner-Entscheid.

### Schwellen

Regel: **Schwelle = größter gemessener Wert der ersten Messreihe je Betriebssystem und Klasse (über beide Modi) + 0,25**. Die Schwellen werden danach nicht automatisch nachgezogen, wenn ein späterer Lauf einen höheren Wert zeigt. Sonst stiege die Schwelle mit jedem Ausreißer.

Erste Messreihe: Lauf 43060173567 auf dem Commit `e09b614`. Werte sind die Mediane der Block-Verhältnisse je Szenario.

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
| mit Ledger | 2,40 | 1,70 |

Kontrolle mit dem fertigen Gate, ohne Injektion (Lauf 43069811876, Commit `4923e73`, grün auf beiden Betriebssystemen):

| Betriebssystem, Modus | ohne Ledger | mit Ledger |
|---|---|---|
| Linux, direkt | 1,08–1,38 | 1,92–1,97 |
| Linux, Wrapper | 1,05–1,31 | 1,84–1,97 |
| macOS, direkt | 1,06–1,16 | 1,27–1,31 |
| macOS, Wrapper | 1,00–1,18 | 1,08–1,20 |

- Linux „ohne Ledger“ zeigte hier 1,38 (long-heredoc, direkt), mehr als die 1,30 der ersten Messreihe. Die Schwelle bleibt 1,55; der Abstand beträgt damit 0,17 statt 0,25. long-heredoc liegt auf Linux durchweg höher als die übrigen Szenarien dieser Klasse (1,24 bis 1,38 gegen höchstens 1,26).
- Linux „mit Ledger“ ist mit jetzt vier Läufen ohne Injektion bestätigt (höchster Wert weiter 2,15). Die Schwelle 2,40 gilt. Werte aus dem Injektionslauf sind in keine Schwelle eingegangen.
- Überschreitet ein Lauf ohne Code-Änderung eine Schwelle in beiden Versuchen, wird neu gemessen und hier entschieden, nicht still angehoben.

- Die Klasse „mit Ledger“ schreibt nach der Entscheidung eine Zeile ins Ledger (`src/Monarch.Cli/HookRunner.cs:199-223`). Der ganze Aufschlag dieser Szenarien auf den Bezug lag auf Linux bei 4,50 bis 7,66 ms, auf macOS bei 1,13 bis 2,72 ms. Der Anteil des Ledgers daran (Aufschlag minus in-scope) liegt auf Linux bei etwa 3,7 bis 6,5 ms, auf macOS bei etwa 0 bis 1,9 ms. Warum Linux so viel teurer ist, ist nicht belegt und wird ein eigenes Issue.
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
- Im finalen Stand ist die Injektion nicht aktiv; `ci.yml` setzt `BENCH_INJECT` nicht.

**Ergebnis der Injektion** (Lauf 43071057248, Commit `fa1377c`, Schritt „Hook latency, relative gate (direct)“ rot auf beiden Betriebssystemen):

| Betriebssystem | Kosten der Injektion | Verhältnisse mit Injektion | über der Schwelle |
|---|---|---|---|
| macOS (Job 130699480479) | 6,47 ms | ohne Ledger 2,14–2,62, mit Ledger 2,51–2,61 | 8 von 8 Szenarien |
| Linux (Job 130699480518) | 1,78 ms | ohne Ledger 1,41–1,64, mit Ledger 2,34–2,45 | 2 von 8 Szenarien (long-heredoc 1,64, out-of-scope 2,45) |

- Auf macOS ist ein `git`-Start teuer, der Nachweis ist dort eindeutig.
- Auf Linux kostet derselbe `git`-Start nur 1,78 ms. Das Gate wurde rot, aber knapp: Sechs Szenarien blieben unter der Schwelle (1,41 bis 1,55 bei Schwelle 1,55; 2,34 bei 2,40). **Ein Regress von knapp 2 ms liegt auf Linux an der Erkennungsgrenze.** Das passt zur Tabelle „Gate wird rot ab“.
- Zum Vergleich: Das alte Gate (p99 ≤ 20 ms) hätte denselben Regress auf Linux nur durch einen Ausreißer bemerkt. Sieben von acht Szenarien lagen mit Injektion bei p99 12 bis 19 ms.
- Der Wrapper-Schritt lief in diesem Lauf nicht, weil der Direkt-Schritt schon rot war.

**Beleg grün:** Lauf 43069811876 (Commit `4923e73`) mit dem relativen Gate, direkt und über den Wrapper, auf beiden Betriebssystemen; Zahlen in der Kontrolltabelle oben. Der Lauf am finalen Head steht im PR.

## Folgen

- Die CI wird nicht mehr rot, nur weil ein Runner langsam ist.
- C1 bleibt offen (Issue #70). Dieses ADR ändert daran nichts; die Arbeit liegt beim Wrapper und beim Programmstart (#70, #31).
- ADR-0011 Regel 9 und der Kommentar in `ci.yml` verweisen auf dieses ADR; `docs/lessons.md` bekommt einen Nachtrag.
- Neue Szenarien brauchen eine Zuordnung zu einer Klasse.
- Wird der Hook absichtlich teurer, muss die Schwelle mit Messwerten angepasst werden. Das ist beabsichtigt.
- Zweite Störquelle, hier nicht behoben: `apt-get update` in der CI scheiterte am 11.11. sechsmal an einer DNS-Störung des Runners (Lauf 43026276365, Versuche 4 bis 7; Lauf 43060173567, Versuche 1 und 2).
- Offen als Issues (minor aus der Design-Runde): eine Untergrenze gegen ein Gate, das nichts prüft, und eine einmalige Prüfung, dass jedes Szenario seine erwartete Entscheidung liefert.

## Nachtrag 12.11.2026: C1 unter Last auf dem Mac (Q-0150, G-0109)

**Status:** Design-Runde Warden G-0109: REQUEST_CHANGES am Stand `105f972`, drei major-Befunde eingearbeitet (falsche Angaben, Tests schon vorher grün, CI-Sperre zu stark formuliert), Delta-Runde APPROVE am Stand `e7ed10d`.

### Kurz in einfacher Sprache

- C1 ist das MVP-Kriterium für die Hook-Latenz auf dem Mac des Owners. Bisher hieß es: 99 von 100 Hook-Starts bleiben unter 20 ms.
- Der Owner arbeitet meist unter voller Last. Dann braucht schon ein leerer Programmstart länger als 20 ms. Kein Hook kann das ausgleichen.
- C1 hat deshalb seit dem 12.11. zwei Teile. Teil 1: 20 ms im p99 auf ruhiger Maschine, wie bisher. Teil 2: Unter Last darf der Hook höchstens das **1,64-Fache** des bloßen Programmstarts kosten.
- Für Teil 2 bekommt `bench/hook-latency.sh` einen eigenen Modus `GATE_MODE=c1-load`. Er läuft nur auf macOS und nie in der CI.
- **Die CI ändert sich nicht.** Ihre Schwellen, ihre Schritte und die Modi `absolute` und `relative` bleiben, wie sie sind.

### Entscheide des Owners (genannt, nicht neu ausgelegt)

Entscheid vom 12.11.2026, wörtlich aus `09-proof-report.md`, Unterabschnitt „Entscheid des Owners vom 12.11.“:

> 1. **Die 20 ms im p99 gelten auf ruhiger Maschine**: WT-Stack läuft, keine Nebenprogramme, Messung über den Wrapper mit dem installierten Binary. Die Zahl selbst bleibt.
> 2. **Unter voller Last gilt ein relativer Wert**: Der Hook darf höchstens einen festen Faktor langsamer sein als der bloße Programmstart (`monarch --version`) auf derselben Maschine im selben Moment, nach dem Muster von ADR-0015. […]

Entscheid zum Faktor vom 12.11.2026, 21:00: **1,64**, abgeleitet aus der Messung unter Abendlast (`evidence/teil-c/latenz-2026-11-12/gate-last-2042-*`, im Ordner `monarch-proof` des Owners): höchstes p95 1,39 plus 0,25. Dieser Faktor ersetzt den Vorschlag der Session aus der Nacht (1,48 ohne Ledger, 1,62 mit Ledger; `09-proof-report.md`, „Vorschlag für den Faktor“).

### Messwerte

Alle Messungen: Mac des Owners (M3 Pro, macOS 27.2), installiertes Binary `1.0.0+d28148ed`, echter Wrapper, echtes HOME, `GATE_MODE=relative ATTEMPTS=1`, sechs Läufe je Reihe. Der Wert je Szenario ist der Median aus fünf Block-Verhältnissen, wie im CI-Gate. Load und die größten Prozesse sind je Lauf notiert, am Anfang und am Ende, nicht je Block. Das weicht von der DoD ab; je Block gibt es sie nur in Reihe A.

| Reihe | Zeit | Load (1 min) | Klasse | n | Median | p95 | größter | Streuung (sd) |
|---|---|---|---|---|---|---|---|---|
| B: Nacht, mäßige Last | 03:19–03:27 | 2,5–4,0 | ohne Ledger | 36 | 1,08 | 1,23 | 1,52 | 0,09 |
| | | | mit Ledger | 12 | 1,19 | 1,31 | 1,31 | 0,06 |
| C: künstliche Last (24 CPU-Schleifen) | 03:27–03:44 | 12–105 | ohne Ledger | 36 | 1,07 | 1,12 | 1,15 | 0,03 |
| | | | mit Ledger | 12 | 1,15 | 1,21 | 1,21 | 0,03 |
| **Abendlast** | 20:42–20:54 | 4,1–25,0 | ohne Ledger | 36 | 1,08 | **1,39** | 1,65 | 0,17 |
| | | | mit Ledger | 12 | 1,10 | 1,69 | 1,69 | 0,19 |

- Abendlast laut den Logs: Docker-VM bis 106 % CPU, Opera, WindowServer, Spotlight, WebKit, eine Claude-Session; zeitweise `homed` und `trustd` über 70 %. Die Logs zeigen nur die fünf größten Prozesse. Ob das die „volle Last“ aus dem Entscheid war (mehrere VS-Code-Fenster, Safari, Terminal, ChatGPT, Claude Desktop), belegen sie nicht; VS Code taucht darin nicht auf.
- Unter Abendlast lag der p99 in 44 von 48 Szenario-Läufen über 20 ms (Median der p99-Werte 30,3 ms, höchstens 249,7 ms). Der Median des Verhältnisses blieb bei 1,08 und 1,10.
- Dazu Reihe A vom selben Tag (40 Minuten, 744 Blöcke über den Wrapper): einzelner Block Median 1,07 und p95 1,39 ohne Ledger, 1,21 und 1,51 mit Ledger. Details in `09-proof-report.md`, Abschnitt „C1 relativ“.

### Was der Modus tut

Aufruf: `GATE_MODE=c1-load bench/hook-latency.sh plugin/scripts/monarch`, also über den Wrapper mit dem installierten Binary.

- Gemessen und gerechnet wird genau wie im Modus `relative`: fünf Blöcke, je 60 Läufe Bezug und 60 Läufe Hook, Median der fünf Block-Verhältnisse, höchstens zwei Versuche.
- Anders ist nur die Schwelle: **1,64 für jedes Szenario**. Sie steht fest im Skript, getrennt von den CI-Schwellen.
- Die absoluten Werte p50, p95 und p99 werden weiter ausgegeben, entscheiden hier aber nicht. Für sie gilt Teil 1 von C1 auf ruhiger Maschine (`GATE_MODE=absolute`, der Standard).

### Ein Faktor für alle Szenarien

Der Entscheid nennt einen Faktor. Der Modus wendet ihn deshalb auf alle acht Szenarien an, auch auf die zwei mit Ledger. Offen benannt:

- Der eine Faktor gilt für beide Klassen. Die DoD sprach noch von einem Faktor je Klasse; der Entscheid kam danach.
- Die 1,39 sind das p95 der Klasse „ohne Ledger“ und zugleich das p95 über alle 48 Abendwerte zusammen. Die Klasse „mit Ledger“ hatte unter Abendlast ein p95 von 1,69. Das ist bei zwölf Werten zugleich der größte Wert und stammt aus einem einzigen Lauf (out-of-scope, Lauf 1, bei Load 7,5).
- Für „mit Ledger“ ist 1,64 strenger als die CI-Schwelle für macOS (1,70). Für „ohne Ledger“ ist 1,64 lockerer als die CI-Schwelle (1,47). Darum darf der Modus die CI nie ersetzen, siehe unten.
- Ob „mit Ledger“ einen eigenen Wert bekommt, ist eine Frage an den Owner. Dieser Nachtrag legt keinen fest.

### Was die Abendmessung gegen 1,64 zeigt

- 46 von 48 Szenario-Läufen lagen im ersten Versuch bei höchstens 1,64. Darüber lagen out-of-scope mit 1,69 (Lauf 1) und bash-targets mit 1,65 (Lauf 2).
- Vier von sechs Läufen wären damit im ersten Versuch ganz grün gewesen. Die Messung lief mit einem Versuch. Ob der zweite Versuch die beiden anderen Läufe gerettet hätte, ist nicht gemessen.
- Vermutlich war in beiden Fällen der Moment gestört, belegt ist es nicht. Hinweise: Bei out-of-scope lag der p99 bei 145,8 ms (Load 7,5, Spotlight bei 63 %), bei bash-targets bei 249,7 ms.

### Der Modus kann die CI nicht lockern

- Nur auf macOS. Auf jedem anderen Betriebssystem endet der Modus mit Exit 2.
- Nicht in der CI: Ist `CI` oder `GITHUB_ACTIONS` gesetzt, endet der Modus mit Exit 2. „Gesetzt“ meint auch einen leeren Wert.
- Das verhindert eine stille oder versehentliche Nutzung. Eine absichtliche verhindert es nicht: Ein Schritt in `ci.yml` könnte die Variablen entfernen. Das wäre aber eine sichtbare Änderung an `ci.yml` und geht durch den Warden.
- Zusätzlich schlägt `bench/test-gate.sh` fehl, wenn `ci.yml` das Wort `c1-load` als Wert enthält oder ein Latenz-Schritt einen anderen `GATE_MODE` als `relative` trägt. Der Selbsttest läuft in der CI blockierend.
- Hintergrund: Auf dem macOS-Runner würde ein Wechsel von `relative` auf `c1-load` die Schwelle „ohne Ledger“ von 1,47 auf 1,64 heben.
- `MONARCH_BIN` ist in diesem Modus nicht erlaubt (Exit 2). Sonst startete der Wrapper ein anderes Binary als das installierte. Der Modus gibt aus, welche Version er misst.
- Die Schwelle steht fest im Skript und lässt sich nicht über die Umgebung ändern.
- Alle Prüfungen aus „Das Gate lässt sich nicht still abschalten“ gelten auch hier: `ATTEMPTS` nur 1 oder 2, fünf Blöcke und 60 Läufe fest, fehlende Daten sind ein Fehler.
- `GATE_MODE` kennt danach genau drei Werte: `absolute`, `relative`, `c1-load`. Alles andere bleibt Exit 2.
- An `absolute` und `relative` ändert sich nichts, auch nicht an ihren Schwellen.

### Was das Kriterium erkennt und was nicht

- Rot wird es ab einem Aufschlag von etwa `(1,64 − Verhältnis) × Bezug`. Unter Abendlast (Verhältnis 1,08, Bezug im Median 14,5 ms) sind das rund 8 ms, bei mäßiger Last nachts (Reihe B, Bezug 10,9 ms) rund 6 ms. Unter Last ist das Kriterium also gröber als die CI.
- **Erkennt nicht:** ein Binary oder einen Wrapper, die insgesamt langsamer werden (Bezug und Hook gleich; auch der Bezug läuft über den Wrapper). Dafür bleibt Teil 1 von C1.
- **Erkennt nicht:** Ausreißer. Unter Last kommen sie von der Maschine.
- **Grenzen der Messung:** ein Mac, ein Abend, zwölf Minuten, 48 Szenario-Läufe. Reihe C war künstliche Last und belastet nur die CPU.

### Teststrategie

`bench/test-gate.sh`, deterministisch über `BENCH_RESULTS_DIR`, vorher rot, danach grün. Die Fälle des Modus laufen dort mit einem `uname`, das `Darwin` meldet, und ohne `CI` und `GITHUB_ACTIONS`. So prüft auch der Linux-Runner sie.

- Knapp unter 1,64 in beiden Klassen → grün. Knapp über 1,64 nur „ohne Ledger“ → rot. Knapp über 1,64 nur „mit Ledger“ → rot.
- Ein Verhältnis von 1,60 „ohne Ledger“ ist in `c1-load` grün und in `relative` auf macOS weiter rot (CI-Schwelle 1,47 unverändert).
- Ein Verhältnis von 1,68 „mit Ledger“ ist in `c1-load` rot und in `relative` auf macOS weiter grün (CI-Schwelle 1,70 unverändert).
- Die Fälle mit Exit 2 laufen auf Daten, die ohne die Schutzprüfung grün wären, und prüfen zusätzlich die Meldung auf stderr. Sonst wären sie schon vorher grün, weil der Modus heute unbekannt ist und deshalb ebenfalls mit Exit 2 endet.
  - `uname` meldet Linux → Exit 2 mit „c1-load: only on macOS“.
  - `CI=true`, `GITHUB_ACTIONS=true` und `CI=` (leer) → je Exit 2 mit „c1-load: not in CI“.
  - `MONARCH_BIN` gesetzt → Exit 2 mit eigener Meldung.
  - `ATTEMPTS=0` → Exit 2 mit der Meldung zu `ATTEMPTS`. Fehlende Bezugs-Datei → Exit 2 mit der Meldung zur fehlenden Datei.
- `relative` wertet mit `CI=true GITHUB_ACTIONS=true` weiter aus (Exit 0 auf den Grunddaten). Die CI-Sperre wirkt also nur auf `c1-load`.
- Die Schwelle lässt sich nicht über die Umgebung heben: Daten knapp über 1,64 bleiben rot, auch wenn der interne Variablenname der Schwelle und ähnliche Namen in der Umgebung auf 99 stehen.
- `ci.yml` wird geprüft: kein `c1-load` als Wert, jeder `GATE_MODE` dort ist `relative`. Gegenprobe an einer veränderten Kopie: Der Test wird rot.
- Alle bestehenden Fälle bleiben unverändert und grün.

Dazu ein echter Lauf des Modus auf dem Mac des Owners als Funktionsnachweis, Ausgabe im PR. Er geht in keinen Faktor ein.

### Folgen

- C1 Teil 2 ist mit diesem Modus prüfbar. Ob C1 damit als erfüllt gilt, entscheidet der Owner am Bericht, nicht dieses ADR.
- Der Kommentar zu C1 in `.github/workflows/ci.yml` nennt die Bedingung „ruhige Maschine“ und diesen Nachtrag. Die CI-Schritte ändern sich nicht.
- ADR-0011 Regel 9 und `docs/lessons.md` bekommen je einen kurzen Nachtrag.
- Wer die 1,64 ändert, braucht einen Owner-Entscheid und trägt Messwerte hier nach.
