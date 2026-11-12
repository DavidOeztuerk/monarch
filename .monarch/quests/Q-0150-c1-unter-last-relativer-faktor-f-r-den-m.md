---
id: Q-0150
gate: G-0109
kind: main
rank: A
state: cleared
dod:
  - Design zuerst (einfache Sprache, im PR): Nachtrag in docs/adr/0015-relatives-latenz-gate.md. Er nennt den Owner-Entscheid vom 12.11.2026 wörtlich (20 ms p99 auf ruhiger Maschine; unter voller Last ein relativer Faktor gegen monarch --version) und legt ihn nicht neu aus. Er enthält die Messwerte vom 12.11.2026 (abwechselnde Blöcke, über den Wrapper mit installiertem Binary, Load und Prozesse je Block), Median, p95 und Streuung des Verhältnisses und daraus einen vorgeschlagenen Faktor je Klasse. Der Faktor ist als Vorschlag markiert, bis der Owner ihn übernimmt
  - Grenzen der Messung offen benannt: unter welcher Last gemessen wurde, was davon künstliche Last war und was das relative Kriterium nicht erkennt (ein insgesamt langsameres Binary; dafür bleibt Teil 1 von C1 auf ruhiger Maschine)
  - bench/hook-latency.sh bekommt einen eigenen, benannten Modus für C1 unter Last mit eigenen, fest im Skript stehenden Schwellen, getrennt von den CI-Schwellen. Die CI-Schwellen und das Verhalten von GATE_MODE absolute und relative bleiben unverändert. Der neue Modus lässt sich nicht nutzen, um das CI-Gate zu lockern: nur auf macOS, Schwellen nicht über die Umgebung änderbar, ungültige Eingaben sind Exit 2
  - bench/test-gate.sh erweitert, vorher rot, danach grün, deterministisch über BENCH_RESULTS_DIR: knapp unter den C1-Schwellen grün, knapp darüber rot je Klasse; Schwellen nicht über die Umgebung anhebbar; auf einem anderen Betriebssystem Exit 2; alle bestehenden Prüfungen unverändert grün
  - Verweise: der Kommentar zu C1 in .github/workflows/ci.yml nennt die Bedingung ruhige Maschine und den Nachtrag; docs/lessons.md bekommt einen kurzen Nachtrag; die CI-Schritte selbst ändern sich nicht
  - Heikle Quest nach Standardregel 3: Warden-Runde über das Design vor dem Code, kein src-Code, kein anderer Test und keine andere Schwelle aufgeweicht, Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["bench/**", "docs/adr/0015-relatives-latenz-gate.md", "docs/adr/0011-bedrohungsmodell-phase-0.md", "docs/lessons.md", ".github/workflows/ci.yml"]
  exclude: ["src/**"]
estimate: { session_pct: 30, weekly_pct: 5, gold_usd: 0 }
boss: "bench/test-gate.sh grün mit den neuen Fällen (vorher rot), CI auf dem Gate-Branch grün auf beiden Betriebssystemen, und ein echter Lauf des neuen Modus auf dem Mac des Owners über den Wrapper mit installiertem Binary, Ausgabe im PR"
board: { pr: 88 }
---
# C1 unter Last: relativer Faktor für den Mac als Nachtrag zu ADR-0015 (Issue 70)
