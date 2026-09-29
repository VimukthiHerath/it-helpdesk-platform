#!/usr/bin/env bash
# =============================================================================
# verify-kafka-resilience.sh
# Sprint 3 – Kafka Storage Resilience Verification (Bash)
# IT Helpdesk & Ticketing Platform (SE3022)
#
# Acceptance Criteria:
#   AC1: Persistent volume disk usage ≤ 80%
#   AC2: Canary message survives broker restart
#   AC3: All 5 microservices reachable after restart
#
# Usage:
#   bash scripts/verify-kafka-resilience.sh
#   KAFKA_CONTAINER=local-kafka bash scripts/verify-kafka-resilience.sh
# =============================================================================

set -euo pipefail

KAFKA_CONTAINER="${KAFKA_CONTAINER:-local-kafka}"
KAFKA_BOOTSTRAP="${KAFKA_BOOTSTRAP:-localhost:9092}"
CANARY_TOPIC="storage-resilience-audit"
DISK_THRESHOLD="${DISK_THRESHOLD:-80}"
BROKER_READY_SEC="${BROKER_READY_SEC:-60}"

PASS=0
FAIL=0
AUDIT_FILE="docs/KAFKA_STORAGE_AUDIT_SPRINT3.md"
RUN_TS=$(date -u '+%Y-%m-%dT%H:%M:%SZ')

# ── Colour helpers ─────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'

header()   { echo -e "\n${CYAN}$(printf '=%.0s' {1..68})${NC}"; echo -e "${CYAN}  $1${NC}"; echo -e "${CYAN}$(printf '=%.0s' {1..68})${NC}"; }
pass_msg() { echo -e "  ${GREEN}[PASS]${NC} $1"; PASS=$((PASS+1)); }
fail_msg() { echo -e "  ${RED}[FAIL]${NC} $1"; FAIL=$((FAIL+1)); }
info_msg() { echo -e "  ${YELLOW}[INFO]${NC} $1"; }

# ── Audit log buffer (array of lines) ─────────────────────────────────────────
AUDIT=()
audit() { AUDIT+=("$1"); }

audit "# Kafka Storage Resilience Audit – Sprint 3 (Bash Run)"
audit ""
audit "> **Run Timestamp:** $RUN_TS  "
audit "> **Kafka Container:** \`$KAFKA_CONTAINER\`  "
audit "> **Bootstrap Server:** \`$KAFKA_BOOTSTRAP\`"
audit ""

# =============================================================================
# AC1 – Disk Utilisation
# =============================================================================
header "AC1: Persistent Volume Disk Utilisation"
audit "## AC1: Disk Utilisation"
audit ""

CONTAINER_STATE=$(docker inspect --format '{{.State.Status}}' "$KAFKA_CONTAINER" 2>/dev/null || echo "not-found")
if [ "$CONTAINER_STATE" != "running" ]; then
    fail_msg "Container '$KAFKA_CONTAINER' not running (state: $CONTAINER_STATE). Start with: docker compose up -d kafka"
    audit "- **FAIL** Container not running – skipping disk check."
else
    info_msg "Container state: $CONTAINER_STATE"

    # Try KRaft path, fall back to /var/lib/kafka/data, fall back to root
    DF_OUT=$(docker exec "$KAFKA_CONTAINER" bash -c \
        "df -h /tmp/kraft-combined-logs 2>/dev/null || df -h /var/lib/kafka/data 2>/dev/null || df -h /" 2>&1)

    info_msg "df output:\n$DF_OUT"
    audit "**df output (inside container):**"
    audit '```'
    audit "$DF_OUT"
    audit '```'

    # Parse Use% column from last matching line
    USE_PCT=$(echo "$DF_OUT" | grep -E '[0-9]+%' | tail -1 | awk '{print $5}' | tr -d '%' || echo "")
    if [ -z "$USE_PCT" ]; then
        fail_msg "Could not parse disk utilisation from df output."
        audit "- **FAIL** Could not parse disk utilisation."
    else
        info_msg "Disk utilisation: ${USE_PCT}% (threshold: ${DISK_THRESHOLD}%)"
        audit "- **Disk utilisation:** ${USE_PCT}%  (threshold ≤ ${DISK_THRESHOLD}%)"
        if [ "$USE_PCT" -le "$DISK_THRESHOLD" ]; then
            pass_msg "Disk utilisation ${USE_PCT}% is within the ${DISK_THRESHOLD}% threshold."
            audit "- **PASS** ${USE_PCT}% ≤ ${DISK_THRESHOLD}% threshold."
        else
            fail_msg "Disk utilisation ${USE_PCT}% EXCEEDS ${DISK_THRESHOLD}% threshold!"
            audit "- **FAIL** ${USE_PCT}% > ${DISK_THRESHOLD}% threshold — prune old log segments."
        fi
    fi

    # Mount details
    MOUNTS=$(docker inspect --format '{{json .Mounts}}' "$KAFKA_CONTAINER" 2>/dev/null)
    info_msg "Volume mounts: $MOUNTS"
    audit "- **Volume mounts (JSON):** \`$MOUNTS\`"
