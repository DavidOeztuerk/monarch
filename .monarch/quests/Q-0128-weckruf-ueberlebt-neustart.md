---
id: Q-0128
gate: G-0101
kind: main
rank: B
severity: medium
risk_tags: []
state: in_review
scope:
  repo: monarch
  include: ["src/Monarch.Cli/**", "src/Monarch.Domain/**", "tests/**", "docs/adr/0008-*", "plugin/skills/rest/**", "plugin/README.md"]
  exclude: ["src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/ProjectPaths.cs"]
estimate: { session_pct: 15, weekly_pct: 3, gold_usd: 0 }
dod:
  - ADR-0008 nach Design-Auflagen des Wardens (G-0101) ergänzt: Anlassfall (Session lebt, Cron weg) gelöst – fire zur Weckzeit + Grace (> 90 s), Session idle und ohne Ledger-Aktivität seit resets_at → bei bg-Session claude stop + --bg --resume, bei interaktiver Session WakeSkipped; claude agents fehlgeschlagen → kein Resume; Rest-Race als Grenze; launchd RunAtLoad + Zeitprüfung, veraltetes --at verwerfen; systemd-run transient als Grenze in README
  - monarch rest berechnet Weckzeit (max resets_at + 3 min, Minute ≠ :00/:30) und gibt CronCreate-Ausdruck aus; Abschnitt „Weckruf“ im rest-Skill
  - wake schedule nur bei Opt-in in system.yaml (Binary prüft selbst), festes Prompt-Template (kein Agent-Freitext im persistenten Job), Session-ID als UUID geprüft, ProgramArguments ohne Shell, absolute Pfade, WorkingDirectory = Projekt, höchstens ein Job je Session, Shadows verweigert
  - Tests nur mit HOME im Temp-Verzeichnis (nie ~/Library/LaunchAgents), Plattform injizierbar, Fake-launchctl/Fake-claude; rot → grün; bestehende Tests nicht abgeschwächt
boss: "Hermetischer Test: bg-Session idle ohne Aktivität seit resets_at → fire ruft claude stop und claude --bg --resume <id> mit festem Prompt; laufende Session mit Aktivität → keine Kopie; interaktive Session → WakeSkipped"
board: { pr: 15 }
---
# Weckruf, der einen Session-Neustart überlebt

Ersetzt Q-0114 (abgebrochen: Scope enthielt policy-Pfade bei Rank B, vom Warden als Abschwächung gewertet). Siehe ADR-0008.
