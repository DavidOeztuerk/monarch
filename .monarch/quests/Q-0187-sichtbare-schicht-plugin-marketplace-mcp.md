---
id: Q-0187
gate: G-0127
kind: main
rank: A
state: cleared
dod:
  - "plugin.json name umbrarch und Version 0.1.1; marketplace umbrarch-local mit Plugin umbrarch; Beschreibungen angepasst"
  - "MONARCH_VERSION im Wrapper und ServerVersion in McpTools.cs auf 0.1.1; jede weitere Stelle mit 0.1.0, die Wrapper oder install-local.sh lesen, ist angepasst (git grep belegt)"
  - "Test in plugin/tests/wrapper-test.sh für den fail-closed-Fall: Wrapper 0.1.1, nur Binary 0.1.0 installiert, pre-tool-use im erweckten Projekt fragt nach (vorher rot, falls der Fall nicht schon grün ist, sonst als Regressionsfall benannt)"
  - "description in plugin/skills/*/SKILL.md nennt Umbrarch statt Monarch"
  - "Briefing-Kopf und CLAUDE.md-Titel bleiben bis v0.2"
  - "Server umbrarch in .mcp.json; tools-Listen der Agents und alle Nennungen in Skills und READMEs auf mcp__plugin_umbrarch_umbrarch__"
  - "Verweise monarch:<name> auf umbrarch:<name> in Skills, Agents, CLAUDE.md, StandardRulesLine, Hook-Texten und Slash-Befehlen"
  - "README und plugin/README: Produktname Umbrarch, Repo-URL github.com/DavidOeztuerk/umbrarch, neue Installationsbefehle, Hinweis Codename Monarch; kein fester Pfad /Users/davidozturk/Projects/monarch mehr, sondern der Platzhalter <repo>"
  - "Repo-Test: Manifeste und Agent-Listen stimmen mit der Konstante überein, kein monarch:<name> und kein mcp__plugin_monarch mehr in der sichtbaren Schicht (vorher rot)"
  - "claude plugin validate für Plugin und Marketplace ohne Fehler"
scope:
  include: ["plugin/**", ".claude-plugin/**", "README.md", "CLAUDE.md", "src/Monarch.Cli/RuleTexts.cs", "src/Monarch.Cli/McpTools.cs", "scripts/install-local.sh", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 7, weekly_pct: 1.8, gold_usd: 0 }
boss: "dotnet test grün; wrapper-test.sh grün; claude plugin validate ohne Fehler"
board: { pr: 154 }
---
# Sichtbare Schicht: Plugin, Marketplace, MCP-Server, Verweise und READMEs auf Umbrarch
