#!/bin/sh
# What CI runs: the tests with line coverage of Gleap.Core and Gleap.Extensions.Logging, and its floor. Raise the floor
# when coverage rises; lower it only with the reason in the PR.
set -eu
floor=76
cd "$(git rev-parse --show-toplevel)"
results="$PWD/TestResults"
rm -f "$results/coverage.cobertura.xml"
dotnet test --project tests/Gleap.Core.Tests -c Release "$@" -- \
  --results-directory "$results" --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
# The report's first <coverage> element holds the total line rate (0..1).
rate=$(sed -n 's/.*<coverage [^>]*line-rate="\([0-9.]*\)".*/\1/p' "$results/coverage.cobertura.xml" | head -n 1)
[ -n "$rate" ] || { echo "No line rate in $results/coverage.cobertura.xml"; exit 1; }
awk -v rate="$rate" -v floor="$floor" 'BEGIN {
  pct = rate * 100
  printf "Line coverage of Gleap.Core and Gleap.Extensions.Logging: %.1f%% (floor %d%%)\n", pct, floor
  if (pct < floor) { print "below the floor"; exit 1 }
}'
