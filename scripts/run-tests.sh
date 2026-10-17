#!/usr/bin/env bash
# =============================================================================
# One command for every test layer (SE3112 take-home).
#
# Usage:   ./scripts/run-tests.sh [unit|mutation|quality|ui|all]     (default: all)
#
# It runs testing/<area>/run.sh for each area and prints a summary table. The same scripts are what
# the GitHub Actions workflow .github/workflows/testing-pipeline.yml calls, so a laptop run and a
# pipeline run do exactly the same thing.
#
# Exit code: 0 = every layer passed, 1 = at least one layer failed.
# On Windows run it from Git Bash.
# =============================================================================
set -u
cd "$(dirname "$0")/.."

ALL_AREAS="unit mutation quality ui"
want="${1:-all}"
case "$want" in
  all) areas="$ALL_AREAS" ;;
  unit|mutation|quality|ui) areas="$want" ;;
  -h|--help) sed -n '2,13p' "$0"; exit 0 ;;
  *) echo "Unknown area '$want'. Use: unit | mutation | quality | ui | all"; exit 2 ;;
esac

declare -A result
failed=0
for a in $areas; do
  echo
  echo "=================================================================="
  echo " $a"
  echo "=================================================================="
  if [ ! -f "testing/$a/run.sh" ]; then
    echo "testing/$a/run.sh does not exist yet."
    result[$a]="MISSING"
    failed=1
    continue
  fi
  if bash "testing/$a/run.sh"; then result[$a]="PASS"; else result[$a]="FAIL"; failed=1; fi
done

echo
echo "=================================================================="
echo " Summary"
echo "=================================================================="
for a in $areas; do printf '  %-10s %s\n' "$a" "${result[$a]}"; done
echo
if [ "$failed" = 0 ]; then echo "All requested layers passed."; else echo "At least one layer did not pass."; fi
exit "$failed"
