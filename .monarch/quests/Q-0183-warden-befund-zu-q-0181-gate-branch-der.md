---
id: Q-0183
gate: G-0126
kind: main
rank: A
state: in_review
dod:
  - "Edit, Write, MultiEdit, NotebookEdit auf einen Pfad in einer Worktree auf monarch/G-*, die nicht das cwd-Projekt mit aktiver in_progress-Quest ist, werden verweigert (auch bei Aufruf aus dem Haupt-Checkout)"
  - "Unlesbares HEAD oder kaputte .git-Datei auf dem Zielpfad verweigert, statt still zu erlauben"
  - "Test je Fall vorher rot, danach grün; keine andere Regel gelockert"
  - "Test: **/.DS_Store zählt als vorhanden, awaken legt es nicht doppelt an (Q-0180, minor)"
scope:
  include: ["src/Monarch.Policy/**", "tests/Monarch.Policy.Tests/**", "tests/Monarch.Cli.Tests/**"]
  exclude: []
estimate: { session_pct: 4, weekly_pct: 1.5, gold_usd: 0 }
boss: "Kein Edit auf einem Gate-Branch vor quest start, auch nicht von außen"
board: { pr: 150 }
---
# Warden-Befund zu Q-0181: Gate-Branch der Zieldatei prüfen, unlesbares HEAD verweigern (PR 150, major)
