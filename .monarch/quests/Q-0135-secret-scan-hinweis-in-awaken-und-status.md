---
id: Q-0135
gate: G-0104
kind: main
rank: C
state: abandoned
dod:
  - awaken und status (Text und --json) melden, ob ein aktiver gitleaks-pre-commit-Hook vorhanden ist; Erkennung: core.hooksPath falls gesetzt, sonst `<git rev-parse --git-common-dir>/hooks/pre-commit` (gilt auch in Worktrees); aktiv heißt vorhanden, ausführbar und ruft gitleaks auf
  - Monarch installiert nichts und ruft gitleaks nicht auf (Test: kein Schreibzugriff unter den Hook-Verzeichnissen)
  - Tests in tests/Monarch.Cli.Tests vorher rot, danach grün: Hook fehlt, Hook ohne gitleaks, nicht ausführbar, core.hooksPath gesetzt, Worktree mit Hook im git-common-dir
  - plugin/agents/knight.md:17 ersetzt: gitleaks läuft über den pre-commit-Hook, der Agent ruft es nicht auf; meldet status keinen aktiven Hook, sagt der Agent das dem Owner; plugin/skills/quest/SKILL.md und plugin/skills/gate/SKILL.md enthalten denselben Satz (fehlt dort heute)
  - Warden-Runde über den Code mit Verdict am Head-SHA; plugin/README.md nennt den Hinweis
  - Abhängigkeit: Start nach Q-0134 (Owner-Reihenfolge)
scope:
  include: ["src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/SecretScan*.cs", "tests/Monarch.Cli.Tests/**", "plugin/agents/knight.md", "plugin/skills/quest/SKILL.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 5, weekly_pct: 1, gold_usd: 0 }
boss: "Worktree ohne gitleaks-Hook -> status meldet keinen aktiven gitleaks-pre-commit-Hook (auch in --json); ausführbarer gitleaks-Hook im git-common-dir -> aktiv"
---
# Secret-Scan-Hinweis in awaken und status
