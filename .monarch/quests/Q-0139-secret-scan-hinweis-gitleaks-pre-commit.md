---
id: Q-0139
gate: G-0105
kind: main
rank: C
state: cleared
dod:
  - awaken (AwakenCommand.cs:48-66) und status (Text StatusCommand.cs:51-69, --json WriteJson :102-136, damit auch MCP system_status) melden den Secret-Scan-Hook mit Pfad: 'aktiv' (vorhanden, ausführbar, ruft gitleaks auf), 'pre-commit-Hook ohne erkennbaren gitleaks-Aufruf' oder 'kein pre-commit-Hook'; außerhalb von git 'unbekannt'. Erkennung: core.hooksPath falls gesetzt (relativ gegen das Top-Level des Arbeitsbaums), sonst `<git rev-parse --git-common-dir>/hooks/pre-commit` (gilt in Worktrees)
  - Monarch installiert nichts, schreibt nie in Hook-Verzeichnisse und ruft gitleaks nicht auf (Test: Hook-Verzeichnis vor und nach awaken und status unverändert); kein neues Ledger-Feld
  - Tests vorher rot, danach grün: Hook fehlt, Hook ohne gitleaks, Hook nicht ausführbar, core.hooksPath gesetzt, Worktree mit Hook im git-common-dir, status --json-Feld, MCP system_status-Feld (McpTests.cs:43/:55); status bleibt <= 15 Zeilen (CommandTests.cs:141 unverändert)
  - plugin/agents/knight.md:17 ersetzt: gitleaks läuft über den pre-commit-Hook, der Agent ruft es nicht auf; bricht ein Commit mit gitleaks-Fund ab -> stoppen und melden, nie --no-verify; meldet status keinen aktiven Hook -> dem Owner sagen. plugin/skills/quest/SKILL.md und plugin/skills/gate/SKILL.md enthalten denselben Satz; plugin/README.md nennt den Hinweis und die Grenze (Hook-Manager wie pre-commit/lefthook werden nicht aufgelöst)
  - dotnet build/test -c Release grün; Warden-Runde über den Code mit Verdict am Head-SHA
  - Abhängigkeit: nach Q-0138 (Owner-Reihenfolge); vor Q-0140 (beide ändern AwakenCommand.cs)
scope:
  include: ["src/Monarch.Cli/AwakenCommand.cs", "src/Monarch.Cli/StatusCommand.cs", "src/Monarch.Cli/SecretScan*.cs", "tests/Monarch.Cli.Tests/SecretScan*.cs", "tests/Monarch.Cli.Tests/CommandTests.cs", "tests/Monarch.Cli.Tests/McpTests.cs", "tests/Monarch.Cli.Tests/TestSupport.cs", "plugin/agents/knight.md", "plugin/skills/quest/SKILL.md", "plugin/skills/gate/SKILL.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 7, weekly_pct: 1, gold_usd: 0 }
boss: "Test: Worktree ohne Hook -> status (Text und --json) meldet 'kein pre-commit-Hook'; ausführbarer gitleaks-Hook im git-common-dir -> 'aktiv'; Hook-Verzeichnis danach unverändert"
board: { pr: 57 }
---
# Secret-Scan-Hinweis: gitleaks-pre-commit-Hook in awaken und status, knight.md korrigiert
