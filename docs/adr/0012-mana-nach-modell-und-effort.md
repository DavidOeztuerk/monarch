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

**Datum:** 19.11.2026 · **Status:** Entwurf, Warden-Designrunde offen. Der Owner hat am 19.11.2026 vorab festgelegt: Hält sich das ADR an seinen Entscheid und an #156, gilt es als freigegeben; nur bei Abweichungen wird gefragt. Alles, was nicht aus dem Entscheid stammt, ist im Text als **Vorschlag der Session** markiert (mythos wie Opus; Untergrenze `Grenze − 10`; kein Sicherheitsfaktor; Definition des Überlaufs; Recaps ohne Modellfeld zählen für Opus; Einträge je Modell und `work_until_pct` für alle Modelle; Wochenfenster als maßgebliches Fenster bei Ultra; ein 5h-Wert bei Ultra zählt nur bis zu seinem `resets_at`; die Formel `100 − (EWMA + Überlauf)` und die Zählung der drei Recaps je Modell; `api-key` bleibt 90/90) und geht gebündelt an den Owner · **Anlass:** Issue #156 (mit Kommentar zu Pro, Max und Ultra) · **Quest:** Q-0189 (Design), Q-0190 (Umsetzung)

### Kurz in einfacher Sprache

- Bisher hielt eine Session schon an, wenn ein Schritt plus das Recap die Grenze überschritten hätte. Am 19.11. stand das 5h-Fenster bei 80 %, der Schritt war mit 8 % geschätzt, die Grenze lag bei 90 %. 80 + 8 = 88 hätte gepasst; der Guard rechnete 80 + 8 + 5 = 93 und verweigerte.
- Künftig gilt: **Bis zur Grenze wird gearbeitet.** Der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen.
- Die Grenzen hängen von Plan, Modell und Fenster ab. Sonnet und Haiku dürfen länger arbeiten als Opus. Der Grund laut Owner (#156, Zusammenfassung): Ein Recap kostet bei ihnen weniger; die Kosten je Modell sind noch nicht gemessen (alle `RecapWritten` vom 19.11. haben `cost5h` 0).
- Die Messung der echten Recap-Kosten darf eine Grenze weiter **senken**, wenn die Reserve für das Recap nicht mehr reicht. Sie hebt keine Grenze über den eingestellten Wert.
- Die Pläne heißen Pro, Max und Ultra. Die alten Namen `max5` und `max20` gelten weiter als Max und Ultra.
- Das Fable-Wochenfenster gibt es in den Statusline-Daten nicht (Beleg unten). Der Wert 98 dafür steht im Entscheid, kann aber heute nicht angewendet werden.

### Owner-Entscheid (wörtlich, 19.11.2026)

„Die Mana-Grenze ist die Arbeitsgrenze: Bis dorthin wird gearbeitet, der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen. Die Pläne heißen Pro, Max und Ultra; max5 gilt als Max, max20 als Ultra. Die Grenzen gelten je Plan, Modell und Fenster.
5h-Fenster – Pro: Opus 90, Sonnet 93, Haiku 95. Max: Opus 94, Sonnet 96, Haiku 97. Ultra: kein 5h-Fenster; meldet die Statusline trotzdem eines, gelten die Werte von Max.
Wochenfenster – Pro: Opus 96, Sonnet 97, Haiku 98. Max und Ultra: 98 für alle Modelle, ebenso für das eigene Fable-Wochenfenster.
Fable gilt wie Opus. Unbekanntes Modell: Wert von Opus. Unbekannter Plan: Werte von Pro. Ein fehlendes 5h-Fenster ist nur bei Ultra „nicht vorhanden“, bei Pro und Max bleibt es ein veralteter Sensor. Die Messung darf eine Grenze senken, wenn das gemessene Recap plus Überlauf des letzten Schritts nicht mehr in die Reserve passt, aber nie über den eingestellten Wert heben.“

Alles, was in #156 steht und nicht aus diesem Entscheid stammt, ist dort als „Zusammenfassung“ oder „Vorschlag“ gekennzeichnet und wird hier ebenso behandelt.

### Ursache (belegt)

1. `ManaGuard.Decide` verlangt `aktuell + Schritt + Recap <= Grenze` (`src/Monarch.Policy/ManaPolicy.cs:88-89`). Im 5h-Fenster zieht jeder Aufrufer die Recap-Reserve ab (`QuestCommands.RecapReservePct` = 5, `QuestCommands.cs:13`), sie liegt damit **in** der Grenze statt darüber. Im Wochenfenster übergibt kein Aufrufer `recapEstimate7d` (`ManaCommands.cs:235`, `:293`, `:449`; `QuestCommands.cs:77`; `StatusCommand.cs:44`); dort wird heute kein Recap abgezogen (Nachtrag Q-0142, Folgen). Dies ist die Ursache des 19.11.
2. `AdaptiveLimit.Compute` rechnet die Reserve ein zweites Mal ab: `100 − (1,5 × EWMA(Recaps) + p90(Schritte))` (`ManaPolicy.cs:70`, aufgerufen in `src/Monarch.Cli/Support.cs:253-254`), danach zieht `Decide` Schritt und Recap erneut ab. In diesem Repo wirkt das heute nicht: Der untere `clamp`-Wert gleicht `work_until_pct` (`.monarch/system.yaml:8-9`), also liefert `Compute` immer den eingestellten Wert. Für den 19.11. war das nicht ursächlich.
3. Die Grenzen kennen nur den Plan, nicht das Modell (`src/Monarch.Domain/Mana.cs:55-61`: Pro 90/96, Max5 94/97, Max20 96/98).

### Was die Statusline liefert (Beleg)

Quelle: Dokumentation „Customize your status line“, `https://code.claude.com/docs/en/statusline`, abgerufen am 19.11.2026 (WebFetch; Zeilen 193-196, 344 und 822 der abgerufenen Seite). Wörtlich, Zeile 344: „`rate_limits`: appears only for claude.ai Pro, Max, and Ultra subscribers, or behind a Claude apps gateway that sets a spend limit for you. Even then, it appears only after the first API response in the session. Each window (`five_hour`, `seven_day`, `spend_limit`) may be independently absent, and Claude Code drops a window once its `resets_at` time passes.“ Zeile 822: „For subscribers, the `rate_limits` object can contain a rolling `five_hour` window and a weekly `seven_day` window.“ Teilbeleg im Repo: `docs/adr/0004-rate-limits-in-der-statusline.md:14-16`.

- Das Feld `rate_limits` hat die Fenster `five_hour` und `seven_day`, je mit `used_percentage` (0 bis 100) und `resets_at` (Unix-Sekunden). Dazu kommt `spend_limit` hinter einem Claude-Apps-Gateway; für Abos mit Pro, Max oder Ultra ist es nicht gesetzt.
- Jedes Fenster kann einzeln fehlen. Claude Code lässt ein Fenster weg, sobald dessen `resets_at` vorbei ist. `rate_limits` erscheint erst nach der ersten API-Antwort der Session.
- **Es gibt kein Fable-Fenster und kein Fenster je Modell.** Die Seite nennt weder `fable` noch ein modellbezogenes Wochenfenster (`grep -c -i fable` auf die abgerufene Seite: 0). Der Code liest ebenfalls nur `five_hour` und `seven_day` (`src/Monarch.Cli/Dtos.cs:137-139`).
- Folge: Der Wert 98 für das Fable-Wochenfenster steht in diesem ADR, hat aber **keinen Messwert**, auf den der Guard ihn anwenden könnte. Bei Max und Ultra prüft der Guard das Wochenfenster `seven_day` mit 98. Sobald die Statusline ein Fable-Fenster liefert, ist das ein eigenes Gate. Das ist eine Lücke des Sensors, keine Lockerung: Fable-Verbrauch zählt im `seven_day`-Fenster mit, soweit Claude Code ihn dort einrechnet; ob er zusätzlich ein eigenes Limit erreicht, sieht der Guard nicht. Dem Owner im Schlussbericht als Hinweis genannt.
- Ultra: Für Konten ohne 5h-Fenster fehlt `five_hour` (Ultra-Hilfeseite, Owner-Kommentar zu #156). Das ist bei `ultra` der Normalfall, bei Pro und Max ein Sensorproblem (Entscheid: „veralteter Sensor“).

### Entscheidung

**1. Der Guard.** `ManaGuard.Decide` lässt einen Schritt zu, wenn in jedem vorhandenen Fenster `aktuell + Schritt <= Grenze` gilt. Die Reserve `100 − Grenze` wird nicht mehr abgezogen; der Parameter für die Recap-Schätzung entfällt. Sonst `PauseWithoutRecap`, wenn der Reset des maßgeblichen Fensters in weniger als 10 Minuten ansteht, andernfalls `RecapAndStop` (beides wie heute). Maßgebliches Fenster ist das 5h-Fenster, bei Ultra ohne 5h-Fenster das Wochenfenster. Veraltete Werte gehen weiter über `Effective5h` und `Effective7d` ein.

**2. Die Tabelle.** Startwerte im Code (`ManaLimits`), je (Plan, Modell, Fenster):

| Plan | 5h Opus | 5h Sonnet | 5h Haiku | Woche Opus | Woche Sonnet | Woche Haiku |
|---|---|---|---|---|---|---|
| Pro | 90 | 93 | 95 | 96 | 97 | 98 |
| Max | 94 | 96 | 97 | 98 | 98 | 98 |
| Ultra | kein 5h-Fenster; kommt eines, gelten die Werte von Max | | | 98 | 98 | 98 |

- **Modellklasse:** `opus`, `sonnet`, `haiku`. `fable` zählt als Opus („Fable gilt wie Opus“); `unknown` und alles Unbekannte ebenso („Unbekanntes Modell: Wert von Opus“; Opus ist der strengste Wert). `mythos` nennt der Entscheid nicht; der Code kennt die Familie (`src/Monarch.Domain/ManaCalibration.cs:8`). **Vorschlag der Session (kein Owner-Entscheid):** `mythos` zählt wie Opus, weil es in der Tabelle keinen Wert hat und der strengste gilt. Ändert der Owner das, ist es ein Eintrag in der Tabelle. Die Quelle ist dieselbe wie bei der Mana-Schätzung: das Modell der aufrufenden Session aus `mana.json:sessions` (`ManaCalibration.CurrentPair`, `src/Monarch.Cli/ManaCalibrationStore.cs:135`).
- **Pläne:** `PlanKind` heißt `Pro`, `Max`, `Ultra`, `ApiKey`. `max5` liest die Datei als Max, `max20` als Ultra (Owner-Entscheid). Ein unbekannter oder fehlender Plan ist Pro („Unbekannter Plan: Werte von Pro“; so war es auch bisher in `SystemConfigReader.ParsePlan`). `api-key` bleibt wie heute 90/90 für alle Modelle: Der Entscheid regelt ihn nicht, und das Budget-Verhalten ändert sich hier nicht.

**3. `system.yaml`: bestehende und neue Werte.** Die Datei bleibt gültig, auch in Noelia, WT und CP.

| Eintrag | Bedeutung künftig |
|---|---|
| `mana.plan: max5` / `max20` | gilt als `max` / `ultra`. Neue Dateien schreiben `max` und `ultra`. |
| `limits.five_hour.opus`, `.sonnet`, `.haiku` und `limits.seven_day.opus`, `.sonnet`, `.haiku` (neu, optional) | ersetzt die Tabelle für dieses Modell und Fenster. Das ist die Tabelle aus Vorschlag 2 von #156. **Vorschlag der Session.** |
| `work_until_pct` (alt) | gilt für **alle Modelle** und ersetzt die Tabelle (er ist die Einstellung des Owners, wie bisher); ein Eintrag je Modell geht vor. Der Wert wird nie stillschweigend geändert: weder gesenkt noch gehoben. **Vorschlag der Session.** |
| `clamp: [min, max]` | `min` bleibt die Untergrenze für die Messung (Nachtrag Q-0142), begrenzt auf den eingestellten Wert: Untergrenze = `min(clamp-min, eingestellter Wert)`. Das ersetzt `max(w, min)` aus Q-0142 (der Entscheid „nie über den eingestellten Wert“ geht vor). `max` bleibt ungenutzt. |

Gründe: Ein Wert aus der Datei ist eine bewusste Einstellung des Owners; er wird nie stillschweigend geändert (keine Heuristik „gleich dem alten Planwert“). So bleiben auch die bestehenden Tests mit `work_until_pct` 96,8 und 98 gültig. Der Preis: In einer Datei mit `work_until_pct: 90` bleibt Sonnet bei 90 statt 93, bis der Owner die Zeile entfernt. Diese Änderungen an `system.yaml` (geschützter Pfad) macht die Session nicht selbst; sie legt sie dem Owner als Befehle vor (Schlussbericht).

**4. Ultra ohne 5h-Fenster.** Bei `ultra` fehlt das 5h-Fenster im Sensor ohne Meldung „veraltet“: Der Snapshot hat dann kein 5h-Fenster (`HasFiveHour = false`), `Decide` prüft nur die Woche, `mana` und `status` schreiben „5h: nicht vorhanden“. Meldet die Statusline bei Ultra doch ein 5h-Fenster, gelten dessen Werte mit den Grenzen von Max. Bei Ultra zählt ein 5h-Wert nur, solange sein `resets_at` in der Zukunft liegt; danach gilt das Fenster wieder als „nicht vorhanden“ und nicht als veralteter Wert (**Vorschlag der Session**; sonst hielte `Statusline.cs:219-221` den alten Wert fest). Bei Pro und Max ändert sich nichts: Fehlt das 5h-Fenster, läuft der letzte bekannte Wert als veraltet weiter (`Statusline.cs:189`, `:219-221`, `State.cs:128`, ADR-0004, `QuestCommands.cs:81-82`); nur wenn nie ein 5h-Wert vorlag, gibt es keinen Snapshot (`State.cs:122-124`). Maßgebliches Fenster für `PauseWithoutRecap` bei Ultra ohne 5h-Fenster ist das Wochenfenster: **Vorschlag der Session**, weil der Entscheid dazu schweigt.

**5. Messung statt Doppelabzug.** Die Reserve eines (Modells, Fensters) ist `100 − Grenze`. Sie muss das gemessene Recap plus den Überlauf des letzten Schritts fassen:

- Recap-Kosten: EWMA der letzten fünf `RecapWritten`-Ereignisse **dieses Modells** (`cost5h`, `cost7d`). Das Ereignis bekommt dafür das Feld `model` (Modellklasse). Ältere Ereignisse ohne Feld zählen für Opus, weil sie vor der Unterscheidung entstanden und Opus der strengste Wert ist. **Vorschlag der Session.**
- Überlauf des letzten Schritts: **Vorschlag der Session**, denn der Entscheid nennt den Überlauf, definiert ihn aber nicht. Quelle ist das jüngste Ledger-Ereignis `ManaStepRecorded` des Modells (`ManaCalibrationStore.cs:176-212`; es trägt `model`, `estimate5h`, `actual5h`, `actual7d`, `reason`). 5h: `max(0, actual5h − estimate5h)`. Woche: `max(0, actual7d − estimate5h × (Step7d / Step5h))`, die Schätzung, die der Guard für die Woche ableitet (`ManaCommands.cs`, `ManaSupport.Step7d`/`Step5h`). Ereignisse ohne `actual5h` und solche mit einem Grund für Reset, Überlappung oder veralteten Wert (`reason`, `ManaCalibrator.Observe`) zählen nicht. Eine bekannte Schwäche: Ein Sprung über eine Pause im selben Fenster zählt als ein großer Schritt (Nachtrag Q-0142, Folgen). `ManaSnapshotTaken` trägt keine Schätzung (`Support.cs:283-287`) und taugt dafür nicht.
- Passt `EWMA + Überlauf` nicht in die Reserve, gilt `Grenze' = 100 − (EWMA + Überlauf)`. Sonst bleibt die Grenze. **Vorschlag der Session** (der Entscheid sagt nur, dass die Messung senken darf, nicht auf welchen Wert).
- Die Messung greift erst ab drei gemessenen Recaps des Modells (`AdaptiveLimit.MinRecaps`, wie heute; **Vorschlag der Session**, je Modell zu zählen).
- Sie **hebt nie** über den eingestellten Wert (Tabelle, Eintrag je Modell oder `work_until_pct`). Die Obergrenze ist damit der eingestellte Wert selbst.
- Untergrenze (**Vorschlag der Session**): der untere `clamp`-Wert (begrenzt auf den eingestellten Wert, siehe Tabelle oben), wenn die Datei einen gültigen hat. Sonst **Vorschlag der Session** (kein Owner-Entscheid): `Grenze − 10` Prozentpunkte. Das **ersetzt** die Regel „`clamp` fehlt → `min = w`“ aus Nachtrag Q-0142 (Zeile 99), nach der die Messung ohne `clamp` nie senken konnte; der Entscheid „Die Messung darf eine Grenze senken“ geht vor. Mit gültigem `clamp` ist der Fall vom 30.10. (96 → 92) ausgeschlossen; ohne `clamp` ist der Schaden einer Messschwäche auf 10 Punkte begrenzt, nicht ausgeschlossen.
- **Vorschlag der Session (kein Owner-Entscheid):** Der Sicherheitsfaktor 1,5 aus der alten Formel entfällt, weil der Entscheid „das gemessene Recap plus Überlauf“ nennt und keinen Faktor. Er ließe sich wieder einführen, ohne die Tabelle zu ändern.

**6. Anzeige.** `mana` und `mana --estimate` zeigen je Fenster die Grenze, die Reserve (`100 − Grenze`) und die Herkunft (`Tabelle`, `system.yaml` oder `gemessen`), samt Plan und Modellklasse, in zusätzlichen Zeilen. Die bestehende Zeile `Limits in use (<Quelle>)` behält ihre Quellwörter (`plan default`, `adaptive`), damit die Tests dazu unverändert bleiben (`ManaLimitTests.cs:49`, `McpTests.cs:60`, `McpProjectTests.cs:793`, `Part2Tests.cs:353`). `status`, Briefing, `rest` und die Ablehnung von `quest accept` nennen dieselbe Grenze (Nachtrag Q-0142, Punkt „Alle Befehle nennen denselben Wert“, bleibt). Die Ablehnungsmeldung nennt keinen Recap-Anteil mehr.

### Warum das für Nutzer und Daten keine neue Gefahr ist

Der Guard schützt vor dem Überziehen des Budgets. Die Reserve bleibt, ihr Umfang ist der Entscheid des Owners. Eine fehlende Messung (kein Snapshot) bleibt wie heute ein Proceed-Fall ohne Prüfung; das ändert sich hier nicht. Der Guard wird **lockerer** (er stoppt später): durch den Wegfall des Recap-Abzugs im 5h-Fenster und durch die Tabelle (Pro 5h Sonnet 90 → 93, Max-Woche 97 → 98; alt `Mana.cs:57-58`). Das ist der ausdrückliche Zweck des Owner-Entscheids. Er wird **strenger**: durch die Messung je Modell; bei Ultra ohne 5h-Fenster, weil künftig das Wochenfenster geprüft wird (heute gibt es dort keinen Snapshot und damit keine Prüfung, `State.cs:122-124`, `QuestCommands.cs:74`); und bei Ultra, falls die Statusline doch ein 5h-Fenster meldet, für Opus (alt 96, neu 94; alt `Mana.cs:59`). Die Messung selbst wird **lockerer** als die alte Formel: Der Faktor 1,5 entfällt und der Überlauf des letzten Schritts ersetzt das p90 der Schritte (alt `ManaPolicy.cs:70`).

### Was dieser Nachtrag ablöst

- Nachtrag Q-0142, Entscheidungstabelle (Zeile 97-99): `max(w, min)` und „`clamp` fehlt → `min = w`“ (ersetzt, siehe Punkt 3 und 5). Der obere `clamp`-Wert bleibt ungenutzt (Zeile 108).
- Entscheidung Punkt 7 (Zeile 58-60) und Nachtrag Q-0149 (Zeile 148: „hängen nicht vom Paar ab“): Die Grenzen hängen jetzt vom Modell ab; die Recap-Reserve im Guard entfällt. Weiter gilt: Die Kalibrierung (Faktor je Paar) ändert keine Grenze, `Resolve()` liefert mit und ohne Kalibrierdatei dasselbe. Die Schätzung je (Modell, Effort) bleibt unverändert.
- Zeile 100 (Ledger nicht lesbar → `max(w, min)`): gilt künftig als „eingestellter Wert“ ohne Messung.
- Zeile 112 („Im 5h-Fenster rechnet der Guard weiter `Schritt + Recap-Reserve` gegen die Grenze.“): entfällt mit Punkt 1.

### Teststrategie

Vorher rot, danach grün, jeweils mit dem Fall, der am alten Code fehlschlägt:

- Der Fall vom 19.11.: 5h 80 %, Schritt 8 %, Grenze 90 → `Proceed` (alt: `RecapAndStop`). Schritt über die Grenze (83 + 8 bei 90) → `RecapAndStop`; Reset in weniger als 10 Minuten → `PauseWithoutRecap`.
- Tabelle: je Plan, Modell und Fenster die Werte aus dem Entscheid; `fable`, `mythos` und `unknown` wie Opus; unbekannter Plan wie Pro; `max5` → Max, `max20` → Ultra in `ParsePlan` und in der Datei.
- Ultra: Snapshot ohne 5h-Fenster, Woche frei → `Proceed`; Woche über 98 → `RecapAndStop`; Statusline meldet ein 5h-Fenster → Grenzen von Max. Pro und Max ohne 5h-Fenster → der letzte Wert läuft als veraltet weiter wie heute (`IsStale`); kein Snapshot nur, wenn nie ein Wert vorlag.
- Kein Doppelabzug: `AdaptiveLimit` ohne eigenen Abzug der Reserve; passen EWMA der Recap-Kosten plus Überlauf in die Reserve, bleibt die Grenze gleich dem eingestellten Wert.
- Messung: drei Recaps, die nicht in die Reserve passen → Grenze sinkt, nie unter die Untergrenze; Recaps eines anderen Modells zählen nicht; Messung hebt nie über den eingestellten Wert (auch nicht bei Recap-Kosten 0); Recaps ohne Feld `model` zählen für Opus; ein Schritt über einen Reset hinweg ergibt keinen Überlauf.
- `system.yaml`: alter `work_until_pct` gilt für alle Modelle (Sonnet bleibt bei 90, wenn 90 eingetragen ist); Eintrag je Modell ersetzt die Tabelle; `clamp` als Untergrenze; ungültige Werte (`NaN`, negativ, über 100) werden wie bisher verworfen.
- Ausgabe: `mana --estimate` zeigt Grenze, Reserve und Herkunft je Fenster; `status`, Briefing, `rest` und `quest accept` nennen denselben Wert im selben Zustand.
- Alte Tests ändern sich nur dort, wo der Entscheid die Semantik ändert: (a) `aktuell + Schritt + Recap` im Guard (`ManaTests.cs`); (b) die alten Plannamen `Max5`/`Max20` und ihre Werte (`ManaTypesTests.cs:25-29`, `CommandTests.cs:20`, `:44`, `ConfigTests.cs:34`); (c) `max(w, min)` (`ManaLimitTests.cs:72-77`: bei `w` 85 und `clamp [90, 97]` galt 90, künftig gilt der eingestellte Wert 85; bei `w` 92 und `clamp [96, 99]` galt 96 in der Woche, künftig 92); (d) fehlender oder ungültiger `clamp` ergab `w` (`ManaLimitTests.cs:80-99`), künftig senkt die Messung bis `Grenze − 10`; (e) `awaken --plan max5` liest `Limits` (94, 97) (`CommandTests.cs:21`), künftig 94 und 98, weil `awaken` keinen `limits`-Block mehr schreibt. Jeder geänderte Fall steht im PR mit altem und neuem Soll-Wert und dem Grund; kein Test wird gelockert, gelöscht oder übersprungen.

### Folgen

- `ManaLimits` wird je (Plan, Modell) aufgelöst; der Aufruf von `ManaSupport.Resolve` bekommt die Modellklasse (aus `ManaCalibration.CurrentPair`, `ManaCalibrationStore.cs:135`).
- `ManaPool.CanAfford` (`src/Monarch.Domain/Mana.cs:30`) wird in `src/` nicht aufgerufen, nur in Tests (`ManaTypesTests.cs:14-20`, `DomainPropertyTests.cs:55-61`). Es bleibt unverändert und gehört nicht zu diesem Entscheid.
- `awaken` schreibt `plan: pro | max | ultra | api-key` und keinen `limits`-Block mehr; die Werte kommen aus der Tabelle im Code.
- Fable-Wochenfenster: offen, bis die Statusline es liefert (eigenes Gate).
- Die Folgen für bestehende `system.yaml` (hier, Noelia, WT, CP) legt die Session dem Owner als Befehle vor; sie ändert keine `system.yaml`.
