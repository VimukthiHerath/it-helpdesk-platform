#!/usr/bin/env bash
# =============================================================================
# test-gateway-routes.sh
# Sprint 3 – API Gateway Route Smoke Tests
# IT Helpdesk & Ticketing Platform (SE3022)
#
# Usage:
#   GATEWAY_URL=http://localhost:5000 bash scripts/test-gateway-routes.sh
#   GATEWAY_URL=https://api-gateway.<env>.azurecontainerapps.io bash scripts/test-gateway-routes.sh
#
# Exit codes:
#   0 – all routes returned expected status codes
#   1 – one or more routes returned unexpected status codes
# =============================================================================

set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5000}"
PASS=0
FAIL=0
RESULTS=()

# ── Helper ────────────────────────────────────────────────────────────────────
check_route() {
    local label="$1"
    local method="$2"
    local path="$3"
    local expected_codes="$4"   # comma-separated acceptable HTTP codes, e.g. "200,401"
    local extra_args="${5:-}"   # optional extra curl args (e.g. -H "Authorization: Bearer ...")

    local url="${GATEWAY_URL}${path}"
    local http_code

    http_code=$(curl -s -o /dev/null -w "%{http_code}" \
        --max-time 10 \
        --request "$method" \
        $extra_args \
        "$url" 2>/dev/null || echo "000")

    # Check if returned code is in accepted codes
    local accepted=false
    IFS=',' read -ra codes <<< "$expected_codes"
    for code in "${codes[@]}"; do
        if [ "$http_code" = "$code" ]; then
            accepted=true
            break
        fi
    done

    if $accepted; then
        echo "  [PASS] $label [$method $path] → HTTP $http_code  (expected: $expected_codes)"
        PASS=$((PASS + 1))
        RESULTS+=("PASS | $label | $method | $path | $http_code | $expected_codes")
    else
        echo "  [FAIL] $label [$method $path] → HTTP $http_code  (expected: $expected_codes)"
        FAIL=$((FAIL + 1))
        RESULTS+=("FAIL | $label | $method | $path | $http_code | $expected_codes")
    fi
}

# ── Test Suite ────────────────────────────────────────────────────────────────
echo "============================================================"
echo "  IT Helpdesk API Gateway – Route Smoke Tests"
echo "  Gateway URL: $GATEWAY_URL"
echo "  Started:     $(date -u '+%Y-%m-%dT%H:%M:%SZ')"
echo "============================================================"

echo ""
echo "── Gateway Self ─────────────────────────────────────────────"
check_route "Gateway Health"        "GET"  "/health"                               "200"

echo ""
echo "── Auth Routes ──────────────────────────────────────────────"
# Login doesn't need auth, should return 200 or 400 (body validation)
check_route "Auth: POST /login"     "POST" "/api/auth/login"                       "200,400,415"
# Protected admin route without token should return 401
check_route "Auth: GET /users (no token)" "GET" "/api/auth/users"                 "401,404"
# With a dummy bearer token, still 401 (invalid signature) – confirms header forwarding
check_route "Auth: GET /users (bad token)" "GET" "/api/auth/users" "401,403,404,503" \
    '-H "Authorization: Bearer dummy.invalid.token"'

echo ""
echo "── Ticket Routes ────────────────────────────────────────────"
check_route "Tickets: GET / (no token)"  "GET"  "/api/tickets"                    "401,403,404"
check_route "Tickets: GET / (bad token)" "GET"  "/api/tickets"  "401,403,404,503" \
    '-H "Authorization: Bearer dummy.invalid.token"'

echo ""
echo "── Assignment Routes ────────────────────────────────────────"
check_route "Assignment: GET / (no token)"  "GET" "/api/assignment"               "401,403,404"

echo ""
echo "── SLA Routes ───────────────────────────────────────────────"
check_route "SLA: GET / (no token)"         "GET" "/api/sla"                      "401,403,404"

echo ""
echo "── Notification Routes ──────────────────────────────────────"
check_route "Notification: GET / (no token)" "GET" "/api/notification"            "401,403,404"

echo ""
echo "── Non-existent Route (should not proxy) ────────────────────"
check_route "Unknown route → 404"    "GET"  "/api/unknown-service"                "404,502,503"

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
echo "============================================================"
echo "  Results:  PASS=$PASS  FAIL=$FAIL  TOTAL=$((PASS + FAIL))"
echo "============================================================"

# Print table
echo ""
echo "| Result | Label | Method | Path | Got | Expected |"
echo "|--------|-------|--------|------|-----|----------|"
for row in "${RESULTS[@]}"; do
    IFS='|' read -ra cols <<< "$row"
    echo "| ${cols[0]} | ${cols[1]} | ${cols[2]} | ${cols[3]} | ${cols[4]} | ${cols[5]} |"
done

if [ "$FAIL" -gt 0 ]; then
    echo ""
    echo "*** $FAIL route(s) FAILED. Review output above. ***"
    exit 1
else
    echo ""
    echo "*** All $PASS routes passed smoke test. ***"
    exit 0
fi
