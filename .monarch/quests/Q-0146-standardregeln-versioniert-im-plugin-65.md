---
id: Q-0146
gate: G-0107
kind: main
rank: A
state: cleared
dod:
  - Die Standardregeln liegen versioniert im Plugin; das Briefing nennt den Kern und verweist auf den Rest und bleibt kurz.
  - Abgedeckt: Worktree je Gate, Prüfung nach Risiko, Merge nur per gate merge, kein [skip ci] vor dem Merge, Issue-Texte per --body-file, Aufräumen nach dem Merge.
  - Test: Das Briefing nennt Kern und Verweis ohne Hinweis in der Prompt.
scope:
  include: ["plugin/skills/**", "plugin/README.md", "plugin/tests/**", "src/Monarch.Cli/QuestCommands.cs", "src/Monarch.Cli/HookRunner.cs", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1, gold_usd: 0 }
boss: "Die Standardregeln fehlen im Briefing oder werden nur per Hinweis in der Prompt weitergegeben und gehen verloren."
board: { pr: 77 }
---
# Standardregeln versioniert im Plugin (#65)
