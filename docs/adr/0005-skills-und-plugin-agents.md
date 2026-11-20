# ADR-0005 — Shadows tragen Skills im Frontmatter; Policy kommt nur aus Plugin-Hooks

**Datum:** 27.10.2026 · **Status:** angenommen · **Quest:** Q-0100 (A0)
**Konzept:** Prüfpunkt 6 („Unterstützt das Subagent-Format `skills` im Frontmatter auch
für projektlokale Agents wie erwartet?“)

## Gemessen

Claude Code 2.1.311: Projekt-Agent `.claude/agents/probe.md` mit
`skills: [probe-skill]`; der Skill enthält das Wort `ZEBRA-4711`. Der Agent nannte
das Wort, **ohne** das Skill-Tool aufzurufen (kein `PreToolUse` mit `tool_name: Skill`
im Hook-Log). Der Inhalt war also vorgeladen.

Doku (`code.claude.com/docs/en/sub-agents`): „the full content of each listed skill
is injected“ (bis 32 Namen); `skills` steuert das Vorladen, nicht den Zugriff.
**Für Plugin-Agents werden `hooks`, `mcpServers`, `permissionMode` (und
`initialPrompt`) ignoriert.**

## Entscheidung (Design-Anpassung)

1. Die Default-Army (scout, strategist, knight, warden) liegt als Plugin-Agents vor
   und lädt ihre Monarch-Skills per `skills:` vor.
2. **Eine Shadow-Policy darf nicht im Agent-Frontmatter stehen.** Permission-Mode und
   Hooks je Shadow werden ignoriert. Stattdessen wertet der Plugin-Hook `PreToolUse`
   `agent_type` aus (ADR-0001) und wendet die Regeln der Shadow-Klasse an, etwa
   „warden ist read-only: Edit/Write → deny“.
3. Werkzeuge werden zusätzlich per `tools`/`disallowedTools` im Frontmatter begrenzt
   (nicht ignoriert). Die Grenze steht damit an zwei Stellen.
4. Eine extrahierte Shadow (Arise), die eigene Hooks braucht, wird in
   `.claude/agents/` des Projekts geschrieben, nicht ins Plugin.
