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
        $null = Invoke-RestMethod -Uri "$Wso2Host/api/am/publisher/v4/apis" `
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
# NOTE: WSO2 appends the API's own version ("v1") onto whatever context is given
# here, so the context must be version-less (e.g. "/auth") or the exposed path
# ends up doubled ("/auth/v1/v1").
$apiMap = [ordered]@{
    "auth-api.json"        = "/auth"
    "ticket-api.json"      = "/ticket"
    "assignment-api.json"  = "/assignment"
    "sla-api.json"         = "/sla"
    "notification-api.json"= "/notification"
}

$bearerHeader = @{ Authorization = "Bearer $AccessToken" }
$failed = 0

foreach ($entry in $apiMap.GetEnumerator()) {
    $file = $entry.Key
    $ctx  = $entry.Value
    $path = Join-Path $ApisDir $file
    $spec = Get-Content $path | ConvertFrom-Json
    $name = $spec.info.title

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
            -Uri "$Wso2Host/api/am/publisher/v4/apis/import-openapi" `
            -Method Post `
            -Headers $bearerHeader `
            -ContentType "multipart/form-data; boundary=$boundary" `
            -Body ([System.Text.Encoding]::UTF8.GetBytes($multipart)) `
            @script:SkipCert

        $ApiId = $importResp.id
        Write-Ok "Imported '$name' (ID: $ApiId)"

        # import-openapi does NOT read the x-wso2-*-endpoints vendor extensions
        # from the file, so the API is left with endpointConfig=null. WSO2
        # refuses to publish an API with no endpoint and no subscription
        # policy, so both must be set explicitly before publishing. It also
        # does NOT translate a per-operation "security: []" (public, no auth)
        # into WSO2's own authType="None" - every operation defaults to
        # requiring a token, which would make even /login unreachable through
        # the gateway unless corrected here.
        Write-Log "Configuring endpoint + subscription policy for: $name"
        $prodUrl    = $spec.'x-wso2-production-endpoints'.urls[0]
        $sandboxUrl = $spec.'x-wso2-sandbox-endpoints'.urls[0]
        $apiDetail  = Invoke-RestMethod -Uri "$Wso2Host/api/am/publisher/v4/apis/$ApiId" `
            -Headers $bearerHeader @script:SkipCert
        $apiDetail | Add-Member -Force -NotePropertyName endpointConfig -NotePropertyValue ([pscustomobject]@{
            endpoint_type       = "http"
            production_endpoints = @{ url = $prodUrl }
            sandbox_endpoints     = @{ url = $sandboxUrl }
        })
        $apiDetail | Add-Member -Force -NotePropertyName policies -NotePropertyValue @("Unlimited")

        foreach ($op in $apiDetail.operations) {
            $verbLower = $op.verb.ToLower()
            $pathProp = $spec.paths.PSObject.Properties[$op.target]
            if ($pathProp) {
                $verbProp = $pathProp.Value.PSObject.Properties[$verbLower]
                if ($verbProp -and (-not $verbProp.Value.security -or $verbProp.Value.security.Count -eq 0)) {
                    $op | Add-Member -Force -NotePropertyName authType -NotePropertyValue "None"
                }
            }
        }
        $null = Invoke-RestMethod -Uri "$Wso2Host/api/am/publisher/v4/apis/$ApiId" `
            -Method Put -Headers $bearerHeader -ContentType "application/json" `
            -Body ($apiDetail | ConvertTo-Json -Depth 20) @script:SkipCert

        # Publish
        $null = Invoke-RestMethod `
            -Uri "$Wso2Host/api/am/publisher/v4/apis/change-lifecycle?action=Publish&apiId=$ApiId" `
            -Method Post -Headers $bearerHeader @script:SkipCert
        Write-Ok "Published '$name'"

        # Publishing only changes the lifecycle state; the API is not actually
        # live on the gateway until a revision is created and deployed.
        Write-Log "Deploying revision to gateway for: $name"
        $revResp = Invoke-RestMethod -Uri "$Wso2Host/api/am/publisher/v4/apis/$ApiId/revisions" `
            -Method Post -Headers $bearerHeader -ContentType "application/json" `
            -Body '{"description":"Automated deploy"}' @script:SkipCert
        $revBody = '[{"name":"Default","vhost":"localhost","displayOnDevportal":true}]'
        $null = Invoke-RestMethod -Uri "$Wso2Host/api/am/publisher/v4/apis/$ApiId/deploy-revision?revisionId=$($revResp.id)" `
            -Method Post -Headers $bearerHeader -ContentType "application/json" `
            -Body $revBody @script:SkipCert
        Write-Ok "Revision deployed to Default gateway."

    } catch {
        Write-Warn "'$name': $($_.Exception.Message) - may already be deployed."
        $failed++
    }
}

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
if ($failed -eq 0) {
    Write-Ok "All 5 APIs imported and published."
    Write-Log "Publisher UI : $Wso2Host/publisher"
    Write-Log "DevPortal    : $Wso2Host/devportal"
    Write-Log "HTTP Gateway : http://localhost:8280/<context>/v1"
} else {
    Write-Warn "$failed API(s) had issues - check output above."
}
