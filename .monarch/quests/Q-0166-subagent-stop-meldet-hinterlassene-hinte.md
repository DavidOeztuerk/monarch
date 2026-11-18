---
id: Q-0166
gate: G-0118
kind: main
rank: A
state: in_progress
dod:
  - Abhängigkeit: nach Q-0165 (gleiche Datei HookRunner.cs). Zuerst messen und als Nachtrag zu ADR-0001 in einfacher Sprache belegen (Claude-Code-Version, Befehl, Ergebnis): Felder des SubagentStop-Inputs; ob Hintergrund-Shells des Subagenten bei SubagentStop noch laufen und woran sie sich von Shells der Hauptsession und fremder Sitzungen unterscheiden; ob und über welches Feld eine Hook-Ausgabe die Hauptsession erreicht (Kandidaten: Ausgabe von SubagentStop, additionalContext im PostToolUse des Agent-Tools der Hauptsession). Unbelegtes bleibt als unbelegt markiert
  - Warden-Designrunde über den Nachtrag (eine Runde, höchstens eine Delta-Runde), danach TDD (vorher rot, danach grün)
  - Meldet nur: beendet keinen Prozess, sendet kein Signal, hält den Stopp des Subagenten nicht auf (kein decision block); ein Test belegt, dass ein gestarteter Kindprozess nach dem Hook noch läuft
  - Findet der Hook Shells: ein Ledger-Ereignis (Name laut Nachtrag) mit PID, Befehl und Laufzeit je Shell sowie agent_id und agent_type; der Befehl kommt nach der Regel des Nachtrags ins getrackte Ledger (Länge, mögliche Secrets in Kommandozeilen). Keine Shells: kein weiteres Ereignis, Subagent_stop_stop_and_session_end_append_events (HookTests.cs:262-273) unverändert grün
  - Hinweis an die Hauptsession nur über den gemessen belegten Weg; ohne Beleg kein Hinweis-Feld, dann nur Ledger und Nennung im PR
  - Plattformfest: Prozessliste auf macOS und Linux, CI auf beiden Betriebssystemen grün; Hilfsprogramm nur absolut aus PATH (ADR-0016); feste Frist für die Abfrage, Hook-Laufzeit im Test deutlich unter dem Timeout von 10 s (hooks.json:38-47 unverändert); Fehler der Abfrage nur in hook-errors.log, keine Ausgabe, Exit 0
  - Tests über eine injizierbare Prozessliste plus ein Integrationsfall mit echtem Kindprozess; PreToolUse-Pfad unverändert, Latenz-Gate grün; keine bestehende Testzeile geändert oder entfernt
  - Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["docs/adr/0001-subagent-identitaet-im-hook-input.md", "src/Monarch.Cli/HookRunner.cs", "src/Monarch.Cli/BackgroundShells.cs", "src/Monarch.Cli/CliEnv.cs", "src/Monarch.Cli/Dtos.cs", "tests/Monarch.Cli.Tests/HookSubagentStopShellsTests.cs"]
  exclude: ["src/Monarch.Policy/**", ".github/**", "bench/**", "plugin/**"]
estimate: { session_pct: 20, weekly_pct: 5, gold_usd: 0 }
boss: "Nachtrag zu ADR-0001 mit Messung, Warden-Designrunde approve; dotnet test -c Release grün mit den neuen Fällen (vorher rot), CI grün auf macOS und Linux, Warden-Schlussrunde approve am Head-SHA"
---
# subagent-stop meldet hinterlassene Hintergrund-Shells und beendet nichts (Issue 116 Punkt 1)
