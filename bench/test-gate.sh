#!/usr/bin/env bash
# Deterministic self-test for the gate logic of bench/hook-latency.sh (Q-0121, Q-0124): no Monarch, no hyperfine, no timing.
# Synthetic hyperfine results with a known distribution (97.5 % at 5 ms, 2.5 % at 50 ms) are fed through BENCH_RESULTS_DIR.
# The p95 gate must pass, the p99 gate must fail, and the output must carry the p95 column.
set -euo pipefail
export LC_ALL=C
# The self-test decides the mode itself: nothing inherited from the caller (the CI job, a shell) may change a case.
unset GATE_MODE GATE_PCT ATTEMPTS BENCH_INJECT LIMIT_MS
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
command -v jq >/dev/null || { echo "jq not found" >&2; exit 2; }

# 200 samples: 195 x 5 ms, 5 x 50 ms (seconds, as in hyperfine's JSON).
jq -cn '{results: [{times: ([range(0; 195) | 0.005] + [range(0; 5) | 0.050])}]}' >"$tmp/sample.json"
for s in in-scope out-of-scope denied-bash non-awakened bash-targets abs-target nested-projects long-heredoc; do
  cp "$tmp/sample.json" "$tmp/$s.result.json"
done
export BENCH_RESULTS_DIR="$tmp"

fail=0
if GATE_PCT=95 ATTEMPTS=1 "$repo/bench/hook-latency.sh" >"$tmp/p95.log" 2>&1; then
  echo "ok: p95 gate passes with 2.5 % outliers"
else
  echo "FAIL: p95 gate should pass with 2.5 % outliers"; cat "$tmp/p95.log"; fail=1
fi
if GATE_PCT=99 ATTEMPTS=1 "$repo/bench/hook-latency.sh" >"$tmp/p99.log" 2>&1; then
  echo "FAIL: p99 gate should fail with 2.5 % outliers"; cat "$tmp/p99.log"; fail=1
else
  echo "ok: p99 gate fails with 2.5 % outliers"
fi
grep -q "p95_ms" "$tmp/p95.log" || { echo "FAIL: output lacks the p95 column"; fail=1; }

# --- Relative gate (ADR-0015, Q-0148): hook against the bare program start, median of five block ratios. ---
# Data: per scenario <name>.result.json (hook) and <name>.ref.result.json (reference), five blocks of 60 samples each.
scenarios="in-scope out-of-scope denied-bash non-awakened bash-targets abs-target nested-projects long-heredoc"
case "$(uname -s)" in
  Linux) lim_plain=1.55 lim_ledger=2.40 ;;
  Darwin) lim_plain=1.47 lim_ledger=1.70 ;;
  *) echo "unsupported OS for the relative self-test" >&2; exit 2 ;;
esac
blocks() { # ms, [blocks=5] -> hyperfine-like JSON with that many blocks of 60 equal samples
  jq -cn --argjson ms "$1" --argjson n "${2:-5}" '{results: [range(0; $n) | {times: [range(0; 60) | $ms / 1000]}]}'
}
dataset() { # dir, ref_ms, plain_ratio, ledger_ratio
  local dir="$1" ref="$2" s ratio
  mkdir -p "$dir"
  for s in $scenarios; do
    case "$s" in out-of-scope|denied-bash) ratio="$4" ;; *) ratio="$3" ;; esac
    blocks "$ref" >"$dir/$s.ref.result.json"
    blocks "$(awk -v r="$ref" -v q="$ratio" 'BEGIN{print r*q}')" >"$dir/$s.result.json"
  done
}
rel() { # dir, [env assignments...] -> exit code of the relative gate; log in $tmp/rel.log
  local dir="$1"; shift
  env BENCH_RESULTS_DIR="$dir" GATE_MODE=relative "$@" "$repo/bench/hook-latency.sh" >"$tmp/rel.log" 2>&1
}
expect() { # wanted exit code, description, command...
  local want="$1" what="$2" got=0; shift 2
  "$@" || got=$?
  if [[ $got -eq $want ]]; then echo "ok: $what"; else echo "FAIL: $what (exit $got, wanted $want)"; cat "$tmp/rel.log" 2>/dev/null || true; fail=1; fi
}
near() { awk -v l="$1" -v d="$2" 'BEGIN{print l+d}'; }

