# ADR-0012 — Mana-Schätzung je (Modell, Effort)

**Datum:** 28.10.2026 · **Status:** angenommen (Design-Review Warden G-0101, Auflagen eingearbeitet) · **Quest:** Q-0127 (G-0101; ersetzt Q-0126)

## Problem

Die Kosten eines Schritts in Prozentpunkten (pp) der 5h- und Wochenfenster hängen
stark davon ab, welches Modell mit welchem Effort arbeitet. Der Main lief in Lauf 2
mit Opus bei Effort xhigh, Knights und Wardens mit Sonnet. Monarch schätzt heute
pauschal je Quest (`estimate: {session_pct, weekly_pct}`) und kennt das Paar nicht.
Die Schätzungen lagen deshalb systematisch daneben (Lauf 2: G-0100 geschätzt
≈ 70 pp 5h, verbraucht mehrere Fenster).

## Grundsatz

**Modell und Effort wählt der User.** Monarch liest sie, rechnet mit ihnen und
kalibriert je Paar. Monarch schlägt **nie** vor, Modell oder Effort zu senken,
auch nicht bei knappem Mana. Bei knappem Mana bleiben die bestehenden Wege:
stoppen, Recap, Weckruf (ADR-0004, ADR-0008).

## Entscheidung

1. **Beobachtungseinheit = Quest.** Eine Beobachtung ist die Differenz Δpp (5h-Fenster)
   zwischen `quest accept` und `quest submit`. Beide Zeitpunkte halten den Statusline-Snapshot
   (5h, Woche, `observed_at`), das Paar und die Quest-Schätzung fest. Der Schlüssel ist das
   **Paar des Main** `(model, effort)`. Subagent-Token dienen nur dazu, überlappende Quests
   aufzuteilen. Ohne Token-Daten wird eine überlappende Quest nicht kalibriert, nur verbucht
   (Überlappung = ein anderer Session-Eintrag in `mana.json` wurde im Intervall geschrieben).
2. **Eine Einheit: der Faktor auf die Quest-Schätzung.** Schätzung = Quest-Schätzung × Faktor(Paar).
   Quest-Schätzungen bleiben in pp. `Faktor` ist die EMA (α = 0,3) von `Δpp_ist / Δpp_geschätzt`
   (5h; die Wochen-Schätzung wird mit demselben Faktor skaliert). Die EMA startet mit dem
   Startfaktor 1,0. Der Faktor wird auf 0,5 bis 4 geklemmt.
3. **Nicht kalibriert, nur verbucht** wird, wenn im Intervall ein Fenster-Reset liegt, ein
   Snapshot veraltet ist (`stale`), Δpp_geschätzt < 1 pp ist oder die Quest überlappt, ohne dass
   Token-Daten vorliegen. Der Grund steht im Ledger-Event. Status `unkalibriert`, bis n ≥ 3
   kalibrierte Beobachtungen vorliegen.
4. **Quellen des Paars.**
   - `effort` aus `CLAUDE_EFFORT` in der Umgebung des aufrufenden Prozesses. `monarch quest
     accept/submit` läuft über das Bash-Tool; dort ist die Variable gesetzt (Messung in Q-0127:
     `medium`). Sonst `unknown`. Ob der Statusline-Prozess sie sieht, wird nicht gemessen.
   - `model` aus dem sessionsspezifischen Eintrag `sessions[<id>]` in `~/.monarch/mana.json`;
     den schreibt der Statusline-Sensor. Die Session ermittelt der Aufrufer über
     `CLAUDE_CODE_SESSION_ID` (gemessen gesetzt), Fallback `unknown`.
   - Beide Werte werden auf ein festes Vokabular normalisiert (Familie opus/sonnet/haiku;
     Effort low/medium/high/xhigh/max/default; alles andere `unknown`). Freitext aus der Umgebung
     gelangt nie in eine Ausgabe.
   - Das Modell eines Subagents käme aus dem Transcript (`message.model`), das Frontmatter wäre
     nur der Fallback. Das bleibt ungenutzt, weil die Beobachtungseinheit die Quest des Main ist.
5. **Startwerte.** Alle Paare starten mit Faktor 1,0, `unkalibriert`. Die Zahlen aus Lauf 2 sind
   Beleg ohne eigene Einheit und gehen nicht in die Rechnung ein:
   - `(sonnet, *)` Subagent: 146k Token ≈ 8–9 pp, 198k ≈ 11 pp 5h (Verhältnis 5h:Woche ≈ 7:1).
   - `(opus, xhigh)` Main: Mischfenster 28.10. 03:53–05:30 (≈ 1,2 Mio. Sonnet-Subagent-Token plus
     Opus-xhigh-Main): 88 pp 5h, 11 pp Woche.
6. **Anzeige.** `monarch mana`, MCP `mana_forecast` und `quest accept` rechnen mit Schätzung ×
   Faktor und zeigen im festen Format `Paar (model, effort): Faktor 1.30, unkalibriert (n=1)` das
   Paar, den Faktor und den Kalibrierstatus. Der Text enthält keine Empfehlung zu Modell oder
   Effort, auch nicht am Limit; ein Test prüft Ausgaben und alle Skill-Texte gegen eine Deny-Liste.
7. **Kein Einfluss auf Grenzen.** Die Grenzen (5h 90 %, Woche 96 %), `Resolve()` und die
   Recap-Reserve bleiben, wie sie sind. Die Kalibrierung ändert nur die Schätzung; ein Test
   belegt, dass `Resolve()` mit und ohne Kalibrierdatei dasselbe liefert.

## Folgen

- Neues Ledger-Event `ManaStepRecorded` bei `quest submit` (Paar, Schätzung, Ist, Quelle,
  `calibrated` samt Grund). Kalibrierwerte und offene Beobachtungen liegen in
  `.monarch/local/mana-calibration.json`. Schreiber ist das Binary über eine Engine-Datei-API;
  der Pfad liegt unter `.monarch/local/**` und ist per Tool nicht schreibbar.
- Grenze: Ohne Abo-Statusline (API-Key) gibt es kein Δpp, also keine Kalibrierung
  (ADR-0004).

