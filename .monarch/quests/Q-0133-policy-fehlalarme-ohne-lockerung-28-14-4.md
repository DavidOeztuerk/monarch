---
id: Q-0133
gate: G-0104
kind: main
rank: A
state: in_review
dod:
  - Design zuerst: docs/adr/0014-fehlalarme-ohne-lockerung.md nennt je Fehlalarm (Issue 28: gh --jq bzw. sed/perl-Muster mit `.*` als Glob gewertet; Issue 14: Interpreter-Prüfung über das ganze Kommando; Issue 47: git restore --staged) Ursache mit Datei:Zeile und Korrektur und zeigt am Maßstab ADR-0011 (Klassen 1-3, G1-G3), dass keine Klasse-1-Nennung eines Schutzpfads durchrutscht; eine Warden-Runde (Design) mit Verdict vor dem ersten Code-Commit; ADR-0011 Regel 2 verweist auf ADR-0014
  - TDD, Tests vorher rot und danach grün, mit dem echten Kommando je Issue in Adr0011Tests.cs bzw. BashFailClosedTests.cs: `gh api repos/o/r/commits --jq '.[0].message'` und ein sed/perl-Muster mit `.*` (Issue 28) -> allow; `cd ~/Projects/.monarch-worktrees/monarch-G-0101 && python3 - <<'EOF' ... EOF` ohne Schutzpfad im Code (Issue 14) -> allow; `git restore --staged .monarch` (Issue 47) -> allow
  - Allow-Tests für lesende Befehle: gh pr checks/view (auch mit --json/--jq), gh run list/view, git fetch/rev-parse/log/diff, jq-Filter wie `jq '.[0]' datei`
  - Deny bleibt: bestehende Tests unverändert grün (BashFailClosedTests.cs:35-41 und 64-68, ToolCallPolicyTests.cs:96, Adr0011Tests.cs:127-128); neue Deny-Tests für rm/mv/Umleitung auf Schutzpfade, `rm -rf .*`, `git restore .monarch`, `git restore --staged --worktree .monarch`, `git restore --source=HEAD~1 --staged .monarch`, Inline-Code mit `.monarch/ledger` (auch zerlegt wie '.mon' + 'arch'), `gh api ... --jq ... > .monarch/ledger/x`
  - Warden-Runde über den Code mit Verdict am Head-SHA; kein Test gelöscht oder gelockert, Latenz-Grenze unverändert, CI grün; plugin/README.md Abschnitt Grenzen nachgezogen
scope:
  include: ["src/Monarch.Policy/**", "tests/Monarch.Policy.Tests/**", "docs/adr/0014-fehlalarme-ohne-lockerung.md", "docs/adr/0011-bedrohungsmodell-phase-0.md", "plugin/README.md"]
  exclude: []
estimate: { session_pct: 20, weekly_pct: 4, gold_usd: 0 }
boss: "Die Kommandos aus #28, #14 und #47 -> allow; `git restore --staged --worktree .monarch`, `rm -rf .*` und `python3 -c` mit '.mon' + 'arch/local/x' -> deny"
---
# Policy: Fehlalarme ohne Lockerung (#28, #14, #47)
