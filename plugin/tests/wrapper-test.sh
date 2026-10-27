#!/bin/sh
# Tests plugin/scripts/monarch with a fake HOME, fake binary and fake SHA256SUMS.
# Usage: plugin/tests/wrapper-test.sh   (POSIX sh; needs sha256sum or shasum, touch, ln)
here=$(cd "$(dirname "$0")" && pwd)
wrapper=$here/../scripts/monarch
version=$(sed -n 's/^MONARCH_VERSION=//p' "$wrapper" | head -n1)
work=$(mktemp -d) || exit 2
trap 'rm -rf "$work"' EXIT
fails=0
pass() { echo "ok   - $1"; }
bad() { echo "FAIL - $1"; fails=$((fails + 1)); }
check() { # name, condition-result
  if [ "$2" = 0 ]; then pass "$1"; else bad "$1"; fi
}
if command -v sha256sum >/dev/null 2>&1; then sha() { sha256sum "$1" | cut -d' ' -f1; }; else sha() { shasum -a 256 "$1" | cut -d' ' -f1; }; fi

mkhome() { # -> sets H, D (fake home, version dir) with a good binary and SUMS
  H=$work/home.$$.$n; n=$((n + 1))
  D=$H/.monarch/bin/$version
  mkdir -p "$D"
  printf '#!/bin/sh\necho REAL "$@"\n' >"$D/monarch"
  chmod +x "$D/monarch"
  printf '%s  monarch\n' "$(sha "$D/monarch")" >"$D/SHA256SUMS"
}
n=0
proj=$work/proj; mkdir -p "$proj/.monarch" "$proj/sub/deep" "$work/plain"
: >"$proj/.monarch/system.yaml"

run() { # dir, home, args... -> sets OUT, CODE (stdin: {})
  d=$1; h=$2; shift 2
  OUT=$(cd "$d" && echo '{}' | env -u MONARCH_BIN HOME="$h" sh "$wrapper" "$@" 2>/dev/null); CODE=$?
}
isask() { case $OUT in *'"permissionDecision":"ask"'*) return 0 ;; *) return 1 ;; esac; }

# 1. happy path, first run hashes, second run uses the fast path
mkhome
run "$proj" "$H" hook pre-tool-use; [ "$CODE" = 0 ] && [ "$OUT" = "REAL hook pre-tool-use" ]; check "good binary runs" $?
run "$proj" "$H" hook pre-tool-use; [ "$OUT" = "REAL hook pre-tool-use" ]; check "good binary runs again (fast path)" $?

# 2. missing binary
mkhome; rm "$D/monarch"
run "$proj/sub/deep" "$H" hook pre-tool-use; isask; check "missing binary, pre-tool-use, awakened parent: ask" $?
run "$work/plain" "$H" hook pre-tool-use; [ "$CODE" = 0 ] && [ -z "$OUT" ]; check "missing binary, outside awakened project: fail-open" $?
run "$proj" "$H" hook post-tool-use; [ "$CODE" = 0 ] && [ -z "$OUT" ]; check "missing binary, post-tool-use: fail-open" $?
run "$proj" "$H" hook stop; [ "$CODE" = 0 ] && [ -z "$OUT" ]; check "missing binary, stop: fail-open" $?
run "$proj" "$H" status; [ "$CODE" = 1 ]; check "missing binary, other mode: exit 1" $?

# 3. missing SHA256SUMS
mkhome; rm "$D/SHA256SUMS"
run "$proj" "$H" hook pre-tool-use; isask; check "missing SUMS, awakened: ask" $?
run "$work/plain" "$H" hook pre-tool-use; [ "$CODE" = 0 ] && [ -z "$OUT" ]; check "missing SUMS, plain dir: fail-open" $?

# 4. no hash tool (restricted PATH without sha256sum/shasum)
mkhome
bindir=$work/nohash; mkdir -p "$bindir"
for t in cat touch rm ln sed head cut mkdir env dirname printf; do
  p=$(command -v "$t") && [ -x "$p" ] && ln -sf "$p" "$bindir/$t"
done
OUT=$(cd "$proj" && echo '{}' | PATH=$bindir HOME="$H" /bin/sh "$wrapper" hook pre-tool-use 2>/dev/null); CODE=$?
isask; check "no hash tool, awakened: ask" $?

# 5. SHA mismatch asks
mkhome; printf '#!/bin/sh\necho EVIL\n' >"$D/monarch"
run "$work/plain" "$H" hook pre-tool-use; isask; check "tampered binary before first run: ask" $?

# 6. tampering after verification must not run unverified
mkhome
run "$proj" "$H" hook pre-tool-use
printf '#!/bin/sh\necho EVIL\n' >"$D/monarch.new"; chmod +x "$D/monarch.new"
mv "$D/monarch.new" "$D/monarch"
run "$proj" "$H" hook pre-tool-use; isask; check "replaced binary (new inode) is re-verified: ask" $?

mkhome
run "$proj" "$H" hook pre-tool-use
sleep 1
printf '#!/bin/sh\necho EVIL\n' >"$D/monarch"   # in place, same inode, new mtime
sleep 1
touch "$D/.verified"                             # stamp touched afterwards (mtime granularity of test -nt is one second)
run "$proj" "$H" hook pre-tool-use; isask; check "in-place edit plus touched stamp is re-verified: ask" $?

mkhome
run "$proj" "$H" hook pre-tool-use
printf '#!/bin/sh\necho EVIL\n' >"$D/monarch"
touch -t 200001010000 "$D/monarch"               # old mtime on the binary
run "$proj" "$H" hook pre-tool-use; isask; check "in-place edit with old mtime is re-verified: ask" $?

# 7. a stamp alone (touched, no verification) does not vouch
mkhome; : >"$D/.verified"; printf '#!/bin/sh\necho EVIL\n' >"$D/monarch"
run "$proj" "$H" hook pre-tool-use; isask; check "forged stamp file does not skip verification: ask" $?

[ "$fails" = 0 ] && echo "all wrapper tests passed" || echo "$fails wrapper test(s) failed"
exit "$fails"
