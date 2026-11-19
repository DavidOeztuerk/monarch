---
id: Q-0186
gate: G-0127
kind: main
rank: A
state: proposed
dod:
  - "ShadowReadOnly schneidet das Präfix bis zum letzten Doppelpunkt ab, bei Agent-Typ und Listeneintrag (Test je Fall vorher rot, wo er heute nicht schon grün ist; monarch:warden ist schon heute read-only und gilt als Regressionsfall)"
  - "Fälle mit Standardliste warden, scout: umbrarch:warden, umbrarch:scout, x:scout, a:b:warden read-only (die neuen davon vorher rot); umbrarch:knight schreibt; warden: ist nicht read-only"
  - "Fälle mit Eintrag monarch:scribe: schützt umbrarch:scribe und scribe (vorher rot) und weiter monarch:scribe"
  - "Warden-Nachweis für gate merge nur umbrarch:warden und warden; monarch:warden, x:warden, umbrarch:warden-extra, Umbrarch:warden und Wildcards werden verweigert (Test vorher rot)"
  - "Plugin-Name steht im Code genau an einer Stelle (Konstante RuleTexts.PluginName), WardenTypes leitet sich davon ab; Repo-Test: in src/**/*.cs nennt genau eine Zeile umbrarch (vorher rot)"
  - "Übergang neues Binary, altes Plugin als Test: alte Präfixe bleiben read-only und zählen nicht als Warden"
  - "Keine bestehende Schutzprüfung gelockert; volle Suite grün"
scope:
  include: ["src/Monarch.Policy/ToolCallPolicy.cs", "src/Monarch.Cli/GateMergeCommands.cs", "src/Monarch.Cli/RuleTexts.cs", "tests/Monarch.Policy.Tests/**", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 6, weekly_pct: 1.5, gold_usd: 0 }
boss: "dotnet test grün; die neuen Fälle waren vor der Änderung rot"
---
# Schutzregeln beim Namenswechsel fail-closed (ShadowReadOnly, Warden-Nachweis)
