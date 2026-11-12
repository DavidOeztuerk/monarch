#!/usr/bin/env bash
# Measures end-to-end latency of `monarch hook pre-tool-use` (AOT binary) with hyperfine.
# Usage: bench/hook-latency.sh [path-to-monarch-binary]   (default: publishes Release for this host first)
# Env:   GATE_MODE absolute (default): gate on the percentile in ms, the MVP criterion on the target machine.
#                  relative (CI, ADR-0015): gate on ratio = p50(hook) / p50(`monarch --version`), median of five
#                  blocks of 60 runs, against limits fixed in this script; ms values are reported, not gated.
#                  c1-load (owner's Mac under load, MVP criterion C1 part 2, ADR-0015 addendum of 2026-11-12): the
#                  relative statistic against one fixed limit of 1.64 for every scenario. macOS only, never in CI,
#                  not with MONARCH_BIN. Call: GATE_MODE=c1-load bench/hook-latency.sh plugin/scripts/monarch
#        LIMIT_MS  latency limit in ms (default 20) for the gate percentile; exit 1 if any scenario exceeds it
#                  (absolute mode; in relative mode it only labels the report).
#        GATE_PCT  gate percentile in absolute mode: 99 (default) or 95. p50/p95/p99 are always printed.
#        BENCH_INJECT=git  proof only: one extra git process before every hook call (never the reference).
#        BENCH_TMPDIR  parent directory for the scratch project (default $TMPDIR or /tmp).
#        WRAPPER=1 measure through plugin/scripts/monarch (temp HOME with .monarch/bin/<version>/monarch + SHA256SUMS)
#                  instead of calling the binary directly.
#        RUNS     hyperfine runs per scenario in absolute mode (default 300).
#        ATTEMPTS measurements per scenario before it fails: 1 or 2 (default 2); anything else is an error.
#        BENCH_RESULTS_DIR  evaluation only: read <scenario>.result.json (hyperfine --export-json format) from this directory
#                  instead of measuring; needs neither binary, hyperfine nor timing (used by bench/test-gate.sh).
#                  Relative mode also reads <scenario>.ref.result.json; both files carry five blocks in .results.
# Exit:  0 pass, 1 gate failed, 2 invalid input or missing data (never reported as a pass).
set -euo pipefail
export LC_ALL=C

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
limit_ms="${LIMIT_MS:-20}"
gate_pct="${GATE_PCT:-99}"
case "$gate_pct" in 95|99) ;; *) echo "GATE_PCT must be 95 or 99" >&2; exit 2 ;; esac
# Invalid input is an error, never a pass (ADR-0015): ATTEMPTS=0 used to report OK without a single measurement.
gate_mode="${GATE_MODE:-absolute}"
case "$gate_mode" in absolute|relative|c1-load) ;; *) echo "GATE_MODE must be absolute, relative or c1-load" >&2; exit 2 ;; esac
attempts="${ATTEMPTS:-2}"
case "$attempts" in 1|2) ;; *) echo "ATTEMPTS must be 1 or 2" >&2; exit 2 ;; esac
inject="${BENCH_INJECT:-}"
case "$inject" in ""|git) ;; *) echo "BENCH_INJECT must be empty or git" >&2; exit 2 ;; esac
# Relative gate (ADR-0015): limits on ratio = p50(hook) / p50(monarch --version), per operating system and class.
# They are fixed here on purpose and cannot be set from the environment. Rule: largest measured value + 0.25;
# the measured values and run ids are in docs/adr/0015-relatives-latenz-gate.md. Change both together.
rel_blocks=5
rel_runs=60
lim_plain="" lim_ledger=""
if [[ "$gate_mode" == relative ]]; then
  case "$(uname -s)" in
    Linux) lim_plain=1.55 lim_ledger=2.40 ;;
    Darwin) lim_plain=1.47 lim_ledger=1.70 ;;
    *) echo "relative gate: no limits for $(uname -s)" >&2; exit 2 ;;
  esac
