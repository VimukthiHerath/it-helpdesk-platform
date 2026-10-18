#!/usr/bin/env bash
# Mutation testing of the Ticket service with Stryker.NET. Owner: Rashaad.
#
# Stryker changes the Ticket code on purpose (a "mutant", for example turning > into >=) and runs the unit tests
# against each change. A test that fails has "killed" the mutant. One that still passes has let it "survive",
# which means the tests would not notice that bug.
#
# This script FAILS when:
#   - Stryker is not installed, or crashes
#   - no mutation report is produced
#   - the mutation score is below the "break" threshold in stryker-config.json
# The HTML report is copied to testing/mutation/reports/index.html either way, so a failed run can be inspected.
#
# Setup:  dotnet tool install --global dotnet-stryker
set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_DIR="$REPO_ROOT/services/ticket/Ticket.Api.Tests"
OUTPUT_DIR="$SCRIPT_DIR/reports"
CONFIG="$SCRIPT_DIR/stryker-config.json"

if ! command -v dotnet-stryker >/dev/null 2>&1; then
  echo "dotnet-stryker is not installed. Run: dotnet tool install --global dotnet-stryker" >&2
  exit 2
fi

mkdir -p "$OUTPUT_DIR"
cd "$TEST_DIR" || exit 2
rm -rf StrykerOutput

echo "Running Stryker against the Ticket service (config: $CONFIG)"
status=0
dotnet-stryker --config-file "$CONFIG" || status=$?

# Stryker exits 1 when the score is below the break threshold, and another non-zero code on a real error.
LATEST_REPORT=$(find StrykerOutput -type f -name "mutation-report.html" 2>/dev/null | sort -r | head -n 1)
report_found=0
if [ -n "$LATEST_REPORT" ] && [ -f "$LATEST_REPORT" ]; then
  cp "$LATEST_REPORT" "$OUTPUT_DIR/index.html"
  report_found=1
  echo "Mutation report: $OUTPUT_DIR/index.html"
else
  echo "ERROR: Stryker produced no report in $TEST_DIR/StrykerOutput" >&2
  [ "$status" -eq 0 ] && status=1
fi

if [ "$status" -ne 0 ]; then
  if [ "$status" -eq 1 ] && [ "$report_found" -eq 1 ]; then
    echo "Mutation testing FAILED: the mutation score is below the break threshold in stryker-config.json." >&2
  else
    echo "Mutation testing FAILED (exit code $status)." >&2
  fi
fi
exit "$status"
