#!/usr/bin/env bash
# =============================================================================
# deploy-monitoring.sh - create or update the dashboard (workbook), the email
# action group and the alert rules for the Application Insights resource.
#
#   ./scripts/deploy-monitoring.sh [--dry-run] [--emails "a@x.lk,b@x.lk"]
#
# Safe to run again: it deploys infra/monitoring/monitoring.json into the
# existing resource group and only changes what differs.
# --dry-run only validates the template and changes nothing in Azure.
# =============================================================================
set -euo pipefail

RG="${RESOURCE_GROUP:-it-helpdesk-rg}"
EMAILS="it24101500@my.sliit.lk,it24101503@my.sliit.lk,it24101497@my.sliit.lk,it24100146@my.sliit.lk"
DRY_RUN=0

while [ $# -gt 0 ]; do
  case "$1" in
    --dry-run) DRY_RUN=1 ;;
    --emails) shift; EMAILS="${1:?--emails needs a comma-separated list}" ;;
    -h|--help) sed -n '2,11p' "$0"; exit 0 ;;
    *) echo "Unknown option: $1" >&2; exit 2 ;;
  esac
  shift
done

# Git Bash on Windows rewrites arguments that look like paths (e.g. /subscriptions/...)
export MSYS_NO_PATHCONV=1

HERE="$(cd "$(dirname "$0")" && pwd)"
TEMPLATE="$HERE/../infra/monitoring/monitoring.json"
WORKBOOK="$HERE/../infra/monitoring/workbook.json"

echo "== Checking your Azure setup"
ACCOUNT="$(az account show --query user.name -o tsv)"
echo "  account: $ACCOUNT   resource group: $RG"
az group show -n "$RG" -o none
az monitor app-insights component show -a ithelpdesk-appinsights -g "$RG" -o none

# turn "a,b,c" into a JSON array for the template
EMAIL_JSON="[$(printf '%s' "$EMAILS" | sed 's/[[:space:]]//g; s/,/","/g; s/^/"/; s/$/"/')]"
echo "  alert emails: $EMAILS"

# Parameters go in a file: the workbook JSON is long and would not survive shell quoting
PARAMS="$(mktemp)"
trap 'rm -f "$PARAMS"' EXIT
# the file is read from stdin so Windows Python never has to understand a Git Bash path
JSON_QUOTE='import json,sys; print(json.dumps(sys.stdin.read()))'
WORKBOOK_STR="$(python3 -c "$JSON_QUOTE" < "$WORKBOOK" 2>/dev/null || python -c "$JSON_QUOTE" < "$WORKBOOK")"
printf '{"$schema":"https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#","contentVersion":"1.0.0.0","parameters":{"alertEmails":{"value":%s},"workbookData":{"value":%s}}}' \
  "$EMAIL_JSON" "$WORKBOOK_STR" > "$PARAMS"

# az is a Windows program under Git Bash, so the files it reads need C:/... style paths
winpath() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
TEMPLATE_ARG="$(winpath "$TEMPLATE")"
PARAMS_ARG="@$(winpath "$PARAMS")"

echo "== Validating the template"
az deployment group validate -g "$RG" --template-file "$TEMPLATE_ARG" --parameters "$PARAMS_ARG" -o none
echo "  template is valid."

if [ "$DRY_RUN" -eq 1 ]; then
  echo "Dry run: nothing was created. Run again without --dry-run to deploy."
  exit 0
fi

echo "== Deploying"
az deployment group create -g "$RG" -n ithelpdesk-monitoring --template-file "$TEMPLATE_ARG" --parameters "$PARAMS_ARG" \
  --query "properties.provisioningState" -o tsv

echo "== Result"
SUB="$(az account show --query id -o tsv)"
az rest --method get --url "https://management.azure.com/subscriptions/$SUB/resourceGroups/$RG/providers/Microsoft.Insights/scheduledQueryRules?api-version=2022-06-15" \
  --query "value[].{rule:properties.displayName, enabled:properties.enabled, every:properties.evaluationFrequency}" -o table
az monitor action-group show -g "$RG" -n ithelpdesk-alerts --query "emailReceivers[].emailAddress" -o tsv
echo "Done. Open the dashboard: Azure portal > ithelpdesk-appinsights > Workbooks > IT Helpdesk health."
