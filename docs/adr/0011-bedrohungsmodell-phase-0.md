# ADR-0011 — Bedrohungsmodell Phase 0: Leitplanke im Prozess, Erkennung danach, Durchsetzung außerhalb später

**Datum:** 28.10.2026 · **Status:** vorgeschlagen (Warden-Review mit Q-0122) · **Quest:** Q-0122 (G-0100)

## Anlass

Zwei unabhängige Warden-Runden (PR #1, Kommentare 6455275866 und 6456150588)
haben jeweils neue Umgehungen der Bash-Analyse gefunden: Symlink im selben
Kommando, `if … then rm`, `\rm`, Unicode-Faltung (`ſ`), Köder-`.monarch`,
`tar -C`. Eine Policy, die im Agent-Prozess Shell-Text analysiert, kann gegen
einen Agenten, der sie **gezielt** umgehen will, nicht vollständig sein. Die
Shell ist Turing-vollständig, Pfade können zur Laufzeit entstehen. Ohne
ausdrückliches Bedrohungsmodell wird jede Konstruktion zum Blocker, und die
Reviews konvergieren nicht.

## Akteure und Annahmen

- **Agent (Main, Shadows):** kooperativ, aber fehlbar. Er kann Fehler machen,
  Anweisungen aus Daten (Issues, Webseiten, Code) aufnehmen (Prompt-Injection)
  und unter Druck Abkürzungen nehmen. Er hat Bash, Write und Edit mit den
  Rechten des Owners (dieselbe UID).
- **Owner:** vertrauenswürdig; merged nach `main`, quittiert Anker-Resets.
- **Forge (GitHub):** externer Anker für committete und gepushte Ledger-Dateien
  und für Checks/Merges.

## Schutzziele und Garantien

| Ziel | Phase 0 garantiert | Wie |
|---|---|---|
| G1 Plausible Fehlgriffe verhindern | **ja** | PreToolUse deny/ask für jedes Kommando, das einen Schutzpfad nennt und nicht als lesend bekannt ist (fail-closed), plus Datei-Tools mit kanonisierten Pfaden |
| G2 Policy nicht still abschaltbar | **ja für plausible Wege** | Schutz von `.monarch/system.yaml`, `quests`, `gates`, `~/.monarch/**`, Hook-Konfiguration; Fehler → ask; Config-Fehler → Defaults |
| G3 Manipulation des Ledgers erkennbar | **ja für getrackte Dateien; ungetrackte bis zum Commit nur über den lokalen Anker** | Hash-Kette, Anker, Git-Abgleich, `LedgerAnchorReset` nur durch den Owner quittierbar (ADR-0010) |
| G4 Gezielte Umgehung verhindern | **nein** (dokumentierte Grenze) | erst mit Durchsetzung außerhalb des Agent-Prozesses (ADR-0009, OpenShell) |

## Entscheidung

1. **Maßstab für Reviews (Rank A, Phase 0):** Ein Blocker ist eine Umgehung von
   G1 oder G2 durch ein **plausibles** Kommando. Gemeint sind Formen, die ein
   Agent bei normaler Arbeit oder nach einer Injection in einem Issue
   realistisch erzeugt: Kontrollstrukturen (`if`/`for`/`{ }`), Escapes,
   Aliasse für Schreiber (`tar -C`, `unzip`, `curl -o`, `gsed -i`), relative
   Pfade in Nachbar-Worktrees und Symlinks, die im selben Kommando angelegt
   werden. Ebenso ist jede Lücke in G3 ein Blocker.
2. **Konstruktionen, die nur ein gezielt umgehender Agent baut** (Pfade aus
   Laufzeit-Berechnung, Interpreter-Skriptdateien, die Pfade dekodieren,
   Netzwerk-API-Aufrufe), sind **Follow-ups**, sofern
   - sie in `plugin/README.md` („Grenzen“) stehen und
   - ihre Folgen für das Ledger über G3 erkennbar sind.
3. **Strukturelle Regeln statt Einzelfälle** (Q-0122):
   - Normalisierung vor der Analyse: Shell-Schlüsselwörter, `\`-Escapes,
     `builtin`/`command`, Brace-Expansion, `$'…'`, Unicode NFC plus Faltung
     (Upper→Lower).
   - Fail-closed: Jedes Segment, das einen Schutzpfad nennt und dessen
     Kommandowort nicht auf der Lese-Liste steht, ist deny. Fehlalarme sind
     hinnehmbar (Ausweg: konkreter Pfad oder Owner).
   - Symlinks und `cd`: Kommandos, die einen Link anlegen (`ln -s`) oder mit
     `cd` über `..` navigieren und zugleich mutieren, → ask.
   - Projekte: Jedes erweckte Vorfahr-Projekt wird **vollständig**
     ausgewertet; ein `.monarch` darf innerhalb eines erweckten Projekts nur
     das Binary anlegen.
   - Owner-Aktionen (`ledger ack-reset`, `permit`, `promote`) sind für den
     Agenten deny, unabhängig von Pfad oder Alias des Binarys.

## Folgen

- Wardens prüfen künftig gegen G1–G3 und Maßstab 1/2; G4 ist bis ADR-0009
  ausdrücklich außerhalb.
- Das Plugin-README nennt die Grenzen in einem eigenen Abschnitt.
- Der Owner trägt beim Merge nach `main` die Restrisiken von G4 bewusst.
