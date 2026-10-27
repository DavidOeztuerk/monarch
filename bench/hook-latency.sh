#!/usr/bin/env bash
# Measures end-to-end latency of `monarch hook pre-tool-use` (AOT binary) with hyperfine.
# Usage: bench/hook-latency.sh [path-to-monarch-binary]   (default: publishes osx-arm64 Release first)
# Exits 1 if any scenario's p99 exceeds LIMIT_MS (default 20).
set -euo pipefail
export LC_ALL=C

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
limit_ms="${LIMIT_MS:-20}"
bin="${1:-${MONARCH_BIN:-}}"
if [[ -z "$bin" ]]; then
  dotnet publish "$repo/src/Monarch.Cli" -c Release -r osx-arm64 -v quiet >/dev/null
  bin="$repo/src/Monarch.Cli/bin/Release/net10.0/osx-arm64/publish/monarch"
fi
command -v hyperfine >/dev/null || { echo "hyperfine not found" >&2; exit 2; }
command -v jq >/dev/null || { echo "jq not found" >&2; exit 2; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export MONARCH_HOME="$work/home"
mkdir -p "$MONARCH_HOME"

proj="$work/proj"
mkdir -p "$proj/.monarch/local" "$proj/.monarch/ledger" "$proj/src" "$work/plain"
cat >"$proj/.monarch/local/active.json" <<JSON
{"questId":"Q-0003","gateId":"G-0001","rank":"B","kind":"main","scope":{"include":["src/**"],"exclude":[]},"acceptedAt":"2026-10-27T10:00:00Z"}
JSON

input() { # tool, tool_input-json, cwd
  printf '{"session_id":"bench","cwd":"%s","hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"%s","tool_input":%s}' "$3" "$1" "$2" >"$work/$4.json"
}
input Edit '{"file_path":"src/a.cs"}' "$proj" in-scope
input Edit '{"file_path":"docs/readme.md"}' "$proj" out-of-scope
input Bash '{"command":"git push --force origin main"}' "$proj" denied-bash
input Edit '{"file_path":"src/a.cs"}' "$work/plain" non-awakened

status=0
printf '%-16s %10s %10s\n' scenario p50_ms p99_ms
run() { # name, cwd
  local name="$1" dir="$2"
  (cd "$dir" && hyperfine -N --warmup 10 --runs 300 --input "$work/$name.json" \
    --export-json "$work/$name.result.json" "$bin hook pre-tool-use" >/dev/null)
  read -r p50 p99 < <(jq -r '(.results[0] | (.times // [.measurements[].time_wall_clock.value])) | sort | [(.[length/2|floor]*1000), (.[((length*0.99)|ceil)-1]*1000)] | @tsv' "$work/$name.result.json")
  printf '%-16s %10.2f %10.2f\n' "$name" "$p50" "$p99"
  if awk -v p="$p99" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then status=1; fi
}
run in-scope "$proj"
run out-of-scope "$proj"
run denied-bash "$proj"
run non-awakened "$work/plain"
[[ $status -eq 0 ]] && echo "OK: all p99 <= ${limit_ms} ms" || echo "FAIL: p99 > ${limit_ms} ms" >&2
exit $status
