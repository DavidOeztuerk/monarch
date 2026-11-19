---
id: Q-0176
gate: G-0124
kind: main
rank: C
state: abandoned
dod:
  - Test mit Sonderzeichen in den DoD-Texten (Anfang mit Raute, Doppelpunkt, Backtick, Leerzeichen plus Raute) liest den vollstaendigen Satz zurueck; vorher rot
  - Gate-Datei enthaelt boss, estimate und risk_tags, Quest-Datei risk_tags; Test vorher rot
  - dotnet test -c Release gruen
scope:
  include: ["src/Monarch.Cli/McpTools.cs", "src/Monarch.Infrastructure.Boards/**"]
  exclude: []
estimate: { session_pct: 6, weekly_pct: 1.5, gold_usd: 0 }
---
# gate_plan quotet DoD-Texte und schreibt boss, estimate, risk_tags (Issue 50)
