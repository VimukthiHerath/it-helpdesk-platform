<#
.SYNOPSIS
    Publish the five IT Helpdesk APIs into the WSO2 gateway.

.DESCRIPTION
    The gateway only knows a route once its API has been published AND deployed. Run this:
      - on your machine after  docker compose up -d   (-Mode local)
      - on Azure after start-all, or whenever the gateway's API definitions are lost or
        changed, for example after editing infra/wso2/apis/*.json   (-Mode azure)

    local   (default) the WSO2 container started by docker-compose (local-wso2-gateway).
            The APIs point at the services on your machine: auth 5121, ticket 5164,
            assignment 5067, sla 5262, notification 5214 (the host names auth-api, ticket-api,
            ... are mapped to your machine by docker-compose). Needs Docker.
    azure   the deployed gateway. Its management port is never exposed, so the files are
            staged on the gateway's Azure Files share and run INSIDE the container with
            "az containerapp exec". The APIs point at the Azure services. The gateway must be
            running (run scripts\start-all.ps1 first). Needs the Azure CLI.

    It is safe to run again: the old copies of these five APIs are replaced. Every operation
    is published with "x-auth-type: None": the gateway only ROUTES, and each service checks the
    JWT itself (see docs/ARCHITECTURE_AND_END_TO_END_FLOW.md, section 3.4).

    Exit code: 0 = all five APIs published and deployed, 1 = something failed, 2 = could not start.

.PARAMETER Mode
    local (default) or azure.

.PARAMETER CheckBackends
    After publishing, also call each service's /health through the gateway. On Azure this WAKES every
    service that is scaled to zero (and needs MySQL running), so it is off by default. By default only
    the gateway is asked whether each route exists.

.PARAMETER DryRun
    Show what would be done, change nothing.

.EXAMPLE
    .\scripts\publish-wso2-apis.ps1
    .\scripts\publish-wso2-apis.ps1 azure
    .\scripts\publish-wso2-apis.ps1 azure -DryRun

.NOTES
    Environment variables: HELPDESK_RG, HELPDESK_SUBSCRIPTION_ID, HELPDESK_ENV,
    WSO2_ADMIN_USER / WSO2_ADMIN_PASSWORD (default admin / admin), WSO2_CONTAINER
    (default local-wso2-gateway).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][ValidateSet('local', 'azure')][string]$Mode = 'local',
    [switch]$DryRun,
    [switch]$CheckBackends,
    [string]$ResourceGroup = $(if ($env:HELPDESK_RG)              { $env:HELPDESK_RG }              else { 'it-helpdesk-rg' }),
    [string]$Subscription  = $(if ($env:HELPDESK_SUBSCRIPTION_ID) { $env:HELPDESK_SUBSCRIPTION_ID } else { '7e76da9f-fdb8-43bd-9551-28bc90afcd2f' }),
    [string]$Environment   = $(if ($env:HELPDESK_ENV)             { $env:HELPDESK_ENV }             else { 'it-helpdesk-envstd' }),
    [string]$AdminUser     = $(if ($env:WSO2_ADMIN_USER)          { $env:WSO2_ADMIN_USER }          else { 'admin' }),
    [string]$AdminPassword = $(if ($env:WSO2_ADMIN_PASSWORD)      { $env:WSO2_ADMIN_PASSWORD }      else { 'admin' }),
    [string]$Container     = $(if ($env:WSO2_CONTAINER)           { $env:WSO2_CONTAINER }           else { 'local-wso2-gateway' })
)

$ErrorActionPreference = 'Continue'      # az and docker write to stderr; that must not stop the script
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$GatewayApp = 'wso2-gateway'
$Share      = 'wso2-data'
$ShareDir   = 'database/pub'
$ContainerSharePath = '/home/wso2carbon/wso2am-4.3.0/repository/database/pub'
$LocalStage = '/tmp/wso2pub'
$Apis       = @('auth', 'ticket', 'assignment', 'sla', 'notification')

function Say($t)  { Write-Host $t }
function Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Azq { $o = & az @args 2>$null; if ($LASTEXITCODE -ne 0) { return '' }; return (($o | Out-String)).Trim() }
function AzJson { $o = & az @args -o json 2>$null; if ($LASTEXITCODE -ne 0 -or -not $o) { return $null }; return ($o | Out-String | ConvertFrom-Json) }
function Get-HttpCode($url, $method = 'GET', $headers = @{}, $timeout = 25) {
    try   { return [int](Invoke-WebRequest -Uri $url -Method $method -Headers $headers -UseBasicParsing -TimeoutSec $timeout).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return 0 } }
}

$Root     = Split-Path -Parent $PSScriptRoot
$SpecDir  = Join-Path $Root 'infra\wso2\apis'
$Core     = Join-Path $Root 'scripts\lib\wso2-publish-core.sh'

# --- check the files exist ---------------------------------------------------------
foreach ($a in $Apis) { if (-not (Test-Path (Join-Path $SpecDir "$a-api.json"))) { Say "ERROR: $SpecDir\$a-api.json not found. Run this from a full checkout of the repository."; exit 2 } }
if (-not (Test-Path $Core)) { Say "ERROR: $Core not found."; exit 2 }

# --- backends and public address, by mode ---------------------------------------------
if ($Mode -eq 'local') {
    $B = @{ auth = 'http://auth-api:5121/api/Auth'; ticket = 'http://ticket-api:5164/api/Ticket'; assignment = 'http://assignment-api:5067/api/Assignments'
            sla = 'http://sla-api:5262'; notification = 'http://notification-api:5214' }
    $GatewayUrl = 'http://localhost:8280'
}
else {
    Step 'Checking your Azure setup'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Say "ERROR: the Azure CLI ('az') is not installed. See https://aka.ms/installazurecli"; exit 2 }
    $cur = Azq account show --query id -o tsv
    if (-not $cur) { Say 'ERROR: you are not logged in to Azure. Run:  az login'; exit 2 }
    if ($cur -ne $Subscription) {
        & az account set --subscription $Subscription 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Say "ERROR: cannot select subscription $Subscription with this login."; exit 2 }
    }
    if ((Azq extension show -n containerapp --query name -o tsv) -ne 'containerapp') { & az extension add -n containerapp --upgrade -y 2>$null | Out-Null }
    if ((Azq group show -n $ResourceGroup --query name -o tsv) -ne $ResourceGroup) { Say "ERROR: resource group '$ResourceGroup' not found."; exit 2 }
    $Domain = Azq containerapp env show -g $ResourceGroup -n $Environment --query properties.defaultDomain -o tsv
    if (-not $Domain) { Say "ERROR: environment '$Environment' not found in '$ResourceGroup'."; exit 2 }
    Say ("  account: " + (Azq account show --query user.name -o tsv) + "   resource group: $ResourceGroup   domain: $Domain")
    $B = @{ auth = "https://auth-service.$Domain/api/Auth"; ticket = "https://ticket-service.$Domain/api/Ticket"; assignment = "https://assignment-service.$Domain/api/Assignments"
            sla = "https://sla-service.internal.$Domain"; notification = "https://notification-service.internal.$Domain" }
    $GatewayUrl = "https://$GatewayApp.$Domain"
}
$Titles = [ordered]@{ auth = 'Auth API'; ticket = 'Ticket API'; assignment = 'Assignment API'; sla = 'SLA API'; notification = 'Notification API' }

Step "Plan ($Mode)"
Say "  gateway address:  $GatewayUrl"
foreach ($a in $Apis) { Say ('  {0,-16} -> {1}' -f $Titles[$a], $B[$a]) }
if ($DryRun) { Say ''; Say 'DRY RUN: nothing was changed.'; exit 0 }

# --- stage the files (specs + props + the core script) ----------------------------------
$Stage = Join-Path ([IO.Path]::GetTempPath()) ('wso2pub-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $Stage | Out-Null
try {
    $utf8 = New-Object System.Text.UTF8Encoding($false)      # UTF-8 without a BOM: WSO2's JSON parser dislikes a BOM
    $ctx  = @{ auth = '/auth'; ticket = '/ticket'; assignment = '/assignment'; sla = '/sla'; notification = '/notification' }
    foreach ($a in $Apis) {
        Copy-Item (Join-Path $SpecDir "$a-api.json") (Join-Path $Stage "$a.spec.json")
        $json = '{"name":"' + $Titles[$a] + '","context":"' + $ctx[$a] + '","version":"v1","policies":["Unlimited"],"endpointConfig":{"endpoint_type":"http","production_endpoints":{"url":"' + $B[$a] + '"},"sandbox_endpoints":{"url":"' + $B[$a] + '"}}}'
        [IO.File]::WriteAllText((Join-Path $Stage "$a.props.json"), $json, $utf8)
    }
    # the core script must have Unix line endings, whatever the checkout did
    $coreText = (Get-Content -Raw $Core) -replace "`r`n", "`n"
    [IO.File]::WriteAllText((Join-Path $Stage 'wso2-publish-core.sh'), $coreText, $utf8)

    $out = ''
    if ($Mode -eq 'local') {
        # --- run it inside the local container -----------------------------------------------
        Step "Local WSO2 container ($Container)"
        if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { Say 'ERROR: Docker is not installed or not on the PATH.'; exit 2 }
        $running = @(& docker ps --format '{{.Names}}' 2>$null)
        if ($running -notcontains $Container) { Say "ERROR: the container '$Container' is not running. Start it with:  docker compose up -d wso2-gateway"; exit 2 }
        & docker exec $Container mkdir -p $LocalStage 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Say 'ERROR: cannot reach the container.'; exit 2 }
        foreach ($f in (Get-ChildItem $Stage -File)) { & docker cp $f.FullName "${Container}:$LocalStage/$($f.Name)" 2>$null | Out-Null }
        $out = (& docker exec $Container sh "$LocalStage/wso2-publish-core.sh" $LocalStage $AdminUser $AdminPassword 2>&1 | Out-String)
        & docker exec $Container rm -rf $LocalStage 2>$null | Out-Null
    }
    else {
        # --- run it inside the Azure gateway via the file share --------------------------------
        Step "Azure gateway ($GatewayApp)"
        $revs = @(AzJson containerapp revision list -g $ResourceGroup -n $GatewayApp)
        $active = $revs | Where-Object { $_.properties.active } | Select-Object -First 1
        $rep = $null
        if ($active) { $reps = @(AzJson containerapp replica list -g $ResourceGroup -n $GatewayApp --revision $active.name); if ($reps.Count -gt 0) { $rep = $reps[0].name } }
        if (-not $active -or -not $rep) { Say 'ERROR: the gateway is not running (no active revision or replica). Run  .\scripts\start-all.ps1  first (or activate the gateway revision).'; exit 2 }
        Say "  using revision $($active.name), replica $rep"
        $sa = @(AzJson storage account list -g $ResourceGroup) | Where-Object { $_.name -like '*wso2*' } | Select-Object -First 1
        if (-not $sa) { Say "ERROR: cannot find the WSO2 storage account in $ResourceGroup."; exit 2 }
        $key = Azq storage account keys list -g $ResourceGroup -n $sa.name --query '[0].value' -o tsv
        if (-not $key) { Say "ERROR: cannot read the storage account key (needs Contributor on $ResourceGroup)."; exit 2 }
        Say "  staging 11 files on the file share ($($sa.name)/$Share/$ShareDir)"
        & az storage directory create --account-name $sa.name --account-key $key -s $Share -n $ShareDir -o none 2>$null | Out-Null
        & az storage file upload-batch --account-name $sa.name --account-key $key -d $Share --destination-path $ShareDir -s $Stage -o none 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Say 'ERROR: could not upload the files to the file share.'; exit 2 }
        Say '  running the publish steps inside the gateway (this takes about a minute)...'
        $raw = & az containerapp exec -g $ResourceGroup -n $GatewayApp --revision $active.name --replica $rep --container $GatewayApp `
                 --command "sh $ContainerSharePath/wso2-publish-core.sh $ContainerSharePath $AdminUser $AdminPassword" 2>&1
        $out = (($raw | ForEach-Object { "$_" }) | Where-Object { $_ -notmatch '^(WARNING|INFO)|Alive\[|Finished\[' }) -join "`n"
        foreach ($name in (Azq storage file list --account-name $sa.name --account-key $key -s $Share -p $ShareDir --query '[].name' -o tsv) -split "\r?\n") {
            if ($name) { & az storage file delete --account-name $sa.name --account-key $key -s $Share -p "$ShareDir/$name" -o none 2>$null | Out-Null }
        }
        & az storage directory delete --account-name $sa.name --account-key $key -s $Share -n $ShareDir -o none 2>$null | Out-Null
    }
}
finally {
    Remove-Item -Recurse -Force $Stage -ErrorAction SilentlyContinue
}

($out -split "\r?\n" | Where-Object { $_.Trim() }) | ForEach-Object { Say "  $_" }
$published = $out -match '(?m)^RESULT: OK'

# --- check the routes through the public gateway address ----------------------------------
Step "Checking the routes through the gateway ($GatewayUrl)"
Say '  The gateway answers a CORS preflight (OPTIONS) for a route by itself, without calling the service:'
Say '  200 = the route is live; 404 = the route is NOT there.'
Start-Sleep -Seconds 15
$routesOk = $true
$preflight = @{ 'Origin' = 'http://localhost:3000'; 'Access-Control-Request-Method' = 'GET' }
foreach ($a in $Apis) {
    $code = Get-HttpCode "$GatewayUrl/$a/v1/health" 'OPTIONS' $preflight
    if ($code -ne 200) { $routesOk = $false }
    Say ('  {0,-26} route check HTTP {1}' -f "$GatewayUrl/$a/v1/health", $code)
}
if ($CheckBackends) {
    Say '  -CheckBackends: asking each service for /health through the gateway (may wake services)...'
    foreach ($a in $Apis) { Say ('  {0,-26} backend HTTP {1}' -f "$GatewayUrl/$a/v1/health", (Get-HttpCode "$GatewayUrl/$a/v1/health" 'GET' @{} 60)) }
}

Write-Host ''
if ($published -and $routesOk) { Say 'Done: all five APIs are published, deployed and answering.'; exit 0 }
if ($published) { Write-Host 'Published, but the gateway is not answering for every route yet. Wait a minute and check again, or see docs/WSO2_API_GATEWAY.md.' -ForegroundColor Red; exit 1 }
Write-Host 'FAILED: see the messages above.' -ForegroundColor Red
exit 1
