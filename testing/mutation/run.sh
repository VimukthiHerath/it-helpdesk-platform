#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
TEST_DIR="$REPO_ROOT/services/ticket/Ticket.Api.Tests"
OUTPUT_DIR="$SCRIPT_DIR/reports"

mkdir -p "$OUTPUT_DIR"

echo "Running Stryker in: $TEST_DIR"
cd "$TEST_DIR"

# Run Stryker (capturing exit status safely if break thresholds are set)
dotnet-stryker || true

echo "Copying mutation report to $OUTPUT_DIR..."
LATEST_REPORT=$(find StrykerOutput -type f -name "mutation-report.html" | sort -r | head -n 1)

if [ -n "$LATEST_REPORT" ] && [ -f "$LATEST_REPORT" ]; then
  cp "$LATEST_REPORT" "$OUTPUT_DIR/index.html"
  echo "Report copied successfully to $OUTPUT_DIR/index.html"
else
  echo "No Stryker report found in $TEST_DIR/StrykerOutput"
fi