## Nachtrag Q-0142 (G-0105): Die Grenze fällt nie unter `clamp`

**Datum:** 04.11.2026 · **Status:** angenommen (Design-Review Warden G-0105, Auflagen eingearbeitet)

### Kurz in einfacher Sprache

- Monarch hat die Wochengrenze von selbst auf 92 % gesenkt, obwohl in der `system.yaml` „nie unter 96 %“ steht. Dieser Eintrag wurde nie gelesen.
- Künftig gilt: Die Grenze fällt nie unter den unteren `clamp`-Wert, weder im 5h-Fenster noch in der Woche.
- Die Grenze steigt auch nicht von selbst über `work_until_pct`. In den heutigen Repos ist sie damit fest: 5h 90 %, Woche 96 %.
- Alle Befehle nennen denselben Wert. Bisher zeigten Briefing, `status` und `quest accept` den Plan-Wert, `mana` und `rest` den adaptiven Wert.

### Ursache (belegt)

1. `SystemConfigReader` liest je Fenster nur `work_until_pct` (`SystemConfig.cs:76-77`). `clamp` schreibt `awaken` zwar (`AwakenCommand.cs:113-114`), aber kein Code liest es.
2. Die adaptive Grenze läuft stattdessen zwischen `Start − 15` und `Start` (`Support.cs:216`, `:242-243`), in der Woche also zwischen 81 und 96.
3. Der Wert 92 lässt sich nachrechnen (Ledger der Worktree G-0105, 30.10.): fünf `RecapWritten` mit Kosten 0, vier davon innerhalb einer Sekunde, heben die Sperre „ab drei Recaps“ auf. Das p90 der Wochenschritte ist 8, weil der Sprung 84 → 92 über eine Pause von vier Stunden als ein Schritt zählt. `100 − (1,5 · 0 + 8) = 92`.
4. Verschiedene Werte haben zwei Gründe:
   - Der Wechsel hängt an der Zahl der Recaps im Ledger der jeweiligen Root. `mana` vor dem dritten Recap nennt 96, `rest` danach 92 (`RestStarted` um 23:10:11, nach den Recaps). Auf `develop` steht kein `RecapWritten`, dort bleibt es bei 96.
   - Briefing (`HookRunner.cs:383`), `status` (`StatusCommand.cs:42`, `:61`, `:122`, `:124`) und der Guard von `quest accept` (`QuestCommands.cs:89`) nutzen `config.Limits` statt `Resolve()`. Am 04.11. stand im Briefing 96, in `mana` 92.

### Entscheidung

Je Fenster gilt mit `w = work_until_pct` und `clamp: [min, max]`:

| Fall | Grenze |
|---|---|
| Weniger als drei Recaps | `max(w, min)` |
| Ab drei Recaps | `Clamp(adaptiv, min, max(w, min))` |
| `clamp` fehlt oder ist ungültig | wie oben mit `min = w` |
| Ledger nicht lesbar (defekte Zeile, Nicht-Objekt-Zeile) | `max(w, min)` |

- **Gültig** ist `clamp` nur mit genau zwei Einträgen, beide endlich, und `0 ≤ min ≤ max ≤ 100`. Ein nicht endliches `work_until_pct` (`NaN`, `Infinity`) zählt als fehlend, es gilt der Plan-Wert.
- `AdaptiveFloorBelowStart` entfällt.
- `SystemConfig.Limits` bleibt der Rohwert aus der Datei; `ManaSupport.Resolve()` ist die einzige Stelle, die die geltende Grenze bestimmt. Die Zeile `start limits` von `mana` (`ManaCommands.cs:218`) zeigt bewusst weiter den Rohwert, die Zeile `Limits in use` den geltenden Wert.
- `Resolve()` wirft bei Lesefehlern nicht (`JsonException`, `InvalidOperationException`). Eine defekte Ledger-Zeile bleibt ein Befund von `ledger verify` und `status` (`StatusCommand.cs:67`) und darf das **Lesen** der Grenze in `status`, im Briefing und im Guard von `quest accept` nicht abbrechen. Das Schreiben ist davon getrennt: Der Store verweigert `Append` weiter bei einer defekten letzten Zeile der Session-Datei (`JsonlLedgerStore.cs:189`, `:221`). `LedgerScan.All` bleibt streng, weil `gate merge` darüber Verdicts liest.
- Briefing, `status` (Text, `--json`, damit auch MCP `system_status`) und `quest accept` (Guard `QuestCommands.cs:89` und Ablehnungstext `:93`) rufen `Resolve()` auf. Damit nennen alle Befehle in demselben Zustand denselben Wert. Die Aussage aus Punkt 7 oben („Grenzen bleiben“) gilt jetzt auch im Code.
- Alle Ausgaben formatieren Grenzen gleich (`0.#`), damit 96,8 nicht an einer Stelle als 97 erscheint.
- Der obere `clamp`-Wert bleibt ungenutzt. Als Deckel würde er die Woche bis 99 % steigen lassen, über die Owner-Vorgabe 96–98 % hinaus. Ein automatisches Anheben ist eine Owner-Entscheidung.

### Warum das keine Lockerung ist

Der Mana-Guard schützt vor dem Überziehen des Budgets und sichert die Recap-Reserve. Die Woche darf durch diese Änderung nicht weiter ausgeschöpft werden als vorgesehen: Der Wert 96 steht in der `system.yaml`, in Punkt 7 dieses ADR und in der Owner-Vorgabe. Der gesenkte Wert 92 entstand aus einem Messfehler. Die Reserve bleibt unverändert: Im 5h-Fenster rechnet der Guard weiter `Schritt + Recap-Reserve` gegen die Grenze.

### Teststrategie

