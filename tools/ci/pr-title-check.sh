#!/usr/bin/env bash
# Checks a pull request title against the Conventional Commits rule the repository uses.
# The title is read from the PR_TITLE environment variable, never from the command line, so the
# workflow passes it without ever placing it inside a shell script (a title is untrusted input).
#
#   type(scope)!: summary (#123)
#
# type is one of the list below; the scope is optional, lowercase letters, digits and hyphens, with
# commas between several; "!" marks a breaking change; the summary starts with any character but a
# space and does not end with a period or a space; one trailing issue reference, " (#123)" or
# " (#123, #456)", may follow it.
#
# Exit status: 0 when the title follows the rule, 1 when it does not.
set -euo pipefail
export LC_ALL=C # [a-z] means ASCII lowercase only, whatever the runner's locale

# The rule lives in these three patterns and nowhere else.
readonly PR_TITLE_REFERENCE=' \(#[0-9]+(, #[0-9]+)*\)$'
readonly PR_TITLE_BAD_REFERENCE='\(#[^)]*\)$'
readonly PR_TITLE_PATTERN='^(feat|fix|docs|chore|refactor|test|ci|perf|build|style|revert)(\([a-z0-9-]+(,[a-z0-9-]+)*\))?!?: ([^ ].*)?[^. ]$'

title="${PR_TITLE-}"
# The issue reference is taken off first, so a period before it is refused like one at the end.
summary="$title"
if [[ $summary =~ $PR_TITLE_REFERENCE ]]; then
  summary="${summary%"${BASH_REMATCH[0]}"}"
fi

# Anything still shaped like a reference, such as (#) or (#12,#13), is a malformed one.
if [[ ! $summary =~ $PR_TITLE_BAD_REFERENCE && $summary =~ $PR_TITLE_PATTERN ]]; then
  exit 0
fi

{
  echo "::error title=PR title::The pull request title does not follow Conventional Commits."
  echo "Title:    ${title}"
  echo "Expected: type(scope)!: summary (#123)"
  echo "  type     feat, fix, docs, chore, refactor, test, ci, perf, build, style or revert"
  echo "  scope    optional; lowercase letters, digits and hyphens, commas between several: (api,world)"
  echo "  !        optional; marks a breaking change"
  echo "  summary  starts with any character but a space; no period or space at the end"
  echo "  (#123)   optional; the issues it closes, (#123) or (#123, #456)"
  echo "Examples:"
  echo "  fix(security): keep secrets out of Redis key names and exception messages (#535)"
  echo "  feat(api,world)!: key presence by world (#556)"
} >&2
exit 1