dataset "$tmp/base" 5 1.20 1.30
expect 0 "relative: today's ratios pass" rel "$tmp/base"
grep -q "ratio" "$tmp/rel.log" || { echo "FAIL: relative output lacks the ratio column"; fail=1; }

dataset "$tmp/regress" 5 3.00 3.00
expect 1 "relative: a regression on the hook samples only fails" rel "$tmp/regress"
dataset "$tmp/regress-one" 5 1.20 1.30
blocks 15 >"$tmp/regress-one/in-scope.result.json"
expect 1 "relative: a regression in a single scenario fails" rel "$tmp/regress-one"

dataset "$tmp/under" 5 "$(near "$lim_plain" -0.02)" "$(near "$lim_ledger" -0.02)"
expect 0 "relative: just under both limits passes" rel "$tmp/under"
dataset "$tmp/over-plain" 5 "$(near "$lim_plain" 0.02)" 1.30
expect 1 "relative: just over the limit without ledger fails" rel "$tmp/over-plain"
dataset "$tmp/over-ledger" 5 1.20 "$(near "$lim_ledger" 0.02)"
expect 1 "relative: just over the limit with ledger fails" rel "$tmp/over-ledger"

dataset "$tmp/slow" 8.5 1.20 1.30
expect 0 "relative: a runner 1.7 times slower (reference and hook) passes" rel "$tmp/slow"

# Outliers: two of five blocks disturbed on the hook side, plus single 50 ms samples in the others. The median of the
# block ratios must hold; the absolute p99 of the same samples is far over 20 ms.
dataset "$tmp/outliers" 5 1.20 1.30
for s in $scenarios; do
  jq -c '.results[0].times |= map(. * 4) | .results[1].times |= map(. * 4) | .results[2].times[0] = 0.050 | .results[3].times[0] = 0.050' \
    "$tmp/outliers/$s.result.json" >"$tmp/o.json" && mv "$tmp/o.json" "$tmp/outliers/$s.result.json"
  jq -c '.results[4].times[0] = 0.050' "$tmp/outliers/$s.ref.result.json" >"$tmp/o.json" && mv "$tmp/o.json" "$tmp/outliers/$s.ref.result.json"
done
expect 0 "relative: outliers and two disturbed blocks of five pass" rel "$tmp/outliers"
grep -q "p99 > 20 ms" "$tmp/rel.log" || { echo "FAIL: relative output does not report the absolute p99 over 20 ms"; cat "$tmp/rel.log"; fail=1; }
abs() { env BENCH_RESULTS_DIR="$1" ATTEMPTS=1 "$repo/bench/hook-latency.sh" >"$tmp/rel.log" 2>&1; }
expect 1 "absolute (default mode): the same samples still fail on p99 > 20 ms" abs "$tmp/outliers"

# The gate cannot be switched off quietly: invalid input is an error (exit 2), never a pass.
expect 2 "ATTEMPTS=0 is rejected (relative)" rel "$tmp/regress" ATTEMPTS=0
expect 2 "ATTEMPTS=3 is rejected (relative)" rel "$tmp/regress" ATTEMPTS=3
abs_attempts0() { env BENCH_RESULTS_DIR="$tmp/outliers" ATTEMPTS=0 "$repo/bench/hook-latency.sh" >"$tmp/rel.log" 2>&1; }
expect 2 "ATTEMPTS=0 is rejected (absolute)" abs_attempts0
expect 2 "unknown GATE_MODE is rejected" rel "$tmp/base" GATE_MODE=off
expect 2 "unknown BENCH_INJECT is rejected" rel "$tmp/base" BENCH_INJECT="sleep 0"
cp -R "$tmp/base" "$tmp/noref" && rm "$tmp/noref/abs-target.ref.result.json"
expect 2 "missing reference samples are an error" rel "$tmp/noref"
cp -R "$tmp/base" "$tmp/zeroref" && blocks 0 >"$tmp/zeroref/abs-target.ref.result.json"
expect 2 "a reference median of 0 is an error" rel "$tmp/zeroref"
cp -R "$tmp/base" "$tmp/fewblocks" && blocks 6 4 >"$tmp/fewblocks/abs-target.result.json"
expect 2 "fewer than five blocks are an error" rel "$tmp/fewblocks"
mkdir -p "$tmp/fakebin" && printf '#!/bin/sh\necho Plan9\n' >"$tmp/fakebin/uname" && chmod +x "$tmp/fakebin/uname"
expect 2 "an unknown operating system is an error" rel "$tmp/base" PATH="$tmp/fakebin:$PATH"

