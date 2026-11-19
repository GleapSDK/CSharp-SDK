#!/bin/sh
# Judges what a branch adds to the C# code, on the lines it adds compared with where it left the base: the compiler's
# and the analyzers' warnings (the rules of Directory.Build.props and .editorconfig) and dotnet format's whitespace
# rules. Old code may stay as it is. A compile error fails wherever it is.
#
#   sh scripts/lint-new.sh                  every C# file the branch changes (pre-push, the stop hook, CI)
#   sh scripts/lint-new.sh --base <ref>     compare with <ref>; default origin/main
#   sh scripts/lint-new.sh --file <path>    one file and its project, for the edit hook
#   sh scripts/lint-new.sh --all            every finding, old code included; for reading, it never fails
#
# Exit codes: 0 clean, 1 findings or a compile error, 3 the check could not run.
set -u

base=origin/main
file=
all=
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base=${2:?--base needs a ref}; shift 2 ;;
    --file) file=${2:?--file needs a path}; shift 2 ;;
    --all) all=1; shift ;;
    *) echo "usage: lint-new.sh [--base <ref>] [--file <path>] [--all]" >&2; exit 3 ;;
  esac
done

root=$(git rev-parse --show-toplevel 2>/dev/null) || { echo "lint-new: not in a git checkout" >&2; exit 3; }
cd "$root" || exit 3
root=$(pwd -P)
tmp=$(mktemp -d) || exit 3
trap 'rm -rf "$tmp"' EXIT
# Projects the gate does not build: the MAUI sample is a full app build of several minutes. Its whitespace is judged.
skip_build='examples/Gleap.Sample.Maui/'

# The C# files in scope and, unless --all, the lines this branch adds to them ("path:line").
if [ -n "$all" ]; then
  git ls-files -- '*.cs' >"$tmp/files"
