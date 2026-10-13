#!/usr/bin/env bash
# =============================================================================
# start-all.sh - bring the whole IT Helpdesk platform up on Azure
#
# What it does, in order:
#   1. checks the Azure CLI, your login, the subscription and the resource group
#   2. starts the MySQL server (if it is stopped) and waits until it is Ready
#   3. starts Kafka, then the five backend services and the frontend
#   4. activates the WSO2 gateway (exactly ONE revision - see scripts/README.md)
#   5. waits until everything answers, and prints a summary
#
# It is safe to run again at any time: anything already running is left alone.
# It never uses "az containerapp update" on the WSO2 gateway (that creates a second
# replica which fights the first one over the gateway's database file).
#
# Usage:   ./scripts/start-all.sh [--dry-run] [--no-wait] [--help]
#   --dry-run   show what would be done, change nothing
#   --no-wait   send the start commands and return without waiting for health
#
# Settings (environment variables, defaults are for this project):
#   HELPDESK_RG, HELPDESK_SUBSCRIPTION_ID, HELPDESK_MYSQL, HELPDESK_ENV,
#   HELPDESK_TIMEOUT_MIN (how long to wait for health, default 15)
#
# Exit code: 0 = everything is up, 1 = something did not come up, 2 = could not start
# (not logged in, wrong subscription, ...).
# =============================================================================
set -u

RG="${HELPDESK_RG:-it-helpdesk-rg}"
SUBSCRIPTION="${HELPDESK_SUBSCRIPTION_ID:-7e76da9f-fdb8-43bd-9551-28bc90afcd2f}"
MYSQL="${HELPDESK_MYSQL:-it-helpdesk-mysql02}"
ENVIRONMENT="${HELPDESK_ENV:-it-helpdesk-envstd}"
TIMEOUT_MIN="${HELPDESK_TIMEOUT_MIN:-15}"

KAFKA_APP="kafka-broker"
GATEWAY_APP="wso2-gateway"
FRONTEND_APP="it-helpdesk-frontend"
BACKEND_APPS="auth-service assignment-service ticket-service sla-service notification-service"

DRY_RUN=0
WAIT=1

usage() { sed -n '2,/^# ====/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --no-wait) WAIT=0 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1 (try --help)"; exit 2 ;;
  esac
  shift
done

export MSYS_NO_PATHCONV=1   # Git Bash on Windows must not rewrite /subscriptions/... paths

# --- helpers -----------------------------------------------------------------
# azq: run a read-only az command, print its output (CR removed), hide the noisy
# "extension altered the behaviour" warnings. Prints nothing if the command fails.
azq() { local out; out=$(az "$@" 2>/dev/null); local rc=$?; printf '%s' "$out" | tr -d '\r'; return $rc; }

# run: run a command that changes something (or just print it with --dry-run).
run() {
  if [ "$DRY_RUN" = 1 ]; then echo "    [dry-run] az $*"; return 0; fi
  local out rc
  out=$(az "$@" 2>&1 >/dev/null); rc=$?
  if [ $rc -ne 0 ]; then
    echo "    ! az $1 $2 failed: $(printf '%s' "$out" | tr -d '\r' | grep -E '^ERROR' | head -2 | tr '\n' ' ')"
  fi
  return $rc
}

say()  { echo "$*"; }
step() { echo; echo "== $*"; }
FAILED=""
fail() { FAILED="$FAILED $1"; }

app_exists()     { [ "$(azq containerapp show -g "$RG" -n "$1" --query name -o tsv)" = "$1" ]; }
min_replicas()   { local v; v=$(azq containerapp show -g "$RG" -n "$1" --query properties.template.scale.minReplicas -o tsv); case "$v" in ''|None|null) echo 0 ;; *) echo "$v" ;; esac; }
active_count()   { local v; v=$(azq containerapp revision list -g "$RG" -n "$1" --query "length([?properties.active])" -o tsv); echo "${v:-0}"; }
replicas_of()    { local v; v=$(azq containerapp revision list -g "$RG" -n "$1" --query "sum([?properties.active].properties.replicas)" -o tsv); case "$v" in ''|None|null) echo 0 ;; *) echo "$v" ;; esac; }
running_state()  { azq containerapp revision list -g "$RG" -n "$1" --query "[?properties.active].properties.runningState | [0]" -o tsv; }
newest_revision(){ azq containerapp revision list -g "$RG" -n "$1" --all --query "sort_by(@, &properties.createdTime)[-1].name" -o tsv; }