fi

# =============================================================================
# AC2 – Broker Restart & Canary Persistence
# =============================================================================
header "AC2: Broker Restart + Canary Message Persistence"
audit ""
audit "## AC2: Broker Restart & Canary Persistence"
audit ""

CANARY_MSG="resilience-canary-$(date -u '+%Y%m%d%H%M%S')"
AC2_PASSED=false

CONTAINER_STATE=$(docker inspect --format '{{.State.Status}}' "$KAFKA_CONTAINER" 2>/dev/null || echo "not-found")
if [ "$CONTAINER_STATE" != "running" ]; then
    fail_msg "Container not running – cannot perform AC2 restart test."
    audit "- **FAIL** Container not running."
else
    # Step A: Produce canary
    info_msg "Producing canary: '$CANARY_MSG' → topic '$CANARY_TOPIC'"
    PRODUCE_RESULT=$(docker exec "$KAFKA_CONTAINER" bash -c \
        "echo '$CANARY_MSG' | /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic $CANARY_TOPIC" 2>&1 || true)
    info_msg "Produce result: $PRODUCE_RESULT"
    audit "- **Canary message:** \`$CANARY_MSG\`"
    audit "- **Produce output:** $PRODUCE_RESULT"

    sleep 3

    # Offset before restart
    OFFSET_BEFORE=$(docker exec "$KAFKA_CONTAINER" bash -c \
        "/opt/kafka/bin/kafka-run-class.sh kafka.tools.GetOffsetShell --broker-list localhost:9092 --topic $CANARY_TOPIC --time -1 2>/dev/null | tail -1" 2>/dev/null || echo "N/A")
    info_msg "Offset before restart: $OFFSET_BEFORE"
    audit "- **Offset before restart:** $OFFSET_BEFORE"

    # Step B: Restart broker
    RESTART_TS=$(date -u '+%Y-%m-%dT%H:%M:%SZ')
    info_msg "Restarting Kafka container '$KAFKA_CONTAINER'..."
    docker restart "$KAFKA_CONTAINER" > /dev/null
    audit "- **Restart initiated:** $RESTART_TS"

    # Step C: Wait for readiness
    info_msg "Polling for broker readiness (max ${BROKER_READY_SEC}s)..."
    ELAPSED=0
    BROKER_READY=false
    while [ "$ELAPSED" -lt "$BROKER_READY_SEC" ]; do
        sleep 2; ELAPSED=$((ELAPSED+2))
        if docker exec "$KAFKA_CONTAINER" /opt/kafka/bin/kafka-topics.sh \
                --bootstrap-server localhost:9092 --list > /dev/null 2>&1; then
            BROKER_READY=true
            info_msg "Broker ready after ${ELAPSED}s."
            audit "- **Broker ready after:** ${ELAPSED}s"
            break
        fi
    done

    if [ "$BROKER_READY" = false ]; then
        fail_msg "Broker did not become ready within ${BROKER_READY_SEC}s after restart."
        audit "- **FAIL** Broker did not become ready in time."
    else
        # Step D: Consume and verify
        info_msg "Consuming from '$CANARY_TOPIC' to verify persistence..."
        CONSUMED=$(docker exec "$KAFKA_CONTAINER" bash -c \
            "/opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 \
             --topic $CANARY_TOPIC --from-beginning --max-messages 200 \
             --timeout-ms 12000 2>/dev/null" 2>/dev/null || true)
        audit "- **Consumed messages:**"
        audit '```'
        audit "$CONSUMED"
        audit '```'

        if echo "$CONSUMED" | grep -qF "$CANARY_MSG"; then
            pass_msg "Canary '$CANARY_MSG' found after restart – data persisted!"
            audit "- **PASS** Canary message found post-restart."
            AC2_PASSED=true
        else
            fail_msg "Canary '$CANARY_MSG' NOT found after restart – data may not have persisted."
            audit "- **FAIL** Canary message not found post-restart."
        fi

        OFFSET_AFTER=$(docker exec "$KAFKA_CONTAINER" bash -c \
            "/opt/kafka/bin/kafka-run-class.sh kafka.tools.GetOffsetShell --broker-list localhost:9092 --topic $CANARY_TOPIC --time -1 2>/dev/null | tail -1" 2>/dev/null || echo "N/A")
        info_msg "Offset after restart: $OFFSET_AFTER"
        audit "- **Offset after restart:** $OFFSET_AFTER"
    fi
