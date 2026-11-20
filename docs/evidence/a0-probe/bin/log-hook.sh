#!/bin/sh
# Appends the raw hook input (one JSON object per line) to hooks.jsonl.
input=$(cat)
printf '%s\n' "$input" >> "$(dirname "$0")/../hooks.jsonl"
case "$input" in
  *MONARCH_ASK_PROBE*) printf '%s' '{"hookSpecificOutput":{"hookEventName":"PreToolUse","permissionDecision":"ask","permissionDecisionReason":"monarch probe: rank S"}}' ;;
esac
exit 0
