#!/usr/bin/env bash
# Measures end-to-end latency of `monarch hook pre-tool-use` (AOT binary) with hyperfine.
# Usage: bench/hook-latency.sh [path-to-monarch-binary]   (default: publishes Release for this host first)
# Env:   LIMIT_MS  p99 limit in ms (default 20); exit 1 if any scenario exceeds it.
#        WRAPPER=1 measure through plugin/scripts/monarch (temp HOME with .monarch/bin/<version>/monarch + SHA256SUMS)
#                  instead of calling the binary directly.
#        RUNS     hyperfine runs per scenario (default 300).
#        ATTEMPTS measurements per scenario before it fails (default 2; see run()).
set -euo pipefail
export LC_ALL=C

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
limit_ms="${LIMIT_MS:-20}"
bin="${1:-${MONARCH_BIN:-}}"
if [[ -z "$bin" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) rid=osx-arm64 ;;
    Linux-x86_64) rid=linux-x64 ;;
    *) echo "unsupported host $(uname -s)-$(uname -m); pass a binary path" >&2; exit 2 ;;
  esac
  dotnet publish "$repo/src/Monarch.Cli" -c Release -r "$rid" -v quiet >/dev/null
  bin="$repo/src/Monarch.Cli/bin/Release/net10.0/$rid/publish/monarch"
fi
[[ -x "$bin" ]] || { echo "binary $bin not executable" >&2; exit 2; }
runs="${RUNS:-300}"
command -v hyperfine >/dev/null || { echo "hyperfine not found" >&2; exit 2; }
command -v jq >/dev/null || { echo "jq not found" >&2; exit 2; }

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
export MONARCH_HOME="$work/home"
mkdir -p "$MONARCH_HOME"

target="$bin"
if [[ "${WRAPPER:-0}" == "1" ]]; then
  version="$(sed -n 's/^MONARCH_VERSION=//p' "$repo/plugin/scripts/monarch" | head -n1)"
  [[ -n "$version" ]] || { echo "MONARCH_VERSION not found in wrapper" >&2; exit 2; }
  fakehome="$work/fakehome"
  mkdir -p "$fakehome/.monarch/bin/$version"
  cp "$bin" "$fakehome/.monarch/bin/$version/monarch"
  if command -v sha256sum >/dev/null; then sum="$(sha256sum "$bin" | cut -d' ' -f1)"; else sum="$(shasum -a 256 "$bin" | cut -d' ' -f1)"; fi
  printf '%s  monarch\n' "$sum" >"$fakehome/.monarch/bin/$version/SHA256SUMS"
  export HOME="$fakehome"
  unset MONARCH_BIN
  target="$repo/plugin/scripts/monarch"
  echo "mode: wrapper ($target, version $version)"
else
  echo "mode: direct ($bin)"
fi

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
input Bash '{"command":"cd /usr/local && git -C /usr/local status && ls /usr/bin/../lib"}' "$proj" bash-targets
input Edit '{"file_path":"'"$proj"'/src/deep/er/a.cs"}' "$proj" abs-target

status=0
printf '%-16s %10s %10s\n' scenario p50_ms p99_ms
measure() { # name, dir -> prints "p50 p99"
  local name="$1" dir="$2"
  (cd "$dir" && hyperfine -N --warmup 30 --runs "$runs" --input "$work/$name.json" \
    --export-json "$work/$name.result.json" "$target hook pre-tool-use" >/dev/null 2>&1)
  jq -r '(.results[0] | (.times // [.measurements[].time_wall_clock.value])) | sort | [(.[length/2|floor]*1000), (.[((length*0.99)|ceil)-1]*1000)] | @tsv' "$work/$name.result.json"
}
# A scenario passes if p99 <= LIMIT_MS in one of up to ATTEMPTS (default 2) independent measurements: a single
# scheduler hiccup on a shared machine must not fail the gate, a real regression fails every attempt.
run() { # name, cwd
  local name="$1" dir="$2" attempt p50 p99 history=""
  for ((attempt = 1; attempt <= ${ATTEMPTS:-2}; attempt++)); do
    read -r p50 p99 < <(measure "$name" "$dir")
    history+=$(printf ' [%d: p50 %.2f p99 %.2f]' "$attempt" "$p50" "$p99")
    if ! awk -v p="$p99" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then break; fi
  done
  printf '%-16s %10.2f %10.2f   attempts:%s\n' "$name" "$p50" "$p99" "$history"
  if awk -v p="$p99" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then status=1; fi
}
run in-scope "$proj"
run out-of-scope "$proj"
run denied-bash "$proj"
run non-awakened "$work/plain"
run bash-targets "$proj"
run abs-target "$proj"
[[ $status -eq 0 ]] && echo "OK: all p99 <= ${limit_ms} ms" || echo "FAIL: p99 > ${limit_ms} ms" >&2
exit $status
