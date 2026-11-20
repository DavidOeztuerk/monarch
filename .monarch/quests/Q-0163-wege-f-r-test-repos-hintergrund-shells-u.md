---
id: Q-0163
gate: G-0117
kind: main
rank: E
state: in_review
dod:
  - Vorher: dotnet test monarch.slnx -c Release auf dem unveränderten Stand (Basis 52615df), bestandene Tests je Testprojekt im PR notiert; nachher alle grün, Anzahl gleich vorher plus neue Tests; Release-Build ohne Warnung (TreatWarningsAsErrors, Directory.Build.props:7)
  - Kein src-Code, keine Hook-, Skript-, Bench- oder CI-Änderung: git diff origin/develop --stat -- src .github bench plugin/hooks plugin/scripts ist leer; keine bestehende Testdatei geändert und keine Testzeile entfernt: git diff origin/develop --numstat -- tests zeigt nur neue Dateien mit 0 gelöschten Zeilen
  - plugin/skills/regeln/SKILL.md: Regel 6 (Zeile 34) um den Satz ergänzt: Der im Deny-Text genannte Weg ist kein Umweg (z. B. Skriptdatei im Scratchpad statt Inline-Interpreter). Regel 10 (Zeile 43) um den Satz ergänzt: Rank S und „Immer einzeln fragen“ gehen vor jedem Default. Der Diff der Datei zeigt nur diese zwei Zeilen; Issue 107 Punkte 2 bis 4 sind nicht enthalten
  - RulesAtPlaceTests.Regeln_skill_exists_and_names_all_six_topics (RulesAtPlaceTests.cs:73-89) und CalibrationTests.Skill_texts_contain_no_recommendation_to_change_model_or_effort (CalibrationTests.cs:203-220, Wortmuster CalibrationTests.cs:12) bleiben grün; grep-Beleg im PR, dass der Regeltext nur in plugin/skills/regeln/SKILL.md steht (keine Kopie in src, README oder Briefing)
  - tests/Monarch.Policy.Tests/GitDirOutsideProjectTests.cs: die 21 Gegenproben aus der G-0115-Datei (Zeilen 56-98: 14 plus 5 InlineData-Fälle und 2 Facts) mit den genutzten Helfern übernommen, alle grün ohne src-Änderung; die 4 roten Fälle (Zeilen 44-53) und der nur dort genutzte Helfer Allowed fehlen; der Klassenkommentar beschreibt nur das heutige Verhalten, nennt Issue 105 als Ort der roten Fälle und verweist nicht auf ADR-0019
  - tests/Monarch.Policy.Tests/TestRepoWaysTests.cs (ToolCallPolicy.Evaluate, Root /work/proj, Rank A wie Adr0011Tests.cs:17-21) belegt das heutige Verhalten: Klon in einen absoluten Zielordner außerhalb (z. B. git clone /work/proj /scratch/t/c1) allow; Klon mit Ziel in einer Schutzzone deny protect_paths; git checkout -f -- src/a.cs allow, auch mit git -C oder nach cd in einen Ordner außerhalb; git checkout -f -- . deny, auch nach cd außerhalb; git checkout -f -- .monarch/ledger/x.jsonl deny; cd /scratch/t/c1 && rm .monarch/ledger/x.jsonl sowie cd /scratch/t/c1 && git rm --cached .monarch/ledger/x.jsonl deny; mutierender absoluter Pfad außerhalb ohne .monarch (z. B. rm -rf /scratch/t/c1/build) allow. Keine neuen Tests mit git -C und relativem .monarch-Operanden (gehören zu Issue 105)
  - tests/Monarch.Cli.Tests/HookTestRepoWaysTests.cs (Hook pre-tool-use mit TempProject wie HookProjectResolutionTests.cs:8-16) belegt für ein Test-Repo mit .monarch-Ordner außerhalb des cwd-Projekts: git clone des Projekts in einen neuen absoluten Ordner, git -C Test-Repo checkout -f -- src/a.cs und Schreiben einer ungeschützten Datei per absolutem Pfad ergeben keine Ausgabe (allow); rm -rf auf den Test-Repo-Ordner ergibt deny
  - plugin/agents/warden.md und plugin/agents/knight.md bekommen dieselben neuen Zeilen (gleiche hinzugefügte Zeilen im git diff beider Dateien, Frontmatter unverändert) zu Test-Repos: absolute Pfade außerhalb des Projekts; frischer Klon in einen neuen absoluten Zielordner außerhalb statt Zurücksetzen; git checkout -f -- mit konkretem Pfad, nicht Punkt; relative .monarch-Pfade gehen nicht, auch nicht nach cd in das Test-Repo. Dokumentiert wird nur, was ein Test aus diesem Gate oder ein bestehender Test belegt; der Zusatz auch mit .monarch/ledger im Pfad entfällt, weil der Hook ein Test-Repo mit .monarch-Ordner wie ein Projekt schützt (HookProjectResolutionTests.cs:45 deny)
  - Dieselben Zeilen zu Hintergrund-Shells (Issue 116) in beiden Dateien: vor der Übergabe eigene Hintergrund-Shells beenden und im Bericht nennen; nie auf ein Wort in einer selbst gefilterten Ausgabe warten; jede Warteschleife bekommt eine Frist
  - PR-Beschreibung ordnet jeden dokumentierten Weg seinem Test zu (Testname) und nennt Issue 105 (nur Gegenproben), Issue 107 (nur Punkt 1) und Issue 116
scope:
  include: ["plugin/skills/regeln/SKILL.md", "plugin/agents/warden.md", "plugin/agents/knight.md", "tests/Monarch.Policy.Tests/GitDirOutsideProjectTests.cs", "tests/Monarch.Policy.Tests/TestRepoWaysTests.cs", "tests/Monarch.Cli.Tests/HookTestRepoWaysTests.cs"]
  exclude: ["src/**", ".github/**", "bench/**", "plugin/hooks/**", "plugin/scripts/**"]
estimate: { session_pct: 14, weekly_pct: 3, gold_usd: 0 }
boss: "Warden-Schlussrunde approve; grep -n auf den fertigen Dateien belegt die zwei Sätze in SKILL.md und identische neue Zeilen in warden.md und knight.md; dotnet test -c Release grün"
board: { pr: 118 }
---
# Wege für Test-Repos, Hintergrund-Shells und zwei Regelsätze mit Policy-Belegen
