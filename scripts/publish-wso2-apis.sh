#!/usr/bin/env bash
# =============================================================================
# publish-wso2-apis.sh - publish the five IT Helpdesk APIs into the WSO2 gateway
#
# The gateway only knows a route once its API has been published AND deployed. Run this:
#   - on your machine after  docker compose up -d   (mode: local)
#   - on Azure after start-all, or whenever the gateway's API definitions are lost or
#     changed, for example after editing infra/wso2/apis/*.json   (mode: azure)
#
# Usage:   ./scripts/publish-wso2-apis.sh [local|azure] [--check-backends] [--dry-run] [--help]
#   local     (default) the WSO2 container started by docker-compose (local-wso2-gateway).
#             The APIs point at the services on your machine: auth 5121, ticket 5164,
#             assignment 5067, sla 5262, notification 5214 (the host names auth-api,
#             ticket-api, ... are mapped to your machine by docker-compose).
#   azure     the deployed gateway. Its management port is never exposed, so the files are
#             staged on the gateway's Azure Files share and run INSIDE the container with
#             "az containerapp exec". The APIs point at the Azure services. The gateway
#             must be running (run scripts/start-all first).
#   --check-backends  after publishing, also call each service's /health through the gateway.
#             On Azure this WAKES every service that is scaled to zero (and needs MySQL running), so it
#             is off by default. By default only the gateway is asked whether each route exists.
#   --dry-run show what would be done, change nothing
#
# It is safe to run again: the old copies of these five APIs are replaced.
# Every operation is published with "x-auth-type: None": the gateway only ROUTES, and each
# service checks the JWT itself (see docs/ARCHITECTURE_AND_END_TO_END_FLOW.md, section 3.4).
#
# Settings (environment variables): HELPDESK_RG, HELPDESK_SUBSCRIPTION_ID, HELPDESK_ENV,
#   WSO2_ADMIN_USER / WSO2_ADMIN_PASSWORD (default admin / admin), WSO2_CONTAINER
#   (default local-wso2-gateway)
#
# Exit code: 0 = all five APIs published and deployed, 1 = something failed, 2 = could not start.
# =============================================================================
set -u

MODE="local"
DRY_RUN=0
CHECK_BACKENDS=0
RG="${HELPDESK_RG:-it-helpdesk-rg}"
SUBSCRIPTION="${HELPDESK_SUBSCRIPTION_ID:-7e76da9f-fdb8-43bd-9551-28bc90afcd2f}"
ENVIRONMENT="${HELPDESK_ENV:-it-helpdesk-envstd}"
WSO2_USER="${WSO2_ADMIN_USER:-admin}"
WSO2_PASS="${WSO2_ADMIN_PASSWORD:-admin}"
CONTAINER="${WSO2_CONTAINER:-local-wso2-gateway}"
GATEWAY_APP="wso2-gateway"
SHARE="wso2-data"
SHARE_DIR="database/pub"                                   # shows up in the container as ...repository/database/pub
CONTAINER_SHARE_PATH="/home/wso2carbon/wso2am-4.3.0/repository/database/pub"
LOCAL_STAGE="/tmp/wso2pub"

usage() { sed -n '2,/^# ====/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    local|azure) MODE="$1" ;;
    --dry-run)   DRY_RUN=1 ;;
    --check-backends) CHECK_BACKENDS=1 ;;
    -h|--help)   usage; exit 0 ;;
    *) echo "Unknown option: $1 (try --help)"; exit 2 ;;
  esac
  shift
done

export MSYS_NO_PATHCONV=1   # Git Bash on Windows must not rewrite /tmp/... paths meant for the container

say()  { echo "$*"; }
step() { echo; echo "== $*"; }
azq()  { local out; out=$(az "$@" 2>/dev/null); local rc=$?; printf '%s' "$out" | tr -d '\r'; return $rc; }
# host path form that docker / az (Windows programs) understand
hostpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi; }

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SPEC_DIR="$ROOT/infra/wso2/apis"
CORE="$ROOT/scripts/lib/wso2-publish-core.sh"
APIS="auth ticket assignment sla notification"

# --- check the files exist ------------------------------------------------------------
for f in $APIS; do
  [ -f "$SPEC_DIR/$f-api.json" ] || { say "ERROR: $SPEC_DIR/$f-api.json not found. Run this from a full checkout of the repository."; exit 2; }
done
[ -f "$CORE" ] || { say "ERROR: $CORE not found."; exit 2; }

# --- backends and public address, by mode ---------------------------------------------
if [ "$MODE" = "local" ]; then
  B_AUTH="http://auth-api:5121/api/Auth";       B_TICKET="http://ticket-api:5164/api/Ticket"
  B_ASSIGN="http://assignment-api:5067/api/Assignments"
  B_SLA="http://sla-api:5262";                  B_NOTIF="http://notification-api:5214"
  GATEWAY_URL="http://localhost:8280"
