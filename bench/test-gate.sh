#!/usr/bin/env bash
# Deterministic self-test for the gate logic of bench/hook-latency.sh (Q-0121, Q-0124): no Monarch, no hyperfine, no timing.
# Synthetic hyperfine results with a known distribution (97.5 % at 5 ms, 2.5 % at 50 ms) are fed through BENCH_RESULTS_DIR.
# The p95 gate must pass, the p99 gate must fail, and the output must carry the p95 column.
set -euo pipefail
export LC_ALL=C
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
exit $fail
