#!/usr/bin/env pwsh
# =============================================================================
# deploy-apis.ps1
# Sprint 3 – WSO2 API Gateway: Automated API Deployment (PowerShell)
# IT Helpdesk & Ticketing Platform (SE3022)
#
# Equivalent to infra/wso2/deploy-apis.sh for Windows environments.
#
# Usage:
#   .\infra\wso2\deploy-apis.ps1
#   .\infra\wso2\deploy-apis.ps1 -Wso2Host "https://localhost:9443" -AdminUser "admin" -AdminPass "admin"
# =============================================================================

param(
    [string]$Wso2Host    = "https://localhost:9443",
    [string]$AdminUser   = "admin",
    [string]$AdminPass   = "admin",
    [string]$ApisDir     = "$PSScriptRoot\apis",
    [int]   $ReadyTimeout = 120
)

$ErrorActionPreference = "Stop"

# Trust self-signed cert in dev (PowerShell 7 way)
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $script:SkipCert = @{ SkipCertificateCheck = $true }
} else {
    Add-Type @"
        using System.Net; using System.Security.Cryptography.X509Certificates;
        public class TrustAll : ICertificatePolicy {
            public bool CheckValidationResult(ServicePoint sp, X509Certificate cert, WebRequest req, int problem) { return true; }
        }
"@
    [System.Net.ServicePointManager]::CertificatePolicy = New-Object TrustAll
    $script:SkipCert = @{}
}

function Write-Ok([string]$m)   { Write-Host "  [OK] $m" -ForegroundColor Green }
function Write-Warn([string]$m) { Write-Host "  [WARN] $m" -ForegroundColor Yellow }
function Write-Err([string]$m)  { Write-Host "  [ERR] $m" -ForegroundColor Red }
function Write-Log([string]$m)  { Write-Host "[WSO2-DEPLOY] $m" -ForegroundColor Cyan }

# ── Step 1: Wait for WSO2 ─────────────────────────────────────────────────────
Write-Log "Waiting for WSO2 at $Wso2Host (max ${ReadyTimeout}s)..."
$base64Creds = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${AdminUser}:${AdminPass}"))
$authHeader  = @{ Authorization = "Basic $base64Creds" }
$elapsed = 0
$ready = $false

while ($elapsed -lt $ReadyTimeout) {
    try {
        $null = Invoke-RestMethod -Uri "$Wso2Host/publisher/v4/apis" `
            -Headers $authHeader @script:SkipCert -ErrorAction Stop
        $ready = $true; break
    } catch { }
    Start-Sleep -Seconds 5; $elapsed += 5
    Write-Log "  Still waiting... (${elapsed}s)"
}

if (-not $ready) {
    Write-Err "WSO2 not ready within ${ReadyTimeout}s. Start: docker compose up -d wso2-gateway"
    exit 1
}
Write-Ok "WSO2 is ready."

# ── Step 2: Dynamic Client Registration ──────────────────────────────────────
Write-Log "Registering OAuth2 DCR client..."
$dcrBody = @{
    callbackUrl = "http://localhost"
    clientName  = "deploy-apis-ps1"
    owner       = $AdminUser
    grantType   = "client_credentials password refresh_token"
    saasApp     = $true
} | ConvertTo-Json

$dcr = Invoke-RestMethod -Uri "$Wso2Host/client-registration/v0.17/register" `
    -Method Post -Headers $authHeader -ContentType "application/json" `
    -Body $dcrBody @script:SkipCert

$ClientId     = $dcr.clientId
$ClientSecret = $dcr.clientSecret
Write-Ok "DCR client registered: $ClientId"

# ── Step 3: Obtain access token ───────────────────────────────────────────────
Write-Log "Obtaining OAuth2 access token..."
$b64 = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${ClientId}:${ClientSecret}"))
$tokenHeaders = @{ Authorization = "Basic $b64" }
$tokenBody    = "grant_type=password&username=${AdminUser}&password=${AdminPass}&scope=apim:api_create apim:api_publish apim:api_view"

$tokenResp   = Invoke-RestMethod -Uri "$Wso2Host/oauth2/token" `
    -Method Post -Headers $tokenHeaders `
    -ContentType "application/x-www-form-urlencoded" `
    -Body $tokenBody @script:SkipCert

$AccessToken = $tokenResp.access_token
Write-Ok "Access token obtained."

# ── Step 4: Import & Publish ──────────────────────────────────────────────────
$apiMap = [ordered]@{
    "auth-api.json"        = "/auth/v1"
    "ticket-api.json"      = "/ticket/v1"
    "assignment-api.json"  = "/assignment/v1"
    "sla-api.json"         = "/sla/v1"
    "notification-api.json"= "/notification/v1"
}

$bearerHeader = @{ Authorization = "Bearer $AccessToken" }
$failed = 0

foreach ($entry in $apiMap.GetEnumerator()) {
    $file = $entry.Key
    $ctx  = $entry.Value
    $path = Join-Path $ApisDir $file
    $name = (Get-Content $path | ConvertFrom-Json).info.title

    Write-Log "Importing: $name ($ctx)"

    try {
        # Multipart form upload
        $boundary  = [System.Guid]::NewGuid().ToString()
        $fileBytes = [System.IO.File]::ReadAllBytes($path)
        $fileEnc   = [System.Text.Encoding]::UTF8.GetString($fileBytes)
        $addProps   = "{`"name`":`"$name`",`"context`":`"$ctx`",`"version`":`"v1`"}"

        $multipart = "--$boundary`r`n"
        $multipart += "Content-Disposition: form-data; name=`"file`"; filename=`"$file`"`r`n"
        $multipart += "Content-Type: application/json`r`n`r`n"
        $multipart += $fileEnc
        $multipart += "`r`n--$boundary`r`n"
        $multipart += "Content-Disposition: form-data; name=`"additionalProperties`"`r`n`r`n"
        $multipart += $addProps
        $multipart += "`r`n--$boundary--`r`n"

        $importResp = Invoke-RestMethod `
            -Uri "$Wso2Host/publisher/v4/apis/import-openapi" `
            -Method Post `
            -Headers $bearerHeader `
            -ContentType "multipart/form-data; boundary=$boundary" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($multipart)) `
            @script:SkipCert

        $ApiId = $importResp.id
        Write-Ok "Imported '$name' (ID: $ApiId)"

        # Publish
        $null = Invoke-RestMethod `
            -Uri "$Wso2Host/publisher/v4/apis/change-lifecycle?action=Publish&apiId=$ApiId" `
            -Method Post -Headers $bearerHeader @script:SkipCert
        Write-Ok "Published '$name'"

    } catch {
        Write-Warn "'$name': $($_.Exception.Message) – may already be deployed."
        $failed++
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
if ($failed -eq 0) {
    Write-Ok "All 5 APIs imported and published."
    Write-Log "Publisher UI : $Wso2Host/publisher"
    Write-Log "DevPortal    : $Wso2Host/devportal"
    Write-Log "HTTP Gateway : http://localhost:8280/<context>"
} else {
    Write-Warn "$failed API(s) had issues – check output above."
}
