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

**Datum:** 04.11.2026 · **Status:** Entwurf (Design-Review Warden ausstehend)

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
- `Resolve()` wirft bei Lesefehlern nicht. Eine defekte Ledger-Zeile bleibt ein Befund von `ledger verify` und `status` (`StatusCommand.cs:67`) und darf `status`, das Briefing oder `quest accept` nicht abbrechen. Dasselbe Muster steht schon in `ManaCommands.cs:266-268`.
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
