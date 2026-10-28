# ADR-0012 — Mana-Schätzung je (Modell, Effort)

**Datum:** 28.10.2026 · **Status:** vorgeschlagen (Design-Phase Warden G-0101) · **Quest:** Q-0126 (G-0101)

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

1. **Paar je Schritt.** Jeder Schritt (Quest-Phase, Subagent-Lauf) wird mit einem
   Paar `(model, effort)` verbucht.
   - Main: `model` aus dem Statusline-Input. `effort` aus der Umgebung der
     Claude-Code-Prozesse (`CLAUDE_EFFORT`, beobachtet im Tool-Env von 2.1.312),
     sonst `unknown`.
   - Subagent: `model` aus der Agent-Definition (Frontmatter), `effort` vom Main
     geerbt, sofern nichts anderes belegbar ist.
   - Die Quelle wird im Ledger mitgeschrieben (`ManaStepRecorded`).
2. **Schätzung = Quest-Schätzung × Faktor(Paar).**
   - Quest-Schätzungen bleiben in pp und gelten für das Referenzpaar, mit dem
     sie erstellt wurden (Default `(sonnet, default)`).
   - `f(Paar)` ist das gleitende Mittel (EMA, α = 0,3) von `Δpp_ist / Δpp_geschätzt`,
     getrennt für 5h und Woche.
   - Δpp_ist kommt aus den Statusline-Snapshots am Anfang und Ende des Schritts.
     Bei überlappenden Schritten wird proportional zu den Token zugeteilt
     (Token aus SubagentStop/Transcript-Usage). Ohne Token-Angabe wird der Schritt
     nicht kalibriert, nur verbucht.
3. **Startwerte** (Lauf 2, grob, Status `unkalibriert`, bis je Paar 3 Beobachtungen
   vorliegen):
   - `(sonnet, *)` Subagent: 5h ≈ 0,06 pp je 1k Token, Woche ≈ 0,008 pp je 1k
     (Knights Lauf 1/2: 146k ≈ 8–9 pp, 198k ≈ 11 pp; Verhältnis 5h:Woche ≈ 7:1).
   - `(opus, xhigh)` Main: keine isolierte Messung. Obergrenze aus dem Mischfenster
     28.10. 03:53–05:30 (≈ 1,2 Mio. Sonnet-Subagent-Token + Opus-xhigh-Main:
     88 pp 5h, 11 pp Woche, ≈ 0,07 pp 5h je 1k Gesamttoken). Startfaktor
     gegenüber dem Referenzpaar: 1,0, unkalibriert markiert.
   - Unbekanntes Paar: Faktor 1,0, `unkalibriert`.
4. **Anzeige.** `monarch mana`, `mana_forecast` und `quest accept` zeigen für jede
   Schätzung Paar, Faktor und Kalibrierstatus (`n` Beobachtungen). Der Text enthält
   keine Empfehlung zu Modell oder Effort; ein Test prüft das.
5. **Kein Einfluss auf Grenzen.** Die Grenzen (5h 90 %, Woche 96 %) und die
   Recap-Reserve bleiben, wie sie sind. Die Kalibrierung ändert nur die Schätzung.

## Folgen

- Neue Ledger-Events `ManaStepRecorded` (Paar, Δpp geschätzt/ist, Token, Quelle).
  Kalibrierwerte liegen in `.monarch/local/mana-calibration.json` (Engine-geschützt
  wie der Rest von `.monarch/local`).
- Grenze: Ohne Abo-Statusline (API-Key) gibt es kein Δpp, also keine Kalibrierung
  (ADR-0004).
