#!/usr/bin/env bash
# Tests for channel-version.sh (homelab spec 2026-09-28-release-channels-design §4.1).
set -euo pipefail
cd "$(dirname "$0")"
fail=0
check() {
  local want="$1"; shift
  local got; got="$(./channel-version.sh "$@" 2>/dev/null || echo ERR)"
  if [[ "$got" != "$want" ]]; then echo "FAIL: $* -> '$got', want '$want'"; fail=1; fi
}
check "0.7.1-dev.412" dev-version v0.7.0 412
check "1.0.1-dev.3"   dev-version v1.0.0 3
check "ERR"           dev-version 0.7.0 412
check "ERR"           dev-version v0.7.0 abc
check "0.7.1-dev.412" newest-dev 0.7.0 0.7.1-dev.9 0.7.1-dev.412 0.7.1-dev.87 0.7.1-nightly.500 latest
check ""              newest-dev 0.7.0 latest
check "0.7.1-nightly.412" nightly-of 0.7.1-dev.412
check "ERR"           nightly-of 0.7.1
[[ $fail -eq 0 ]] && echo "channel-version: all passed"
exit $fail
