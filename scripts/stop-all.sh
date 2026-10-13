#!/usr/bin/env bash
# =============================================================================
# stop-all.sh - shut the whole IT Helpdesk platform down on Azure and PROVE it
#
# What it does, in order:
#   1. checks the Azure CLI, your login, the subscription and the resource group
#   2. sets every container app to "minimum replicas 0" (scale to zero)
#   3. deactivates the WSO2 gateway revision (it is never changed with "update")
#   4. waits and CHECKS that no replica is left running anywhere; if an app does not
#      go down by itself (Kafka sometimes does not), its revision is deactivated
#   5. stops the MySQL server (unless --keep-mysql); the request is sent early so it runs
#      in parallel with the apps shutting down, and is checked at the end
#
# Why the check matters: "az ... update --min-replicas 0" only tells Azure that zero
# is allowed. Replicas are removed a few minutes later, and a stuck app keeps running
# and billing. This script only reports success when it has seen zero replicas.
#
# Usage:   ./scripts/stop-all.sh [--keep-mysql] [--dry-run] [--no-wait] [--help]
#   --keep-mysql  leave the MySQL server running (it keeps billing about $12-15/month)
#   --dry-run     show what would be done, change nothing
#   --no-wait     send the stop commands and return without checking
#
# Settings (environment variables, defaults are for this project):
#   HELPDESK_RG, HELPDESK_SUBSCRIPTION_ID, HELPDESK_MYSQL,
#   HELPDESK_TIMEOUT_MIN (how long to wait for replicas to disappear, default 15),
#   HELPDESK_NUDGE_MIN (after how many minutes an app that will not scale down is deactivated, default 4)
#
# Exit code: 0 = verified stopped, 1 = something is still running, 2 = could not start.
# Note: Azure restarts a stopped MySQL server by itself after 7 days. Run this again.
# =============================================================================
set -u

RG="${HELPDESK_RG:-it-helpdesk-rg}"
SUBSCRIPTION="${HELPDESK_SUBSCRIPTION_ID:-7e76da9f-fdb8-43bd-9551-28bc90afcd2f}"
MYSQL="${HELPDESK_MYSQL:-it-helpdesk-mysql02}"
TIMEOUT_MIN="${HELPDESK_TIMEOUT_MIN:-15}"
NUDGE_MIN="${HELPDESK_NUDGE_MIN:-4}"     # after this long, apps that did not scale down by themselves are deactivated
GATEWAY_APP="wso2-gateway"

DRY_RUN=0
WAIT=1
STOP_MYSQL=1

usage() { sed -n '2,/^# ====/p' "$0" | sed -e '$d' -e 's/^# \{0,1\}//'; }

while [ $# -gt 0 ]; do
  case "$1" in
    --keep-mysql) STOP_MYSQL=0 ;;
    --dry-run)    DRY_RUN=1 ;;
    --no-wait)    WAIT=0 ;;
    -h|--help)    usage; exit 0 ;;
    *) echo "Unknown option: $1 (try --help)"; exit 2 ;;
  esac
  shift
done

export MSYS_NO_PATHCONV=1   # Git Bash on Windows must not rewrite /subscriptions/... paths

# --- helpers (same as start-all.sh) -----------------------------------------
azq() { local out; out=$(az "$@" 2>/dev/null); local rc=$?; printf '%s' "$out" | tr -d '\r'; return $rc; }

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

min_replicas() { local v; v=$(azq containerapp show -g "$RG" -n "$1" --query properties.template.scale.minReplicas -o tsv); case "$v" in ''|None|null) echo 0 ;; *) echo "$v" ;; esac; }
replicas_of()  { local v; v=$(azq containerapp revision list -g "$RG" -n "$1" --query "sum([?properties.active].properties.replicas)" -o tsv); case "$v" in ''|None|null) echo 0 ;; *) echo "$v" ;; esac; }
active_names() { azq containerapp revision list -g "$RG" -n "$1" --query "[?properties.active].name" -o tsv; }

# --- preflight ---------------------------------------------------------------
preflight() {
  step "Checking your setup"
  command -v az >/dev/null 2>&1 || { say "ERROR: the Azure CLI ('az') is not installed. See https://aka.ms/installazurecli"; exit 2; }
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
  say "  resource group: $RG"
  [ "$DRY_RUN" = 1 ] && say "  DRY RUN: nothing will be changed."
}

# --- stopping ----------------------------------------------------------------
stop_gateway() {
  step "WSO2 gateway ($GATEWAY_APP)"
  local r any=0
  for r in $(active_names "$GATEWAY_APP"); do
    any=1; say "  deactivating revision $r"; run containerapp revision deactivate -g "$RG" -n "$GATEWAY_APP" --revision "$r"
  done
  [ $any = 0 ] && say "  no active revision - already stopped"
}