# Limits are fixed in the script: nothing in the environment moves them.
expect 1 "limits cannot be raised through the environment" rel "$tmp/regress" \
  REL_LIMIT=99 REL_LIMIT_PLAIN=99 REL_LIMIT_LEDGER=99 LIMIT_RATIO=99 RATIO_LIMIT=99 LIMIT_MS=9999 GATE_PCT=95

# --- C1 under load on the owner's Mac (GATE_MODE=c1-load, addendum of 2026-11-12 to ADR-0015, Q-0150). ---
# Same statistic as the relative gate, one fixed limit of 1.64 for every scenario (owner decision), macOS only, never
# in CI. The cases run with a uname that reports Darwin and without CI variables, so the Linux runner checks them too.
# Every exit-2 case uses data that would pass without the guard and checks the reason: an unknown mode also exits 2.
c1_limit=1.64
mkdir -p "$tmp/darwinbin" && printf '#!/bin/sh\necho Darwin\n' >"$tmp/darwinbin/uname" && chmod +x "$tmp/darwinbin/uname"
mkdir -p "$tmp/linuxbin" && printf '#!/bin/sh\necho Linux\n' >"$tmp/linuxbin/uname" && chmod +x "$tmp/linuxbin/uname"
c1() { # dir, [env assignments...] -> exit code of the C1 load mode on a simulated Mac outside CI
  local dir="$1"; shift
  env -u CI -u GITHUB_ACTIONS -u MONARCH_BIN BENCH_RESULTS_DIR="$dir" GATE_MODE=c1-load PATH="$tmp/darwinbin:$PATH" "$@" \
    "$repo/bench/hook-latency.sh" >"$tmp/rel.log" 2>&1
}
mac_rel() { # dir, [env assignments...] -> the unchanged relative gate with the macOS limits
  local dir="$1"; shift
  rel "$dir" PATH="$tmp/darwinbin:$PATH" "$@"
}
reason() { # text, description: the last run must have named this reason
  if grep -qF -- "$1" "$tmp/rel.log"; then echo "ok: $2"; else echo "FAIL: $2 (reason '$1' not in the output)"; cat "$tmp/rel.log"; fail=1; fi
}

dataset "$tmp/c1-under" 5 "$(near "$c1_limit" -0.02)" "$(near "$c1_limit" -0.02)"
expect 0 "c1-load: just under 1.64 in both classes passes" c1 "$tmp/c1-under"
reason "1.64" "c1-load: the output shows the limit 1.64"
dataset "$tmp/c1-over-plain" 5 "$(near "$c1_limit" 0.02)" 1.30
expect 1 "c1-load: just over 1.64 without ledger fails" c1 "$tmp/c1-over-plain"
dataset "$tmp/c1-over-ledger" 5 1.20 "$(near "$c1_limit" 0.02)"
expect 1 "c1-load: just over 1.64 with ledger fails" c1 "$tmp/c1-over-ledger"

# Separate from the CI limits, which stay as they are (macOS: 1.47 without ledger, 1.70 with ledger).
dataset "$tmp/c1-plain160" 5 1.60 1.30
expect 0 "c1-load: 1.60 without ledger passes" c1 "$tmp/c1-plain160"
expect 1 "relative on macOS: 1.60 without ledger still fails (CI limit 1.47 unchanged)" mac_rel "$tmp/c1-plain160"
dataset "$tmp/c1-ledger168" 5 1.20 1.68
expect 1 "c1-load: 1.68 with ledger fails" c1 "$tmp/c1-ledger168"
expect 0 "relative on macOS: 1.68 with ledger still passes (CI limit 1.70 unchanged)" mac_rel "$tmp/c1-ledger168"

