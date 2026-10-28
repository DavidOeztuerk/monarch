---
id: Q-0114
gate: G-0101
kind: main
rank: B
severity: medium
risk_tags: []
state: proposed
scope:
  repo: monarch
  include: ["src/Monarch.Cli/**", "src/Monarch.Domain/**", "tests/**", "docs/adr/0008-*", "plugin/skills/rest/**", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
dod:
  - ADR-0008 umgesetzt: monarch rest berechnet Weckzeit (max resets_at + 3 min, Minute ≠ :00/:30) und gibt CronCreate-Ausdruck aus
  - monarch wake schedule/fire mit launchd (macOS) bzw. systemd-run (Linux), Opt-in über system.yaml mana.wake; fire prüft claude agents --json und resumt nur gestoppte/fehlende Sessions per claude --bg --resume
  - Tests hermetisch mit Fake-launchctl/Fake-claude; rot → grün
boss: "Hermetischer Test: Session gestoppt → wake fire ruft claude --bg --resume <id> mit Prompt auf; Session läuft → keine Kopie"
---
# Weckruf, der einen Session-Neustart überlebt

Siehe ADR-0008. Anlass: CronCreate-Weckruf (08:53) verfiel beim Session-Neustart am 28.10.