fi
# C1 under load (GATE_MODE=c1-load, ADR-0015 addendum of 2026-11-12): one limit for every scenario, the owner's
# decision of 2026-11-12 (highest p95 under evening load 1.39 + 0.25). It is separate from the CI limits above, fixed
# here and not settable from the environment; changing it needs an owner decision and measured numbers in the ADR.
# The mode must not loosen the CI gate (1.64 is above the macOS CI limit without ledger): macOS only, an error when
# CI or GITHUB_ACTIONS is set (even empty), and an error with MONARCH_BIN, which would make the wrapper start another
# binary than the installed one.
lim_c1=""
if [[ "$gate_mode" == c1-load ]]; then
  [[ "$(uname -s)" == Darwin ]] || { echo "c1-load: only on macOS (found $(uname -s))" >&2; exit 2; }
  if [[ -n "${CI+x}" || -n "${GITHUB_ACTIONS+x}" ]]; then echo "c1-load: not in CI (CI or GITHUB_ACTIONS is set)" >&2; exit 2; fi
  if [[ -n "${MONARCH_BIN+x}" ]]; then echo "c1-load: MONARCH_BIN must not be set (measure the installed binary through the wrapper)" >&2; exit 2; fi
  lim_c1=1.64
fi
rel_limit() { # scenario -> limit; a scenario without a class gets the stricter one
  if [[ "$gate_mode" == c1-load ]]; then echo "$lim_c1"; return; fi
  case "$1" in out-of-scope|denied-bash) echo "$lim_ledger" ;; *) echo "$lim_plain" ;; esac
}
results_dir="${BENCH_RESULTS_DIR:-}"
bin="${1:-${MONARCH_BIN:-}}"
if [[ -n "$results_dir" ]]; then
  [[ -d "$results_dir" ]] || { echo "BENCH_RESULTS_DIR $results_dir is not a directory" >&2; exit 2; }
elif [[ -z "$bin" ]]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) rid=osx-arm64 ;;
    Linux-x86_64) rid=linux-x64 ;;
    *) echo "unsupported host $(uname -s)-$(uname -m); pass a binary path" >&2; exit 2 ;;
  esac
  dotnet publish "$repo/src/Monarch.Cli" -c Release -r "$rid" -v quiet >/dev/null
  bin="$repo/src/Monarch.Cli/bin/Release/net10.0/$rid/publish/monarch"
fi
runs="${RUNS:-300}"
command -v jq >/dev/null || { echo "jq not found" >&2; exit 2; }
if [[ -z "$results_dir" ]]; then
  [[ -x "$bin" ]] || { echo "binary $bin not executable" >&2; exit 2; }
  bin="$(cd "$(dirname "$bin")" && pwd)/$(basename "$bin")" # absolute: every measurement changes the directory
  command -v hyperfine >/dev/null || { echo "hyperfine not found" >&2; exit 2; }
fi

work="$(mktemp -d "${BENCH_TMPDIR:-${TMPDIR:-/tmp}}/hl.XXXXXX")"
trap 'rm -rf "$work"' EXIT
export MONARCH_HOME="$work/home"
mkdir -p "$MONARCH_HOME" "$work/plain"

proj="" inner="" target=""
if [[ -z "$results_dir" ]]; then
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
# hook_target runs the scenarios, target the reference. They differ only under BENCH_INJECT=git (ADR-0015, proof that
# the relative gate sees a real regression): a script that starts one git process and then execs the program. git
# reads from /dev/null, so the hook still gets its input. The reference never runs through it.
hook_target="$target"
if [[ "$inject" == git ]]; then
  command -v git >/dev/null || { echo "BENCH_INJECT=git: git not found" >&2; exit 2; }
  printf '#!/bin/sh\ngit --version >/dev/null 2>&1 </dev/null\nexec "%s" "$@"\n' "$target" >"$work/inject.sh"
  chmod +x "$work/inject.sh"
  hook_target="$work/inject.sh"
  echo "INJECTION ACTIVE: one extra git process before every hook call (BENCH_INJECT=git)"
fi

proj="$work/proj"
mkdir -p "$proj/.monarch/local" "$proj/.monarch/ledger" "$proj/src" "$work/plain"
cat >"$proj/.monarch/local/active.json" <<JSON
{"questId":"Q-0003","gateId":"G-0001","rank":"B","kind":"main","scope":{"include":["src/**"],"exclude":[]},"acceptedAt":"2026-10-27T10:00:00Z"}
JSON