fi

# =============================================================================
# AC3 – Microservice Health Checks
# =============================================================================
header "AC3: All 5 Microservices Reach Kafka Broker"
audit ""
audit "## AC3: Microservice Reconnection Health Checks"
audit ""
audit "| Service | Port | URL | Status | Response | Timestamp |"
audit "|---------|------|-----|--------|----------|-----------|"

declare -A SERVICES=(
    ["Auth.Api"]="5121"
    ["Ticket.Api"]="5164"
    ["Assignment.Api"]="5165"
    ["Sla.Api"]="5166"
    ["Notification.Api"]="5167"
)

for SVC in "Auth.Api:5121" "Ticket.Api:5164" "Assignment.Api:5165" "Sla.Api:5166" "Notification.Api:5167"; do
    NAME="${SVC%%:*}"
    PORT="${SVC##*:}"
    URL="http://localhost:${PORT}/health"
    TS=$(date -u '+%Y-%m-%dT%H:%M:%SZ')

    HTTP_CODE=$(curl -s -o /tmp/svc_resp.txt -w "%{http_code}" \
        --max-time 5 "$URL" 2>/dev/null || echo "000")
    BODY=$(cat /tmp/svc_resp.txt 2>/dev/null | tr -d '\n' | cut -c1-80 || echo "")

    if echo "$HTTP_CODE" | grep -qE '^[23]'; then
        pass_msg "$NAME [$URL] → HTTP $HTTP_CODE"
        audit "| $NAME | $PORT | $URL | ✅ $HTTP_CODE | $BODY | $TS |"
    else
        fail_msg "$NAME [$URL] → HTTP $HTTP_CODE (unreachable or unexpected)"
        info_msg "Start with: dotnet run --project services/${NAME,,}/${NAME}"
        audit "| $NAME | $PORT | $URL | ❌ $HTTP_CODE | $BODY | $TS |"
    fi
done

# =============================================================================
# Summary
# =============================================================================
header "Verification Summary"
TOTAL=$((PASS+FAIL))
echo -e "  Total  : $TOTAL"
echo -e "  ${GREEN}Passed : $PASS${NC}"
if [ "$FAIL" -gt 0 ]; then
    echo -e "  ${RED}Failed : $FAIL${NC}"
else
    echo -e "  Failed : $FAIL"
fi

audit ""
audit "## Summary"
audit ""
audit "| Metric | Value |"
audit "|--------|-------|"
audit "| Run timestamp | $RUN_TS |"
audit "| Total checks  | $TOTAL |"
audit "| Passed        | $PASS |"
audit "| Failed        | $FAIL |"
OVERALL=$([ "$FAIL" -eq 0 ] && echo "✅ ALL PASS" || echo "❌ FAILURES DETECTED")
audit "| Overall       | $OVERALL |"

# Write audit log
printf '%s\n' "${AUDIT[@]}" > "$AUDIT_FILE"
echo -e "\n  ${CYAN}Audit log written → $AUDIT_FILE${NC}"

[ "$FAIL" -gt 0 ] && { echo -e "\n${RED}*** $FAIL check(s) FAILED ***${NC}\n"; exit 1; }
echo -e "\n${GREEN}*** All $PASS checks PASSED – Kafka storage resilience verified ***${NC}\n"
exit 0
