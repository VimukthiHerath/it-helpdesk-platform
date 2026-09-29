#!/usr/bin/env bash
# =============================================================================
# deploy-apis.sh
# Sprint 3 – WSO2 API Gateway: Automated API Deployment Script
# IT Helpdesk & Ticketing Platform (SE3022)
#
# What this script does:
#   1. Waits for WSO2 Publisher REST API to become available.
#   2. Obtains a DCR (Dynamic Client Registration) client and OAuth2 access token.
#   3. Imports each of the 5 OpenAPI definition files as APIs in WSO2.
#   4. Publishes (activates) all imported APIs automatically.
#
# Prerequisites:
#   - WSO2 container running (docker compose up -d wso2-gateway)
#   - curl, jq installed
#
# Usage:
#   bash infra/wso2/deploy-apis.sh
#   WSO2_HOST=https://localhost:9443 bash infra/wso2/deploy-apis.sh
# =============================================================================

set -euo pipefail

WSO2_HOST="${WSO2_HOST:-https://localhost:9443}"
WSO2_ADMIN_USER="${WSO2_ADMIN_USER:-admin}"
WSO2_ADMIN_PASS="${WSO2_ADMIN_PASS:-admin}"
APIS_DIR="${APIS_DIR:-$(dirname "$0")/apis}"
READY_TIMEOUT="${READY_TIMEOUT:-120}"
INSECURE="-k"  # WSO2 uses self-signed cert in dev

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'

log()  { echo -e "${CYAN}[WSO2-DEPLOY]${NC} $1"; }
ok()   { echo -e "${GREEN}  ✔ $1${NC}"; }
warn() { echo -e "${YELLOW}  ⚠ $1${NC}"; }
err()  { echo -e "${RED}  ✘ $1${NC}"; }

# ── Step 1: Wait for WSO2 readiness ──────────────────────────────────────────
log "Waiting for WSO2 Publisher API at $WSO2_HOST (max ${READY_TIMEOUT}s)..."
elapsed=0
until curl $INSECURE -sf "$WSO2_HOST/api/am/publisher/v4/apis" \
        -u "$WSO2_ADMIN_USER:$WSO2_ADMIN_PASS" -o /dev/null 2>/dev/null; do
    sleep 5; elapsed=$((elapsed+5))
    if [ "$elapsed" -ge "$READY_TIMEOUT" ]; then
        err "WSO2 did not become ready within ${READY_TIMEOUT}s."
        err "Start with: docker compose up -d wso2-gateway"
        exit 1
    fi
    log "  Still waiting... (${elapsed}s elapsed)"
done
ok "WSO2 is ready."

# ── Step 2: Dynamic Client Registration ──────────────────────────────────────
log "Registering OAuth2 client via DCR..."
DCR_PAYLOAD='{
  "callbackUrl": "http://localhost",
  "clientName": "deploy-apis-script",
  "owner": "admin",
  "grantType": "client_credentials password refresh_token",
  "saasApp": true
}'

DCR_RESPONSE=$(curl $INSECURE -X POST \
    "$WSO2_HOST/client-registration/v0.17/register" \
    -H "Content-Type: application/json" \
    -u "$WSO2_ADMIN_USER:$WSO2_ADMIN_PASS" \
    -d "$DCR_PAYLOAD" 2>/dev/null)

CLIENT_ID=$(echo "$DCR_RESPONSE"     | jq -r '.clientId' 2>/dev/null || echo "")
CLIENT_SECRET=$(echo "$DCR_RESPONSE" | jq -r '.clientSecret' 2>/dev/null || echo "")

if [ -z "$CLIENT_ID" ] || [ "$CLIENT_ID" = "null" ]; then
    err "DCR failed. Response: $DCR_RESPONSE"
    exit 1
fi
ok "OAuth2 client registered. Client ID: $CLIENT_ID"

# ── Step 3: Obtain access token ───────────────────────────────────────────────
log "Obtaining OAuth2 access token (scope: apim:api_create apim:api_publish)..."
TOKEN_RESPONSE=$(curl $INSECURE -X POST \
    "$WSO2_HOST/oauth2/token" \
    -u "$CLIENT_ID:$CLIENT_SECRET" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    -d "grant_type=password&username=${WSO2_ADMIN_USER}&password=${WSO2_ADMIN_PASS}&scope=apim:api_create%20apim:api_publish%20apim:api_view" \
    2>/dev/null)

ACCESS_TOKEN=$(echo "$TOKEN_RESPONSE" | jq -r '.access_token' 2>/dev/null || echo "")
if [ -z "$ACCESS_TOKEN" ] || [ "$ACCESS_TOKEN" = "null" ]; then
    err "Token request failed. Response: $TOKEN_RESPONSE"
    exit 1
fi
ok "Access token obtained."

# ── Step 4: Import & Publish APIs ─────────────────────────────────────────────
# NOTE: WSO2 appends the API's own version ("v1") onto whatever context is given
# here, so the context must be version-less (e.g. "/auth") or the exposed path
# ends up doubled ("/auth/v1/v1").
declare -A API_CONTEXTS=(
    ["auth-api.json"]="/auth"
    ["ticket-api.json"]="/ticket"
    ["assignment-api.json"]="/assignment"
    ["sla-api.json"]="/sla"
    ["notification-api.json"]="/notification"
)