# nested-projects (ADR-0011 rule 9): the cwd project sits inside another awakened project; both are evaluated in full.
nest="$work/outer"
mkdir -p "$nest/.monarch/local" "$nest/.monarch/ledger" "$nest/src/inner/.monarch/local" "$nest/src/inner/.monarch/ledger"
cat >"$nest/.monarch/local/active.json" <<JSON
{"questId":"Q-0004","gateId":"G-0001","rank":"B","kind":"main","scope":{"include":["src/**"],"exclude":[]},"acceptedAt":"2026-10-27T10:00:00Z"}
JSON
sed 's#"src/\*\*"#"**"#' "$nest/.monarch/local/active.json" >"$nest/src/inner/.monarch/local/active.json"
inner="$nest/src/inner"

input() { # tool, tool_input-json, cwd
  printf '{"session_id":"bench","cwd":"%s","hook_event_name":"PreToolUse","permission_mode":"default","tool_name":"%s","tool_input":%s}' "$3" "$1" "$2" >"$work/$4.json"
}
input Edit '{"file_path":"src/a.cs"}' "$proj" in-scope
input Edit '{"file_path":"docs/readme.md"}' "$proj" out-of-scope
input Bash '{"command":"git push --force origin main"}' "$proj" denied-bash
input Edit '{"file_path":"src/a.cs"}' "$work/plain" non-awakened
input Bash '{"command":"cd /usr/local && git -C /usr/local status && ls /usr/bin/../lib"}' "$proj" bash-targets
input Edit '{"file_path":"'"$proj"'/src/deep/er/a.cs"}' "$proj" abs-target
input Edit '{"file_path":"a.cs"}' "$inner" nested-projects
# long-heredoc: Bash writing ~200 lines of notes via a quoted heredoc, no protected path (the body is data).
body="$(for i in $(seq 1 200); do printf 'Zeile %d: Notizen zum Entwurf, ohne Pfade, Kommandos wie rm oder git push nur als Prosa.\n' "$i"; done)"
heredoc_cmd="$(printf "cat <<'EOF' > notes.md\n%s\nEOF" "$body")"
input Bash "$(jq -cn --arg c "$heredoc_cmd" '{command:$c}')" "$proj" long-heredoc
fi