else
  step "Checking your Azure setup"
  command -v az >/dev/null 2>&1 || { say "ERROR: the Azure CLI ('az') is not installed. See https://aka.ms/installazurecli"; exit 2; }
  cur=$(azq account show --query id -o tsv)
  [ -n "$cur" ] || { say "ERROR: you are not logged in to Azure. Run:  az login"; exit 2; }
  if [ "$cur" != "$SUBSCRIPTION" ]; then
    az account set --subscription "$SUBSCRIPTION" >/dev/null 2>&1 || { say "ERROR: cannot select subscription $SUBSCRIPTION with this login."; exit 2; }
  fi
  azq extension show -n containerapp --query name -o tsv | grep -q containerapp || az extension add -n containerapp --upgrade -y >/dev/null 2>&1
  [ "$(azq group show -n "$RG" --query name -o tsv)" = "$RG" ] || { say "ERROR: resource group '$RG' not found."; exit 2; }
  DOMAIN=$(azq containerapp env show -g "$RG" -n "$ENVIRONMENT" --query properties.defaultDomain -o tsv)
  [ -n "$DOMAIN" ] || { say "ERROR: environment '$ENVIRONMENT' not found in '$RG'."; exit 2; }
  say "  account: $(azq account show --query user.name -o tsv)   resource group: $RG   domain: $DOMAIN"
  B_AUTH="https://auth-service.$DOMAIN/api/Auth";            B_TICKET="https://ticket-service.$DOMAIN/api/Ticket"
  B_ASSIGN="https://assignment-service.$DOMAIN/api/Assignments"
  B_SLA="https://sla-service.internal.$DOMAIN";              B_NOTIF="https://notification-service.internal.$DOMAIN"
  GATEWAY_URL="https://$GATEWAY_APP.$DOMAIN"
fi

step "Plan ($MODE)"
say "  gateway address:  $GATEWAY_URL"
say "  Auth API         -> $B_AUTH"
say "  Ticket API       -> $B_TICKET"
say "  Assignment API   -> $B_ASSIGN"
say "  SLA API          -> $B_SLA"
say "  Notification API -> $B_NOTIF"
[ "$DRY_RUN" = 1 ] && { say; say "DRY RUN: nothing was changed."; exit 0; }

# --- stage the files (specs + props + the core script) --------------------------------
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
props() { printf '{"name":"%s","context":"%s","version":"v1","policies":["Unlimited"],"endpointConfig":{"endpoint_type":"http","production_endpoints":{"url":"%s"},"sandbox_endpoints":{"url":"%s"}}}' "$1" "$2" "$3" "$3" > "$STAGE/$4.props.json"; }
props "Auth API"         "/auth"         "$B_AUTH"   auth
props "Ticket API"       "/ticket"       "$B_TICKET" ticket
props "Assignment API"   "/assignment"   "$B_ASSIGN" assignment
props "SLA API"          "/sla"          "$B_SLA"    sla
props "Notification API" "/notification" "$B_NOTIF"  notification
for f in $APIS; do cp "$SPEC_DIR/$f-api.json" "$STAGE/$f.spec.json"; done
cp "$CORE" "$STAGE/wso2-publish-core.sh"
STAGE_HOST=$(hostpath "$STAGE")
OUT=""

# --- run it inside the container -------------------------------------------------------
if [ "$MODE" = "local" ]; then
  step "Local WSO2 container ($CONTAINER)"
  command -v docker >/dev/null 2>&1 || { say "ERROR: Docker is not installed or not on the PATH."; exit 2; }
  docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$CONTAINER" \
    || { say "ERROR: the container '$CONTAINER' is not running. Start it with:  docker compose up -d wso2-gateway"; exit 2; }
  docker exec "$CONTAINER" mkdir -p "$LOCAL_STAGE" || { say "ERROR: cannot reach the container."; exit 2; }
  for f in $APIS; do docker cp "$STAGE_HOST/$f.spec.json" "$CONTAINER:$LOCAL_STAGE/$f.spec.json" >/dev/null && docker cp "$STAGE_HOST/$f.props.json" "$CONTAINER:$LOCAL_STAGE/$f.props.json" >/dev/null; done
  docker cp "$STAGE_HOST/wso2-publish-core.sh" "$CONTAINER:$LOCAL_STAGE/wso2-publish-core.sh" >/dev/null
  OUT=$(docker exec "$CONTAINER" sh "$LOCAL_STAGE/wso2-publish-core.sh" "$LOCAL_STAGE" "$WSO2_USER" "$WSO2_PASS" 2>&1)
  docker exec "$CONTAINER" rm -rf "$LOCAL_STAGE" >/dev/null 2>&1
