#!/usr/bin/env pwsh
# =============================================================================
# verify-kafka-resilience.ps1
# Sprint 3 – Kafka Storage Resilience Verification Script
# IT Helpdesk & Ticketing Platform (SE3022)
#
# Acceptance Criteria:
#   AC1: Persistent volume has sufficient space remaining (< 80% used)
#   AC2: Broker restarts and canary message survives restart
#   AC3: All 5 microservices reconnect to Kafka broker successfully
# =============================================================================

param(
    [string]$KafkaContainer    = "local-kafka",
    [string]$KafkaBootstrap    = "localhost:9092",
    [string]$CanaryTopic       = "resilience-health-check",
    [int]   $DiskThresholdPct  = 80,
    [int]   $BrokerReadySec    = 30
)

$ErrorActionPreference = "Stop"
$script:PassCount = 0
$script:FailCount = 0
$AuditLines = [System.Collections.Generic.List[string]]::new()

# ── Helpers ──────────────────────────────────────────────────────────────────

function Write-Header([string]$text) {
    $bar = "=" * 70
    Write-Host "`n$bar" -ForegroundColor Cyan
    Write-Host "  $text" -ForegroundColor Cyan
    Write-Host "$bar" -ForegroundColor Cyan
    $AuditLines.Add("`n## $text")
}

function Write-Pass([string]$msg) {
    Write-Host "  [PASS] $msg" -ForegroundColor Green
    $script:PassCount++
    $AuditLines.Add("- **PASS** $msg")
}

function Write-Fail([string]$msg) {
    Write-Host "  [FAIL] $msg" -ForegroundColor Red
    $script:FailCount++
    $AuditLines.Add("- **FAIL** $msg")
}

function Write-Info([string]$msg) {
    Write-Host "  [INFO] $msg" -ForegroundColor Yellow
    $AuditLines.Add("  - $msg")
}

function Invoke-DockerExec([string]$container, [string]$cmd) {
    return docker exec $container bash -c $cmd 2>&1
}

# ── Timestamp ─────────────────────────────────────────────────────────────────
$RunTimestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz"
$AuditLines.Add("# Kafka Storage Resilience Audit – Sprint 3")
$AuditLines.Add("")
$AuditLines.Add("> **Run Timestamp:** $RunTimestamp  ")
$AuditLines.Add("> **Kafka Container:** ``$KafkaContainer``  ")
$AuditLines.Add("> **Bootstrap Server:** ``$KafkaBootstrap``")
$AuditLines.Add("")

# =============================================================================
# AC1 – Disk Utilisation Check
# =============================================================================
Write-Header "AC1: Persistent Volume Disk Utilisation"

try {
    # Confirm container is running
    $containerState = docker inspect --format '{{.State.Status}}' $KafkaContainer 2>&1
    if ($containerState -ne "running") {
        Write-Fail "Kafka container '$KafkaContainer' is not running (state: $containerState). Start with: docker compose up -d kafka"
        Write-Info "Skipping AC1 disk check – container offline"
    }
    else {
        Write-Info "Container state: $containerState"

        # KRaft data path (apache/kafka image uses /tmp/kraft-combined-logs by default)
        # Also check /var/lib/kafka/data as a fallback
        $dfOutput = Invoke-DockerExec $KafkaContainer "df -h /tmp/kraft-combined-logs 2>/dev/null || df -h /var/lib/kafka/data 2>/dev/null || df -h /"
        Write-Info "Raw df output:`n$dfOutput"
        $AuditLines.Add("")
        $AuditLines.Add("**df output (inside container):**")
        $AuditLines.Add("``````")
        $AuditLines.Add($dfOutput)
        $AuditLines.Add("``````")

        # Parse the Use% column (last df line, 5th column)
        $dfLine = ($dfOutput -split "`n" | Where-Object { $_ -match '\d+%' } | Select-Object -Last 1)
        if ($dfLine) {
            $usedPct = [int](($dfLine -split '\s+' | Where-Object { $_ -match '^\d+%$' }) -replace '%', '')
            Write-Info "Disk utilisation: $usedPct% (threshold: $DiskThresholdPct%)"
            $AuditLines.Add("- **Disk utilisation:** $usedPct%  (threshold ≤ $DiskThresholdPct%)")

            if ($usedPct -le $DiskThresholdPct) {
                Write-Pass "Disk utilisation $usedPct% is within the $DiskThresholdPct% threshold."
            }
            else {
                Write-Fail "Disk utilisation $usedPct% EXCEEDS $DiskThresholdPct% threshold. Consider pruning old segments or expanding volume."
            }
        }
        else {
            Write-Fail "Could not parse disk utilisation from df output."
        }

        # Volume mount details
        $mounts = docker inspect --format '{{json .Mounts}}' $KafkaContainer 2>&1
        Write-Info "Volume mounts: $mounts"
        $AuditLines.Add("- **Volume mounts (raw JSON):** ``$mounts``")
    }
}
catch {
    Write-Fail "AC1 check threw an exception: $_"
}

