#!/usr/bin/env bash
# Table test for pr-title-check.sh: every title below must get the expected verdict.
# Run with: bash tools/ci/pr-title-check.test.sh
set -uo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
check="$here/pr-title-check.sh"

# verdict<TAB>title. The first four and the unprefixed or "Area:" titles are the 25 most recently merged
# pull requests when the rule was added; the rest are edge cases of the rule.
cases=$(cat <<'TABLE'
pass	feat(api): /client distribution endpoints (game builds and launcher)
pass	fix(security): keep secrets out of Redis key names and exception messages (#535)
pass	fix(api): storage down answers 503 when listing builds, not 500
fail	fix(telemetry): Redis spans, per-packet cost, robustness (#540)
pass	chore(deps): update dependency xunit.runner.visualstudio to v11
pass	chore(deps): update actions/checkout action to v7
pass	feat: world select budget
pass	feat(api,world): key presence by world (#556)
pass	feat(api)!: drop the v1 routes (#123, #456)
pass	refactor(world-server): split the tick loop
pass	docs: `ICommand` now declares its access level
pass	revert: 2 flaky tests
pass	ci: enforce conventional pr titles
pass	test(auth): budgets survive a restart (#12)
fail	TCP server: an awaited accept loop that survives errors and stops cleanly (#578, #584)
fail	API: remove the unread ApplicationConfig.Database binding (#582)
fail	Docs: rewrite the configuration reference's class table from the code (#551)
fail	Auth: budget world selects per connection, closing past the cap (#574)
fail	TCP keepalive on every accepted auth and world socket (#571)
fail	CI: keep per-test results and upload them when tests fail (#572)
fail	Auth: sweep Accounts.Online for sessions that are no longer live connections (#555)
fail	Answer an unknown or inaccessible world select with WorldUnavailable (#554)
fail	Rate limit the REST API per account or per source (#561)
fail	Key presence by world, and look up a character's presence under its world (#556)
fail	Confine chunk assets to the asset root with a separator-aware check (#559)
fail	Exporter: read the World connection string only from user-secrets or the environment (#557)
fail	API: register OpenTelemetry logging after Serilog so its logs export over OTLP (#562)
fail	API chart: require the auth connection string when the chart manages the Secret (#564)
fail	API: any number of worlds, each with its own world and characters databases (#523)
fail	Sensitive data logging only in Development; EF logs at Warning for every provider (#558)
fail	401 for RefreshAlreadyRotatedException in the middleware; validate hosting, cache and database settings before startup (#543)
fail	A heal never lowers health that sits above the maximum (#548)
fail	Heal threat counts only the health restored; threat columns refuse NaN and Infinity (#531, #529)
fail	Drop a leaving caster's scripts, contain a throwing script, interrupt a dead caster's cast out loud (#541, #530)
fail	API: split the exception mapping and AddAuth below MA0051, correct the ValidateOnStart claim (#534)
fail	feat(api): Distribution endpoints
fail	feat(api): distribution endpoints.
fail	feat(api): distribution endpoints. (#12)
fail	feat(api): distribution endpoints\x20
fail	feat(api): distribution endpoints (#12)\x20
fail	feat(API): distribution endpoints
fail	feat(api world): distribution endpoints
fail	feat(api, world): distribution endpoints
fail	feat(): distribution endpoints
fail	feat(api) distribution endpoints
fail	feat(api):distribution endpoints
fail	feat(api):  distribution endpoints
fail	feature(api): distribution endpoints
fail	Feat(api): distribution endpoints
fail	feat(api)!!: distribution endpoints
fail	feat(api): distribution endpoints (#)
fail	feat(api): distribution endpoints (#12,#13)
fail	feat(api): distribution endpoints (#12) (#)
fail	feat:
fail	feat: 
fail
fail	feat(api): $(touch pwned) `id`.
TABLE
)

failures=0
total=0
while IFS=$'\t' read -r expected title; do
  printf -v title '%b' "$title" # \x20 in the table is a trailing space, which editors strip
  total=$((total + 1))
  if PR_TITLE="$title" bash "$check" 2>/dev/null; then actual=pass; else actual=fail; fi
  if [[ $actual == "$expected" ]]; then
    printf 'ok    %s  %s\n' "$expected" "$title"
  else
    printf 'FAIL  expected %s, got %s  %s\n' "$expected" "$actual" "$title"
    failures=$((failures + 1))
  fi
done <<< "$cases"

echo "$((total - failures))/$total passed"
[[ $failures -eq 0 ]]
