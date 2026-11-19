#!/bin/sh
# Before a turn ends, once the branch changes C# code or the build: scripts/lint-new.sh, scripts/api-new.sh and the
# tests. Red means the turn is not done. An unchanged tree costs the incremental builds; the tests take seconds.
command -v jq >/dev/null 2>&1 || exit 0
set -f # the pathspecs below are git's, not the shell's
active=$(jq -r '.stop_hook_active // false')
cd "${CLAUDE_PROJECT_DIR:-.}" || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0
[ -f scripts/lint-new.sh ] || exit 0

fork=$(git merge-base origin/main HEAD 2>/dev/null) || exit 0
checked='*.cs *.csproj *.props *.targets .editorconfig global.json dotnet-tools.json'
# shellcheck disable=SC2086 # pathspecs
changed=$( (git diff --name-only "$fork" -- $checked; git ls-files -o --exclude-standard -- $checked) 2>/dev/null)
[ -n "$changed" ] || exit 0

# The agent was told once and changed nothing since: another block would only burn a turn.
state="$(git rev-parse --git-dir)/claude-stop-gate"
# shellcheck disable=SC2086
key=$( (git rev-parse HEAD; git diff HEAD; git ls-files -o --exclude-standard -- $checked | xargs cat 2>/dev/null) | git hash-object --stdin)
if [ "$active" = true ] && [ "$(cat "$state" 2>/dev/null)" = "$key" ]; then
  exit 0
fi

failures=
step() { # name, command...
  name=$1
  shift
  output=$("$@" 2>&1 </dev/null)
  code=$?
  # Exit 3: the check could not run here (a workload, the network); say so without blocking.
  if [ "$code" = 3 ]; then
    printf 'Not checked (%s): %s\n' "$name" "$(printf '%s\n' "$output" | tail -n 2)" >&2
  elif [ "$code" != 0 ]; then
    failures="$failures
## $name
$(printf '%s\n' "$output" | tail -n 80)
"
  fi
}
step "sh scripts/lint-new.sh" sh scripts/lint-new.sh
step "sh scripts/api-new.sh" sh scripts/api-new.sh
step "dotnet test --project tests/Gleap.Core.Tests" dotnet test --project tests/Gleap.Core.Tests

if [ -z "$failures" ]; then
  rm -f "$state"
  exit 0
fi
printf '%s\n' "$key" >"$state"
printf 'Fix this before you finish, or report BLOCKED with the reason:\n%s\n' "$failures" >&2
exit 2
