---
id: Q-0187
gate: G-0127
kind: main
rank: A
state: proposed
dod:
  - "plugin.json name umbrarch; marketplace umbrarch-local mit Plugin umbrarch; Beschreibungen angepasst"
  - ".mcp.json Server umbrarch; tools-Listen der Agents und alle Nennungen in Skills und READMEs auf mcp__plugin_umbrarch_umbrarch__"
  - "Verweise monarch:<name> auf umbrarch:<name> in Skills, Agents, CLAUDE.md, StandardRulesLine, Hook-Texten und Slash-Befehlen"
  - "README und plugin/README: Produktname Umbrarch, Repo-URL github.com/DavidOeztuerk/umbrarch, neue Installationsbefehle, Hinweis Codename Monarch"
  - "Repo-Test: Manifeste und Agent-Listen stimmen mit der Konstante überein, kein monarch:<name> und kein mcp__plugin_monarch mehr in der sichtbaren Schicht (vorher rot)"
  - "claude plugin validate für Plugin und Marketplace ohne Fehler"
scope:
  include: ["plugin/**", ".claude-plugin/**", "README.md", "CLAUDE.md", "src/Monarch.Cli/RuleTexts.cs", "src/Monarch.Cli/McpTools.cs", "scripts/install-local.sh", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 6, weekly_pct: 1.5, gold_usd: 0 }
boss: "dotnet test grün; claude plugin validate ohne Fehler"
---
# Sichtbare Schicht: Plugin, Marketplace, MCP-Server, Verweise und READMEs auf Umbrarch