else
  fork=$(git merge-base "$base" HEAD 2>/dev/null) || { echo "lint-new: no merge base with $base (git fetch origin?)" >&2; exit 3; }
  if [ -n "$file" ]; then
    case "$file" in /*) file=$(cd "$(dirname "$file")" && pwd -P)/$(basename "$file") ;; esac
    file=${file#"$root"/}
    printf '%s\n' "$file" >"$tmp/files"
  else
    (git diff --name-only --diff-filter=AMR "$fork" -- '*.cs'; git ls-files -o --exclude-standard -- '*.cs') | sort -u >"$tmp/files"
  fi
  # Committed and uncommitted changes against the fork point; a moved file counts only with the lines it changed.
  # shellcheck disable=SC2046 # one path per line, none with spaces in this repository
  git -c core.quotepath=off diff -U0 --no-color --no-ext-diff --diff-filter=AMR "$fork" -- $(cat "$tmp/files") | awk '
    /^\+\+\+ / { f = substr($0, 7); next }
    /^@@/ { split($3, a, ","); s = substr(a[1], 2) + 0; n = (a[2] == "" ? 1 : a[2] + 0); for (i = 0; i < n; i++) print f ":" s + i }
  ' >"$tmp/added"
  while read -r f; do
    if [ -f "$f" ] && ! git ls-files --error-unmatch -- "$f" >/dev/null 2>&1; then awk -v f="$f" '{ print f ":" NR }' "$f" >>"$tmp/added"; fi
  done <"$tmp/files"
  if [ ! -s "$tmp/files" ]; then
    echo "lint-new: no C# file changed compared with $base."
    exit 0
  fi
fi

# The project of each file: the nearest .csproj above it. A file outside any project (unity/) gets whitespace only.
: >"$tmp/projects"
while read -r f; do
  [ -f "$f" ] || continue
  case "$f" in "$skip_build"*) continue ;; esac
  d=$(dirname "$f")
  while [ "$d" != . ] && [ "$d" != / ]; do
    p=$(ls "$d"/*.csproj 2>/dev/null | head -n 1)
    if [ -n "$p" ]; then echo "$p" >>"$tmp/projects"; break; fi
    d=$(dirname "$d")
  done
done <"$tmp/files"
sort -u -o "$tmp/projects" "$tmp/projects"

# 1. Compiler and analyzers. An up-to-date project prints no warnings, so every project is rebuilt.
# EnableWindowsTargeting lets WebView2 and the WPF sample build on macOS and Linux.
: >"$tmp/build.log"
: >"$tmp/failed"
while read -r p; do
  case "$p" in
    maui/*)
      if ! dotnet workload list 2>/dev/null | grep -q '^ *maui '; then
        echo "lint-new: $p needs the maui workload: dotnet workload install maui" >&2
        [ -n "$all" ] && continue
        exit 3
      fi
      ;;
  esac
  if ! dotnet build "$p" --no-incremental -nologo -tl:off -v:q -clp:NoSummary \
    -p:EnableWindowsTargeting=true -p:GenerateFullPaths=true >>"$tmp/build.log" 2>&1 </dev/null; then
    echo "$p" >>"$tmp/failed"
  fi
done <"$tmp/projects"

# "path(line,col): warning ID: message [project]" -> "relative/path:line<TAB>ID: message"
diagnostics() { # severity pattern
  sed -n -E "s#^$root/([^(]+\.cs)\(([0-9]+),[0-9]+\): ($1) ([A-Za-z]+[0-9]*): (.*) \[[^]]*\]\$#\1:\2	\4: \5#p"
}
diagnostics 'warning' <"$tmp/build.log" | sort -u >"$tmp/findings"

# 2. Whitespace, from .editorconfig. Folder mode needs no project, so unity/ is covered too.
# shellcheck disable=SC2046
dotnet format whitespace --folder --verify-no-changes -v q --include $(cat "$tmp/files") >"$tmp/format.log" 2>&1 </dev/null
format=$?
# Exit code 2 means "would change files"; anything else but 0 means it did not run.
if [ "$format" != 0 ] && [ "$format" != 2 ]; then
  cat "$tmp/format.log" >&2
  echo "lint-new: dotnet format could not run (exit $format)" >&2
  exit 3
fi
diagnostics 'error|warning' <"$tmp/format.log" | sort -u >>"$tmp/findings"

if [ -n "$all" ]; then
  sed 's/	/: /' "$tmp/findings"
  echo "lint-new --all: $(wc -l <"$tmp/findings" | tr -d ' ') findings in all C# files, old code included. For reading; nothing gates on this."
  exit 0
fi

# The edit hook judges its file only.
if [ -n "$file" ]; then
  awk -v f="$file:" 'index($0, f) == 1' "$tmp/findings" >"$tmp/mine"
  mv "$tmp/mine" "$tmp/findings"
fi
awk -F'\t' 'NR == FNR { added[$1] = 1; next } ($1 in added) { print $1 "\t" $2 }' "$tmp/added" "$tmp/findings" >"$tmp/new"
old=$(($(wc -l <"$tmp/findings") - $(wc -l <"$tmp/new")))

status=0
if [ -s "$tmp/failed" ]; then
  echo "Build failed: $(tr '\n' ' ' <"$tmp/failed")"
  sed -n -E "s#^($root/)?(.*): error ([A-Za-z]+[0-9]+): (.*) \[[^]]*\]\$#  \2: \3: \4#p" "$tmp/build.log" | sort -u >"$tmp/errors"
  if [ -s "$tmp/errors" ]; then head -n 30 "$tmp/errors"; else tail -n 30 "$tmp/build.log"; fi
  status=1
fi
if [ -s "$tmp/new" ]; then
  echo "On lines this branch adds:"
  sed 's/	/: /; s/^/  /' "$tmp/new"
  if [ -n "${GITHUB_ACTIONS:-}" ]; then
    # GitHub annotations: path:line<TAB>ID: message
    awk -F'\t' '{ split($1, p, ":"); id = $2; sub(/:.*/, "", id); msg = $2; sub(/^[^:]*: /, "", msg); print "::error file=" p[1] ",line=" p[2] ",title=" id "::" msg }' "$tmp/new"
  fi
  status=1
fi

# Suppressions on added lines do not fail, but reviewers should see them.
# shellcheck disable=SC2046
git diff -U0 --no-color "$fork" -- $(cat "$tmp/files") '*.csproj' '*.props' '*.targets' .editorconfig 2>/dev/null |
  grep -E '^\+[^+]' | grep -E 'pragma warning disable|SuppressMessage|NoWarn|severity *= *(none|silent|suggestion)' >"$tmp/suppressed"
if [ -s "$tmp/suppressed" ]; then
  echo "Not blocking, for the reviewer: this branch adds suppressions:"
  sed 's/^+/  /' "$tmp/suppressed"
  [ -z "${GITHUB_ACTIONS:-}" ] || echo "::warning title=New suppressions::$(sed 's/^+//' "$tmp/suppressed" | tr '\n' ' ')"
fi

if [ "$status" = 0 ]; then
  echo "lint-new: nothing new on the lines this branch adds (old lines, not judged: $old findings)."
else
  echo "Fix these on your lines (old lines, not judged: $old findings). Whitespace: dotnet format whitespace --folder --include <file>."
fi
exit "$status"
