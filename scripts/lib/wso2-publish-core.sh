#!/bin/sh
# =============================================================================
# wso2-publish-core.sh - publish the five IT Helpdesk APIs into WSO2 API Manager
#
# You normally do NOT run this file yourself. scripts/publish-wso2-apis.sh (or .ps1)
# copies it, together with the API files, INTO the WSO2 container and runs it there,
# on the local Docker container and on Azure alike. Inside the container WSO2's
# management API is simply https://localhost:9443, curl is installed, and nothing
# else is needed (no jq, no Python).
#
# Usage:   sh wso2-publish-core.sh <dir> [admin-user] [admin-password]
#
# <dir> must contain, for each of  auth ticket assignment sla notification :
#   <name>.spec.json    the OpenAPI file (infra/wso2/apis/<name>-api.json)
#   <name>.props.json   name, context, version, policy and the backend URL
#
# What it does:
#   1. waits until WSO2's Publisher API answers
#   2. gets an OAuth token (registers a client first)
#   3. deletes any existing copy of these five APIs (so it can be run again)
#   4. imports each API, publishes it, creates a revision and deploys it
#      (published is not the same as live: only a deployed revision serves traffic)
#
# It prints one line per API and ends with   RESULT: OK   or   RESULT: FAILED <n>
# =============================================================================
DIR="${1:-}"
U="${2:-admin}"
P="${3:-admin}"
H="https://localhost:9443"
FAILED=0

say() { printf '%s\n' "$*"; }
# value of the first "<key>":"<value>" in the text on stdin
V() { grep -o "\"$1\":\"[^\"]*\"" | head -n 1 | cut -d'"' -f4; }

if [ -z "$DIR" ] || [ ! -d "$DIR" ]; then
  say "usage: sh wso2-publish-core.sh <dir> [admin-user] [admin-password]"
  say "RESULT: FAILED 1"
  exit 2
fi

# --- 1. wait for WSO2 --------------------------------------------------------
i=0
while : ; do
  code=$(curl -sk -o /dev/null -w '%{http_code}' -u "$U:$P" "$H/api/am/publisher/v4/apis?limit=1")
  case "$code" in
    200) break ;;
    401) say "ERROR: WSO2 rejected the login '$U'. If the admin password was changed, pass it as the 3rd argument."
         say "RESULT: FAILED 1"; exit 3 ;;
  esac
  i=$((i + 1))
  if [ "$i" -ge 60 ]; then say "ERROR: WSO2 did not become ready in 5 minutes (last HTTP $code)."; say "RESULT: FAILED 1"; exit 2; fi
  say "waiting for WSO2 to be ready (HTTP $code)..."
  sleep 5
done
say "WSO2 is ready."

# --- 2. token ----------------------------------------------------------------
printf '{"callbackUrl":"http://localhost","clientName":"publish-wso2-apis","owner":"%s","grantType":"client_credentials password refresh_token","saasApp":true}' "$U" > "$DIR/dcr.json"
R=$(curl -sk -u "$U:$P" -H 'Content-Type: application/json' -d @"$DIR/dcr.json" "$H/client-registration/v0.17/register")
CID=$(printf '%s' "$R" | V clientId)
CS=$(printf '%s' "$R" | V clientSecret)
if [ -z "$CID" ]; then say "ERROR: could not register an OAuth client: $(printf '%s' "$R" | cut -c1-200)"; say "RESULT: FAILED 1"; exit 3; fi
T=$(curl -sk -u "$CID:$CS" -d "grant_type=password&username=$U&password=$P&scope=apim:api_create%20apim:api_publish%20apim:api_view%20apim:api_delete" "$H/oauth2/token" | V access_token)
if [ -z "$T" ]; then say "ERROR: could not get an access token."; say "RESULT: FAILED 1"; exit 3; fi

# --- 3. remove the old copies of these five APIs -------------------------------
say "Removing existing copies of the five APIs (if any)..."
curl -sk --oauth2-bearer "$T" "$H/api/am/publisher/v4/apis?limit=200" | grep -o '"id":"[0-9a-f-]\{36\}","name":"[^"]*"' > "$DIR/existing.txt"
while IFS= read -r line; do
  id=$(printf '%s' "$line" | cut -d'"' -f4)
  name=$(printf '%s' "$line" | cut -d'"' -f8)
  case "$name" in
    "Auth API"|"Ticket API"|"Assignment API"|"SLA API"|"Notification API")
      c=$(curl -sk -o /dev/null -w '%{http_code}' -X DELETE --oauth2-bearer "$T" "$H/api/am/publisher/v4/apis/$id")
      say "  deleted $name ($id) -> HTTP $c"
      ;;
  esac
done < "$DIR/existing.txt"

# --- 4. import, publish, deploy --------------------------------------------------
printf '[{"name":"Default","vhost":"localhost","displayOnDevportal":true}]' > "$DIR/deploy.json"
for n in auth ticket assignment sla notification; do
  if [ ! -f "$DIR/$n.spec.json" ] || [ ! -f "$DIR/$n.props.json" ]; then say "FAIL  $n: $n.spec.json or $n.props.json is missing"; FAILED=$((FAILED + 1)); continue; fi
  title=$(V name < "$DIR/$n.props.json")
  ctx=$(V context < "$DIR/$n.props.json")
  R=$(curl -sk --oauth2-bearer "$T" -F "file=@$DIR/$n.spec.json;type=application/json" -F "additionalProperties=<$DIR/$n.props.json;type=application/json" "$H/api/am/publisher/v4/apis/import-openapi")
  ID=$(printf '%s' "$R" | V id)
  if [ -z "$ID" ]; then say "FAIL  $title: import failed: $(printf '%s' "$R" | cut -c1-250)"; FAILED=$((FAILED + 1)); continue; fi
  state=$(curl -sk -X POST --oauth2-bearer "$T" "$H/api/am/publisher/v4/apis/change-lifecycle?action=Publish&apiId=$ID" | grep -o '"state":"[^"]*"' | head -n 1 | cut -d'"' -f4)
  REV=$(curl -sk -X POST --oauth2-bearer "$T" -H 'Content-Type: application/json' -d '{"description":"published by publish-wso2-apis"}' "$H/api/am/publisher/v4/apis/$ID/revisions" | V id)
  dep="000"
  if [ -n "$REV" ]; then
    dep=$(curl -sk -o /dev/null -w '%{http_code}' -X POST --oauth2-bearer "$T" -H 'Content-Type: application/json' -d @"$DIR/deploy.json" "$H/api/am/publisher/v4/apis/$ID/deploy-revision?revisionId=$REV")
  fi
  case "$dep" in
    200|201) say "OK    $title  ${ctx}/v1  (lifecycle: ${state:-?}, deploy: HTTP $dep)" ;;
    *)       say "FAIL  $title: deploy returned HTTP $dep (lifecycle: ${state:-?})"; FAILED=$((FAILED + 1)) ;;
  esac
done

rm -f "$DIR/dcr.json" "$DIR/existing.txt" "$DIR/deploy.json"
if [ "$FAILED" -eq 0 ]; then say "RESULT: OK"; exit 0; fi
say "RESULT: FAILED $FAILED"
exit 1