import_and_publish() {
    local file="$1"
    local ctx="$2"
    local name
    name=$(jq -r '.info.title' "$APIS_DIR/$file" 2>/dev/null || echo "$file")

    log "Importing: $name ($ctx)"

    # Import via Publisher REST API (OpenAPI 3.0 import)
    IMPORT_RESPONSE=$(curl $INSECURE -X POST \
        "$WSO2_HOST/api/am/publisher/v4/apis/import-openapi" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -F "file=@${APIS_DIR}/${file};type=application/json" \
        -F "additionalProperties={\"name\":\"$name\",\"context\":\"$ctx\",\"version\":\"v1\"}" \
        2>/dev/null || echo '{"error": "curl_failed"}')

    API_ID=$(echo "$IMPORT_RESPONSE" | jq -r '.id' 2>/dev/null || echo "")
    if [ -z "$API_ID" ] || [ "$API_ID" = "null" ]; then
        # API may already exist; try to find it
        warn "Import did not return an API ID. Checking if API already exists..."
        API_ID=$(curl $INSECURE \
            "$WSO2_HOST/api/am/publisher/v4/apis?query=context:${ctx}" \
            -H "Authorization: Bearer $ACCESS_TOKEN" 2>/dev/null \
            | jq -r '.list[0].id' 2>/dev/null || echo "")
    fi

    if [ -z "$API_ID" ] || [ "$API_ID" = "null" ]; then
        err "Failed to import/find '$name'. Response: $IMPORT_RESPONSE"
        return 1
    fi
    ok "Imported '$name' (ID: $API_ID)"

    # import-openapi does NOT read the x-wso2-*-endpoints vendor extensions from
    # the file, so the API is left with endpointConfig=null. WSO2 refuses to
    # publish an API with no endpoint and no subscription policy, so both must
    # be set explicitly via a follow-up update before publishing. It also does
    # NOT translate a per-operation "security: []" (public, no auth) into WSO2's
    # own authType="None" - every operation defaults to requiring a token, which
    # would make even /login unreachable through the gateway unless corrected.
    log "Configuring endpoint + subscription policy for: $name"
    PROD_URL=$(jq -r '."x-wso2-production-endpoints".urls[0]' "$APIS_DIR/$file")
    SANDBOX_URL=$(jq -r '."x-wso2-sandbox-endpoints".urls[0]' "$APIS_DIR/$file")
    API_JSON=$(curl $INSECURE "$WSO2_HOST/api/am/publisher/v4/apis/$API_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN" 2>/dev/null)
    UPDATED_JSON=$(echo "$API_JSON" | jq \
        --arg prod "$PROD_URL" --arg sandbox "$SANDBOX_URL" \
        --argjson spec "$(cat "$APIS_DIR/$file")" \
        '.endpointConfig = {endpoint_type:"http", production_endpoints:{url:$prod}, sandbox_endpoints:{url:$sandbox}}
         | .policies = ["Unlimited"]
         | .operations = (.operations | map(
             . as $op
             | (($spec.paths[$op.target][$op.verb | ascii_downcase].security) // [{}]) as $sec
             | if ($sec | length) == 0 then .authType = "None" else . end
           ))')
    curl $INSECURE -X PUT "$WSO2_HOST/api/am/publisher/v4/apis/$API_ID" \
        -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
        -d "$UPDATED_JSON" -o /dev/null 2>/dev/null

    # Publish the API
    log "Publishing: $name (ID: $API_ID)"
    PUB_RESPONSE=$(curl $INSECURE -X POST \
        "$WSO2_HOST/api/am/publisher/v4/apis/change-lifecycle?action=Publish&apiId=${API_ID}" \
        -H "Authorization: Bearer $ACCESS_TOKEN" \
        -H "Content-Type: application/json" \
        2>/dev/null || echo '{"error": "publish_failed"}')

    PUB_STATE=$(echo "$PUB_RESPONSE" | jq -r '.lifecycleState.state' 2>/dev/null || echo "")
    if [ "$PUB_STATE" = "Published" ] || echo "$PUB_RESPONSE" | grep -qi '"state":"Published"'; then
        ok "Published '$name' successfully."
    else
        warn "'$name' publish response: $PUB_RESPONSE (may already be published)"
    fi

    # Publishing only changes the lifecycle state; the API is not actually live
    # on the gateway until a revision is created and deployed to an environment.
    log "Deploying revision to gateway for: $name"
    REV_RESPONSE=$(curl $INSECURE -X POST "$WSO2_HOST/api/am/publisher/v4/apis/$API_ID/revisions" \
        -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
        -d '{"description":"Automated deploy"}' 2>/dev/null)
    REV_ID=$(echo "$REV_RESPONSE" | jq -r '.id' 2>/dev/null || echo "")
    if [ -n "$REV_ID" ] && [ "$REV_ID" != "null" ]; then
        curl $INSECURE -X POST "$WSO2_HOST/api/am/publisher/v4/apis/$API_ID/deploy-revision?revisionId=$REV_ID" \
            -H "Authorization: Bearer $ACCESS_TOKEN" -H "Content-Type: application/json" \
            -d '[{"name":"Default","vhost":"localhost","displayOnDevportal":true}]' -o /dev/null 2>/dev/null
        ok "Revision deployed to Default gateway."
    else
        warn "Could not create a new revision for '$name' (an active deployment may already exist)."
    fi
}

FAILED=0
for api_file in auth-api.json ticket-api.json assignment-api.json sla-api.json notification-api.json; do
    ctx="${API_CONTEXTS[$api_file]}"
    import_and_publish "$api_file" "$ctx" || FAILED=$((FAILED+1))
done

# ── Summary ───────────────────────────────────────────────────────────────────
echo ""
log "════════════════════════════════════════════════════════════════════"
if [ "$FAILED" -eq 0 ]; then
    ok "All 5 APIs imported and published successfully!"
    log "  Publisher UI  : ${WSO2_HOST}/publisher"
    log "  DevPortal     : ${WSO2_HOST}/devportal"
    log "  HTTP Gateway  : http://localhost:8280/<context>/v1"
else
    err "$FAILED API(s) failed to deploy. Check output above."
    exit 1
fi
