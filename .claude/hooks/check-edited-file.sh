#!/bin/sh
# After every edit of a C# file: a file that is new on this branch gets dotnet format's whitespace fixes (indentation,
# final newline, the UTF-8 BOM .editorconfig asks for); old files are never reformatted. Then
# `scripts/lint-new.sh --file` builds the file's project and reports compile errors and the warnings on the lines this
# branch added. ApiCompat and the tests wait for the stop hook and CI. Exit 2 with stderr reaches the agent; a note on
# exit 0 goes out as JSON, since plain stdout reaches no one.
command -v jq >/dev/null 2>&1 || exit 0
file=$(jq -r '.tool_input.file_path // empty')
case "$file" in *.cs) ;; *) exit 0 ;; esac
[ -f "$file" ] || exit 0

# The checkout the file is in, whichever worktree that is.
dir=$(cd "$(dirname "$file")" 2>/dev/null && pwd -P) || exit 0
root=$(git -C "$dir" rev-parse --show-toplevel 2>/dev/null) || exit 0
root=$(cd "$root" && pwd -P)
[ -f "$root/scripts/lint-new.sh" ] || exit 0
cd "$root" || exit 0
rel=${dir#"$root"/}/$(basename "$file")
[ "$dir" = "$root" ] && rel=$(basename "$file")

note=
fork=$(git merge-base origin/main HEAD 2>/dev/null)
if [ -n "$fork" ] && ! git cat-file -e "$fork:$rel" 2>/dev/null; then
  before=$(git hash-object "$rel")
  dotnet format whitespace --folder --include "$rel" -v q >/dev/null 2>&1 </dev/null
  [ "$(git hash-object "$rel")" = "$before" ] || note="dotnet format fixed the whitespace of $rel (a new file). Read it again before the next edit."
fi

output=$(sh scripts/lint-new.sh --file "$rel" 2>&1 </dev/null)
case $? in
  0) ;;
  1)
    printf 'The edit is on disk, but %s has problems. Fix them before you continue:\n%s\n%s\n' "$rel" "$output" "$note" >&2
    exit 2
    ;;
  *) note="${note:+$note }scripts/lint-new.sh could not check $rel: $(printf '%s' "$output" | tail -n 3)" ;;
esac
[ -z "$note" ] || jq -n --arg note "$note" '{hookSpecificOutput: {hookEventName: "PostToolUse", additionalContext: $note}}'
exit 0