- `ManaLimitTests.cs`: `clamp [96, 99]`, fünf Recaps mit Kosten 0, Schritt 8 → 7d 96 statt 92 (Fehlerfall vom 30.10.); hohe Recap-Kosten → weiter 96; 5h `clamp [90, 97]` mit Schritten, die adaptiv unter 90 ergeben → 90; `w` unter `min` → `min`.
- Fehlendes oder ungültiges `clamp` → `w`, jeweils mit mindestens drei Recaps und adaptiv unter `w`, damit der Test vorher rot ist. Ungültig sind `[-Infinity, 99]`, `[960, 990]`, `[99, 96]`, ein Skalar, drei Einträge; die Blockliste ist gültig. `w: NaN` → Plan-Wert.
- Gleicher Wert: `mana`, `rest`, `recap`, `status` (Text und `--json`), Briefing, Guard und Ablehnungstext von `quest accept` im selben Zustand, auch in einem Fall, in dem adaptiv und `w` auseinanderlägen (`work_until_pct: 98`, `clamp: [96, 99]`) und mit Bruchwert (96,8).
- Defektes Ledger: `status`, `session-start` und `quest accept` laufen mit einer defekten und einer Nicht-Objekt-Zeile weiter und nennen `max(w, min)`.
- `ConfigTests.cs` bleibt unverändert (`Limits` ist der Rohwert 91/95).

### Folgen

- Die Formel läuft weiter, kann in Repos mit `min = w` aber nichts mehr ändern. Das gilt heute für Monarch, Noelia und Workertransfer.
- Zwei Messschwächen bleiben: Recaps mit Kosten 0 zählen als gemessen, und ein Sprung über eine Pause zählt als ein Schritt. Beide wirken nur noch innerhalb von `clamp`.
- Das Briefing liest bei SessionStart zusätzlich das Ledger. Der Hook-Hot-Path (`PreToolUse`) ist nicht betroffen.
- Die Woche hat keine eigene Recap-Reserve: Kein Aufrufer übergibt `recapEstimate7d`, `Resolved.Recap7d` wird nicht gelesen. Ihr Puffer ist der Abstand von 96 zu 100. Das ist heute schon so und ändert sich hier nicht.

## Nachtrag Q-0149 (G-0108): Das Vokabular kennt `fable` und `mythos`

**Datum:** 11.11.2026 · **Status:** angenommen · **Issue:** #82

### Kurz in einfacher Sprache

- Monarch kannte nur die Modellfamilien `opus`, `sonnet` und `haiku`. Eine Session mit `claude-fable-5-2` wurde deshalb als `unknown` geführt.
- Künftig erkennt Monarch auch `fable` und `mythos`. Jede dieser Familien bekommt ihren eigenen Schätzfaktor.
- An den Grenzen ändert sich nichts.

### Anlass (gemessen am 11.11.2026)

`~/.monarch/mana.json` führte die Session als `claude-fable-5-2`. `monarch mana` zeigte trotzdem `Paar (unknown, medium): Faktor 1.00, unkalibriert (n=0)`. Schritte mit Fable wären unter `unknown|<effort>` kalibriert worden, zusammen mit jedem anderen unerkannten Modell.

### Entscheidung

- Das feste Vokabular aus Punkt 4 heißt jetzt: Familie `opus`, `sonnet`, `haiku`, `fable`, `mythos`; alles andere bleibt `unknown`.
- Unterschieden wird weiter nach Familie, nicht nach Version. `claude-fable-5-1` und `claude-fable-5-2` teilen sich einen Faktor, so wie die Opus-Versionen.
- `Normalize` bleibt sonst unverändert. Freitext aus der Umgebung gelangt weiter nie in eine Ausgabe; ausgegeben wird nur ein Wort aus dem Vokabular.
- Punkt 7 gilt unverändert: Grenzen, `Resolve()` und Guard hängen nicht vom Paar ab.

### Folgen

- Faktoren, die bisher unter `unknown|<effort>` gelernt wurden, bleiben dort stehen und werden nicht umgebucht. In der Worktree G-0107 war die Kalibrierdatei am 11.11. leer; andere Checkouts wurden nicht geprüft.
- Kommt eine neue Modellfamilie dazu, braucht es wieder eine Zeile im Vokabular und einen Test. Bis dahin zeigt `monarch mana` für sie `unknown`.

### Tests

- `ManaCalibrationTests.cs`: `claude-fable-5-2` und `claude-fable-5-1` → `fable`, `claude-mythos-5-2` → `mythos`; `lower-the-model` bleibt `unknown`.
- `CalibrationTests.cs`: Eine Fable-Session wird unter `fable|medium` kalibriert, nicht unter `unknown|medium`, und `mana` zeigt `Paar (fable, medium)`.

## Nachtrag Q-0189 (G-0129): Die Mana-Grenze ist die Arbeitsgrenze, je Plan, Modell und Fenster

**Datum:** 19.11.2026 · **Status:** angenommen. Der Owner hat am 19.11.2026 den Umfang verkleinert, Fable und die Vorschläge der Session entschieden und Q-0190 ohne weitere Designrunde freigegeben (wörtlich unten); der Warden prüft Design und Code zusammen in der Schlussrunde · **Anlass:** Issue #156 (mit Kommentar zu Pro, Max und Ultra) · **Quest:** Q-0189 (Design), Q-0190 (Umsetzung)

### Kurz in einfacher Sprache

- Bisher hielt eine Session schon an, wenn ein Schritt plus das Recap die Grenze überschritten hätte. Am 19.11. stand das 5h-Fenster bei 80 %, der Schritt war mit 8 % geschätzt, die Grenze lag bei 90 %. 80 + 8 = 88 hätte gepasst; der Guard rechnete 80 + 8 + 5 = 93 und verweigerte.
- Künftig gilt: **Bis zur Grenze wird gearbeitet.** Der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen.
- Die Grenzen hängen von Plan, Modell und Fenster ab. Die Pläne heißen Pro, Max und Ultra; `max5` und `max20` gelten weiter als Max und Ultra.
- **Es gibt keine automatische Senkung der Grenze mehr.** Ohne Messung gilt immer der eingestellte Wert. Die alte adaptive Rechnung (Faktor 1,5 und p90 der Schritte) wird entfernt. Die Messung der Recap-Kosten und des Überlaufs kommt später als eigenes Issue (v0.2).
- Bei Ultra gibt es kein 5h-Fenster. Der Guard prüft dort nur die Woche und gilt nicht dauerhaft als „veraltet“.
- **Wichtig für bestehende Projekte:** Die `system.yaml` enthält heute feste Werte (hier 90 und 96), die `awaken` aus dem alten Plan-Standard geschrieben hat. Solange sie dort stehen, gelten sie für alle Modelle: Sonnet arbeitet dann weiter nur bis 90 statt 93. Erst wenn der Owner die Zeile entfernt, gilt die neue Tabelle. Die Befehle stehen unten.

