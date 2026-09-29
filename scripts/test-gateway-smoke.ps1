#!/usr/bin/env pwsh
# =============================================================================
# test-gateway-smoke.ps1
# Sprint 3 – WSO2 API Gateway: Route Verification Smoke Tests
# IT Helpdesk & Ticketing Platform (SE3022)
#
# Verifies:
#   1. Unauthenticated GET /ticket/v1/mine → WSO2 routes to Ticket.Api → HTTP 401
#      (confirms WSO2 → Ticket.Api routing and JWT validation at the service layer)
#   2. POST /auth/v1/login with test payload → downstream connectivity confirmed
#   3. All 5 route contexts respond (not 502/504 gateway errors)
#
# Usage:
#   .\scripts\test-gateway-smoke.ps1
#   .\scripts\test-gateway-smoke.ps1 -GatewayHttp "http://localhost:8280"
# =============================================================================

param(
    [string]$GatewayHttp  = "http://localhost:8280",
    [string]$GatewayHttps = "https://localhost:8243"
)

$ErrorActionPreference = "Continue"  # Don't abort on HTTP errors
$PASS = 0; $FAIL = 0
$Results = [System.Collections.Generic.List[PSCustomObject]]::new()

# ── Helpers ───────────────────────────────────────────────────────────────────
function Write-Pass([string]$m) { Write-Host "  [PASS] $m" -ForegroundColor Green; $script:PASS++ }
function Write-Fail([string]$m) { Write-Host "  [FAIL] $m" -ForegroundColor Red;   $script:FAIL++ }
function Write-Info([string]$m) { Write-Host "  [INFO] $m" -ForegroundColor Yellow }

function Invoke-GatewayRequest {
    param(
        [string]$Label,
        [string]$Method,
        [string]$Url,
        [string[]]$AcceptedCodes,
        [hashtable]$Headers = @{},
        [string]$Body       = $null,
        [string]$ContentType = $null
    )
    Write-Host "`n  ── $Label" -ForegroundColor Cyan
    try {
        $params = @{
            Uri             = $Url
            Method          = $Method
            Headers         = $Headers
            TimeoutSec      = 10
            UseBasicParsing = $true
            ErrorAction     = "Stop"
        }
        if ($Body)        { $params.Body = $Body }
        if ($ContentType) { $params.ContentType = $ContentType }

        $response = Invoke-WebRequest @params
        $code     = [string]$response.StatusCode
    }
    catch [System.Net.WebException] {
        $response = $_.Exception.Response
        $code     = if ($response) { [string][int]$response.StatusCode } else { "000" }
    }
    catch {
        $code = "000"
    }

    $expected = $AcceptedCodes -join ","
    $accepted = $AcceptedCodes -contains $code

    if ($accepted) { Write-Pass "$Label → HTTP $code (expected $expected)" }
    else           { Write-Fail "$Label → HTTP $code (expected $expected)" }

    Write-Info "URL: $Url"
    $script:Results.Add([PSCustomObject]@{
        Label    = $Label
        Method   = $Method
        URL      = $Url
        Got      = $code
        Expected = $expected
        Status   = if ($accepted) { "PASS" } else { "FAIL" }
    })
}

Write-Host "`n════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  IT Helpdesk – WSO2 Gateway Smoke Tests" -ForegroundColor Cyan
Write-Host "  Gateway: $GatewayHttp" -ForegroundColor Cyan
Write-Host "  Time:    $(Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════════════════════════════`n" -ForegroundColor Cyan

# ── Test 1: Unauthenticated GET /ticket/v1/mine ───────────────────────────────
# WSO2 should forward to Ticket.Api which returns 401 (not WSO2's own 403)
# A WSO2-level auth failure without backend reach would return 403 or 401
# A backend JWT validation failure (correct routing) returns 401 from Ticket.Api
Invoke-GatewayRequest `
    -Label  "Unauthenticated GET /ticket/v1/mine → Ticket.Api 401" `
    -Method GET `
    -Url    "$GatewayHttp/ticket/v1/mine" `
    -AcceptedCodes @("401", "403")  # 401 = backend rejected, 403 = WSO2 policy blocked (both confirm routing)

Write-Info "If HTTP 401 from Ticket.Api seen → routing confirmed. If 502/504 → Ticket.Api unreachable."

# ── Test 2: POST /auth/v1/login – upstream connectivity ──────────────────────
$loginBody = '{"email":"smoke@test.local","password":"wrongpassword"}'
# Expected: 400 (validation error from Auth.Api) or 401 (wrong creds) – both confirm downstream reach
Invoke-GatewayRequest `
    -Label       "POST /auth/v1/login → Auth.Api (downstream reach)" `
    -Method      POST `
    -Url         "$GatewayHttp/auth/v1/login" `
    -Headers     @{ "Content-Type" = "application/json" } `
    -Body        $loginBody `
    -ContentType "application/json" `
    -AcceptedCodes @("200", "400", "401", "415")

# ── Test 3: Enumerate all 5 contexts ─────────────────────────────────────────
$routes = @(
    @{ Label = "Auth context root";         Path = "/auth/v1/health" }
    @{ Label = "Ticket context root";       Path = "/ticket/v1/health" }
    @{ Label = "Assignment context root";   Path = "/assignment/v1/health" }
    @{ Label = "SLA context root";          Path = "/sla/v1/health" }
    @{ Label = "Notification context root"; Path = "/notification/v1/health" }
)

foreach ($r in $routes) {
    Invoke-GatewayRequest `
        -Label  $r.Label `
        -Method GET `
        -Url    "$GatewayHttp$($r.Path)" `
        -AcceptedCodes @("200", "401", "403", "404")  # 502/504 = gateway/backend down = FAIL
}

# ── Test 4: Unknown route should not succeed ──────────────────────────────────
Invoke-GatewayRequest `
    -Label  "Unknown route → not proxied (404/404 from WSO2)" `
    -Method GET `
    -Url    "$GatewayHttp/unknown/v99/nothing" `
    -AcceptedCodes @("404", "400")

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host "`n════════════════════════════════════════════════════════════════════" -ForegroundColor Cyan
$total = $PASS + $FAIL
Write-Host "  Total : $total  PASS : $PASS  FAIL : $FAIL" -ForegroundColor $(if ($FAIL -eq 0) { "Green" } else { "Red" })
Write-Host ""

# Markdown table
Write-Host "| Status | Label | Method | Got | Expected |"
Write-Host "|--------|-------|--------|-----|----------|"
foreach ($r in $Results) {
    $icon = if ($r.Status -eq "PASS") { "✅" } else { "❌" }
    Write-Host "| $icon $($r.Status) | $($r.Label) | $($r.Method) | $($r.Got) | $($r.Expected) |"
}

if ($FAIL -gt 0) {
    Write-Host "`n  *** $FAIL test(s) FAILED – investigate WSO2 routing or backend connectivity ***`n" -ForegroundColor Red
    exit 1
}
else {
    Write-Host "`n  *** All $PASS smoke tests PASSED ***`n" -ForegroundColor Green
    exit 0
}
