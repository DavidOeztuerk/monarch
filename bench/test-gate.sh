#!/usr/bin/env bash
# Self-test for the gate logic of bench/hook-latency.sh (Q-0121), without Monarch: a fake "binary" is slow on every
# 40th call (~2.5 % outliers, like scheduler hiccups on a shared runner). The p95 gate must pass, the p99 gate must fail.
set -euo pipefail
export LC_ALL=C
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
fake="$tmp/fake-monarch"
cat >"$fake" <<'SH'
#!/bin/sh
cat >/dev/null
n=$(cat "$FAKE_COUNTER" 2>/dev/null || echo 0)
n=$((n + 1))
echo "$n" >"$FAKE_COUNTER"
[ $((n % 40)) -eq 0 ] && sleep 0.05
exit 0
SH
chmod +x "$fake"
export FAKE_COUNTER="$tmp/counter"

fail=0
if GATE_PCT=95 RUNS=200 ATTEMPTS=1 "$repo/bench/hook-latency.sh" "$fake" >"$tmp/p95.log" 2>&1; then
  echo "ok: p95 gate passes with ~2.5 % outliers"
else
  echo "FAIL: p95 gate should pass with ~2.5 % outliers"; cat "$tmp/p95.log"; fail=1
fi
if GATE_PCT=99 RUNS=200 ATTEMPTS=1 "$repo/bench/hook-latency.sh" "$fake" >"$tmp/p99.log" 2>&1; then
  echo "FAIL: p99 gate should fail with ~2.5 % outliers"; cat "$tmp/p99.log"; fail=1
else
  echo "ok: p99 gate fails with ~2.5 % outliers"
fi
grep -q "p95_ms" "$tmp/p95.log" || { echo "FAIL: output lacks the p95 column"; fail=1; }
exit $fail