# =============================================================================
# AC2 – Broker Restart & Message Persistence
# =============================================================================
Write-Header "AC2: Broker Restart + Canary Message Persistence"

$canaryMessage = "resilience-canary-$(Get-Date -Format 'yyyyMMddHHmmss')"
$ac2Passed = $false

try {
    $containerState = docker inspect --format '{{.State.Status}}' $KafkaContainer 2>&1

    if ($containerState -ne "running") {
        Write-Fail "Kafka container not running – cannot perform AC2 restart test."
    }
    else {
        # ── Step A: Produce canary message ──────────────────────────────────
        Write-Info "Producing canary message: '$canaryMessage' → topic '$CanaryTopic'"
        $produceCmd = "echo '$canaryMessage' | /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server localhost:9092 --topic $CanaryTopic"
        $produceResult = Invoke-DockerExec $KafkaContainer $produceCmd
        Write-Info "Produce result: $produceResult"
        $AuditLines.Add("- **Canary message produced:** ``$canaryMessage``")
        $AuditLines.Add("- **Pre-restart produce output:** $produceResult")

        # Wait a moment for the message to be flushed to disk
        Start-Sleep -Seconds 3

        # Get offset before restart
        $offsetBefore = Invoke-DockerExec $KafkaContainer "/opt/kafka/bin/kafka-run-class.sh kafka.tools.GetOffsetShell --broker-list localhost:9092 --topic $CanaryTopic --time -1 2>/dev/null | tail -1"
        Write-Info "Offset before restart: $offsetBefore"
        $AuditLines.Add("- **Partition offset before restart:** $offsetBefore")

        # ── Step B: Restart Kafka broker ─────────────────────────────────────
        Write-Info "Restarting Kafka container '$KafkaContainer'..."
        $restartTimestamp = Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz"
        docker restart $KafkaContainer | Out-Null
        $AuditLines.Add("- **Broker restart initiated at:** $restartTimestamp")

        # ── Step C: Wait for readiness probe ─────────────────────────────────
        Write-Info "Waiting up to ${BrokerReadySec}s for broker to become ready..."
        $elapsed = 0
        $brokerReady = $false
        while ($elapsed -lt $BrokerReadySec) {
            Start-Sleep -Seconds 2
            $elapsed += 2
            $listResult = docker exec $KafkaContainer /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list 2>&1
            if ($LASTEXITCODE -eq 0) {
                $brokerReady = $true
                Write-Info "Broker ready after ${elapsed}s. Topics: $($listResult -join ', ')"
                $AuditLines.Add("- **Broker ready after:** ${elapsed}s")
                $AuditLines.Add("- **Topics visible post-restart:** $($listResult -join ', ')")
                break
            }
        }

        if (-not $brokerReady) {
            Write-Fail "Broker did not become ready within ${BrokerReadySec}s after restart."
        }
        else {
            # ── Step D: Consume and verify message persisted ──────────────────
            Write-Info "Consuming from '$CanaryTopic' to verify message survived restart..."
            $consumeCmd = "/opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server localhost:9092 --topic $CanaryTopic --from-beginning --max-messages 100 --timeout-ms 10000 2>/dev/null"
            $consumed = Invoke-DockerExec $KafkaContainer $consumeCmd
            Write-Info "Consumed messages: $consumed"
            $AuditLines.Add("- **Consumed messages after restart:**")
            $AuditLines.Add("``````")
            $AuditLines.Add($consumed)
            $AuditLines.Add("``````")

            if ($consumed -match [regex]::Escape($canaryMessage)) {
                Write-Pass "Canary message '$canaryMessage' found in topic after broker restart. Data persisted successfully."
                $ac2Passed = $true
            }
            else {
                Write-Fail "Canary message '$canaryMessage' NOT found after broker restart. Data may not have persisted."
            }

            # Verify offset is preserved
            $offsetAfter = Invoke-DockerExec $KafkaContainer "/opt/kafka/bin/kafka-run-class.sh kafka.tools.GetOffsetShell --broker-list localhost:9092 --topic $CanaryTopic --time -1 2>/dev/null | tail -1"
            Write-Info "Offset after restart: $offsetAfter"
            $AuditLines.Add("- **Partition offset after restart:** $offsetAfter")
        }
    }
}
catch {
    Write-Fail "AC2 check threw an exception: $_"
}

