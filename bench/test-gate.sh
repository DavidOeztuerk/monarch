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
exit $fail
