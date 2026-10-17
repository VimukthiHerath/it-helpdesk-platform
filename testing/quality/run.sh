#!/usr/bin/env bash
# Code quality and metrics for the Ticket service. Owner: Vimukthi.
#
# 1. Make sure there is a coverage file (runs the unit tests if there is not one yet).
# 2. coverage_report.py   per-file coverage from that file, and an HTML page.
# 3. quality_report.py    the quality indicators and open issues from SonarCloud, with a pass or fail.
#
# Settings (environment variables):
#   MIN_COVERAGE   fail if SonarCloud's coverage for the Ticket API is below this percentage (default 40)
#   PR             a pull request number to report on instead of the main branch
#   BASELINE       a file written earlier by --save-baseline, to show the change since then
#                  (default: testing/quality/baseline.json)
# Reports are written to testing/quality/reports/.
set -u
cd "$(dirname "$0")/../.."

# "python3" is only a Microsoft Store stub on some Windows machines, so check it really runs.
if command -v python3 >/dev/null 2>&1 && python3 -c "import sys" >/dev/null 2>&1; then PY=python3; else PY=python; fi

COVERAGE=TestResults/unit/ticket-unit-coverage.opencover.xml
status=0

echo "--- 1. coverage data"
if [ ! -f "$COVERAGE" ]; then
  echo "No coverage file yet, running the unit tests first."
  bash testing/unit/run.sh || { echo "The unit tests failed, so there is no coverage to report."; exit 1; }
else
  echo "Using $COVERAGE"
fi

echo
echo "--- 2. coverage by file"
"$PY" testing/quality/coverage_report.py "$COVERAGE" || status=1

echo
echo "--- 3. SonarCloud quality indicators"
args=()
if [ -n "${PR:-}" ]; then args+=(--pull-request "$PR"); fi
baseline="${BASELINE:-testing/quality/baseline.json}"
if [ -f "$baseline" ]; then args+=(--baseline "$baseline"); fi
"$PY" testing/quality/quality_report.py "${args[@]}" || status=1

exit "$status"
