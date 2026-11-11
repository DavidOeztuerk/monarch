---
id: Q-0148
gate: G-0108
kind: main
rank: A
state: in_progress
dod:
  - Design zuerst (einfache Sprache, im PR): docs/adr/0015-relatives-latenz-gate.md. Die CI misst auf demselben Runner, verschränkt in Blöcken (Bezug und Szenario abwechselnd), den reinen Programmstart `monarch --version` als Bezug und gatet je Szenario den Aufschlag des Hooks auf diesen Bezug am Median (p50); der Bezug läuft im selben Modus wie das Szenario (direkt: Binary, Wrapper: plugin/scripts/monarch). Das ADR nennt, dass `--version` über System.CommandLine läuft und der Hook es umgeht (src/Monarch.Cli/MonarchApp.cs:12-28), misst diesen Unterschied und begründet den Bezug; ein anderer Bezug nur nach Owner-Entscheid. Der Aufschlag ist so definiert, dass ein gleichmäßig langsamerer Runner ihn nicht verschiebt (gemessen: in-scope p50 5,67 ms in Lauf 41938370015 gegen 8,27 ms in Lauf 42218379672; Verhältnis denied-bash/in-scope stabil 1,55-1,70). Schwelle je Szenario aus gemessenen Läufen (Zahlen und Run-IDs, beide OS, direkt und Wrapper), nicht geschätzt; denied-bash und out-of-scope tragen den Ledger-Append nach der geflushten Entscheidung (HookRunner.cs:199-222). Nachweis, dass ein echter Regress auffällt (z. B. ein zusätzlicher Prozessstart auf dem heißen Pfad). Eine Warden-Design-Runde plus höchstens eine Delta-Runde, Verdict vor dem ersten Code-Commit
  - Lockerung offen benannt: p99 <= 20 ms bleibt unverändert das Kriterium auf der Zielmaschine (MVP-Gate C1,
  - bench/test-gate.sh erweitert, vorher rot, danach grün, deterministisch über BENCH_RESULTS_DIR mit Bezugs-Samples: synthetische Regression nur auf den Hook-Samples -> rot; gleichmäßig langsamerer Runner (Bezug und Hook mit mindestens dem gemessenen Runner-Faktor ≈ 1,5 skaliert) -> grün; Einzelausreißer -> grün; bestehende Prüfungen (p95 besteht, p99 fällt, p95-Spalte; test-gate.sh:20-30) unverändert
  - Echte Fehler-Injektion: ein CI-Lauf auf dem Gate-Branch mit Verzögerungs-Wrapper nur im Bench (kein src-Code) -> relativer Schritt rot auf beiden OS; Run-ID im ADR; im finalen Head ist die Injektion nicht aktiv
  - ci.yml: neuer relativer Schritt blockierend auf Linux und macOS, direkt und über den Wrapper; die macOS-Quarantäne (Kommentar :82-87, continue-on-error :89, Summary-Zeile :95) entfällt, weil der relative Schritt den Wrapper-Modus blockierend abdeckt; der absolute Direkt-Schritt (:80-81) und der Wrapper-Schritt (:88-101) berichten nur noch; der Selbsttest (:78-79) bleibt blockierend; permissions bleiben contents: read (:10-11). Beleg: grüner CI-Lauf auf dem Gate-Branch mit dem relativen Gate auf beiden OS, Run-ID und Zahlen im ADR oder PR
  - Verweise: ADR-0011 Regel 9 (:70) und der ci.yml-Kommentar (:51-53) zeigen auf ADR-0015; docs/lessons.md bekommt einen kurzen Nachtrag (Lockerung offen benannt, C1 weiter offen)
  - Heikle Quest (Standardregel 3: CI-Gate gelockert, Owner-Entscheid 11.11.2026): kein src-Code; kein anderer Test und keine andere Schwelle aufgeweicht; Warden-Runde über den Code mit Verdict am Head-SHA. Abhängigkeit: keine (technisch unabhängig von Q-0149)
scope:
  include: [".github/workflows/ci.yml", "bench/**", "docs/adr/0015-relatives-latenz-gate.md", "docs/adr/0011-bedrohungsmodell-phase-0.md", "docs/lessons.md"]
  exclude: ["src/**"]
estimate: { session_pct: 40, weekly_pct: 6, gold_usd: 0 }
boss: "bench/test-gate.sh: synthetische Regression auf den Hook-Samples -> rot, gleichmäßig langsamerer Runner (Bezug und Hook skaliert) -> grün, Einzelausreißer -> grün, alte Prüfungen unverändert; dazu ein CI-Lauf mit Verzögerungs-Wrapper im Bench rot auf beiden OS und der finale Gate-Lauf grün mit blockierendem relativem Schritt (direkt und Wrapper)"
---
# CI-Latenz: relatives Gate gegen den Programmstart auf demselben Runner (#8)
