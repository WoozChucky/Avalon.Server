#!/bin/bash
# Test-only modes. No evaluated arguments, external command selector or secret output.
set -eu
case "${1-}" in
  descendant|orphan)
    sleep 120 &
    echo "$!" > child.pid
    if [ "$1" = orphan ]; then exit 0; fi
    wait
    ;;
  output)
    head -c 1048576 /dev/zero &
    head -c 1048576 /dev/zero >&2 &
    wait
    ;;
  stdout) head -c 1048576 /dev/zero ;;
  stderr) head -c 1048576 /dev/zero >&2 ;;
  cpu) while :; do :; done ;;
  environment) if [ -n "${AVALON_TEST_SECRET+x}" ]; then echo true; else echo false; fi ;;
  nonzero) exit 23 ;;
  *) exit 64 ;;
esac
