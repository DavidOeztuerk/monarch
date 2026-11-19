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

**Datum:** 19.11.2026 · **Status:** Entwurf nach Warden-Designrunde 1 (REQUEST_CHANGES, überarbeitet); Delta-Runde offen. Freigabe: Der Owner hat am 19.11.2026 festgelegt, dass das ADR als freigegeben gilt, wenn es sich an seinen Entscheid und an #156 hält; Abweichungen gehen gebündelt an ihn (Abschnitt „Offene Fragen an den Owner“) · **Anlass:** Issue #156 (mit Kommentar zu Pro, Max und Ultra) · **Quest:** Q-0189 (Design), Q-0190 (Umsetzung)

### Kurz in einfacher Sprache

- Bisher hielt eine Session schon an, wenn ein Schritt plus das Recap die Grenze überschritten hätte. Am 19.11. stand das 5h-Fenster bei 80 %, der Schritt war mit 8 % geschätzt, die Grenze lag bei 90 %. 80 + 8 = 88 hätte gepasst; der Guard rechnete 80 + 8 + 5 = 93 und verweigerte.
- Künftig gilt: **Bis zur Grenze wird gearbeitet.** Der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen.
- Die Grenzen hängen von Plan, Modell und Fenster ab. Sonnet und Haiku dürfen länger arbeiten als Opus. Der Grund laut Owner (#156, Zusammenfassung): Ihr Recap kostet weniger. Das ist noch nicht gemessen (siehe „Die Recap-Kosten messen“).
- **Wichtig für bestehende Projekte:** Die Datei `system.yaml` enthält heute feste Werte (hier 90 und 96), die `awaken` aus dem alten Plan-Standard geschrieben hat. Solange sie dort stehen, gelten sie für alle Modelle: Sonnet arbeitet dann weiter nur bis 90 statt 93. Erst wenn der Owner die Zeile entfernt, gilt die neue Tabelle. Die Befehle dafür stehen unten.
- Die Messung der echten Kosten darf eine Grenze **senken**, wenn die Reserve für das Recap nicht mehr reicht. Sie hebt keine Grenze über den eingestellten Wert.
- Die Pläne heißen Pro, Max und Ultra. Die alten Namen `max5` und `max20` gelten weiter als Max und Ultra.
- Das Fable-Wochenfenster nennt die Dokumentation der Statusline nicht. Der Wert 98 dafür steht im Entscheid, kann aber heute nicht angewendet werden (Frage 1 unten).

### Owner-Entscheid (wörtlich, 19.11.2026)

„Die Mana-Grenze ist die Arbeitsgrenze: Bis dorthin wird gearbeitet, der Rest bis 100 % ist die Reserve für das Recap und wird nicht noch einmal abgezogen. Die Pläne heißen Pro, Max und Ultra; max5 gilt als Max, max20 als Ultra. Die Grenzen gelten je Plan, Modell und Fenster.
5h-Fenster – Pro: Opus 90, Sonnet 93, Haiku 95. Max: Opus 94, Sonnet 96, Haiku 97. Ultra: kein 5h-Fenster; meldet die Statusline trotzdem eines, gelten die Werte von Max.
Wochenfenster – Pro: Opus 96, Sonnet 97, Haiku 98. Max und Ultra: 98 für alle Modelle, ebenso für das eigene Fable-Wochenfenster.
Fable gilt wie Opus. Unbekanntes Modell: Wert von Opus. Unbekannter Plan: Werte von Pro. Ein fehlendes 5h-Fenster ist nur bei Ultra „nicht vorhanden“, bei Pro und Max bleibt es ein veralteter Sensor. Die Messung darf eine Grenze senken, wenn das gemessene Recap plus Überlauf des letzten Schritts nicht mehr in die Reserve passt, aber nie über den eingestellten Wert heben.“

Alles, was in #156 steht und nicht aus diesem Entscheid stammt, ist dort als „Zusammenfassung“ oder „Vorschlag“ gekennzeichnet und wird hier ebenso behandelt.

### Vorschläge der Session (kein Owner-Entscheid)

Der Entscheid lässt diese Punkte offen. Sie sind im Text mit **(V)** markiert und gehen gebündelt an den Owner:

- V1 `mythos` zählt wie Opus. · V2 Untergrenze der Messung: der untere `clamp`-Wert, sonst `Grenze − 10`. · V3 Kein Sicherheitsfaktor 1,5. · V4 Definition des Überlaufs und seine Quelle. · V5 Ältere `RecapWritten` ohne Modellfeld und ohne Messung zählen nie. · V6 `work_until_pct` gilt für alle Modelle; Einträge je Modell in `system.yaml`. · V7 Bei Ultra ohne 5h-Fenster ist die Woche das maßgebliche Fenster, und ein 5h-Wert zählt nur bis zu seinem `resets_at`. · V8 `100 − (EWMA + Überlauf)` als gesenkte Grenze; drei Recaps je Modell. · V9 `api-key` bleibt 90/90. · V10 `awaken` schreibt `clamp` und keinen `work_until_pct`. · V11 Für das Fable-Wochenfenster gilt bis zu einer Antwort das Wochenfenster `seven_day` mit 98. · V12 Werte in `system.yaml` gelten nur zwischen 0 und 100. · V13 Das Modell kommt aus der Session-ID (Umgebung bzw. `session_id` der Hook-Eingabe); fehlt der Eintrag, gilt Opus.

### Ursache (belegt)

1. `ManaGuard.Decide` verlangt `aktuell + Schritt + Recap <= Grenze` (`src/Monarch.Policy/ManaPolicy.cs:88-89`). Im 5h-Fenster zieht jeder Aufrufer die Recap-Reserve ab (`QuestCommands.RecapReservePct` = 5, `QuestCommands.cs:13`), sie liegt damit **in** der Grenze statt darüber. Im Wochenfenster übergibt kein Aufrufer `recapEstimate7d` (`ManaCommands.cs:235`, `:293`, `:449`; `QuestCommands.cs:77`; `StatusCommand.cs:44`); dort wird heute kein Recap abgezogen (Nachtrag Q-0142, Folgen). Dies ist die Ursache des 19.11.
2. `AdaptiveLimit.Compute` rechnet die Reserve ein zweites Mal ab: `100 − (1,5 × EWMA(Recaps) + p90(Schritte))` (`ManaPolicy.cs:70`, aufgerufen in `src/Monarch.Cli/Support.cs:253-254`), danach zieht `Decide` Schritt und Recap erneut ab. In diesem Repo wirkt das heute nicht: Der untere `clamp`-Wert gleicht `work_until_pct` (`.monarch/system.yaml:8-9`), also liefert `Compute` immer den eingestellten Wert. Für den 19.11. war das nicht ursächlich.
3. Die Grenzen kennen nur den Plan, nicht das Modell (`src/Monarch.Domain/Mana.cs:55-61`: Pro 90/96, Max5 94/97, Max20 96/98).
4. Die Recap-Kosten werden heute nicht gemessen: `ManaCommands.Recap` liest „before“ und „after“ im selben Prozess (`ManaCommands.cs:75-86`), `mana.json` ändert sich dazwischen nicht (nur die Statusline schreibt sie). Alle `RecapWritten` im Ledger haben `cost5h` 0 (Befehl: `grep -rh '"RecapWritten"' .monarch/ledger | grep -o '"cost5h":[^,]*' | sort | uniq -c`). Die Arbeit am Recap liegt zudem vor dem Aufruf des Befehls.

### Was die Statusline liefert (Beleg)

Quelle: Dokumentation „Customize your status line“, `https://code.claude.com/docs/en/statusline`, abgerufen am 19.11.2026 (Zeilen 193-196, 344 und 822 der Seite; vom Warden unabhängig per `curl` bestätigt, `grep -c -i fable` = 0). Wörtlich, Zeile 344: „`rate_limits`: appears only for claude.ai Pro, Max, and Ultra subscribers, or behind a Claude apps gateway that sets a spend limit for you. Even then, it appears only after the first API response in the session. Each window (`five_hour`, `seven_day`, `spend_limit`) may be independently absent, and Claude Code drops a window once its `resets_at` time passes.“ Zeile 822: „For subscribers, the `rate_limits` object can contain a rolling `five_hour` window and a weekly `seven_day` window.“ Teilbeleg im Repo: `docs/adr/0004-rate-limits-in-der-statusline.md:14-16`.

- Die Dokumentation nennt die Fenster `five_hour` und `seven_day`, je mit `used_percentage` und `resets_at`. Jedes Fenster kann einzeln fehlen.
- **Die Dokumentation nennt kein Fable-Fenster und kein Fenster je Modell.** Die echten Statusline-Daten einer Max- oder Ultra-Session wurden nicht geprüft, denn dieses Repo läuft auf `plan: pro` (`.monarch/system.yaml:6`), und `StatuslineInput` verwirft unbekannte Felder (`Dtos.cs`). Der Code liest nur `five_hour` und `seven_day` (`Dtos.cs:137-139`).
- Folge: Der Wert 98 für das Fable-Wochenfenster hat keinen Messwert, auf den der Guard ihn anwenden könnte. **(V11)** Bei Max und Ultra prüft der Guard das Wochenfenster `seven_day` mit 98, auch für Fable-Sessions. Ob Fable zusätzlich ein eigenes Limit erreicht, sieht der Guard nicht. Das ist Frage 1 unten.
- Ultra: Für Konten ohne 5h-Fenster fehlt `five_hour` (Ultra-Hilfeseite, Owner-Kommentar zu #156). Das ist bei `ultra` der Normalfall, bei Pro und Max ein Sensorproblem (Entscheid: „veralteter Sensor“).

### Entscheidung

**1. Der Guard.** `ManaGuard.Decide` lässt einen Schritt zu, wenn in jedem vorhandenen Fenster `aktuell + Schritt <= Grenze` gilt. Die Reserve `100 − Grenze` wird nicht mehr abgezogen; der Parameter für die Recap-Schätzung entfällt. Sonst `PauseWithoutRecap`, wenn der Reset des maßgeblichen Fensters in weniger als 10 Minuten ansteht, andernfalls `RecapAndStop` (beides wie heute). Maßgebliches Fenster ist das 5h-Fenster, bei Ultra ohne 5h-Fenster das Wochenfenster **(V7)**. Veraltete Werte gehen weiter über `Effective5h` und `Effective7d` ein.

**2. Die Tabelle.** Startwerte im Code (`ManaLimits`), je (Plan, Modell, Fenster):

| Plan | 5h Opus | 5h Sonnet | 5h Haiku | Woche Opus | Woche Sonnet | Woche Haiku |
|---|---|---|---|---|---|---|
| Pro | 90 | 93 | 95 | 96 | 97 | 98 |
| Max | 94 | 96 | 97 | 98 | 98 | 98 |
| Ultra | kein 5h-Fenster; kommt eines, gelten die Werte von Max | | | 98 | 98 | 98 |

- **Modellklasse:** `opus`, `sonnet`, `haiku`. `fable` zählt als Opus („Fable gilt wie Opus“); `unknown` und alles Unbekannte ebenso („Unbekanntes Modell: Wert von Opus“; Opus ist der strengste Wert). `mythos` nennt der Entscheid nicht; der Code kennt die Familie (`src/Monarch.Domain/ManaCalibration.cs:8`). **(V1)** `mythos` zählt wie Opus.
- **Quelle des Modells (V13):** das Modell der Session aus `mana.json:sessions[<Session-ID>]` (`ManaCalibration.CurrentPair`, `src/Monarch.Cli/ManaCalibrationStore.cs:135`). Die Session-ID kommt aus `CLAUDE_CODE_SESSION_ID`; das Briefing (SessionStart) nutzt die `session_id` der Hook-Eingabe. Fehlt der Eintrag (beim SessionStart oft noch nicht geschrieben, `Statusline.cs:285-288`), gilt `unknown`, also der Wert von Opus. Das Briefing weist dann „Modell unbekannt, Opus-Grenze“ aus. Darum nennen die Befehle im selben Zustand **für dieselbe Session** denselben Wert.
- **Pläne:** `PlanKind` heißt `Pro`, `Max`, `Ultra`, `ApiKey`. `max5` liest die Datei als Max, `max20` als Ultra (Owner-Entscheid). Ein unbekannter oder fehlender Plan ist Pro („Unbekannter Plan: Werte von Pro“; so war es auch bisher, `SystemConfig.cs:145`). **(V9)** `api-key` bleibt wie heute 90/90 für alle Modelle: Der Entscheid regelt ihn nicht.

**3. `system.yaml`: bestehende und neue Werte.** Die Datei bleibt gültig, auch in Noelia, WT und CP.

| Eintrag | Bedeutung künftig |
|---|---|
| `mana.plan: max5` / `max20` | gilt als `max` / `ultra`. Neue Dateien schreiben `max` und `ultra`. |
| `limits.five_hour.opus`, `.sonnet`, `.haiku` und `limits.seven_day.opus`, `.sonnet`, `.haiku` (neu, optional) | ersetzt die Tabelle für dieses Modell und Fenster. **(V6)** Das ist die Tabelle aus Vorschlag 2 von #156. |
| `work_until_pct` (alt) | gilt für **alle Modelle** und ersetzt die Tabelle (wie bisher die Einstellung des Fensters); ein Eintrag je Modell geht vor. Der Wert wird nie stillschweigend geändert. **(V6)** |
| `clamp: [min, max]` | `min` bleibt die Untergrenze für die Messung (Nachtrag Q-0142), begrenzt auf den eingestellten Wert: Untergrenze = `min(clamp-min, eingestellter Wert)`. Das ersetzt `max(w, min)` aus Q-0142 (der Entscheid „nie über den eingestellten Wert“ geht vor). `max` bleibt ungenutzt. |

- **Gültigkeit (V12).** Jeder Wert (`work_until_pct`, Einträge je Modell) gilt nur, wenn er endlich ist und `0 ≤ Wert ≤ 100` erfüllt; sonst gilt, als stünde er nicht da (Tabelle). Das ist neu: bisher verwirft `SystemConfig.cs:156` nur nicht endliche Werte, die Bereichsprüfung gibt es nur für `clamp` (`:168`). Dazu ein Test, der vorher rot ist (`work_until_pct: 150` und `-5`).
- **Warum Werte in der Datei nicht still geändert werden:** Auch wenn `awaken` sie einst aus den Plan-Standards geschrieben hat, lässt sich ein bewusst gesetzter Wert nicht von einem geschriebenen unterscheiden. Eine Heuristik „gleich dem alten Planwert“ unterbleibt. So bleiben die bestehenden Tests mit `work_until_pct` 96,8 und 98 gültig, soweit ihr Soll-Wert nicht aus dem entfallenden p90 der Schritte stammt; die Ausnahmen stehen in der Testtabelle. Der Preis: In keinem bestehenden Projekt gilt Sonnet 93 oder Haiku 95, bis die Datei geändert ist.
- **Owner-Befehle für `system.yaml`.** Die Session ändert die geschützte Datei nicht. Für dieses Repo (`.monarch/system.yaml`, Zeilen 8-9) nach der Umsetzung:
  - `five_hour: { work_until_pct: 90, clamp: [90, 97] }` wird zu `five_hour: { clamp: [90, 97] }`
  - `seven_day: { work_until_pct: 96, clamp: [96, 99] }` wird zu `seven_day: { clamp: [96, 99] }`
  - Für Noelia, WT und CP gilt dieselbe Regel: In `mana.limits.five_hour` und `mana.limits.seven_day` die Angabe `work_until_pct: N,` entfernen, wenn `N` dem alten Plan-Standard entspricht (Pro 90/96, `max5` 94/97, `max20` 96/98); den `clamp` und `plan: max5`/`max20` stehen lassen. Steht dort ein anderer Wert, ist er bewusst gesetzt und bleibt. Die Dateien dieser Projekte hat die Session nicht gesehen; die Befehle sind darum die Regel, nicht fertige Zeilen.

**4. Ultra ohne 5h-Fenster (V7).** Der Sensor kennt den Plan nicht: Fehlt `five_hour`, setzt die Statusline `rate_limits_stale` immer auf `true` und erneuert `rate_limits_at` nicht (`Statusline.cs:169-178`, `:188-189`); `ToSnapshot` würde die Woche damit bei Ultra immer als veraltet führen (`State.cs:127-128`). Darum:

- `ManaStore.ToSnapshot` bekommt den Plan. Bei `Ultra` und fehlendem `five_hour` gilt: `HasFiveHour = false` im Snapshot; **veraltet** bestimmt allein das Alter des Wochen-Messwerts (`seven_day.observed_at` gegen `StaleAfter`), nicht das Datei-Flag. Alle elf Aufrufer reichen den Plan durch (`HookRunner.cs:364`, `:433`; `ManaCalibrationStore.cs:159`, `:181`; `QuestCommands.cs:70`; `StatusCommand.cs:42`; `ManaCommands.cs:75`, `:82`, `:224`, `:269`, `:418`).
- `ManaGuard.Decide` prüft nur die Woche, wenn `HasFiveHour = false`. `mana` und `status` schreiben „5h: nicht vorhanden“.
- `ManaCalibration.Begin` und `Finish` greifen nicht mehr ungeprüft auf `rl.FiveHour!` zu (`ManaCalibrationStore.cs:169-170`, `:203-205`): Ohne 5h-Fenster entfällt die 5h-Kalibrierung (`reason: no-5h-window`), `quest accept` und `submit` laufen ohne Absturz.
- `rest`, das Briefing und die Meldung aus #87 leiten die Weckzeit und den Reset-Hinweis vom Wochen-Reset ab, wenn `HasFiveHour = false` (`ManaCommands.cs:236-238`, `HookRunner.cs:437`, `QuestCommands.cs:81-82`); kein Reset-Zeitpunkt „Epoch 0“.
- Meldet die Statusline bei Ultra doch ein 5h-Fenster, gelten dessen Werte mit den Grenzen von Max, und zwar nur, solange sein `resets_at` in der Zukunft liegt. Danach gilt es wieder als „nicht vorhanden“ (sonst hielte `Statusline.cs:219-221` den alten Wert fest).
- Bei Pro und Max ändert sich nichts: Fehlt das 5h-Fenster, läuft der letzte bekannte Wert als veraltet weiter (`Statusline.cs:189`, `:219-221`, `State.cs:128`, ADR-0004, `QuestCommands.cs:81-82`); nur wenn nie ein 5h-Wert vorlag, gibt es keinen Snapshot (`State.cs:122-124`).

**5. Messung statt Doppelabzug.** Die Reserve eines (Modells, Fensters) ist `100 − Grenze`. Sie muss das gemessene Recap plus den Überlauf des letzten Schritts fassen.

*Die Recap-Kosten messen.* Heute gibt es keine Messung (Ursache 4). Neu:
- `ManaCommands.Recap` schreibt `RecapWritten` weiter, mit den neuen Feldern `model` (Modellklasse) und `measured: false`. Der Wert `cost5h` in diesem Ereignis zählt **nicht** als Messung; ein Recap ohne gültige Messung zählt nicht als gemessen. Damit zählen auch die alten Nullen nicht mehr für die Schwelle „ab drei Recaps“.
- Die Messung umspannt die Recap-Phase: Beginn ist der letzte frische Snapshot vor dem Recap-Beginn im selben Reset-Fenster. Heute schreiben nur `mana` (`ManaCommands.cs:280`), `recap` (`:76`, `:83`) und SessionEnd (`HookRunner.cs:359-369`) ein `ManaSnapshotTaken`; `rest` (`ManaCommands.cs:215-256`), die Ablehnung bei `quest accept` und SessionStart schreiben keins. Q-0190 ergänzt sie: `rest` und die `RecapAndStop`-Ablehnung von `quest accept` schreiben einen Snapshot (Anlass `guard-stop`), SessionStart schreibt einen (Anlass `session-start`). Ende ist der erste frische Snapshot nach `RecapWritten` im selben Reset-Fenster. Aus beiden folgt ein neues Ereignis `RecapCostMeasured` (Modell, `cost5h`, `cost7d`, Bezug auf das `RecapWritten`). Gültig nur, wenn beide Snapshots frisch sind, denselben Reset haben und zwischen ihnen keine Quest-Arbeit (`QuestStarted`, `QuestSubmitted`) liegt. Ohne gültiges Paar entsteht kein Ereignis.
- Die EWMA läuft über die letzten fünf `RecapCostMeasured`-Ereignisse **dieses Modells** (V8: „letzte fünf“ wie bisher `AdaptiveLimit.EwmaWindow`). Bis dahin gilt die Tabelle (Owner-Werte). Das ist ehrlich: Die Messung greift erst, wenn echte Messwerte vorliegen. **(V5)** Ältere `RecapWritten` ohne `model` zählen nie, weil sie nicht gemessen sind.

*Der Überlauf des letzten Schritts (V4).* Der Entscheid nennt ihn, definiert ihn aber nicht.
- Quelle ist das jüngste **gültige** `ManaStepRecorded` des Modells, nicht älter als sieben Tage. Gültig: `actual5h` vorhanden, kein Grund (`reason`) für Reset, Überlappung oder veralteten Wert (`ManaCalibrator.Observe`). Von 40 Ereignissen im Ledger sind heute 23 ungültig; ein altes gültiges Ereignis darf darum nicht ewig gelten.
- Verglichen wird gegen die Schätzung, mit der der Guard **entschieden** hat, also Schätzung × Faktor beim accept (`QuestCommands.cs:72`, `:77`), nicht gegen die rohe. Dafür speichern `PendingStep` und `ManaStepRecorded` neu `scaled5h` und `scaled7d` (ManaCalibrationStore.cs, im Scope von Q-0190). 5h: `max(0, actual5h − scaled5h)`; Woche: `max(0, actual7d − scaled7d)`.
- Passt `EWMA + Überlauf` nicht in die Reserve, gilt `Grenze' = 100 − (EWMA + Überlauf)` **(V8)**. Sonst bleibt die Grenze. Bekannte Schwäche: Ein Sprung über eine Pause im selben Fenster zählt als ein großer Schritt (Nachtrag Q-0142, Folgen).
- Die Messung greift erst ab drei gemessenen Recaps des Modells (`AdaptiveLimit.MinRecaps`, wie heute) **(V8)**.
- Sie **hebt nie** über den eingestellten Wert (Tabelle, Eintrag je Modell oder `work_until_pct`).
- Untergrenze **(V2)**: der untere `clamp`-Wert (begrenzt auf den eingestellten Wert, siehe Tabelle oben), wenn die Datei einen gültigen hat. Sonst `Grenze − 10` Prozentpunkte. Das **ersetzt** die Regel „`clamp` fehlt → `min = w`“ aus Nachtrag Q-0142 (Zeile 99), nach der die Messung ohne `clamp` nie senken konnte; der Entscheid „Die Messung darf eine Grenze senken“ geht vor. Mit gültigem `clamp` ist der Fall vom 30.10. (96 → 92) ausgeschlossen; ohne `clamp` ist der Schaden einer Messschwäche auf 10 Punkte begrenzt, nicht ausgeschlossen. Darum schreibt `awaken` weiter einen `clamp` **(V10)**: `clamp: [<Wert für Opus>, 97]` im 5h-Fenster und `[<Wert für Opus>, 99]` in der Woche (heute `AwakenCommand.cs:129-131`), aber keinen `work_until_pct` mehr. In neuen Projekten ist die Messung damit standardmäßig nach unten durch den Opus-Wert begrenzt, wie heute. Die Folge, bewusst: Für Opus, Fable und unbekannte Modelle kann die Messung in Projekten mit diesem `clamp` nichts senken, denn ihre Grenze liegt schon auf der Untergrenze (die Reserve ist dort die größte, 10 Punkte im 5h-Fenster bei Pro). Für Sonnet und Haiku senkt sie höchstens bis zum Opus-Wert. Nur in Dateien ohne `clamp` greift `Grenze − 10`.
- **(V3)** Der Sicherheitsfaktor 1,5 aus der alten Formel entfällt, weil der Entscheid „das gemessene Recap plus Überlauf“ nennt und keinen Faktor. Er ließe sich wieder einführen, ohne die Tabelle zu ändern. Ohne den Faktor und ohne das p90 der Schritte ist die Messung **lockerer** als die alte Formel.

**6. Anzeige.** `mana` und `mana --estimate` zeigen je Fenster die Grenze, die Reserve (`100 − Grenze`) und die Herkunft (`Tabelle`, `system.yaml` oder `gemessen`), samt Plan und Modellklasse, in zusätzlichen Zeilen. Die bestehende Zeile `Limits in use (<Quelle>)` behält ihre Quellwörter (`plan default`, `adaptive`), damit die Tests dazu unverändert bleiben (`ManaLimitTests.cs:49`, `McpTests.cs:60`, `McpProjectTests.cs:793`, `Part2Tests.cs:353`). `status`, Briefing, `rest` und die Ablehnung von `quest accept` nennen dieselbe Grenze (Nachtrag Q-0142, „Alle Befehle nennen denselben Wert“, bleibt). Die Ablehnungsmeldung und die Zeile `+ recap 5%` in `mana` (`ManaCommands.cs:294-295`) nennen keinen Recap-Anteil mehr. Im MCP-Ergebnis von `mana` entfällt `recapReservePct` (`ManaCommands.cs:435`); neu ist `reservePct` (= `100 − Grenze`), und `headroom` ist `Grenze − aktuell − Schritt` (`ManaCommands.cs:451`; `McpTests.cs:63` erwartet heute `90 − 40 − 5 − 5`, künftig 45).

### Warum das für Nutzer und Daten keine neue Gefahr ist

Der Guard schützt vor dem Überziehen des Budgets. Die Reserve bleibt, ihr Umfang ist der Entscheid des Owners. Eine fehlende Messung (kein Snapshot) bleibt wie heute ein Proceed-Fall ohne Prüfung. Der Guard wird **lockerer** (er stoppt später): durch den Wegfall des Recap-Abzugs im 5h-Fenster und durch die Tabelle (Pro 5h Sonnet 90 → 93, Max-Woche 97 → 98; alt `Mana.cs:57-58`). Das ist der ausdrückliche Zweck des Owner-Entscheids. Er wird **strenger**: durch die Messung je Modell; bei Ultra ohne 5h-Fenster, weil künftig das Wochenfenster geprüft wird (heute gibt es dort keinen Snapshot und damit keine Prüfung, `State.cs:122-124`, `QuestCommands.cs:74`); und bei Ultra, falls die Statusline doch ein 5h-Fenster meldet, für Opus (alt 96, neu 94; alt `Mana.cs:59`).

**Neu: Die Grenze hängt an der Umgebungsvariable `CLAUDE_CODE_SESSION_ID`.** Mit der ID einer fremden Haiku-Session ergäbe sich bei Pro 5h 95 statt 90. Für den Schätzfaktor galt das schon (ADR-0012, Entscheidung), für die Grenze ist es neu; die Policy prüft solche Präfixe nicht (kein Treffer in `src/Monarch.Policy`). Das gehört zu den Fragen an den Owner (Frage 3).

### Was dieser Nachtrag ablöst

- Nachtrag Q-0142, Entscheidungstabelle (Zeile 97-99): `max(w, min)` und „`clamp` fehlt → `min = w`“ (ersetzt, siehe Punkt 3 und 5). Der obere `clamp`-Wert bleibt ungenutzt (Zeile 108). Zeile 100 (Ledger nicht lesbar → `max(w, min)`): gilt künftig als „eingestellter Wert“ ohne Messung.
- Entscheidung Punkt 7 (Zeile 58-60) und Nachtrag Q-0149 (Zeile 148: „hängen nicht vom Paar ab“): Die Grenzen hängen jetzt vom Modell ab; die Recap-Reserve im Guard entfällt. Weiter gilt: Die Kalibrierung (Faktor je Paar) ändert keine Grenze, `Resolve()` liefert mit und ohne Kalibrierdatei dasselbe. Die Schätzung je (Modell, Effort) bleibt unverändert.
- Zeile 112 („Im 5h-Fenster rechnet der Guard weiter `Schritt + Recap-Reserve` gegen die Grenze.“): entfällt mit Punkt 1.

### Teststrategie

Neue Fälle: vorher rot, danach grün. Fälle, die heute grün sind und nur ihren Soll-Wert wechseln, sind unten als **Soll-Wechsel** mit altem und neuem Wert aufgeführt; sie sind keine Lockerung, sondern die Folge des Entscheids (Tabelle statt `w`, p90 und Faktor entfallen). Fälle, deren Auslöser mit der neuen Formel entfällt, werden mit einem neuen Auslöser neu aufgesetzt, damit sie weiter etwas prüfen.

*Neue Fälle (vorher rot):*
- Der Fall vom 19.11.: 5h 80 %, Schritt 8 %, Grenze 90 → `Proceed` (alt: `RecapAndStop`). 83 + 8 bei 90 → `RecapAndStop`; Reset in weniger als 10 Minuten → `PauseWithoutRecap`.
- Tabelle: je Plan, Modell und Fenster die Werte aus dem Entscheid; `fable`, `mythos`, `unknown` wie Opus; unbekannter Plan wie Pro; `max5` → Max, `max20` → Ultra in `ParsePlan` und in der Datei; Modell aus der Session-ID; fehlender Eintrag → Opus-Wert.
- Ultra: Snapshot ohne 5h-Fenster, Woche frisch → `Proceed`; Woche über 98 → `RecapAndStop`; die Woche gilt bei fehlendem `five_hour` als frisch, wenn `seven_day.observed_at` jung ist; `quest accept` und `submit` ohne 5h-Fenster ohne Ausnahme; die Weckzeit von `rest` kommt aus dem Wochen-Reset; ein gemeldetes 5h-Fenster nutzt die Grenzen von Max und zählt nur bis `resets_at`. Pro und Max ohne 5h-Fenster: veralteter letzter Wert.
- Messung: `RecapWritten` allein zählt nicht (auch nicht mit `cost5h` > 0); `RecapCostMeasured` mit gültigem Paar zählt; ungültiges Paar (stale, anderer Reset, Quest-Arbeit dazwischen) zählt nicht; drei gemessene Recaps eines Modells, die nicht in die Reserve passen → Grenze sinkt, nie unter die Untergrenze; Recaps eines anderen Modells zählen nicht; die Messung hebt nie über den eingestellten Wert (auch nicht bei Kosten 0); Überlauf gegen die skalierte Schätzung, mit Faktor 0,5 und mit Faktor 2; ein ungültiges oder älteres als sieben Tage altes `ManaStepRecorded` zählt nicht.
- `system.yaml`: `work_until_pct` gilt für alle Modelle; Eintrag je Modell geht vor; `150` und `-5` werden verworfen (neu, vorher rot); `clamp` als Untergrenze; `awaken` schreibt `clamp` ohne `work_until_pct`.
- Ausgabe: `mana --estimate` zeigt Grenze, Reserve und Herkunft je Fenster; `status`, Briefing, `rest` und `quest accept` nennen im selben Zustand und für dieselbe Session denselben Wert.

*Neu aufgesetzt (Regressionstests, keine „vorher rot“-Fälle, sie sichern den Bestand):* Tests, deren Zweck „die Messung senkt die Grenze“ oder „der Guard nutzt die aufgelöste Grenze“ ist, behalten ihren Zweck und bekommen einen neuen Auslöser, weil der alte (p90 der Schritte) entfällt: `ManaLimitTests.cs:44-50` (Fall vom 30.10.), `:79-99` (fehlender oder ungültiger `clamp`), `:143-153` (Guard von `quest accept` nutzt die aufgelöste Grenze), `:174-183` (Schritte und `clamp`) und `:185-193`. Der Auslöser ist: drei gültige `RecapCostMeasured` des Modells, die nicht in die Reserve passen, dazu ein gültiges `ManaStepRecorded` mit Überlauf. Erwartung: Mit `clamp [96, 99]` bleibt die Woche bei 96; ohne `clamp` sinkt die Grenze höchstens auf `Grenze − 10`; der Guard von `quest accept` lehnt bei der gesenkten Grenze ab.
- **Seed-Wechsel:** Tests, deren Seed nur `RecapWritten` enthält, brauchen `RecapCostMeasured` (`ManaLimitTests.cs:49`: „(adaptive) … recaps measured 5“ bliebe sonst bei 0 und `plan default`; `Part2Tests.cs:361`: `adaptive` nach drei `recap`-Aufrufen, was künftig `plan default` ist, weil ein `recap` allein nichts misst; dafür ein zweiter Fall mit gültigem Paar).

*Soll-Wechsel (alt → neu), Folge des Entscheids:*

| Test | alt | neu |
|---|---|---|
| `ManaLimitTests.cs:61-66` | 5h 90 | 94 |
| `ManaLimitTests.cs:101-107` | 7d 96 | 98 |
| `ManaLimitTests.cs:117-141`, Fall 98 / `clamp [96, 99]` | 96 | 98 |
| `ManaLimitTests.cs:72-77` | `w` 85, `clamp [90, 97]` → 90; `w` 92, `clamp [96, 99]` → 96 | 85 bzw. 92 (eingestellter Wert) |
| `ManaTypesTests.cs:25-29`, `CommandTests.cs:20`, `:44`, `ConfigTests.cs:34` | `Max5`/`Max20` | `Max`/`Ultra`; Werte aus der Tabelle |
| `CommandTests.cs:21` | `ManaLimits(94, 97)` nach `awaken --plan max5` | 94 und 98 |
| `McpTests.cs:63` | `90 − 40 − 5 − 5` | 45 |
| `ManaTests.cs` | `aktuell + Schritt + Recap` | `aktuell + Schritt` |
| `Part2Tests.cs:354` | `RecapAndStop` bei 60 + 30 gegen 90 | `Proceed` (kein Recap-Abzug) |

Jeder Soll-Wechsel steht im PR mit altem und neuem Wert und dem Grund; kein Test wird gelockert, gelöscht oder übersprungen.

### Folgen

- `ManaLimits` wird je (Plan, Modell) aufgelöst; `ManaSupport.Resolve` bekommt Plan und Modellklasse. Der Scope von Q-0190 umfasst damit auch `ManaCalibrationStore.cs` und `Statusline.cs` (nur falls die Plan-Auswertung dort nötig wird).
- `ManaPool.CanAfford` (`src/Monarch.Domain/Mana.cs:30`) wird in `src/` nicht aufgerufen, nur in Tests (`ManaTypesTests.cs:14-20`, `DomainPropertyTests.cs:55-61`). Es bleibt unverändert und gehört nicht zu diesem Entscheid.
- Neue Ledger-Felder und -Ereignisse: `RecapWritten.model`, `RecapWritten.measured`, `RecapCostMeasured`, `ManaStepRecorded.scaled5h` und `.scaled7d`. Das Ledger bleibt abwärtskompatibel (alte Zeilen ohne diese Felder zählen nicht).
- Fable-Wochenfenster: offen, bis die Statusline es liefert (eigenes Gate).

### Offene Fragen an den Owner (gebündelt)

1. **Fable-Wochenfenster:** Der Entscheid nennt 98 „ebenso für das eigene Fable-Wochenfenster“, aber die Dokumentation der Statusline liefert dafür kein Fenster. Vorschlag (V11, umkehrbar): Bis es Daten gibt, prüft der Guard bei Max und Ultra das Wochenfenster `seven_day` mit 98, auch für Fable-Sessions. Die Alternative ist, die echten Statusline-Daten einer Max- oder Ultra-Session zu prüfen; das kann nur der Owner.
2. **Sicherheitsfaktor und Untergrenze (V2, V3, V8):** Kein Faktor 1,5 mehr; ohne `clamp` Untergrenze `Grenze − 10`. Vorschlag: so lassen. Beides ist ein Eintrag im Code.
3. **Modell aus der Umgebung:** Die Grenze folgt der Session-ID aus der Umgebung. Vorschlag: so lassen, weil die Policy die Variable schon für den Schätzfaktor nutzt; die Fehlbedienung wirkt nur auf die eigene Session.
4. **`system.yaml`:** Die Befehle oben führt der Owner aus; ohne sie gilt in diesem Repo Sonnet weiter bis 90.
5. **Übrige Vorschläge der Session:** V1 (`mythos` wie Opus), V4 (Definition des Überlaufs aus `ManaStepRecorded`), V5 (alte `RecapWritten` ohne Messung zählen nie), V6 (`work_until_pct` für alle Modelle, Einträge je Modell), V7 (Ultra: Woche maßgeblich, 5h-Wert nur bis `resets_at`), V9 (`api-key` bleibt 90/90), V10 (`awaken` schreibt `clamp` mit dem Opus-Wert, keinen `work_until_pct`), V12 (Werte 0 bis 100), V13 (Modell aus der Session-ID). Vorschlag: alle so lassen; jeder ist ein einzelner, umkehrbarer Eintrag im Code oder in der Tabelle.
