# ADR-0003 — `ask` verweigert im Headless-Modus; S-Aktionen nutzen das direkt

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Prüfpunkt 3 („Verhält sich `ask` im PreToolUse-Hook in allen Modi gleich?“)

## Gemessen

Claude Code 2.1.311, `claude -p`, Allow-Regel `Bash(touch:*)`. Ein PreToolUse-Hook
antwortet für `touch ask-marker.txt # MONARCH_ASK_PROBE` mit
`permissionDecision: "ask"` und dem Grund `monarch probe: rank S`.

- Der Call wurde **verweigert**, obwohl die Allow-Regel passte.
- Das Modell bekam den Grund als Tool-Fehler zu sehen.
- `ask-marker.txt` existiert nicht; der Kontroll-Call `touch allowed-marker.txt`
  ohne `ask` lief durch.

Doku (`hooks`, „PreToolUse decision control“): Rangfolge mehrerer Hooks
`deny > defer > ask > allow`; im `-p`-Lauf ohne Antwortmöglichkeit liest Claude den
Grund im Tool-Ergebnis. `defer` beendet den Turn, damit der Call später fortgesetzt
werden kann, und gilt **nur** in `-p`.

## Entscheidung

1. S-Aktionen geben immer `ask` zurück. Interaktiv entsteht der native Dialog,
   headless eine Verweigerung – genau das Verhalten, das das Konzept verlangt. Ein
   eigener Headless-Erkenner ist nicht nötig.
2. Der Grund ist ein Satz, der dem Modell sagt, was es tun soll:
   „Rank S: Freigabe nötig. Stoppe diesen Schritt und melde ihn dem Guild Master.“
3. `defer` wird in Phase 0 nicht genutzt. Kandidat für Phase 1: geparkte S-Aktionen,
   die nach `monarch approve` fortgesetzt werden.

## Folgen

Ein Hook-Fehler (Exit ≠ 0 und ≠ 2) darf **nie** zu einem stillen `allow` werden:
der Wrapper antwortet bei internem Fehler für Rank ≥ A mit `ask`.
