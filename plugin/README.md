# Monarch Plugin

Claude Code Plugin fuer Monarch (Gates, Quests, Shadows, Mana). Lizenz: Apache-2.0.

## Installation (lokal)

```sh
cd /Users/davidozturk/Projects/monarch
scripts/install-local.sh
```

Das Skript baut das CLI (`dotnet publish`, aktuelle Plattform), installiert es nach `~/.monarch/bin/<version>/monarch`, schreibt `SHA256SUMS` und setzt den Symlink `~/.monarch/bin/monarch`.

Danach in Claude Code:

```
/plugin marketplace add /Users/davidozturk/Projects/monarch
/plugin install monarch@monarch-local
```

Anschliessend Claude Code **neu starten** (Hooks, MCP-Server und Statusline werden beim Start geladen). Im Projekt dann `/monarch:awaken`.

## Statusline

Eine vorhandene Statusline wird gewrappt. Ihr Kommando gehoert nach `~/.monarch/statusline.json`, danach zeigt `statusLine` in `~/.claude/settings.json` auf Monarch:

```json
{
  "statusLine": {
    "type": "command",
    "command": "~/.monarch/bin/monarch statusline",
    "refreshInterval": 30
  }
}
```

`refreshInterval: 30` schliesst laut ADR-0004 die Luecke, wenn die Main-Session auf Subagents wartet. `awaken` setzt es, falls nicht vorhanden. Neustart erforderlich.

## Wrapper `scripts/monarch`

Aufloesung: `$MONARCH_BIN` (Entwickler-Override, ohne Pruefsumme) -> `~/.monarch/bin/<version>/monarch`. Beim ersten Lauf je Version wird SHA-256 gegen `SHA256SUMS` geprueft und ein Stamp (Groesse + mtime) geschrieben; spaetere Aufrufe ueberspringen das Hashing. Bei Abweichung wird nicht ausgefuehrt (Meldung auf stderr).

**Fail-open (Phase-0-Trade-off):** Fehlt das Binary oder ist die Pruefung nicht moeglich, beenden `hook` und `statusline` mit Exit 0 und genau einer stderr-Zeile, damit eine fehlende Installation Claude Code nicht blockiert. Das heisst: ohne Binary (oder bei manipuliertem Binary) greift **keine** Policy (Scope Lock, Secret-Schutz). Alle anderen Modi (`mcp`, ...) enden mit Exit 1 und nennen den Installationsbefehl. Ein Fail-closed-Modus ist fuer spaetere Phasen vorgesehen.

## Inhalt

- `hooks/hooks.json`: 7 Events, PreToolUse inkl. `Read`/`Grep` (Secret-Schutz), Timeouts 5 s (Pre/Post) bzw. 10 s.
- `.mcp.json`: Server `monarch`. Tools heissen in Claude Code `mcp__plugin_monarch_monarch__<tool>` (system_status, gate_plan, quest_transition, evidence_sync, mana_forecast, knowledge_write).
- `skills/`: awaken, status, gate, quest, recap, mana, rest.
- `agents/`: scout, strategist, knight, warden. Plugin-Agents ignorieren `hooks`, `mcpServers`, `permissionMode` (ADR-0005); Rechte setzt allein der PreToolUse-Hook.
