#!/usr/bin/env bash
# Read-only smoke check of the Avalon API (#794): sends each line of requests.tsv, beside this
# script, as a GET and compares the status, to show after a deploy or a route move that each route
# group reaches a service that answers it.
#
#   BASE=<url> [AVALON_SMOKE_PAT=avp_...] [AVALON_SMOKE_GROUPS=worlds,distribution] tools/api-smoke/smoke.sh
#
#   BASE                 the API's base URL: https://avalon.example/api through the ingress, or
#                        http://127.0.0.1:18080 after kubectl port-forward svc/<release> 18080:8080.
#   AVALON_SMOKE_PAT     an Admin personal access token, for the lines marked pat; without it those
#                        lines are skipped. It reaches curl on its standard input, never its command line.
#   AVALON_SMOKE_GROUPS  the groups to check, comma-separated: identity, worlds, commerce, distribution
#                        (the API services); every group when unset. Not GROUPS: bash keeps that name
#                        for the user's group ids and never passes it on.
#
# GET only, no redirect followed. Prints each request's group, method, path and status, never a
# response body (channel manifests carry presigned URLs) or the token. Exits 0 when every status is
# one its line expects, 1 when one is not (000: no answer), 2 on bad input.
set -euo pipefail
set +x # a trace would print the token

requests="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/requests.tsv"
base=${BASE:-}
pat=${AVALON_SMOKE_PAT:-}
groups=${AVALON_SMOKE_GROUPS:-}

refuse() { echo "smoke: $*" >&2; exit 2; }
[[ $base =~ ^https?://[^[:space:]]+$ ]] || refuse "BASE is the API's base URL, e.g. https://avalon.example/api"
base=${base%/}
[ -z "$pat" ] || [[ $pat =~ ^avp_[A-Za-z0-9_-]+$ ]] || refuse "AVALON_SMOKE_PAT is not a personal access token (avp_...)"
command -v curl >/dev/null || refuse "curl is required"

# The lines, without comments or the carriage returns of a Windows checkout.
lines=$(tr -d '\r' < "$requests" | grep -v -e '^#' -e '^$')
known=$(cut -f1 <<<"$lines" | sort -u | paste -sd, -)
for group in ${groups//,/ }; do
  [[ ",$known," == *",$group,"* ]] || refuse "AVALON_SMOKE_GROUPS names $group; the groups are $known"
done

# Whether status $1 is one of the comma-separated statuses or classes (2xx) in $2.
expected() {
  local token
  for token in ${2//,/ }; do
    [[ $1 == "$token" || ( $token == [1-5]xx && ${1:0:1} == "${token:0:1}" ) ]] && return 0
  done
  return 1
}

passed=0 failed=0 skipped=0
while IFS=$'\t' read -r group method path expect auth; do
  [ -z "$groups" ] || [[ ",$groups," == *",$group,"* ]] || continue
  [ "$method" = GET ] || refuse "requests.tsv: $method $path: the smoke sends GET only"
  [[ $path == /* && $expect =~ ^([1-5][0-9][0-9]|[1-5]xx)(,([1-5][0-9][0-9]|[1-5]xx))*$ ]] \
    || refuse "requests.tsv: $method $path: a line is group, method, path, expected statuses and auth, tab-separated"
  case $auth in
    none) header="" ;;
    pat)
      if [ -z "$pat" ]; then
        printf 'SKIP  %-12s %s %s (needs AVALON_SMOKE_PAT)\n' "$group" "$method" "$path"
        skipped=$((skipped + 1))
        continue
      fi
      header="header = \"Authorization: Avalon $pat\"" ;;
    *) refuse "requests.tsv: $method $path: auth is none or pat, not '$auth'" ;;
  esac
  # --disable first, so no curlrc can add a trace or a redirect; the header comes in as config on stdin.
  status=$(printf '%s\n' "$header" | curl --disable --silent --output /dev/null --write-out '%{http_code}' \
             --max-time 30 --config - "$base$path" 2>/dev/null) || true
  status=${status:-000}
  if expected "$status" "$expect"; then
    printf 'PASS  %-12s %s %s %s\n' "$group" "$method" "$path" "$status"
    passed=$((passed + 1))
  else
    printf 'FAIL  %-12s %s %s %s (expected %s)\n' "$group" "$method" "$path" "$status" "$expect"
    failed=$((failed + 1))
  fi
done <<<"$lines"

echo "$passed passed, $failed failed, $skipped skipped"
[ "$failed" -eq 0 ]
