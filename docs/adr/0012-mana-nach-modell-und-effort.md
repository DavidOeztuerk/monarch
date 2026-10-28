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
