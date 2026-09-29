#!/usr/bin/env bash
# Channel versions (homelab spec 2026-09-28-release-channels-design §4.1).
#   dev-version <vX.Y.Z> <run>  -> X.Y.(Z+1)-dev.<run>
#   newest-dev <tag>...         -> the X.Y.Z-dev.N with the highest N, or nothing
#   nightly-of <X.Y.Z-dev.N>    -> X.Y.Z-nightly.N
set -euo pipefail
case "${1:-}" in
  dev-version)
    [[ "${3:-}" =~ ^[0-9]+$ ]] || { echo "not a run number: ${3:-}" >&2; exit 1; }
    [[ "${2:-}" =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]] || { echo "not a release tag: ${2:-}" >&2; exit 1; }
    echo "${BASH_REMATCH[1]}.${BASH_REMATCH[2]}.$((BASH_REMATCH[3] + 1))-dev.$3" ;;
  newest-dev)
    shift
    printf '%s\n' "$@" | { grep -E '^[0-9]+\.[0-9]+\.[0-9]+-dev\.[0-9]+$' || true; } | sort -t. -k4,4n | tail -n1 ;;
  nightly-of)
    [[ "${2:-}" =~ ^([0-9]+\.[0-9]+\.[0-9]+)-dev\.([0-9]+)$ ]] || { echo "not a dev version: ${2:-}" >&2; exit 1; }
    echo "${BASH_REMATCH[1]}-nightly.${BASH_REMATCH[2]}" ;;
  *) echo "usage: $0 dev-version <vX.Y.Z> <run> | newest-dev <tag>... | nightly-of <X.Y.Z-dev.N>" >&2; exit 2 ;;
esac