if [[ -z "$results_dir" && -n "${BENCH_SHOW:-}" ]]; then # debug: print the decision of each scenario
  for f in "$work"/*.json; do
    case "$f" in *.result.json) continue ;; esac
    n="$(basename "$f" .json)"; case "$n" in nested-projects) d="$inner" ;; non-awakened) d="$work/plain" ;; *) d="$proj" ;; esac
    echo "$n: $(cd "$d" && MONARCH_HOME="$MONARCH_HOME" "$target" hook pre-tool-use <"$f" | cut -c1-120)"
  done
fi
status=0
# percentiles <hyperfine-json>: the evaluation, independent of how the numbers were obtained. All blocks are pooled.
percentiles() {
  jq -r '[.results[] | (.times // [.measurements[].time_wall_clock.value])] | add | sort | [(.[length/2|floor]*1000), (.[((length*0.95)|ceil)-1]*1000), (.[((length*0.99)|ceil)-1]*1000)] | @tsv' "$1"
}
bench() { # cwd, input-file, export-json, warmup, runs, command
  (cd "$1" && hyperfine -N --warmup "$4" --runs "$5" --input "$2" --export-json "$3" "$6" >/dev/null 2>&1) ||
    { echo "hyperfine failed for: $6" >&2; exit 2; }
}

# --- Absolute gate (default): the MVP criterion on the target machine. ---
measure() { # name, dir -> prints "p50 p95 p99"
  local name="$1" dir="$2"
  if [[ -n "$results_dir" ]]; then
    [[ -f "$results_dir/$name.result.json" ]] || { echo "missing $results_dir/$name.result.json" >&2; exit 2; }
    percentiles "$results_dir/$name.result.json"
    return
  fi
  bench "$dir" "$work/$name.json" "$work/$name.result.json" 30 "$runs" "$hook_target hook pre-tool-use"
  percentiles "$work/$name.result.json"
}
# A scenario passes if the gate percentile (p99, or p95 with GATE_PCT=95) is <= LIMIT_MS in one of up to ATTEMPTS
# (1 or 2, default 2) independent measurements: a single scheduler hiccup on a shared machine must not fail the gate,
# a real regression fails every attempt.
run_absolute() { # name, cwd
  local name="$1" dir="$2" attempt p50 p95 p99 gate history=""
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    read -r p50 p95 p99 < <(measure "$name" "$dir")
    history+=$(printf ' [%d: p50 %.2f p95 %.2f p99 %.2f]' "$attempt" "$p50" "$p95" "$p99")
    if [[ $gate_pct == 95 ]]; then gate=$p95; else gate=$p99; fi
    if ! awk -v p="$gate" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then break; fi
  done
  printf '%-16s %10.2f %10.2f %10.2f   attempts:%s\n' "$name" "$p50" "$p95" "$p99" "$history"
  if awk -v p="$gate" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then status=1; fi
}

# --- Relative gate (GATE_MODE=relative, ADR-0015): the hook against the bare program start on the same machine. ---
# Per scenario five blocks; each block measures the reference (`monarch --version`, same mode, same cwd, same stdin)
# and then the scenario, so both see the same machine state. ratio = p50(scenario) / p50(reference) per block; the
# value of the scenario is the median of the five block ratios. Missing or empty data is an error, never a pass.
# rel_eval <hook-json> <ref-json> -> prints "p50 p95 p99 ref_p50 ratio" (p50/p95/p99 pooled over the hook blocks)
rel_eval() {
  jq -rn --slurpfile h "$1" --slurpfile r "$2" --argjson blocks "$rel_blocks" '
    def t: (.times // [.measurements[].time_wall_clock.value]);
    def med: sort | .[length/2|floor];
    ($h[0].results | map(t)) as $hb | ($r[0].results | map(t)) as $rb
    | if ($hb | length) != $blocks or ($rb | length) != $blocks then error("expected \($blocks) blocks") else . end
    | if any(($hb + $rb)[]; length == 0) then error("empty block") else . end
    | ($rb | map(med)) as $rm
    | if any($rm[]; . <= 0) then error("reference median is 0") else . end
    | ([range(0; $blocks) | ($hb[.] | med) / $rm[.]] | med) as $ratio
    | ($hb | add | sort) as $all
    | [($all[($all|length)/2|floor]*1000), ($all[((($all|length)*0.95)|ceil)-1]*1000), ($all[((($all|length)*0.99)|ceil)-1]*1000), (($rm | med)*1000), $ratio] | @tsv'
}
rel_blocks_merge() { jq -s '{results: map(.results[0])}' "$@"; }
rel_measure() { # name, dir -> sets rel_hook and rel_ref
  local name="$1" dir="$2" b
  if [[ -n "$results_dir" ]]; then
    rel_hook="$results_dir/$name.result.json" rel_ref="$results_dir/$name.ref.result.json"
    [[ -f "$rel_hook" && -f "$rel_ref" ]] || { echo "missing $rel_hook or $rel_ref" >&2; exit 2; }
    return
  fi
  rm -f "$work/$name".blk.*.json
  for ((b = 1; b <= rel_blocks; b++)); do
    bench "$dir" "$work/$name.json" "$work/$name.blk.ref.$b.json" 10 "$rel_runs" "$target --version"
    bench "$dir" "$work/$name.json" "$work/$name.blk.hook.$b.json" 10 "$rel_runs" "$hook_target hook pre-tool-use"
  done
  rel_hook="$work/$name.result.json" rel_ref="$work/$name.ref.result.json"
  rel_blocks_merge "$work/$name".blk.hook.*.json >"$rel_hook"
  rel_blocks_merge "$work/$name".blk.ref.*.json >"$rel_ref"
}
abs_over=0
run_relative() { # name, cwd
  local name="$1" dir="$2" attempt out p50 p95 p99 ref ratio limit history=""
  limit="$(rel_limit "$name")"
  for ((attempt = 1; attempt <= attempts; attempt++)); do
    rel_measure "$name" "$dir"
    out="$(rel_eval "$rel_hook" "$rel_ref")" || { echo "relative gate: no valid data for $name" >&2; exit 2; }
    read -r p50 p95 p99 ref ratio <<<"$out"
    history+=$(printf ' [%d: ratio %.2f]' "$attempt" "$ratio")
    if ! awk -v p="$ratio" -v l="$limit" 'BEGIN{exit !(p>l)}'; then break; fi
  done
  printf '%-16s %10.2f %10.2f %10.2f %10.2f %8.2f %8.2f   attempts:%s\n' "$name" "$p50" "$p95" "$p99" "$ref" "$ratio" "$limit" "$history"
  if awk -v p="$ratio" -v l="$limit" 'BEGIN{exit !(p>l)}'; then status=1; fi
  if awk -v p="$p99" -v l="$limit_ms" 'BEGIN{exit !(p>l)}'; then abs_over=$((abs_over + 1)); fi
}
# Report only: what the injection costs, and how far `--version` is from the smallest hook path (an unknown event in a
# directory without Monarch, which bypasses System.CommandLine). Neither changes the exit code.
rel_report() { # label-a, command-a, label-b, command-b
  local b
  : >"$work/empty.in"
  rm -f "$work"/rep.*.json
  for ((b = 1; b <= rel_blocks; b++)); do
    bench "$work/plain" "$work/empty.in" "$work/rep.a.$b.json" 10 "$rel_runs" "$2"
    bench "$work/plain" "$work/empty.in" "$work/rep.b.$b.json" 10 "$rel_runs" "$4"
  done
  rel_blocks_merge "$work"/rep.a.*.json >"$work/rep.a.json"
  rel_blocks_merge "$work"/rep.b.*.json >"$work/rep.b.json"
  jq -rn --slurpfile a "$work/rep.a.json" --slurpfile b "$work/rep.b.json" --arg la "$1" --arg lb "$3" '
    def med: sort | .[length/2|floor];
    def p50: [.results[] | (.times // [.measurements[].time_wall_clock.value]) | med] | med * 1000;
    ($a[0] | p50) as $x | ($b[0] | p50) as $y
    | "\($la) \($x*100|round/100) ms, \($lb) \($y*100|round/100) ms, difference \(($y-$x)*100|round/100) ms, ratio \($y/$x*100|round/100)"'
}

if [[ "$gate_mode" != absolute ]]; then
  run=run_relative
  if [[ "$gate_mode" == c1-load ]]; then
    echo "C1 under load (ADR-0015 addendum 2026-11-12): ratio limit $lim_c1 for every scenario, not a CI gate"
    if [[ -z "$results_dir" ]]; then echo "measured: $target --version -> $("$target" --version 2>&1 | head -n1)"; fi
  fi
  printf '%-16s %10s %10s %10s %10s %8s %8s   (gate: ratio <= limit; p50/p95/p99 in ms are reported only)\n' scenario p50_ms p95_ms p99_ms ref_p50 ratio limit
else
  run=run_absolute
  printf '%-16s %10s %10s %10s   (gate: p%s <= %s ms)\n' scenario p50_ms p95_ms p99_ms "$gate_pct" "$limit_ms"
fi
$run in-scope "$proj"
$run out-of-scope "$proj"
$run denied-bash "$proj"
$run non-awakened "$work/plain"
$run bash-targets "$proj"
$run abs-target "$proj"
$run nested-projects "$inner"
$run long-heredoc "$proj"
if [[ "$gate_mode" != absolute ]]; then
  if [[ -z "$results_dir" ]]; then
    echo "reference check: $(rel_report "idle hook" "$target hook bench-idle" "--version" "$target --version")"
    if [[ "$inject" == git ]]; then
      echo "injection cost: $(rel_report "--version" "$target --version" "--version with injection" "$hook_target --version")"
    fi
  fi
  if [[ $abs_over -eq 0 ]]; then
    echo "REPORT: absolute p99 <= ${limit_ms} ms in every scenario (not gating in ${gate_mode} mode)"
  else
    echo "REPORT: absolute p99 > ${limit_ms} ms in ${abs_over} scenario(s) (not gating in ${gate_mode} mode)"
  fi
  if [[ $status -eq 0 ]]; then echo "OK: all ratios within their limits"; else echo "FAIL: ratio over its limit" >&2; fi
else
  if [[ $status -eq 0 ]]; then echo "OK: all p${gate_pct} <= ${limit_ms} ms"; else echo "FAIL: p${gate_pct} > ${limit_ms} ms" >&2; fi
fi
exit $status
