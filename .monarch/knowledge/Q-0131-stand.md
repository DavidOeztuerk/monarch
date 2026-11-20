# Q-0131 – Stand (monarch-b4)

- 28.10. 21:51: ADR-0013 vorgeschlagen (f9b3fe9).
- 28.10. ~22:00: Design-Phase Warden G-0103 → APPROVE_WITH_CHANGES; Pflicht T1–T4 vor Code. Kanarie M1 verworfen.
- 28.10. 22:01: ADR-0013 nach Review überarbeitet (a30c6b6): T1 effektive Roots / Prozess-cwd als Root, T2 tatsächliche local/-Pfade, T3 Session vom Aufrufer + lesende Tools ohne Ledger, T4 physischer .monarch-Vergleich, G1–G3, Prüffrage 3, Auflagen 1–7 für den Code.
- **Nächster Schritt (nach 5h-Reset 01:30 CET):** TDD-Knight für Q-0131 im Worktree monarch-G-0103 mit den Auflagen 1–7 aus ADR-0013, inkl. **Roots-Messung mit echten Daten** (Fälle a–f) als Nachtrag ins ADR. Probe-Idee: `claude -p --model haiku --mcp-config <fixture> --strict-mcp-config` aus mehreren cwds, Server loggt empfangene Roots in eine Datei unter `tests/**` (Fixture, im Scope). Danach submit Q-0131 → Q-0132 (Regeln am Ort + gate merge develop-grün) → Warden Code-Phase (neue Instanz, Design-Auflagen aus ADR-0013) → Merge nach develop nach Regel 3.
- Budget G-0103 ≤ 6 pp Woche (Start 28 %, Stand 30 %).
