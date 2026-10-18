#!/usr/bin/env bash
# Owner: Senul. Real browser tests; a failed assertion must fail this script.
set -euo pipefail
cd "$(dirname "$0")"

: "${UI_BASE_URL:?Set UI_BASE_URL to the running frontend address.}"

npm ci --no-audit --no-fund
if [[ "$(uname -s)" == "Linux" ]]; then
  npx playwright install --with-deps chromium
else
  npx playwright install chromium
fi

npm test -- "$@"