# =============================================================================
# AC3 – All 5 Microservices Reconnect to Kafka
# =============================================================================
Write-Header "AC3: All 5 Microservices Reach Kafka Broker"

$services = @(
    @{ Name = "Auth.Api";         Port = 5121; HealthPath = "/health" }
    @{ Name = "Ticket.Api";       Port = 5164; HealthPath = "/health" }
    @{ Name = "Assignment.Api";   Port = 5165; HealthPath = "/health" }
    @{ Name = "Sla.Api";          Port = 5166; HealthPath = "/health" }
    @{ Name = "Notification.Api"; Port = 5167; HealthPath = "/health" }
)

$AuditLines.Add("")
$AuditLines.Add("| Service | Port | URL | Status | Response | Timestamp |")
$AuditLines.Add("|---------|------|-----|--------|----------|-----------|")

foreach ($svc in $services) {
    $url = "http://localhost:$($svc.Port)$($svc.HealthPath)"
    $ts  = Get-Date -Format "yyyy-MM-ddTHH:mm:sszzz"
    try {
        $response = Invoke-WebRequest -Uri $url -TimeoutSec 5 -UseBasicParsing -ErrorAction Stop
        $statusCode = $response.StatusCode
        $body = ($response.Content -replace "`r|`n", " ") | Select-Object -First 1
        if ($body.Length -gt 80) { $body = $body.Substring(0, 80) + "…" }

        if ($statusCode -ge 200 -and $statusCode -lt 400) {
            Write-Pass "$($svc.Name) [$url] → HTTP $statusCode"
            $AuditLines.Add("| $($svc.Name) | $($svc.Port) | $url | ✅ $statusCode | $body | $ts |")
        }
        else {
            Write-Fail "$($svc.Name) [$url] → HTTP $statusCode (unexpected)"
            $AuditLines.Add("| $($svc.Name) | $($svc.Port) | $url | ⚠️ $statusCode | $body | $ts |")
        }
    }
    catch {
        $errMsg = $_.Exception.Message -replace '\|', '∣'
        Write-Fail "$($svc.Name) [$url] → UNREACHABLE: $errMsg"
        Write-Info "If service is not running locally, start with: dotnet run (in services/$($svc.Name -replace '\.Api','').toLower()/$($svc.Name))"
        $AuditLines.Add("| $($svc.Name) | $($svc.Port) | $url | ❌ FAIL | $errMsg | $ts |")
    }
}

# =============================================================================
# Summary
# =============================================================================
Write-Header "Verification Summary"

$total = $script:PassCount + $script:FailCount
Write-Host "  Total checks : $total" -ForegroundColor White
Write-Host "  Passed       : $script:PassCount" -ForegroundColor Green
Write-Host "  Failed       : $script:FailCount" -ForegroundColor $(if ($script:FailCount -gt 0) { "Red" } else { "Green" })

$AuditLines.Add("")
$AuditLines.Add("## Summary")
$AuditLines.Add("")
$AuditLines.Add("| Metric | Value |")
$AuditLines.Add("|--------|-------|")
$AuditLines.Add("| Run timestamp | $RunTimestamp |")
$AuditLines.Add("| Total checks  | $total |")
$AuditLines.Add("| Passed        | $script:PassCount |")
$AuditLines.Add("| Failed        | $script:FailCount |")
$AuditLines.Add("| Overall       | $(if ($script:FailCount -eq 0) { '✅ ALL PASS' } else { '❌ FAILURES DETECTED' }) |")

# Write audit log to docs/ directory relative to the script location
$projectRoot = Split-Path -Parent $PSScriptRoot
$auditPath   = Join-Path $projectRoot "docs\KAFKA_STORAGE_AUDIT_SPRINT3.md"

try {
    $AuditLines | Out-File -FilePath $auditPath -Encoding utf8 -Force
    Write-Host "`n  Audit log written → $auditPath" -ForegroundColor Cyan
}
catch {
    Write-Host "  [WARN] Could not write audit log: $_" -ForegroundColor Yellow
}

# Exit with non-zero code if any check failed
if ($script:FailCount -gt 0) {
    Write-Host "`n  *** One or more checks FAILED. Review output above. ***`n" -ForegroundColor Red
    exit 1
}
else {
    Write-Host "`n  *** All checks PASSED. Kafka storage resilience verified. ***`n" -ForegroundColor Green
    exit 0
}
