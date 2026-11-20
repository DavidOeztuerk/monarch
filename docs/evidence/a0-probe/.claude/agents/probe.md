---
name: probe
description: Probe agent for Monarch A0. Use only when explicitly asked to run the probe agent.
tools: Bash, Read
model: haiku
skills:
  - probe-skill
---
You are a probe. Answer the caller's question in one line. Then run exactly one Bash command: `echo subagent-bash-ok`.
