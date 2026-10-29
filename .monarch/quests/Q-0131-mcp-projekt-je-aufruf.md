---
id: Q-0131
gate: G-0103
kind: main
rank: A
severity: high
risk_tags: [policy]
state: in_review
scope:
  repo: monarch
  include: ["src/Monarch.Mcp/**", "src/Monarch.Cli/McpTools.cs", "src/Monarch.Cli/ProjectPaths.cs", "src/Monarch.Cli/CliEnv.cs", "tests/**", "docs/adr/0013-*", "plugin/README.md", "plugin/skills/**", "plugin/.mcp.json"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
dod:
  - Design zuerst als ADR-0013 → Warden (Design-Phase) → Code
  - Projekt je MCP-Aufruf statt Start-cwd – MCP-Roots haben Vorrang, falls Claude Code 2.1.312 sie liefert (gemessen, nicht angenommen); sonst ein geprüfter Pfad-Parameter
  - Ein Pfad-Parameter darf nur auf ein erwecktes Projekt zeigen (kanonisiert, Symlinks aufgelöst, nie ~/.monarch), öffnet keine Schutzpfade und hebt keine Grenze zwischen Projekten auf (ein Aufruf = ein Projekt; knowledge_write bleibt auf dessen .monarch/knowledge beschränkt)
  - gate_plan, knowledge_write, mana_forecast, system_status, quest_transition, evidence_sync funktionieren aus einer Session, deren Start-cwd kein erwecktes Projekt ist; Tests rot → grün
boss: "MCP knowledge_write und gate_plan schreiben in das Projekt des Aufrufs, wenn das Start-cwd nicht erweckt ist; Pfad auf ein nicht erwecktes Verzeichnis oder ~/.monarch → verweigert"
board: { pr: 29 }
---
# MCP: Projekt je Aufruf

Befund F4 (27.10.) und Blocker zu Regel 1 (28.10.): Der MCP-Server läuft im Start-cwd der Session und meldet sonst „Not an awakened project“.
