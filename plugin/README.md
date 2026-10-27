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

Aufloesung: `$MONARCH_BIN` (Entwickler-Override, ohne Pruefsumme) -> `~/.monarch/bin/<version>/monarch`. Beim ersten Lauf je Version wird SHA-256 gegen `SHA256SUMS` geprueft. Danach legt der Wrapper einen Hardlink `.verified-inode` auf das gepruefte Binary und eine Stamp-Datei `.verified`, deren mtime er per `touch -r` vom Binary uebernimmt. Der Fast Path (ohne Fork, nur `test`-Builtins) fuehrt das Binary nur aus, wenn `monarch -ef .verified-inode` (gleicher Inode, also nicht ersetzt) und die mtime von Binary und Stamp gleich ist (`-nt` in beide Richtungen). Wird das Binary veraendert oder der Stamp angefasst, folgt wieder die SHA-Pruefung. Grenzen: `test -nt` vergleicht je nach Shell nur Sekunden, und wer Binary und Stamp mtime-genau nachfuehrt, taeuscht den Fast Path; der Stamp ist Schutz gegen Versehen und einfache Manipulation, kein Schutz gegen einen Angreifer mit Schreibrecht (dafuer sorgt der Pfadschutz der Policy). Bei Abweichung wird nicht ausgefuehrt (Meldung auf stderr).

**Fail-closed fuer PreToolUse in erweckten Projekten:** Fehlt das Binary oder `SHA256SUMS`, gibt es kein Hash-Tool oder gibt es keinen `monarch`-Eintrag, gibt `hook pre-tool-use` `ask` mit Begruendung aus, sofern im cwd oder darueber `.monarch/system.yaml` liegt (Shell-Schleife, kein Fork). Eine Binary-Abweichung von `SHA256SUMS` fuehrt ebenfalls zu `ask`.

**Fail-open sonst:** Ausserhalb erweckter Projekte sowie fuer die anderen Hook-Modi (`post-tool-use`, `stop`, ...; sie erlauben nichts) und `statusline` endet der Wrapper mit Exit 0 und einer stderr-Zeile, damit eine fehlende Installation Claude Code nicht blockiert. Alle anderen Modi (`mcp`, ...) enden mit Exit 1 und nennen den Installationsbefehl. Tests: `plugin/tests/wrapper-test.sh`.

## Inhalt

- `hooks/hooks.json`: 7 Events, PreToolUse inkl. `Read`/`Grep` (Secret-Schutz), Timeouts 5 s (Pre/Post) bzw. 10 s.
- `.mcp.json`: Server `monarch`. Tools heissen in Claude Code `mcp__plugin_monarch_monarch__<tool>` (system_status, gate_plan, quest_transition, evidence_sync, mana_forecast, knowledge_write).
- `skills/`: awaken, status, gate, quest, recap, mana, rest.
- `agents/`: scout, strategist, knight, warden. Plugin-Agents ignorieren `hooks`, `mcpServers`, `permissionMode` (ADR-0005); Rechte setzt allein der PreToolUse-Hook.
