---
id: Q-0160
gate: G-0115
kind: main
rank: A
state: in_progress
dod:
  - Issue 105 nachgestellt als rote Tests in tests/Monarch.Policy.Tests/GitDirOutsideProjectTests.cs: git -C mit literalem absolutem Ordner ausserhalb des Projekts, Regel 2 (Operand .monarch/ledger/...) und Regel 5 (checkout -f) melden heute deny bzw. ask
  - Gegenproben gruen: git -C auf die Projekt-Root oder einen Unterordner, git -C mit Variable, Tilde oder Glob, zusaetzliche --git-dir/--work-tree/GIT_*-Angaben, git -C ueber einen Symlink ins Projekt bleiben deny bzw. ask
  - ADR-0019 in einfacher Sprache: was als Nennung gilt, warum keine Schutzregel faellt (Massstab ADR-0011/0014), Grenzen, Tests
  - Warden-Designrunde mit hoechstens einer Delta-Runde
scope:
  include: ["docs/adr/0019-fehlalarme-ohne-lockerung-iii.md", "tests/Monarch.Policy.Tests/GitDirOutsideProjectTests.cs"]
  exclude: ["src/**", ".github/**", "bench/**", "plugin/**"]
estimate: { session_pct: 10, weekly_pct: 2, gold_usd: 0 }
boss: "Warden-Designrunde approve ueber ADR-0019; rote Tests belegen Issue 105"
---
# Design: git -C ausserhalb des Projekts, Pfade gegen den tatsaechlichen Ordner (ADR-0019)