### Owner-Entscheid zur Grenze (wörtlich, 19.11.2026)

„Die Mana-Grenze ist die Arbeitsgrenze: Bis dorthin wird gearbeitet, der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen. Die Pläne heißen Pro, Max und Ultra; max5 gilt als Max, max20 als Ultra. Die Grenzen gelten je Plan, Modell und Fenster.
5h-Fenster – Pro: Opus 90, Sonnet 93, Haiku 95. Max: Opus 94, Sonnet 96, Haiku 97. Ultra: kein 5h-Fenster; meldet die Statusline trotzdem eines, gelten die Werte von Max.
Wochenfenster – Pro: Opus 96, Sonnet 97, Haiku 98. Max und Ultra: 98 für alle Modelle, ebenso für das eigene Fable-Wochenfenster.
Fable gilt wie Opus. Unbekanntes Modell: Wert von Opus. Unbekannter Plan: Werte von Pro. Ein fehlendes 5h-Fenster ist nur bei Ultra „nicht vorhanden“, bei Pro und Max bleibt es ein veralteter Sensor. Die Messung darf eine Grenze senken, wenn das gemessene Recap plus Überlauf des letzten Schritts nicht mehr in die Reserve passt, aber nie über den eingestellten Wert heben.“

Der letzte Satz („Die Messung darf …“) ist durch die Antwort 1 unten für dieses Gate zurückgestellt.

### Owner-Antworten zum Design (wörtlich, 19.11.2026)

