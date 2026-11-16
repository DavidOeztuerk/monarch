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

## Nachtrag (16.11.2026, Q-0164): Rückfrage im Subagenten wird Deny mit Ausweg

**Status:** Entwurf, wartet auf die Warden-Designrunde. Kopf, Entscheidung 1–3 und
Folgen oben bleiben wörtlich stehen.

### Owner-Auftrag

Owner-Entscheid (16.11.2026), wörtlich: „Gate B wie geplant: Rückfrage im Subagenten
wird ein Deny mit genanntem Ausweg, mit ADR-0003-Nachtrag, Designrunde und Tests.“

Alles Folgende ist **Zusammenfassung** oder **Vorschlag (nicht vom Owner
entschieden)**. Das Wort „Vorschlag“ steht dort, wo es zutrifft.

### Problem (Zusammenfassung)

Ein Subagent kann keinen Dialog zeigen. Bei `ask` wird der Call dort verweigert,
und das Modell sieht nur den Grund. Der Grund sagt heute nicht, was der Subagent
tun soll. Er kann es mit einer Variante versuchen. Das soll nicht passieren.

### Vorschlag (nicht vom Owner entschieden): Subagent-Kriterium

- Subagent heißt: `agent_id` ist im Hook-Input gesetzt und nicht leer.
  Grund: ADR-0001:28-29 sagt „ohne `agent_id` gehört dem Main-Thread“.
- Main mit `--agent` hat `agent_type`, aber kein `agent_id` (ADR-0001:22-23).
  Diese Session kann einen Dialog zeigen. Sie bleibt bei `ask`.
- `agent_id` ohne `agent_type` gilt als Subagent.
- Offen, nicht gemessen: Teammates (ADR-0001:40). `agent_id` gilt laut ADR-0001:22
  auch für sie. Ob sie einen Dialog zeigen können, weiß niemand.
- Nach Entscheidung 1 (Zeilen 24-26) gibt es weiter keinen Headless-Erkenner. Der
  Nachtrag erkennt nicht „headless“. Er erkennt den Subagenten über ein gemessenes
  Feld.

### Vorschlag (nicht vom Owner entschieden): Ort der Umwandlung

- Eine einzige Stelle im `HookRunner`, nach `Combine` (HookRunner.cs:188 und der
  Fehlerpfad HookRunner.cs:64, :330). Nicht in jeder Regel.
- Die Policy-Regeln und ihre Texte bleiben unverändert. Das Policy-Paket kennt
  keine Identität.
- Mehrere Projekte: `Combine` (ToolCallPolicy.cs:298-310) liefert Deny vor Ask vor
  Allow. Bleibt ein Ask übrig und der Aufrufer ist ein Subagent, wird er zum Deny.
  Ein Deny aus einer anderen Regel bleibt, wie es ist.
- Grund des Deny: ursprünglicher Ask-Grund plus Ausweg-Satz.
  Standard-Ausweg: „Im Subagenten gibt es keine Rückfrage. Stoppe diesen Schritt
  und melde ihn mit Befehl und Grund der Hauptsession.“
- Pro Quelle ein genauerer Ausweg ist möglich (Tabelle). Der Standard-Satz steht
  immer dabei.

### Tabelle der Ask-Quellen

Fundstellen geprüft mit `grep -n` am 16.11.2026. Main: `ask`, Text unverändert.
Subagent (Vorschlag): `deny`, Grund = Originaltext plus Ausweg.

| Fundstelle | Regel | Subagent: Ausweg zusätzlich zum Standard-Satz |
|---|---|---|
| ToolCallPolicy.cs:496 | `rank_s_approval` | keiner; der Standard-Satz genügt |
| ProtectBranchesRule.cs:33 | `protect_branches` (Integrations-Branch) | Hauptsession mergt über `monarch gate merge`, nicht per Push |
| ProtectBranchesRule.cs:142 | `protect_branches` (Push ohne Refspec, Config unlesbar) | Refspec mit Branch-Namen angeben |
| ProtectBranchesRule.cs:189 | `protect_branches` (Push, Branch unlesbar) | Refspec mit Branch-Namen angeben |
| ProtectPathsRule.cs:545 | `protect_paths` Regel 5 | mit Pfadangabe (`-- <pfad>`) wiederholen |
| HookRunner.cs:64 | `hook_error` (Ziel-Projekte nicht ermittelbar) | der Hauptsession melden |
| HookRunner.cs:263 | `hook_error` (Config-Fehler, `ErrorReason`) | der Hauptsession melden |
| HookRunner.cs:330 | `hook_error` (Fehler im Hook-Pfad) | der Hauptsession melden |

Die Texte in den ersten fünf Zeilen nennen den Ausweg zum Teil schon selbst
(ProtectBranchesRule.cs:142, :189, ProtectPathsRule.cs:545). Das Ausweg-Wort
ergänzt dann nur den Standard-Satz. Der genaue Wortlaut je Zeile ist Teil der
Designrunde.

### Vorschlag (nicht vom Owner entschieden): Hook-Fehler-Asks

- Im Subagenten werden auch die drei `hook_error`-Asks zu Deny mit Ausweg, wenn
  `agent_id` aus dem Input lesbar ist. Grund: auch hier kann niemand antworten.
- Ist der Input unlesbar (kein JSON, HookTests.cs:173-179), gibt es keine Identität.
  Dann bleibt es bei `ask`. Der Hook nimmt dann an, es sei Main.
- Ein Hook-Fehler wird nie `allow` (ADR-0003:34-35). Das gilt unverändert.

### Vorschlag (nicht vom Owner entschieden): Ledger

- `PolicyDecided` bekommt `kind: "deny"`.
- Neues Zusatzfeld `converted_from: "ask"` kennzeichnet die umgewandelte Rückfrage.
  Ohne Umwandlung fehlt das Feld.
- Das Regelfeld behält die ursprüngliche Regel (z. B. `protect_branches`).
- Fundstellen heute: HookRunner.cs:207-215 und :334-335.
- Bei mehreren Projekten bekommt jedes Projekt seinen Eintrag mit seinem Ergebnis.

### Keine Lockerung

- `ask` wird nur im Subagenten zu `deny`. Das ist eine Verschärfung.
- Im Main bleibt `ask`, mit unverändertem Text.
- Kein Pfad wird `allow`.
- Hook-Fehler bleiben nie ein stilles `allow` (ADR-0003:34-35).
- Ein Deny bleibt ein Deny; die Umwandlung ändert nur Asks.

### OFFEN (Owner)

1. **Kriterium.** Vorschlag: `agent_id` gesetzt heißt Subagent. Alternative:
   `agent_type`. Dann würde ein Main mit `--agent` fälschlich zum Deny. Dieser
   Nutzer könnte keinen Push-Dialog mehr bestätigen, obwohl er einen sehen kann.
   Und ein Subagent ohne `agent_type` bliebe bei `ask` und wäre verweigert ohne
   Ausweg.
2. **Hook-Fehler-Asks.** Vorschlag: im Subagenten ebenfalls Deny mit Ausweg.
   Alternative: sie bleiben `ask`. Dann sieht der Subagent bei einem Hook-Fehler
   weiter nur den Grund ohne Ausweg und kann es mit Varianten versuchen.
