# A0-Probe (27.10.2026, Claude Code 2.1.311)

    cd <probe-dir>
    claude -p --model haiku --setting-sources project --settings ./hooks-settings.json \
      --permission-mode default --allowedTools "Bash(touch:*)" "Bash(echo:*)" "Agent" "Task" \
      --max-turns 12 --output-format json "<Schritte 1–4, siehe run-result.json>"

`hooks.jsonl` = jede Hook-Eingabe (auf die relevanten Felder gekürzt). Ergebnis: ADR-0001…0005.
Nach dem Lauf existierte nur `allowed-marker.txt`, nicht `ask-marker.txt`.
