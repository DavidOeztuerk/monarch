# ADR-0001 — Subagent-Identität kommt aus dem Hook-Input

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Technische Prüfpunkte vor Phase 1, Punkt 1 („Identifiziert der Hook-Input den aufrufenden Subagent?“)

## Annahme im Konzept

Unklar. Der Fallback war „eine Quest = ein Worktree, Zuordnung über `cwd`“.

## Gemessen

Claude Code **2.1.311**, Headless-Probe (`claude -p --model haiku`) mit Logging-Hooks
auf allen Events, ein projektlokaler Agent `probe`, der selbst ein Bash-Tool ruft:

| Event | Main-Thread | im Subagent |
|---|---|---|
| `PreToolUse` | keine `agent_*`-Felder | `agent_id` (z. B. `ac6fc473…`), `agent_type: "probe"` |
| `PostToolUse` | keine | dieselben |
| `SubagentStop` | – | `agent_id`, `agent_type` |

Die Doku (`code.claude.com/docs/en/hooks`, „Common input fields“) bestätigt das:
`agent_id` ist die „unique identifier for the subagent or in-process teammate“, und
`agent_type` ist gesetzt, „when the session uses `--agent` or the hook fires inside a
subagent“. Zusätzlich gibt es `SubagentStart` mit beiden Feldern.

## Entscheidung

1. **Shadow-Zuordnung über `agent_id`/`agent_type` aus dem Hook-Input.** Ein Tool-Call
   ohne `agent_id` gehört dem Main-Thread (Orchestrator).
2. **Quest-Zuordnung:** Je Session ist höchstens eine Quest aktiv (`quest accept`);
   alle Calls der Session, Main und Subagents, buchen auf sie. `agent_type` bestimmt
   die Shadow-Klasse (scout, strategist, knight, warden) für Metriken und Policy.
3. Der `cwd`-Fallback bleibt nur für Läufe ohne aktive Quest: liegt `cwd` in einem
   Worktree `monarch/G-…`, wird das Gate daraus abgeleitet.
4. `hooks.json` bleibt bei den sieben Events des Konzepts; `SubagentStart` ist für
   die Zuordnung nicht nötig, weil `PreToolUse` die Identität schon trägt.

## Folgen

- Agent Teams (Teammates) sind nicht gemessen; der Prüfpunkt bleibt offen.
- `agent_type` ist bei Plugin-Agents der Name aus dem Frontmatter; Policy-Regeln
  dürfen ihn als Schlüssel verwenden (siehe ADR-0005).