# --- preflight ---------------------------------------------------------------
preflight() {
  step "Checking your setup"
  command -v az >/dev/null 2>&1   || { say "ERROR: the Azure CLI ('az') is not installed. See https://aka.ms/installazurecli"; exit 2; }
  command -v curl >/dev/null 2>&1 || { say "ERROR: 'curl' is not installed."; exit 2; }
  local cur; cur=$(azq account show --query id -o tsv)
  [ -n "$cur" ] || { say "ERROR: you are not logged in to Azure. Run:  az login"; exit 2; }
  if [ "$cur" != "$SUBSCRIPTION" ]; then
    az account set --subscription "$SUBSCRIPTION" >/dev/null 2>&1 \
      || { say "ERROR: cannot select subscription $SUBSCRIPTION with this login ($(azq account show --query user.name -o tsv))."; say "       Log in with an account that has access to it (az login), or set HELPDESK_SUBSCRIPTION_ID."; exit 2; }
  fi
  say "  account:      $(azq account show --query user.name -o tsv)"
  say "  subscription: $(azq account show --query name -o tsv) ($SUBSCRIPTION)"
  if ! azq extension show -n containerapp --query name -o tsv | grep -q containerapp; then
    say "  installing the Azure CLI 'containerapp' extension..."
    az extension add -n containerapp --upgrade -y >/dev/null 2>&1 || { say "ERROR: could not install the containerapp extension."; exit 2; }
  fi
  [ "$(azq group show -n "$RG" --query name -o tsv)" = "$RG" ] || { say "ERROR: resource group '$RG' not found in this subscription."; exit 2; }
  DOMAIN=$(azq containerapp env show -g "$RG" -n "$ENVIRONMENT" --query properties.defaultDomain -o tsv)
  [ -n "$DOMAIN" ] || { say "ERROR: Container Apps environment '$ENVIRONMENT' not found in '$RG'."; exit 2; }
  say "  resource group: $RG   environment domain: $DOMAIN"
  [ "$DRY_RUN" = 1 ] && say "  DRY RUN: nothing will be changed."
}

# --- MySQL -------------------------------------------------------------------
start_mysql() {
  step "MySQL server ($MYSQL)"
  local s started=0 i
  for i in $(seq 1 60); do          # up to ~15 minutes
    s=$(azq mysql flexible-server show -g "$RG" -n "$MYSQL" --query state -o tsv)
    case "$s" in
      Ready)  say "  Ready."; return 0 ;;
      Stopped|Disabled)
        if [ $started = 0 ]; then say "  state: $s -> starting it"; run mysql flexible-server start -g "$RG" -n "$MYSQL" --no-wait; started=1
          [ "$DRY_RUN" = 1 ] && return 0; fi ;;
      '')     say "  ! cannot read the state of MySQL server '$MYSQL' (does it exist?)"; fail mysql; return 1 ;;
      *)      say "  state: $s (waiting)" ;;
    esac
    sleep 15
  done
  say "  ! MySQL did not become Ready in time (last state: $s)"; fail mysql; return 1
}

# --- one ordinary container app ---------------------------------------------
start_app() {
  local app="$1"
  if ! app_exists "$app"; then say "  - $app: not found in $RG, skipping"; return 0; fi
  # an app whose revisions were all deactivated cannot run: activate the newest one
  if [ "$(active_count "$app")" = 0 ]; then
    local rev; rev=$(newest_revision "$app")
    say "  - $app: no active revision, activating $rev"
    run containerapp revision activate -g "$RG" -n "$app" --revision "$rev"
  fi
  if [ "$(min_replicas "$app")" != 1 ]; then
    say "  - $app: setting min replicas to 1"
    run containerapp update -g "$RG" -n "$app" --min-replicas 1 --no-wait
  else
    say "  - $app: already set to run"
  fi
}

