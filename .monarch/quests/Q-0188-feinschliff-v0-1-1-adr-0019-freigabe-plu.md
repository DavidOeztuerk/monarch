---
id: Q-0188
gate: G-0128
kind: main
rank: C
state: in_review
dod:
  - "ADR-0019: Status mit der Freigabe des Owners vom 19.11.2026 (wörtlich), Antwort zur Lücke in 'Nicht in diesem ADR', Beleg zu Zeile 105 korrigiert (wrapper-test.sh:43 allgemeiner Fail-open-Fall); sonst nichts am ADR"
  - "plugin.json: description ohne 'Monarch'; Test in VisibleLayerNameTests vorher rot, danach grün"
  - "README.md und plugin/README.md nennen https://github.com/DavidOeztuerk/umbrarch und 'git clone <url> <repo>' als ersten Installationsschritt"
  - "PluginNameConstantTests: Name entspricht dem Geprüften, Ableitung der Warden-Typen von RuleTexts.PluginName wird geprüft"
  - "dotnet test grün"
scope:
  include: ["docs/adr/0019-produktname-umbrarch.md", "plugin/.claude-plugin/plugin.json", "README.md", "plugin/README.md", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 8, weekly_pct: 2, gold_usd: 0 }
boss: "dotnet test grün und gate check grün"
---
# Feinschliff v0.1.1: ADR-0019 Freigabe, plugin.json ohne Monarch, Repo-URL, Testname
