---
id: Q-0165
gate: G-0118
kind: main
rank: A
state: proposed
dod:
  - Abhängigkeit: startet erst nach Q-0164 (Nachtrag freigegeben, rote Tests auf dem Gate-Branch). Umsetzung genau nach dem Nachtrag, TDD: die roten Fälle aus Q-0164 werden grün, keine weitere Verhaltensänderung
  - Main unverändert: ohne Subagent-Kennung gibt jede Ask-Quelle weiter ask mit unverändertem Text; bestehende Tests grün und unverändert, u. a. HookTests.cs:79-90, :122-132, :146-151, :172-179, ToolCallPolicyTests.cs:274-284, ProtectBranchesTests.cs:183-228
  - Keine Lockerung: kein Aufruf liefert allow, wo vorher ask oder deny stand; Never_prints_allow (HookTests.cs:227-246) unverändert grün; neue Property im Testfile: mit Subagent-Kennung nie allow und nie ask, wo der Nachtrag deny festlegt
  - Deny-Text im Subagenten behält den ursprünglichen Grund und nennt je Ask-Quelle den Ausweg laut Nachtrag; ein Test je Quelle prüft den Ausweg-Satz
  - PolicyDecided hält die umgewandelte Rückfrage fest (kind deny plus Kennzeichen-Feld laut Nachtrag), auch auf dem Fehlerpfad HookRunner.cs:330-336, soweit der Nachtrag ihn umwandelt; ledger verify bleibt ok (Test)
  - Bestehende Tests unverändert: git diff origin/develop --numstat -- tests zeigt bei bestehenden Dateien 0 gelöschte Zeilen; Release-Build ohne Warnung (TreatWarningsAsErrors)
  - Latenz nicht verschlechtert: Latenz-Gate (ADR-0015, ci.yml:51-106) in CI auf beiden Betriebssystemen grün; keine neue Datei- oder Prozessoperation im PreToolUse-Pfad; bench/** unverändert
  - Schlussrunde des Wardens mit Verdict am Head-SHA
scope:
  include: ["src/Monarch.Policy/ToolCallPolicy.cs", "src/Monarch.Policy/ToolCallTypes.cs", "src/Monarch.Policy/ProtectBranchesRule.cs", "src/Monarch.Policy/ProtectPathsRule.cs", "src/Monarch.Cli/HookRunner.cs", "docs/adr/0003-ask-im-headless-modus.md", "tests/Monarch.Cli.Tests/HookSubagentAskTests.cs", "tests/Monarch.Policy.Tests/SubagentAskTests.cs"]
  exclude: [".github/**", "bench/**", "plugin/**"]
estimate: { session_pct: 25, weekly_pct: 6, gold_usd: 0 }
boss: "dotnet test -c Release grün mit den Fällen aus Q-0164 (vorher rot), CI grün auf beiden Betriebssystemen inklusive Latenz-Gate, Warden-Schlussrunde approve am Head-SHA"
---
# Rückfrage im Subagenten wird Deny mit Ausweg: Umsetzung nach dem Nachtrag zu ADR-0003
