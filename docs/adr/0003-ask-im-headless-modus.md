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

**Status:** Warden-Designrunde approve in der Delta-Runde am Head 14e638d. Die
Owner-Entscheide vom 16.11.2026 sind in Q-0165 eingetragen (unten, wörtlich).
Kopf, Entscheidung 1–3 und Folgen oben bleiben wörtlich stehen.

### Owner-Auftrag

Owner-Entscheid (16.11.2026), wörtlich:
„Gate B wie geplant: Rückfrage im Subagenten wird ein Deny mit genanntem Ausweg, mit ADR-0003-Nachtrag, Designrunde und Tests.“

### Owner-Entscheide zu OFFEN 1–5 (16.11.2026)

Wörtlich, je eine Antwort des Owners im Chat:

1. „Erkennung über agent_id. Eine Hauptsession mit --agent behält ihre Rückfrage, Teammates bekommen ein Deny.“
2. „Hook-Fehler im Subagenten: Deny mit dem festen Ausweg-Satz.“
3. „Das Deny gilt für jeden Subagenten, auch im Vordergrund. Ergänz im Nachtrag die Beobachtung vom 15.11.2026 (#105): Die Rückfrage eines Hintergrund-Wardens hat beim Owner einen Dialog gezeigt und rund 5 h gewartet. Der Satz "ein Subagent kann keinen Dialog zeigen" stimmt so nicht.“
4. „Rückfragen des Wrappers bleiben in diesem Gate; als Issue festhalten.“
5. „Umgewandelte Rückfragen zählen nicht als Regelverstoß.“

Die Antworten 6 und 7 vom selben Tag betreffen Q-0166 (subagent-stop) und
stehen im Nachtrag zu ADR-0001.

Alles Folgende ist **Zusammenfassung**. Wo ein Owner-Entscheid gilt, steht seine
Nummer dabei.

### Problem (Zusammenfassung)

Bei `ask` ohne Antwortmöglichkeit wird der Call verweigert, und das Modell sieht
nur den Grund (gemessen für den Main-Thread in `-p`, ADR-0003:8-15). Der Grund
sagt heute nicht, was das Modell tun soll. Es kann es mit einer Variante
versuchen. Das soll nicht passieren.

**Beobachtung vom 15.11.2026 (#105, Abschnitt „Folge der Rückfrage:
Hintergrund-Agent 5 Stunden blockiert“, gelesen mit `gh issue view 105`):** Die
Rückfrage eines Hintergrund-Wardens (Designrunde G-0114) hat beim Owner einen
Dialog gezeigt. Der Agent wartete rund 5 h 03 min auf die Antwort; gearbeitet hat
er in der Runde rund 16 min. Ein Subagent kann also einen Dialog zeigen. Die
Rückfrage hält ihn dann an, bis jemand antwortet. Das Deny gilt trotzdem für jeden
Subagenten, auch im Vordergrund (Owner-Entscheid 3).

### Subagent-Kriterium (Owner-Entscheid 1)

- Subagent heißt: `agent_id` ist ein JSON-String mit mindestens einem Zeichen, das
  kein Leerzeichen ist. Grund: ADR-0001:28-29 sagt „ohne `agent_id` gehört dem
  Main-Thread“.
- Fehlt `agent_id`, ist es leer oder nur Leerraum, gilt der Call als Main. Es
  bleibt `ask`, wie heute. Das ist nie schwächer als heute.
- Main mit `--agent` hat `agent_type`, aber kein `agent_id` (ADR-0001:21-23). Das
  folgt aus der Doku und ist nicht gemessen. Diese Session bleibt bei `ask`.
- `agent_id` ohne `agent_type` gilt als Subagent.
- Teammates bekommen ein Deny (Owner-Entscheid 1). Nicht gemessen ist, dass sie
  `agent_id` tragen (ADR-0001:40); laut ADR-0001:22 gilt das Feld auch für sie.
- Nach Entscheidung 1 (Zeilen 24-26) gibt es weiter keinen Headless-Erkenner. Der
  Nachtrag erkennt nicht „headless“. Er erkennt den Subagenten über ein Feld, das
  ADR-0001 gemessen hat.
- Der bestehende Code erkennt Subagenten über `agent_type`
  (ProtectBranchesRule.cs:20, ToolCallPolicy.cs:453). Der Nachtrag nutzt
  `agent_id`. Das ist ein bewusster Unterschied (Owner-Entscheid 1).

### Ort und Form der Umwandlung (technisches Design, Warden-Freigabe)

- Eine einzige Stelle im `HookRunner`, nach `Combine` (HookRunner.cs:188 und der
  Fehlerpfad HookRunner.cs:64, :330). Nicht in jeder Regel.
- Die Policy-Regeln und ihre Texte bleiben unverändert. Das Policy-Paket kennt
  keine Identität.
- Die Umwandlung ist eine reine Funktion (`internal`, kein Datei- oder
  Prozesszugriff). Eingabe: Entscheidung und `agent_id`. Ausgabe: Entscheidung und
  Kennzeichen. Tests erreichen sie über `InternalsVisibleTo`
  (Monarch.Cli.csproj:18, Eintrag `Monarch.Cli.Tests`).
- Die Umwandlung darf selbst nie werfen. Schlägt sie fehl, bleibt es bei `ask`.
  Grund: Es gibt keinen äußeren `catch` (Program.cs:4 ruft `MonarchApp.Run`
  direkt auf, MonarchApp.cs:14 ruft `HookRunner.Run`). Ein Absturz wäre ein
  stilles `allow` gegen ADR-0003:34-35.
- Mehrere Projekte: Umgewandelt wird das kombinierte Endergebnis nach `Combine`
  (ToolCallPolicy.cs:298-310 liefert Deny vor Ask vor Allow). Bleibt ein Ask übrig
  und der Aufrufer ist ein Subagent, wird er zum Deny. Ein Deny aus einer anderen
  Regel bleibt, wie es ist.
- **Fester Ausweg-Satz, für alle umgewandelten Quellen gleich, ohne Zusätze je
  Quelle:** „Im Subagenten gibt es keine Rückfrage. Stoppe diesen Schritt und melde ihn mit Befehl und Grund der Hauptsession.“
- Deny-Grund = unveränderter Originaltext + Leerzeichen + Ausweg-Satz.
- Begründung: Mehrere Quellen teilen Regelname und Text (drei Mal
  `protect_branches`, drei Mal `hook_error`). Der `HookRunner` müsste die Quelle
  erst erkennen. Außerdem passt ein Ausweg wie „Pfadangabe“ nicht zu
  `git switch -f` oder `git reset --keep/--merge`. Bei `git checkout -f` kann er
  zu einer Aktion verleiten, die Arbeit verwirft (ProtectPathsRule.cs:537-541,
  beim Lesen der Zeilen geprüft: :537-541 enthalten `stash -u`, `checkout -f`,
  `switch -f`, `reset --keep/--merge`, Zeile 545 den Ask).
- Bekannter Rest (#121): Der Originaltext von Quelle 5 (ProtectPathsRule.cs:546,
  gefunden mit `grep -n 'Pfadangabe' src/Monarch.Policy/ProtectPathsRule.cs`) sagt
  weiter „mit Pfadangabe (-- <pfad>) ist es gezielt“. Er bleibt im Deny-Grund
  stehen, weil der Originaltext unverändert bleibt. Das ist keine Lockerung:
  Denselben Weg erlaubt die Policy auch im Main.

### Tabelle der Ask-Quellen

Fundstellen geprüft am 16.11.2026 mit
`grep -n "Ask(" src/Monarch.Policy/*.cs`,
`grep -n '"ask"' src/Monarch.Cli/HookRunner.cs`,
`grep -n 'Ask(' src/Monarch.Cli/HookRunner.cs` (Quelle 7, :263) und
`grep -rn '"ask"' plugin/` (Treffer: plugin/scripts/monarch:37 und die Testdatei
plugin/tests/wrapper-test.sh:33; die Aufrufe :48 und :100 per
`grep -n "ask \"" plugin/scripts/monarch`).
Main: `ask`, Text unverändert. Subagent: wie in der Spalte. Die Spalte
„Auslöser“ nennt den Auslöser für den Test.

| Nr | Fundstelle | Regel | Subagent | Auslöser für den Hook-Test |
|---|---|---|---|---|
| 1 | ToolCallPolicy.cs:496 | `rank_s_approval` | deny, Original + Ausweg-Satz | Quest mit Rank S über TempProject |
| 2 | ProtectBranchesRule.cs:33 | `protect_branches` (Integrations-Branch) | deny, Original + Ausweg-Satz | Push auf den Integrations-Branch, TempProject |
| 3 | ProtectBranchesRule.cs:142 | `protect_branches` (Push ohne Refspec, Config unlesbar) | deny, Original + Ausweg-Satz | Push ohne Refspec mit unlesbarer Git-Config, TempProject |
| 4 | ProtectBranchesRule.cs:189 | `protect_branches` (Push, Branch unlesbar) | deny, Original + Ausweg-Satz | Push bei detached HEAD, TempProject |
| 5 | ProtectPathsRule.cs:545 | `protect_paths` Regel 5 | deny, Original + Ausweg-Satz | `git checkout -f` ohne Pfad, TempProject |
| 6 | HookRunner.cs:64 | `hook_error` (Ziel-Projekte nicht ermittelbar) | deny, Original + Ausweg-Satz | kein Auslöser belegt: nur über die reine Funktion geprüft |
| 7 | HookRunner.cs:263 | `hook_error` (Config-Fehler) | deny, Original + Ausweg-Satz | Config-Fehler wie HookTests.cs:122-151 (kaputte `system.yaml`, Rank A) |
| 8 | HookRunner.cs:330 | `hook_error` (Fehler im Hook-Pfad) | deny, Original + Ausweg-Satz | kein Auslöser belegt: nur über die reine Funktion geprüft |
| 9 | plugin/scripts/monarch:48 | Wrapper (`fail()`: Binary oder SHA256SUMS fehlt und weitere Wrapper-Fehler, Aufrufe :61, :67, :82, :87, :96, :102) | bleibt `ask` (Owner-Entscheid 4) | Wrapper-Test plugin/tests/wrapper-test.sh, außerhalb von Q-0165 |
| 10 | plugin/scripts/monarch:100 | Wrapper (SHA weicht ab) | bleibt `ask` (Owner-Entscheid 4) | Wrapper-Test plugin/tests/wrapper-test.sh, außerhalb von Q-0165 |

Zu 9 und 10: Beide Ausgaben laufen über `ask()` (plugin/scripts/monarch:35-38,
Ausgabe in Zeile 37). Der Wrapper wird aus plugin/hooks/hooks.json:20 aufgerufen.
Er läuft, bevor das Binary den Input liest. Er kennt `agent_id` nicht. Und
`plugin/**` liegt außerhalb des Scopes von Q-0165. Festgehalten in #122
(Owner-Entscheid 4). Die Liste der `fail()`-Aufrufe stammt aus
`grep -n -E 'fail |ask "' plugin/scripts/monarch`.

Zu 6: Der Pfad HookRunner.cs:56-65 ruft nur `Log` und `Emit` auf und schreibt kein
`PolicyDecided`. Er tut das auch künftig nicht (selbst nachgeprüft beim Lesen von
HookRunner.cs:56-66). Die Quelle 6 hat darum keinen Ledger-Eintrag, nur die
Umwandlung der Ausgabe.

Zu 8: Der Fehlerpfad bei :330 schreibt `PolicyDecided` (HookRunner.cs:334-335).
`input` kann dort null sein; dann bleibt es `ask`.

### Hook-Fehler-Asks (Owner-Entscheid 2)

- Im Subagenten werden auch die drei `hook_error`-Asks (Quellen 6-8) zu Deny mit
  Ausweg, wenn `agent_id` aus dem Input lesbar ist. Grund: auch hier gibt es keine
  gemessene Antwortmöglichkeit.
- Unlesbar heißt: kein JSON (HookTests.cs:173-179, Parse-Fehler als
  `JsonException`, HookRunner.cs:33-38) oder ein Feld mit falschem Typ, auch in
  `tool_input` (Dtos.cs:16, :29-41). Dann ist die Identität verloren. Es bleibt
  `ask`, wie heute.
- Ein Hook-Fehler wird nie `allow` (ADR-0003:34-35). Das gilt unverändert.

### Ledger (technisches Design, Warden-Freigabe; Zählung nach Owner-Entscheid 5)

- `PolicyDecided` bekommt `kind: "deny"`.
- Neues Zusatzfeld `converted_from: "ask"` kennzeichnet die umgewandelte Rückfrage.
  Ohne Umwandlung fehlt das Feld.
- Umgewandelte Einträge schreiben auch `agent_id` mit.
- Das Regelfeld behält die ursprüngliche Regel (z. B. `protect_branches`).
- Fundstellen heute: HookRunner.cs:207-215 und :334-335.
- Mehrere Projekte, im Subagenten: Jedes Projekt, dessen eigene Entscheidung
  `ask` war, bekommt `kind: "deny"` mit `converted_from: "ask"`. Projekte mit
  anderem Ergebnis schreiben wie heute. Im Main wird nichts umgewandelt.
- Umgewandelte Rückfragen zählen nicht als Regelverstoß (Owner-Entscheid 5). Sie
  sind über `converted_from` erkennbar. In `src` liest heute niemand
  `PolicyDecided` (`grep -rn PolicyDecided src`: nur die Schreiber). Der e2e-Test
  e2e/agent-tamper.sh:90 zählt jedes `deny` als Mindestzahl für die Abwehr mit,
  also auch umgewandelte; `e2e/**` liegt außerhalb von Q-0165, die Anpassung ist
  ein eigenes Issue.

### Keine Lockerung

- `ask` wird nur im Subagenten zu `deny`. Das ist eine Verschärfung.
- Im Main bleibt `ask`, mit unverändertem Text.
- Kein Pfad wird `allow`.
- Hook-Fehler bleiben nie ein stilles `allow` (ADR-0003:34-35).
- Ein Deny bleibt ein Deny; die Umwandlung ändert nur Asks.

### Früher OFFEN, jetzt entschieden (Zusammenfassung)

1. Kriterium `agent_id` (Owner-Entscheid 1). Nicht gemessen bleibt: dass ein Main
   mit `--agent` kein `agent_id` hat (folgt aus ADR-0001:21-23).
2. Hook-Fehler-Asks im Subagenten werden Deny mit dem festen Ausweg-Satz
   (Owner-Entscheid 2).
3. Das Deny gilt für jeden Subagenten, auch im Vordergrund (Owner-Entscheid 3).
   Ein Subagent kann einen Dialog zeigen (Beobachtung vom 15.11.2026, oben).
4. Wrapper-Asks (Quellen 9 und 10) bleiben in diesem Gate `ask`; Issue #122
   (Owner-Entscheid 4).
5. Umgewandelte Rückfragen zählen nicht als Regelverstoß (Owner-Entscheid 5).