„1. Der Umfang von G-0129 wird verkleinert. Umgesetzt wird jetzt nur: Die Grenze ist die Arbeitsgrenze (aktuell + Schritt ≤ Grenze, kein Recap-Abzug), die Tabelle je Plan, Modell und Fenster, die Plannamen Pro, Max und Ultra mit den Aliasen max5 und max20, und Ultra ohne 5h-Fenster ohne Absturz und ohne dauerhaft als veraltet zu gelten. Die Messung der Recap-Kosten und des Überlaufs und jede automatische Senkung der Grenze entfallen in diesem Gate; bis dahin gilt immer der eingestellte Wert. Die alte adaptive Rechnung (Faktor 1,5 und p90 der Schritte) wird entfernt. Tests, die nur sie prüfen, werden durch einen Test ersetzt, dass ohne Messung immer der eingestellte Wert gilt; jeder Wechsel steht im PR mit Grund. Die Messung kommt als eigenes Issue in den Milestone v0.2.
2. Fable: Bis die Statusline ein eigenes Fenster liefert, gilt das Wochenfenster seven_day mit 98 auch für Fable-Sessions.
3. Die Vorschläge V1, V6, V7, V9, V10, V12 und V13 sind angenommen; V10 heißt: awaken schreibt weder work_until_pct noch clamp. Die Vorschläge zur Messung (V2, V3, V4, V5, V8, V14, V15) entfallen mit Punkt 1.
4. Q-0190 startet ohne weitere Designrunde. Der Warden prüft Design und Code zusammen in der Schlussrunde.
5. G-0129 hebt die Version auf 0.1.2, weil v0.1.1 heute veröffentlicht wird.
6. Freigegeben ist auch die Angleichung von ADR-0019, Zeilen 13, 105 und 115, an den Owner-Entscheid vom 19.11.2026 (Lücke hingenommen, Issue #158).“

Alles, was in #156 steht und nicht aus diesen Entscheiden stammt, ist dort als „Zusammenfassung“ oder „Vorschlag“ gekennzeichnet und wird hier ebenso behandelt. Die angenommenen Vorschläge sind unten mit ihrer Nummer (V1, V6, …) angegeben.

### Ursache (belegt)

1. `ManaGuard.Decide` verlangt `aktuell + Schritt + Recap <= Grenze` (`src/Monarch.Policy/ManaPolicy.cs:88-89`). Im 5h-Fenster zieht jeder Aufrufer die Recap-Reserve ab (`QuestCommands.RecapReservePct` = 5, `QuestCommands.cs:13`), sie liegt damit **in** der Grenze statt darüber. Im Wochenfenster übergibt kein Aufrufer `recapEstimate7d` (`ManaCommands.cs:235`, `:293`, `:449`; `QuestCommands.cs:77`; `StatusCommand.cs:44`). Dies ist die Ursache des 19.11.
2. `AdaptiveLimit.Compute` rechnet die Reserve ein zweites Mal ab: `100 − (1,5 × EWMA(Recaps) + p90(Schritte))` (`ManaPolicy.cs:70`, aufgerufen in `src/Monarch.Cli/Support.cs:253-254`). Die Recap-Kosten werden heute nicht gemessen: `ManaCommands.Recap` liest „before“ und „after“ im selben Prozess (`ManaCommands.cs:75-86`), alle `RecapWritten` im Ledger haben `cost5h` 0. Die adaptive Rechnung hat damit keine tragfähige Grundlage; sie entfällt (Antwort 1).
3. Die Grenzen kennen nur den Plan, nicht das Modell (`src/Monarch.Domain/Mana.cs:55-61`: Pro 90/96, Max5 94/97, Max20 96/98).

### Was die Statusline liefert (Beleg)

Quelle: Dokumentation „Customize your status line“, `https://code.claude.com/docs/en/statusline`, abgerufen am 19.11.2026 als Markdown-Fassung (Zeilen 193-196, 344 und 822 dieser Fassung; `grep -c -i fable` = 0 im Text). Wörtlich, Zeile 344: „`rate_limits`: appears only for claude.ai Pro, Max, and Ultra subscribers, or behind a Claude apps gateway that sets a spend limit for you. Even then, it appears only after the first API response in the session. Each window (`five_hour`, `seven_day`, `spend_limit`) may be independently absent, and Claude Code drops a window once its `resets_at` time passes.“ Zeile 822: „For subscribers, the `rate_limits` object can contain a rolling `five_hour` window and a weekly `seven_day` window.“ Teilbeleg im Repo: `docs/adr/0004-rate-limits-in-der-statusline.md:14-16`.

- Die Dokumentation nennt die Fenster `five_hour` und `seven_day`, je mit `used_percentage` und `resets_at`; jedes kann einzeln fehlen. **Sie nennt kein Fable-Fenster und kein Fenster je Modell.** Der Code liest nur diese beiden Fenster (`Dtos.cs:137-139`).
- Folge (Owner-Antwort 2): Bis die Statusline ein eigenes Fenster liefert, gilt für Fable-Sessions das Wochenfenster `seven_day` mit 98. **Lesart der Session:** Bei Max und Ultra ist das der Tabellenwert 98; bei Pro gilt „Fable gilt wie Opus“, also der Opus-Wert der Woche (96). Der Entscheid nennt 98 für Fable nur bei Max und Ultra. Diese Lesart ist nicht ausdrücklich bestätigt und steht im Schlussbericht als Frage.

### Entscheidung

**1. Der Guard.** `ManaGuard.Decide` lässt einen Schritt zu, wenn in jedem vorhandenen Fenster `aktuell + Schritt <= Grenze` gilt. Die Reserve `100 − Grenze` wird nicht mehr abgezogen; der Parameter für die Recap-Schätzung entfällt. Sonst `PauseWithoutRecap`, wenn der Reset des maßgeblichen Fensters in weniger als 10 Minuten ansteht, andernfalls `RecapAndStop` (beides wie heute). Maßgebliches Fenster ist das 5h-Fenster, bei Ultra ohne 5h-Fenster das Wochenfenster (V7). Veraltete Werte gehen weiter über `Effective5h` und `Effective7d` ein.

**2. Die Tabelle.** Startwerte im Code (`ManaLimits`), je (Plan, Modell, Fenster):

| Plan | 5h Opus | 5h Sonnet | 5h Haiku | Woche Opus | Woche Sonnet | Woche Haiku |
|---|---|---|---|---|---|---|
| Pro | 90 | 93 | 95 | 96 | 97 | 98 |
| Max | 94 | 96 | 97 | 98 | 98 | 98 |
| Ultra | kein 5h-Fenster; kommt eines, gelten die Werte von Max | | | 98 | 98 | 98 |

- **Modellklasse:** `opus`, `sonnet`, `haiku`. `fable` zählt als Opus („Fable gilt wie Opus“); `unknown` und alles Unbekannte ebenso („Unbekanntes Modell: Wert von Opus“). `mythos` zählt wie Opus (V1, angenommen).
- **Quelle des Modells (V13, angenommen):** das Modell der Session aus `mana.json:sessions[<Session-ID>]` (`ManaCalibration.CurrentPair`, `src/Monarch.Cli/ManaCalibrationStore.cs:135`). Die Session-ID kommt aus `CLAUDE_CODE_SESSION_ID`; das Briefing nutzt die `session_id` der Hook-Eingabe. Fehlt der Eintrag, gilt `unknown`, also der Wert von Opus; das Briefing weist dann „Modell unbekannt, Opus-Grenze“ aus.
- **Pläne:** `PlanKind` heißt `Pro`, `Max`, `Ultra`, `ApiKey`. `max5` liest die Datei als Max, `max20` als Ultra. Ein unbekannter oder fehlender Plan ist Pro. `api-key` bleibt 90/90 für alle Modelle (V9, angenommen).

**3. `system.yaml`.** Die Datei bleibt gültig, auch in Noelia, WT und CP.

| Eintrag | Bedeutung künftig |
|---|---|
| `mana.plan: max5` / `max20` | gilt als `max` / `ultra`. Neue Dateien schreiben `max` und `ultra`. |
| `limits.five_hour.opus`, `.sonnet`, `.haiku` und `limits.seven_day.opus`, `.sonnet`, `.haiku` (neu, optional) | ersetzt die Tabelle für dieses Modell und Fenster (V6, angenommen). |
| `work_until_pct` (alt) | gilt für **alle Modelle** und ersetzt die Tabelle; ein Eintrag je Modell geht vor (V6, angenommen). Der Wert wird nie stillschweigend geändert. |
| `clamp: [min, max]` | wird nicht mehr gelesen. Es gibt keine automatische Senkung mehr, also keine Grenzen dafür. Eine vorhandene Angabe stört nicht. |

- **Gültigkeit (V12, angenommen).** Jeder Wert (`work_until_pct`, Einträge je Modell) gilt nur, wenn er endlich ist und `0 ≤ Wert ≤ 100` erfüllt; sonst gilt, als stünde er nicht da (Tabelle). Das ist neu: bisher verwirft `SystemConfig.cs:156` nur nicht endliche Werte.
- **`awaken` (V10, angenommen):** schreibt weder `work_until_pct` noch `clamp`; es schreibt `plan: pro | max | ultra | api-key`. Die Werte kommen aus der Tabelle im Code.
- **Ohne Messung gilt immer der eingestellte Wert.** `ManaSupport.Resolve` liest das Ledger nicht mehr. Der eingestellte Wert ist der Eintrag je Modell, sonst `work_until_pct`, sonst die Tabelle. Damit entfällt auch die Frage, was bei defektem Ledger gilt.
- **Owner-Befehle für `system.yaml`.** Die Session ändert die geschützte Datei nicht. Für dieses Repo (`.monarch/system.yaml`, Zeilen 8-9), nach dem Merge und der Installation:
  - `five_hour: { work_until_pct: 90, clamp: [90, 97] }` wird zu `five_hour: {}` (oder die Zeile entfällt)
  - `seven_day: { work_until_pct: 96, clamp: [96, 99] }` wird zu `seven_day: {}` (oder die Zeile entfällt)
  - Für Noelia, WT und CP gilt dieselbe Regel: `mana.limits.five_hour` und `mana.limits.seven_day` entfernen, wenn `work_until_pct` dem alten Plan-Standard entspricht (Pro 90/96, `max5` 94/97, `max20` 96/98); steht dort ein anderer Wert, ist er bewusst gesetzt und bleibt. Die Dateien dieser Projekte hat die Session nicht gesehen; die Befehle sind die Regel, nicht fertige Zeilen.

**4. Ultra ohne 5h-Fenster (V7, angenommen).** Der Sensor kennt den Plan nicht: Fehlt `five_hour`, setzt die Statusline `rate_limits_stale` immer auf `true` und erneuert `rate_limits_at` nicht (`Statusline.cs:169-178`, `:188-189`); `ToSnapshot` würde die Woche bei Ultra damit immer als veraltet führen (`State.cs:127-128`). Darum:

- `ManaStore.ToSnapshot` bekommt den Plan. Bei `Ultra` und fehlendem `five_hour` gilt: `HasFiveHour = false` im Snapshot; **veraltet** bestimmt allein das Alter der letzten Wochen-Meldung (`seven_day.last_seen.observed_at` gegen `StaleAfter` = 10 Minuten, `State.cs:58`), nicht das Datei-Flag; fehlt `last_seen`, gilt `seven_day.observed_at`. Der Grund: `observed_at` wird nur gesetzt, wenn der Wochenwert steigt, das Fenster zurückgeht, beim ersten Wert oder bei einem neuen Fenster (`Statusline.cs:225-228`, `:234-236`, `:264`); bei unverändertem Wert behält `Keep` das alte `observed_at` (`:239`). `last_seen` wird bei jeder Meldung neu gesetzt (`Statusline.cs:224`, `:239`); Wochenwerte kommen in ganzen Prozent (ADR-0004), und bei Ultra steigt die Woche selten um einen Punkt. Mit `observed_at` gälte der Snapshot dort fast immer als veraltet. Alle elf Aufrufer reichen den Plan durch (`HookRunner.cs:364`, `:433`; `ManaCalibrationStore.cs:159`, `:181`; `QuestCommands.cs:70`; `StatusCommand.cs:42`; `ManaCommands.cs:75`, `:82`, `:224`, `:269`, `:418`).
- `ManaGuard.Decide` prüft nur die Woche, wenn `HasFiveHour = false`. `mana` und `status` schreiben „5h: nicht vorhanden“.
- `ManaCalibration.Begin` und `Finish` greifen nicht mehr ungeprüft auf `rl.FiveHour!` zu (`ManaCalibrationStore.cs:169-170`, `:203-205`): Ohne 5h-Fenster entfällt die 5h-Kalibrierung (`reason: no-5h-window`), `quest accept` und `submit` laufen ohne Absturz.
- `rest`, das Briefing und die Meldung aus #87 leiten die Weckzeit und den Reset-Hinweis vom Wochen-Reset ab, wenn `HasFiveHour = false` (`ManaCommands.cs:236-238`, `HookRunner.cs:437`, `QuestCommands.cs:81-82`); kein Reset-Zeitpunkt „Epoch 0“. Dasselbe gilt für RECAP.md (`ManaCommands.cs:151`), die Ausgabezeile von `rest` (`:242`) und die 5h-Felder der Snapshot-Ereignisse (`Support.cs:286`, `HookRunner.cs:366`): Bei `HasFiveHour = false` bleiben sie leer, nicht 0.
- Meldet die Statusline bei Ultra doch ein 5h-Fenster, gelten dessen Werte mit den Grenzen von Max, und zwar nur, solange sein `resets_at` in der Zukunft liegt; danach gilt es wieder als „nicht vorhanden“ (sonst hielte `Statusline.cs:219-221` den alten Wert fest).
- Bei Pro und Max ändert sich nichts: Fehlt das 5h-Fenster, läuft der letzte bekannte Wert als veraltet weiter (`Statusline.cs:189`, `:219-221`, `State.cs:128`, ADR-0004, `QuestCommands.cs:81-82`); nur wenn nie ein 5h-Wert vorlag, gibt es keinen Snapshot (`State.cs:122-124`).

**5. Ausgabe und Aufrufer.** Weil kein Recap mehr abgezogen wird, rechnen alle Aufrufer ohne `RecapReservePct`: `quest accept` (`QuestCommands.cs:77`), `mana` (`ManaCommands.cs:293`), `rest` (`:235` und `blocked5` in `:236`), `status` (`StatusCommand.cs:44`), die MCP-Prognose (`ManaCommands.cs:449`). Die Ablehnung von `quest accept` und die Zeile `+ recap 5%` in `mana` (`ManaCommands.cs:294-295`) nennen keinen Recap-Anteil mehr. Die Beschreibung des MCP-Werkzeugs `mana_forecast` („plan default or adaptive“, `McpTools.cs:53`) nennt „adaptive“ nicht mehr. Im MCP-Ergebnis von `mana_forecast` entfällt `recapReservePct` (`ManaCommands.cs:435`), und `headroom` ist `Grenze − aktuell − Schritt` (`ManaCommands.cs:451`; `McpTests.cs:63` erwartet heute `90 - 40 - 5 - 5`, künftig 45). Die Zeile `Limits in use (<Quelle>)` behält das bestehende Wort `plan default` (wie heute, auch bei Werten aus der Datei; `Support.cs:246`); `adaptive` und „recaps measured“ entfallen. Eine Anzeige von Reserve und Herkunft ist nicht Teil dieses Gates; die einzige Ausnahme ist der Hinweis im Briefing bei unbekanntem Modell (V13).

**6. Version.** G-0129 hebt die Version auf **0.1.2** (Owner-Antwort 5): `plugin/.claude-plugin/plugin.json:3`, `MONARCH_VERSION` in `plugin/scripts/monarch:4` und `ServerVersion` in `src/Monarch.Cli/McpTools.cs:17`. `scripts/install-local.sh` liest die Version aus `plugin.json` und installiert unter `~/.monarch/bin/0.1.2/`. Der Wrapper-Test (`plugin/tests/wrapper-test.sh:106-113`) und `VisibleLayerNameTests.cs:45` nennen die Version fest und werden auf 0.1.2 gestellt (altes Binary: 0.1.1).

### Was in diesem Gate entfällt (eigenes Issue, v0.2)

Messung der Recap-Kosten (`RecapCostMeasured`, Messung über die Recap-Phase), Messung des Überlaufs des letzten Schritts, jede automatische Senkung der Grenze und ihre Untergrenze (V2, V3, V4, V5, V8, V14, V15). Bis dahin gilt immer der eingestellte Wert.

### Was dieser Nachtrag ablöst

- Nachtrag Q-0142: Die Entscheidungstabelle (Zeile 97-100: `max(w, min)`, `clamp` fehlt, Ledger nicht lesbar) und die Aussage zum unteren `clamp`-Wert. Es gilt immer der eingestellte Wert; `clamp` wird nicht gelesen. Der Fall vom 30.10. (Senkung 96 → 92 durch Messschwächen) kann nicht mehr eintreten, weil es keine Senkung gibt. Ebenso entfällt die Aussage (Zeile 126), das Briefing lese zusätzlich das Ledger: `Resolve()` liest es nicht mehr.
- Entscheidung Punkt 7 (Zeile 58-60) und Nachtrag Q-0149 (Zeile 148: „hängen nicht vom Paar ab“): Die Grenzen hängen jetzt vom Modell ab; die Recap-Reserve im Guard entfällt. Weiter gilt: Die Kalibrierung (Faktor je Paar) ändert keine Grenze.
- Zeile 112 („Im 5h-Fenster rechnet der Guard weiter `Schritt + Recap-Reserve` gegen die Grenze.“): entfällt mit Punkt 1.

### Warum das für Nutzer und Daten keine neue Gefahr ist

Der Guard schützt vor dem Überziehen des Budgets. Die Reserve bleibt, ihr Umfang ist der Entscheid des Owners. Er wird **lockerer** (er stoppt später): durch den Wegfall des Recap-Abzugs im 5h-Fenster, durch die Tabelle (Pro 5h Sonnet 90 → 93, Max-Woche 97 → 98; alt `Mana.cs:57-58`) und durch den Wegfall der automatischen Senkung. Das ist der ausdrückliche Zweck des Owner-Entscheids. Er wird **strenger**: wo `work_until_pct` unter dem früheren `clamp`-Minimum lag (`ManaLimitTests.cs:72-77`: 90 → 85, 96 → 92; ein Beispiel wie `ConfigTests.cs:14-15`: Woche 96 → 95), wo ein Wert ungültig ist (V12: `150` gilt nicht mehr), bei Ultra ohne 5h-Fenster, weil künftig das Wochenfenster geprüft wird (heute gibt es dort keinen Snapshot und damit keine Prüfung, `State.cs:122-124`, `QuestCommands.cs:74`), und bei Ultra, falls die Statusline doch ein 5h-Fenster meldet, für Opus (alt 96, neu 94; alt `Mana.cs:59`). Neu ist, dass die Grenze an der Umgebungsvariable `CLAUDE_CODE_SESSION_ID` hängt (nur `ManaCalibrationStore.cs:137` und `McpTools.cs:76` nutzen sie heute, die Policy prüft sie nicht); eine falsche Modellklasse kann eine zu hohe Grenze setzen (Haiku 95 statt Opus 90). Der Owner hat V13 angenommen.

### Teststrategie

Neue Fälle: vorher rot, danach grün.

- Der Fall vom 19.11.: 5h 80 %, Schritt 8 %, Grenze 90 → `Proceed` (alt: `RecapAndStop`). 83 + 8 bei 90 → `RecapAndStop`; Reset in weniger als 10 Minuten → `PauseWithoutRecap`.
- Tabelle: je Plan, Modell und Fenster die Werte aus dem Entscheid; `fable`, `mythos`, `unknown` wie Opus; unbekannter Plan wie Pro; `max5` → Max, `max20` → Ultra in `ParsePlan` und in der Datei; Modell aus der Session-ID; fehlender Eintrag → Opus-Wert; Fable-Session prüft `seven_day` mit 98 (Max, Ultra).
- **Ohne Messung gilt immer der eingestellte Wert** (ersetzt die adaptiven Tests): mit fünf `RecapWritten`, mit Schritten im Ledger, mit `clamp` in der Datei, mit defektem Ledger: Tabelle, Eintrag je Modell oder `work_until_pct`, nie ein anderer Wert.
- Ultra: Snapshot ohne 5h-Fenster, Woche frisch → `Proceed`; Woche über 98 → `RecapAndStop`; die Woche gilt bei fehlendem `five_hour` als frisch, wenn `seven_day.last_seen.observed_at` jung ist, auch bei seit über 10 Minuten unverändertem Wochenwert, und als veraltet, wenn `last_seen` älter als 10 Minuten ist; fehlt `last_seen`, entscheidet `observed_at`; `quest accept` und `submit` ohne 5h-Fenster ohne Ausnahme; die Weckzeit von `rest` kommt aus dem Wochen-Reset; kein „Epoch 0“; ein gemeldetes 5h-Fenster nutzt die Grenzen von Max und zählt nur bis `resets_at`. Pro und Max ohne 5h-Fenster: veralteter letzter Wert.
- `system.yaml`: `work_until_pct` gilt für alle Modelle; Eintrag je Modell geht vor; `150` und `-5` werden verworfen (neu, vorher rot); `clamp` wird nicht gelesen; `awaken` schreibt weder `work_until_pct` noch `clamp`.
- Version: `plugin.json`, Wrapper und `ServerVersion` nennen 0.1.2; Wrapper 0.1.2 mit nur dem Binary 0.1.1 ist fail-closed wie bisher (Q-0187).

*Ersetzte und geänderte alte Tests* (jeder Wechsel steht im PR mit altem und neuem Wert und dem Grund; kein Test wird gelockert, gelöscht oder übersprungen, außer er prüft nur die entfernte adaptive Rechnung und wird durch den Test „ohne Messung gilt immer der eingestellte Wert“ ersetzt):

| Test | alt | neu und Grund |
|---|---|---|
| `ManaLimitTests.cs:44-50`, `:52-58`, `:61-66`, `:101-107`, `:174-183` | prüfen nur die adaptive Senkung und ihre `clamp`-Untergrenze | ersetzt durch „ohne Messung gilt immer der eingestellte Wert“ (mit denselben Seeds, Erwartung: eingestellter Wert); Grund: die Rechnung entfällt |
| `ManaLimitTests.cs:117-141` | alle Befehle (`mana`, `rest`, `recap`, `status`, Briefing, Guard) nennen denselben Wert | **bleibt** mit seinem Zweck; Soll wechselt, wo es aus der Senkung stammte (Fall `w` 98 / `clamp [96, 99]`: 96 → 98); die Prüfung gilt weiter je Modell (Briefing nutzt `session_id`, die Befehle `CLAUDE_CODE_SESSION_ID`) |
| `ManaLimitTests.cs:143-153` | Guard von `quest accept` nutzt die aufgelöste Grenze; Ablehnung bei 96,5 + 1 gegen 96 | **bleibt** mit seinem Zweck; mit Grenze 98 würde 96,5 + 1 angenommen, darum neue Seeds (`work_until_pct` 96: Ablehnung bleibt) und ein Fall mit 98: Annahme |
| `ManaLimitTests.cs:79-99`, `:185-193`, `:195-220` | Gültigkeit von `clamp`; `gives_max_of_w_and_min` | bleiben, erwarten den eingestellten Wert (`clamp` wird nicht gelesen); weicht ein Soll ab, steht der Grund im PR |
| `CalibrationTests.cs:144-161` | `ManaSupport.Resolve(p.Root, config)`, vergleicht `Recaps` | neue Signatur (Plan, Modell); vergleicht die Grenzen mit und ohne Kalibrierdatei, `Recaps` entfällt |
| `ManaLimitTests.cs:49` | `Limits in use (adaptive)`, `recaps measured 5` | `plan default` ohne „recaps measured“; Grund: Ausgabe nach Punkt 5 |
| `ManaLimitTests.cs:72-77` | `w` 85, `clamp [90, 97]` → 90; `w` 92, `clamp [96, 99]` → 96 | 85 bzw. 92; Grund: `clamp` wird nicht gelesen |
| `Part2Tests.cs:353`, `:361` | `plan default`; `adaptive` nach drei `recap`-Aufrufen | `plan default` in beiden Fällen; Grund: es gibt keine adaptive Senkung |
| `Part2Tests.cs:354` | `RecapAndStop` bei 60 + 30 gegen 90 | Schätzung 31 (60 + 31 > 90), erwartet weiter `RecapAndStop`; ein Fall 60 + 30 → `Proceed` kommt dazu |
| `ManaTypesTests.cs:25-29`, `CommandTests.cs:20`, `:44`, `ConfigTests.cs:34` | `Max5`/`Max20` | `Max`/`Ultra`; Werte aus der Tabelle; `ConfigTests.cs:34` ändert nur den Plannamen, `:35` bleibt (`ManaLimits(91, 95)` ist der Rohwert aus `work_until_pct`) |
| `CommandTests.cs:21` | `ManaLimits(94, 97)` nach `awaken --plan max5` | 94 und 98; `awaken` schreibt keinen `limits`-Block |
| `McpTests.cs:63` | `90 - 40 - 5 - 5` | 45 |
| `ManaTests.cs` (Guard-Fälle `:16-44`, `Exactly_at_limit_proceeds` `:27-28`) | `aktuell + Schritt + Recap` | Eingaben um den Recap-Anteil angepasst, derselbe Abstand zur Grenze; `Exactly_at_limit_proceeds`: 81 + 9 → `Proceed`, 81 + 9,1 → `RecapAndStop`; jeder Fall im PR mit altem und neuem Wert |
| `ManaTests.cs` `Weekly_recap_estimate_counts` (`:33-34`) | nutzt `recapEstimate7d` | Nachfolger `Weekly_step_counts_without_recap`; Grund: Parameter entfällt |
| `ManaTests.cs` `Adaptive_formula`, `Adaptive_always_within_bounds`, `Adaptive_returns_start_with_fewer_than_three_recaps` (`:63-75`, `:96-104`) | prüfen nur `AdaptiveLimit.Compute` | ersetzt durch „ohne Messung gilt immer der eingestellte Wert“; Grund: die Rechnung entfällt |
| `ManaTests.cs` `Guard_never_proceeds_beyond_limit` (`:106-114`) | übergibt `r5`, `r7` als Recap-Schätzungen | gleiche Property ohne `r5`/`r7`, `fits = c5 + s5 <= Grenze5 && c7 + s7 <= Grenze7`, dazu ein Fall mit `HasFiveHour = false` |
| `ManaTests.cs` `Percentile_nearest_rank` und die Ewma-Tests | prüfen `AdaptiveLimit.Percentile` (das p90 der Rechnung) und `Ewma` | ersetzt durch „ohne Messung gilt immer der eingestellte Wert“; Grund: `AdaptiveLimit` (`Compute`, `Ewma`, `Percentile`, `MinRecaps`, `EwmaWindow`) und `Resolved.Recaps`/`Recap7d` entfallen ganz; die spätere Messung (#161) führt eigene Hilfsfunktionen ein |
| `plugin/tests/wrapper-test.sh:106-113`, `VisibleLayerNameTests.cs:45` | Version 0.1.1, altes Binary 0.1.0 | 0.1.2, altes Binary 0.1.1; Grund: Owner-Antwort 5 |

### Folgen

- `ManaLimits` wird je (Plan, Modell) aufgelöst; `ManaSupport.Resolve` bekommt Plan und Modellklasse und liest das Ledger nicht mehr.
- `ManaPool.CanAfford` (`src/Monarch.Domain/Mana.cs:30`) wird in `src/` nicht aufgerufen, nur in Tests (`ManaTypesTests.cs:14-20`, `DomainPropertyTests.cs:55-61`). Es bleibt unverändert und gehört nicht zu diesem Entscheid.
- Der Scope von Q-0190 umfasst zusätzlich `src/Monarch.Cli/ManaCalibrationStore.cs`, `plugin/.claude-plugin/plugin.json`, `plugin/scripts/monarch`, `plugin/tests/wrapper-test.sh` und `src/Monarch.Cli/McpTools.cs` (Version).
- Die Messung (Recap-Kosten, Überlauf, automatische Senkung) steht als Issue #161 im Milestone v0.2.