# --- the WSO2 gateway: ONE revision, never "update" --------------------------
start_gateway() {
  step "WSO2 gateway ($GATEWAY_APP)"
  if ! app_exists "$GATEWAY_APP"; then say "  ! $GATEWAY_APP not found"; fail gateway; return 1; fi
  local active; active=$(azq containerapp revision list -g "$RG" -n "$GATEWAY_APP" --query "[?properties.active].name" -o tsv)
  local want; want=$(azq containerapp revision list -g "$RG" -n "$GATEWAY_APP" --all \
    --query 'sort_by([?properties.template.scale.minReplicas==`1` && properties.template.scale.maxReplicas==`1`], &properties.createdTime)[-1].name' -o tsv)
  if [ -z "$want" ]; then
    say "  ! no revision of $GATEWAY_APP is configured min=1 / max=1. Fix it in the portal (see scripts/README.md)."; fail gateway; return 1
  fi
  local n; n=$(printf '%s\n' "$active" | grep -c .)
  local want_is_active=0
  case " $(echo $active) " in *" $want "*) want_is_active=1 ;; esac
  if [ "$n" -gt 1 ] || { [ "$n" = 1 ] && [ "$want_is_active" = 0 ]; }; then
    say "  more than one (or the wrong) revision is active: $(echo $active) - keeping only $want"
    local r; for r in $active; do [ "$r" != "$want" ] && run containerapp revision deactivate -g "$RG" -n "$GATEWAY_APP" --revision "$r"; done
    if [ "$want_is_active" = 0 ] && [ "$DRY_RUN" = 0 ]; then say "  waiting 90s for the database file lock to be released..."; sleep 90; fi
  fi
  if [ "$want_is_active" = 1 ]; then say "  $want is already active"; return 0; fi
  # if it was switched off a moment ago, give the file lock on its database time to clear
  local last now_ts last_ts age
  last=$(azq containerapp revision show -g "$RG" -n "$GATEWAY_APP" --revision "$want" --query properties.lastActiveTime -o tsv)
  if [ -n "$last" ] && [ "$last" != None ]; then
    now_ts=$(date -u +%s); last_ts=$(date -u -d "$last" +%s 2>/dev/null)
    if [ -n "$last_ts" ]; then
      age=$((now_ts - last_ts))
      if [ "$age" -lt 120 ]; then
        say "  it was active $age s ago; waiting $((120 - age))s so its database lock is released"
        [ "$DRY_RUN" = 1 ] || sleep $((120 - age))
      fi
    fi
  fi
  say "  activating revision $want"
  run containerapp revision activate -g "$RG" -n "$GATEWAY_APP" --revision "$want"
}

# --- waiting and checking ----------------------------------------------------
http_code() { local c; c=$(curl -s -o /dev/null -m 15 -w '%{http_code}' "$1" 2>/dev/null); echo "${c:-000}"; }

wait_until_up() {
  step "Waiting for everything to answer (up to $TIMEOUT_MIN minutes)"
  local gw="https://$GATEWAY_APP.$DOMAIN" fe="https://$FRONTEND_APP.$DOMAIN"
  local deadline=$(( $(date +%s) + TIMEOUT_MIN * 60 ))
  # name|url that must return 200. Through the gateway, so the API routes are tested too.
  local checks="gateway|$gw/
auth (via gateway)|$gw/auth/v1/health
ticket (via gateway)|$gw/ticket/v1/health
assignment (via gateway)|$gw/assignment/v1/health
sla (via gateway)|$gw/sla/v1/health
notification (via gateway)|$gw/notification/v1/health
frontend|$fe/"
  local pending line name url code
  while : ; do
    pending=""
    while IFS= read -r line; do
      name=${line%%|*}; url=${line#*|}; code=$(http_code "$url")
      [ "$code" = 200 ] || pending="$pending [$name=$code]"
    done <<EOF
$checks
EOF
    if [ -z "$pending" ]; then say "  everything answers 200."; return 0; fi
    if [ "$(date +%s)" -ge "$deadline" ]; then say "  ! still not ready after $TIMEOUT_MIN minutes:$pending"; fail "health"; return 1; fi
    say "  waiting...$pending"
    sleep 20
  done
}

summary() {
  step "Summary"
  local a st
  for a in $KAFKA_APP $BACKEND_APPS $FRONTEND_APP $GATEWAY_APP; do
    app_exists "$a" || continue
    st=$(running_state "$a"); printf '  %-22s replicas=%-2s state=%s\n' "$a" "$(replicas_of "$a")" "${st:-?}"
  done
  say "  MySQL $MYSQL: $(azq mysql flexible-server show -g "$RG" -n "$MYSQL" --query state -o tsv)"
  echo
  say "  Frontend:  https://$FRONTEND_APP.$DOMAIN"
  say "  Gateway:   https://$GATEWAY_APP.$DOMAIN"
  echo
  say "  Everything is now running and costing money. When you are done run:  ./scripts/stop-all.sh"
}

# --- main --------------------------------------------------------------------
preflight
start_mysql || { say; say "Cannot continue without the database."; exit 1; }

step "Kafka"
start_app "$KAFKA_APP"
step "Backend services and frontend"
for a in $BACKEND_APPS $FRONTEND_APP; do start_app "$a"; done
start_gateway

if [ "$WAIT" = 1 ] && [ "$DRY_RUN" = 0 ]; then
  wait_until_up
else
  [ "$DRY_RUN" = 1 ] || say "  (--no-wait: not waiting. Check the Azure portal or re-run this script later.)"
fi
[ "$DRY_RUN" = 1 ] || summary
if [ -n "$FAILED" ]; then echo; echo "FINISHED WITH PROBLEMS:$FAILED  (see messages above and scripts/README.md, section Troubleshooting)"; exit 1; fi
exit 0