else
  step "Azure gateway ($GATEWAY_APP)"
  rev=$(azq containerapp revision list -g "$RG" -n "$GATEWAY_APP" --query "[?properties.active].name | [0]" -o tsv)
  rep=""; [ -n "$rev" ] && rep=$(azq containerapp replica list -g "$RG" -n "$GATEWAY_APP" --revision "$rev" --query "[0].name" -o tsv)
  if [ -z "$rev" ] || [ -z "$rep" ]; then
    say "ERROR: the gateway is not running (no active revision or replica). Run  ./scripts/start-all.sh  first (or activate the gateway revision)."; exit 2
  fi
  say "  using revision $rev, replica $rep"
  sa=$(azq storage account list -g "$RG" --query "[?contains(name,'wso2')].name | [0]" -o tsv)
  [ -n "$sa" ] || { say "ERROR: cannot find the WSO2 storage account in $RG."; exit 2; }
  key=$(azq storage account keys list -g "$RG" -n "$sa" --query "[0].value" -o tsv)
  [ -n "$key" ] || { say "ERROR: cannot read the storage account key (needs Contributor on $RG)."; exit 2; }
  say "  staging 11 files on the file share ($sa/$SHARE/$SHARE_DIR)"
  az storage directory create --account-name "$sa" --account-key "$key" -s "$SHARE" -n "$SHARE_DIR" -o none 2>/dev/null
  az storage file upload-batch --account-name "$sa" --account-key "$key" -d "$SHARE" --destination-path "$SHARE_DIR" -s "$STAGE_HOST" -o none 2>/dev/null \
    || { say "ERROR: could not upload the files to the file share."; exit 2; }
  say "  running the publish steps inside the gateway (this takes about a minute)..."
  OUT=$(az containerapp exec -g "$RG" -n "$GATEWAY_APP" --revision "$rev" --replica "$rep" --container "$GATEWAY_APP" \
        --command "sh $CONTAINER_SHARE_PATH/wso2-publish-core.sh $CONTAINER_SHARE_PATH $WSO2_USER $WSO2_PASS" 2>&1 | tr -d '\r' | grep -v -E '^(WARNING|INFO)|Alive\[|Finished\[')
  # clean the staged files off the share
  for f in $(az storage file list --account-name "$sa" --account-key "$key" -s "$SHARE" -p "$SHARE_DIR" --query "[].name" -o tsv 2>/dev/null | tr -d '\r'); do
    az storage file delete --account-name "$sa" --account-key "$key" -s "$SHARE" -p "$SHARE_DIR/$f" -o none 2>/dev/null
  done
  az storage directory delete --account-name "$sa" --account-key "$key" -s "$SHARE" -n "$SHARE_DIR" -o none 2>/dev/null
fi

printf '%s\n' "$OUT" | sed 's/^/  /'
printf '%s' "$OUT" | grep -q '^RESULT: OK' && PUBLISHED=1 || PUBLISHED=0

# --- check the routes through the public gateway address -----------------------------------
step "Checking the routes through the gateway ($GATEWAY_URL)"
say "  The gateway answers a CORS preflight (OPTIONS) for a route by itself, without calling the service:"
say "  200 = the route is live; 404 = the route is NOT there."
sleep 15
ROUTES_OK=1
for ctx in auth ticket assignment sla notification; do
  code=$(curl -s -o /dev/null -m 25 -X OPTIONS -H 'Origin: http://localhost:3000' -H 'Access-Control-Request-Method: GET' -w '%{http_code}' "$GATEWAY_URL/$ctx/v1/health"); [ -n "$code" ] || code=000
  [ "$code" = 200 ] || ROUTES_OK=0
  printf '  %-26s route check HTTP %s
' "$GATEWAY_URL/$ctx/v1/health" "$code"
done
if [ "$CHECK_BACKENDS" = 1 ]; then
  say "  --check-backends: asking each service for /health through the gateway (may wake services)..."
  for ctx in auth ticket assignment sla notification; do
    code=$(curl -s -o /dev/null -m 60 -w '%{http_code}' "$GATEWAY_URL/$ctx/v1/health"); [ -n "$code" ] || code=000
    printf '  %-26s backend HTTP %s
' "$GATEWAY_URL/$ctx/v1/health" "$code"
  done
fi

echo
if [ "$PUBLISHED" = 1 ] && [ "$ROUTES_OK" = 1 ]; then echo "Done: all five APIs are published, deployed and answering."; exit 0; fi
if [ "$PUBLISHED" = 1 ]; then echo "Published, but the gateway is not answering for every route yet. Wait a minute and check again, or see docs/WSO2_API_GATEWAY.md."; exit 1; fi
echo "FAILED: see the messages above."; exit 1