stop_apps() {
  step "Container apps (scale to zero)"
  local app
  for app in $(azq containerapp list -g "$RG" --query "[].name" -o tsv); do
    [ "$app" = "$GATEWAY_APP" ] && continue
    if [ "$(min_replicas "$app")" != 0 ]; then
      say "  - $app: setting min replicas to 0"
      run containerapp update -g "$RG" -n "$app" --min-replicas 0 --no-wait
    else
      say "  - $app: already set to scale to zero"
    fi
  done
}

# poll until every app has zero replicas; deactivate stragglers after NUDGE_MIN minutes
verify_zero() {
  step "Checking that nothing is running (up to $TIMEOUT_MIN minutes)"
  local deadline=$(( $(date +%s) + TIMEOUT_MIN * 60 ))
  local nudge_at=$(( $(date +%s) + NUDGE_MIN * 60 )) nudged=0
  local app n up
  while : ; do
    up=""
    for app in $(azq containerapp list -g "$RG" --query "[].name" -o tsv); do
      n=$(replicas_of "$app"); [ "$n" != 0 ] && up="$up $app=$n"
    done
    if [ -z "$up" ]; then say "  verified: every app has 0 replicas."; return 0; fi
    if [ "$(date +%s)" -ge "$deadline" ]; then say "  ! still running after $TIMEOUT_MIN minutes:$up"; for app in $up; do fail "${app%%=*}"; done; return 1; fi
    # an app that ignores "scale to zero" (Kafka's TCP connection can keep it awake): switch its revision off
    if [ $nudged = 0 ] && [ "$(date +%s)" -ge "$nudge_at" ]; then
      nudged=1
      say "  these did not go down by themselves:$up  -> deactivating their revisions"
      local item name r
      for item in $up; do name=${item%%=*}; for r in $(active_names "$name"); do run containerapp revision deactivate -g "$RG" -n "$name" --revision "$r"; done; done
    fi
    say "  still running:$up"
    sleep 20
  done
}

# Ask Azure to stop MySQL right away: it takes a few minutes, so it runs while the apps drain.
# (The apps are being stopped anyway; a few database errors while they wind down are harmless.)
request_mysql_stop() {
  step "MySQL server ($MYSQL)"
  if [ "$STOP_MYSQL" = 0 ]; then say "  left running (--keep-mysql). It keeps billing; stop it later with: az mysql flexible-server stop -g $RG -n $MYSQL"; return 0; fi
  local s; s=$(azq mysql flexible-server show -g "$RG" -n "$MYSQL" --query state -o tsv)
  case "$s" in
    Ready)            say "  asking Azure to stop it..."; run mysql flexible-server stop -g "$RG" -n "$MYSQL" --no-wait ;;
    Stopped|Disabled) say "  already stopped." ;;
    '')               say "  ! cannot read the state of MySQL server '$MYSQL'"; fail mysql ;;
    *)                say "  state: $s (it is changing already; will check at the end)" ;;
  esac
}

wait_mysql_stopped() {
  [ "$STOP_MYSQL" = 0 ] && return 0
  step "Checking that MySQL is stopped"
  local s i
  for i in $(seq 1 40); do        # up to ~10 minutes
    s=$(azq mysql flexible-server show -g "$RG" -n "$MYSQL" --query state -o tsv)
    case "$s" in
      Stopped|Disabled) say "  Stopped. (Azure starts it again by itself after 7 days - run this script again then.)"; return 0 ;;
      Ready)  say "  still Ready, asking again..."; run mysql flexible-server stop -g "$RG" -n "$MYSQL" --no-wait ;;
      '')     say "  ! cannot read the state of MySQL server '$MYSQL'"; fail mysql; return 1 ;;
      *)      say "  state: $s (waiting)" ;;
    esac
    sleep 15
  done
  say "  ! MySQL did not stop in time (last state: $s)"; fail mysql; return 1
}

summary() {
  step "Summary"
  local app
  for app in $(azq containerapp list -g "$RG" --query "[].name" -o tsv); do
    printf '  %-22s active replicas=%s\n' "$app" "$(replicas_of "$app")"
  done
  say "  MySQL $MYSQL: $(azq mysql flexible-server show -g "$RG" -n "$MYSQL" --query state -o tsv)"
}

# --- main --------------------------------------------------------------------
preflight
stop_apps
stop_gateway
request_mysql_stop
if [ "$DRY_RUN" = 1 ]; then
  :
elif [ "$WAIT" = 1 ]; then
  verify_zero
  wait_mysql_stopped
  summary
else
  say "  (--no-wait: stop commands sent, nothing was verified. Check the Azure portal, or run this script again.)"
fi
echo
if [ -n "$FAILED" ]; then echo "NOT FULLY STOPPED:$FAILED  - these may still be billing. See scripts/README.md, section Troubleshooting."; exit 1; fi
[ "$DRY_RUN" = 1 ] || echo "Done. Everything is stopped (compute billing has ended; storage and the registry still bill a few cents a day)."
exit 0