# The mode cannot loosen the CI gate: macOS only, not in CI (set counts, even empty), no MONARCH_BIN.
expect 2 "c1-load: another operating system is an error" c1 "$tmp/base" PATH="$tmp/linuxbin:$PATH"
reason "c1-load: only on macOS" "c1-load: the error names the operating system rule"
expect 2 "c1-load: CI=true is an error" c1 "$tmp/base" CI=true
reason "c1-load: not in CI" "c1-load: the error names the CI rule (CI=true)"
expect 2 "c1-load: GITHUB_ACTIONS=true is an error" c1 "$tmp/base" GITHUB_ACTIONS=true
reason "c1-load: not in CI" "c1-load: the error names the CI rule (GITHUB_ACTIONS=true)"
expect 2 "c1-load: an empty CI variable is an error" c1 "$tmp/base" CI=
reason "c1-load: not in CI" "c1-load: the error names the CI rule (CI empty)"
expect 2 "c1-load: MONARCH_BIN is an error" c1 "$tmp/base" MONARCH_BIN=/usr/bin/true
reason "c1-load: MONARCH_BIN must not be set" "c1-load: the error names the MONARCH_BIN rule"
expect 0 "relative: CI variables do not stop the CI gate" rel "$tmp/base" CI=true GITHUB_ACTIONS=true
expect 2 "c1-load: ATTEMPTS=0 is rejected" c1 "$tmp/base" ATTEMPTS=0
reason "ATTEMPTS must be 1 or 2" "c1-load: the error names ATTEMPTS"
expect 2 "c1-load: missing reference samples are an error" c1 "$tmp/noref"
reason "missing " "c1-load: the error names the missing file"
expect 1 "c1-load: the limit cannot be raised through the environment" c1 "$tmp/c1-over-plain" \
  lim_c1=99 LIM_C1=99 C1_LIMIT=99 lim_plain=99 lim_ledger=99 REL_LIMIT=99 LIMIT_RATIO=99 LIMIT_MS=9999 GATE_PCT=95

# ci.yml must keep the relative gate: no c1-load outside comments, every GATE_MODE there is `relative`, and both
# latency steps carry one. The check proves itself on altered copies.
ci_keeps_relative() { # workflow file
  local code modes
  code="$(grep -v '^[[:space:]]*#' "$1")"
  if grep -q 'c1-load' <<<"$code"; then return 1; fi
  modes="$(grep 'GATE_MODE' <<<"$code" || true)"
  [[ "$(grep -c . <<<"$modes")" -eq 2 ]] || return 1
  ! grep -qv '^[[:space:]]*GATE_MODE: relative[[:space:]]*$' <<<"$modes"
}
ci="$repo/.github/workflows/ci.yml"
: >"$tmp/rel.log"
expect 0 "ci.yml: both latency steps gate with GATE_MODE relative, c1-load is not used" ci_keeps_relative "$ci"
awk '!done && /GATE_MODE: relative/ {sub(/relative/, "c1-load"); done=1} {print}' "$ci" >"$tmp/ci-c1.yml"
expect 1 "ci.yml check: a step switched to c1-load is caught" ci_keeps_relative "$tmp/ci-c1.yml"
awk '!done && /WRAPPER=1 bench/ {sub(/WRAPPER=1/, "GATE_MODE=c1-load WRAPPER=1"); done=1} {print}' "$ci" >"$tmp/ci-inline.yml"
expect 1 "ci.yml check: c1-load inside a run line is caught" ci_keeps_relative "$tmp/ci-inline.yml"
awk '!done && /GATE_MODE: relative/ {done=1; next} {print}' "$ci" >"$tmp/ci-dropped.yml"
expect 1 "ci.yml check: a latency step without GATE_MODE relative is caught" ci_keeps_relative "$tmp/ci-dropped.yml"
exit $fail
