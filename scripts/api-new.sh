#!/bin/sh
# The public API of the shipped assemblies on this branch, compared with a build of where the branch left the base
# (ApiCompat, pinned in dotnet-tools.json). A breaking change fails; an addition is listed as a notice. Only the shipped
# projects whose files the branch changes are built and compared.
#
#   sh scripts/api-new.sh [--base <ref>]      default origin/main; CI passes the PR's base
#
# A break a person approved: a commit on the branch carries the trailer "API-Break-Approved: <what and why>". The break
# is then listed and does not fail. Reviewers see the trailer in the PR.
#
# Exit codes: 0 compatible or approved, 1 a breaking change or the branch does not build, 3 the check could not run.
set -u

base=origin/main
while [ $# -gt 0 ]; do
  case "$1" in
    --base) base=${2:?--base needs a ref}; shift 2 ;;
    *) echo "usage: api-new.sh [--base <ref>]" >&2; exit 3 ;;
  esac
done

root=$(git rev-parse --show-toplevel 2>/dev/null) || { echo "api-new: not in a git checkout" >&2; exit 3; }
cd "$root" || exit 3
fork=$(git merge-base "$base" HEAD 2>/dev/null) || { echo "api-new: no merge base with $base (git fetch origin?)" >&2; exit 3; }
tmp=$(mktemp -d) || exit 3
trap 'git worktree remove --force "$tmp/base" >/dev/null 2>&1; rm -rf "$tmp"' EXIT

shipped='src/Gleap.Core/Gleap.Core.csproj src/Gleap.Extensions.Logging/Gleap.Extensions.Logging.csproj src/Gleap.WebView2/Gleap.WebView2.csproj maui/Gleap.Maui/Gleap.Maui.csproj'
changed=$( (git diff --name-only "$fork" -- src maui Directory.Build.props global.json; git ls-files -o --exclude-standard -- src maui) 2>/dev/null)
everything=
printf '%s\n' "$changed" | grep -qE '^(Directory\.Build\.props|global\.json)$' && everything=1
projects=
for p in $shipped; do
  dir=$(dirname "$p")/
  if [ -n "$everything" ] || printf '%s\n' "$changed" | grep -q "^$dir"; then projects="$projects $p"; fi
done
if [ -z "$projects" ]; then
  echo "api-new: no shipped project changed compared with $base."
  exit 0
fi
case "$projects" in
  *maui/*)
    if ! dotnet workload list 2>/dev/null | grep -q '^ *maui '; then
      echo "api-new: Gleap.Maui changed and needs the maui workload: dotnet workload install maui" >&2
      exit 3
    fi
    ;;
esac
dotnet tool restore >/dev/null 2>&1 </dev/null || { echo "api-new: dotnet tool restore failed" >&2; exit 3; }
git worktree add --quiet --detach "$tmp/base" "$fork" || exit 3

build() { # checkout project
  dotnet build "$1/$2" -c Release -nologo -tl:off -v:q -clp:NoSummary -p:EnableWindowsTargeting=true >"$tmp/build.log" 2>&1 </dev/null
}

: >"$tmp/breaks"
: >"$tmp/additions"
for p in $projects; do
  name=$(basename "$p" .csproj)
  build "$tmp/base" "$p" || { tail -n 20 "$tmp/build.log" >&2; echo "api-new: $p does not build at $base's fork point" >&2; exit 3; }
  if ! build "$root" "$p"; then
    tail -n 20 "$tmp/build.log"
    echo "api-new: $p does not build on this branch."
    exit 1
  fi
  bin=$(dirname "$p")/bin/Release
  for tfm in $( (ls "$tmp/base/$bin"; ls "$bin") 2>/dev/null | sort -u); do
    left="$tmp/base/$bin/$tfm/$name.dll"
    right="$bin/$tfm/$name.dll"
    if [ ! -f "$left" ]; then echo "$name ($tfm): new target framework" >>"$tmp/additions"; continue; fi
    if [ ! -f "$right" ]; then echo "$name ($tfm): target framework removed" >>"$tmp/breaks"; continue; fi
    mkdir -p "$tmp/cmp/base/$tfm" "$tmp/cmp/head/$tfm"
    cp "$left" "$tmp/cmp/base/$tfm/"
    cp "$right" "$tmp/cmp/head/$tfm/"
    # Messages name the sides base/<tfm>/X.dll and head/<tfm>/X.dll.
    compat() {
      dotnet tool run apicompat -l "$tmp/cmp/base/$tfm/$name.dll" -r "$tmp/cmp/head/$tfm/$name.dll" \
        --enable-rule-cannot-change-parameter-name "$@" 2>&1 </dev/null | sed "s#$tmp/cmp/##g"
    }
    compat >"$tmp/normal.txt"
    compat --strict-mode >"$tmp/strict.txt"
    if ! grep -qE '^CP[0-9]+|without finding any breaking changes' "$tmp/normal.txt"; then
      cat "$tmp/normal.txt" >&2
      echo "api-new: ApiCompat could not compare $name ($tfm)" >&2
      exit 3
    fi
    grep -E '^CP[0-9]+' "$tmp/normal.txt" | sed "s#^#$name ($tfm): #" >>"$tmp/breaks"
    # Strict mode also reports what exists only on the head: those are the additions.
    grep -E '^CP[0-9]+' "$tmp/strict.txt" | grep -vxF -f "$tmp/normal.txt" | sed "s#^#$name ($tfm): #" >>"$tmp/additions"
  done
done

if [ -s "$tmp/additions" ]; then
  echo "Added to the public API (a notice, not a failure; README.md and CHANGELOG.md name new calls):"
  sed 's/^/  /' "$tmp/additions"
  [ -z "${GITHUB_ACTIONS:-}" ] || sed 's/^/::notice title=Public API added::/' "$tmp/additions"
fi
if [ ! -s "$tmp/breaks" ]; then
  echo "api-new: no breaking change in$projects."
  exit 0
fi
echo "Breaking changes to the public API:"
sed 's/^/  /' "$tmp/breaks"
approved=$(git log --format=%B "$fork..HEAD" | grep -E '^API-Break-Approved:' | head -n 1)
if [ -n "$approved" ]; then
  echo "Approved on this branch ($approved): listed, not failing."
  [ -z "${GITHUB_ACTIONS:-}" ] || sed 's/^/::warning title=Approved API break::/' "$tmp/breaks"
  exit 0
fi
[ -z "${GITHUB_ACTIONS:-}" ] || sed 's/^/::error title=Public API break::/' "$tmp/breaks"
echo "Code written against the old API breaks: customers' apps, the platform packages, the Unity runtime. Keep the old"
echo "member (mark it [Obsolete] with what to use instead) and add the new one; a new member on a public interface breaks"
echo "every outside implementation. If a person approved the break, a commit on the branch carries"
echo "'API-Break-Approved: <what and why>'."
exit 1
