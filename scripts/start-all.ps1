<#
.SYNOPSIS
    Bring the whole IT Helpdesk platform up on Azure.

.DESCRIPTION
    What it does, in order:
      1. checks the Azure CLI, your login, the subscription and the resource group
      2. starts the MySQL server (if it is stopped) and waits until it is Ready
      3. starts Kafka, then the five backend services and the frontend
      4. activates the WSO2 gateway (exactly ONE revision - see scripts/README.md)
      5. waits until everything answers, and prints a summary

    It is safe to run again at any time: anything already running is left alone.
    It never uses "az containerapp update" on the WSO2 gateway (that creates a second
    replica which fights the first one over the gateway's database file).

    Exit code: 0 = everything is up, 1 = something did not come up, 2 = could not start
    (not logged in, wrong subscription, ...).

.PARAMETER DryRun
    Show what would be done, change nothing.

.PARAMETER NoWait
    Send the start commands and return without waiting for health.

.PARAMETER TimeoutMinutes
    How long to wait for everything to answer (default 15).

.EXAMPLE
    .\scripts\start-all.ps1
    .\scripts\start-all.ps1 -DryRun

.NOTES
    Settings can also be given as environment variables: HELPDESK_RG,
    HELPDESK_SUBSCRIPTION_ID, HELPDESK_MYSQL, HELPDESK_ENV, HELPDESK_TIMEOUT_MIN.
#>
[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$NoWait,
    [string]$ResourceGroup  = $(if ($env:HELPDESK_RG)              { $env:HELPDESK_RG }              else { 'it-helpdesk-rg' }),
    [string]$Subscription   = $(if ($env:HELPDESK_SUBSCRIPTION_ID) { $env:HELPDESK_SUBSCRIPTION_ID } else { '7e76da9f-fdb8-43bd-9551-28bc90afcd2f' }),
    [string]$MySql          = $(if ($env:HELPDESK_MYSQL)           { $env:HELPDESK_MYSQL }           else { 'it-helpdesk-mysql02' }),
    [string]$Environment    = $(if ($env:HELPDESK_ENV)             { $env:HELPDESK_ENV }             else { 'it-helpdesk-envstd' }),
    [int]$TimeoutMinutes    = $(if ($env:HELPDESK_TIMEOUT_MIN)     { [int]$env:HELPDESK_TIMEOUT_MIN } else { 15 })
)

$ErrorActionPreference = 'Continue'      # az writes warnings to stderr; they must not stop the script
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$KafkaApp    = 'kafka-broker'
$GatewayApp  = 'wso2-gateway'
$FrontendApp = 'it-helpdesk-frontend'
$BackendApps = @('auth-service', 'assignment-service', 'ticket-service', 'sla-service', 'notification-service')
$script:Failed = @()

# --- helpers -----------------------------------------------------------------
# Azq: run a read-only az command and return its text ('' when it fails).
function Azq {
    $out = & az @args 2>$null
    if ($LASTEXITCODE -ne 0) { return '' }
    return (($out | Out-String)).Trim()
}

# AzJson: run az and return the parsed JSON ($null when it fails). Used instead of JMESPath
# queries because "&" and similar characters are fragile when PowerShell calls az.cmd.
function AzJson {
    $out = & az @args -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $out) { return $null }
    return ($out | Out-String | ConvertFrom-Json)
}

# Run a command that changes something (or just print it with -DryRun).
function Invoke-Az {
    if ($DryRun) { Write-Host ("    [dry-run] az " + ($args -join ' ')); return $true }
    $out = & az @args 2>&1
    if ($LASTEXITCODE -ne 0) {
        $msg = (($out | ForEach-Object { "$_" }) | Where-Object { $_ -match 'ERROR' } | Select-Object -First 2) -join ' '
        Write-Host ("    ! az " + $args[0] + " " + $args[1] + " failed: " + $msg) -ForegroundColor Red
        return $false
    }
    return $true
}

function Say($text)  { Write-Host $text }
function Step($text) { Write-Host ''; Write-Host "== $text" -ForegroundColor Cyan }
function Fail($name) { $script:Failed += $name }

function Get-Revisions($app, [switch]$All) {
    if ($All) { $r = AzJson containerapp revision list -g $ResourceGroup -n $app --all }
    else      { $r = AzJson containerapp revision list -g $ResourceGroup -n $app }
    return @($r)
}
function Get-AppNames { return @((Azq containerapp list -g $ResourceGroup --query '[].name' -o tsv) -split "\r?\n" | Where-Object { $_ }) }
function Test-AppExists($app) { return ((Azq containerapp show -g $ResourceGroup -n $app --query name -o tsv) -eq $app) }
function Get-MinReplicas($app) {
    $v = Azq containerapp show -g $ResourceGroup -n $app --query properties.template.scale.minReplicas -o tsv
    if ($v -match '^\d+$') { return [int]$v } else { return 0 }
}
function Get-ActiveReplicas($app) {
    $n = 0
    foreach ($r in (Get-Revisions $app)) { if ($r.properties.active -and $r.properties.replicas) { $n += [int]$r.properties.replicas } }
    return $n
}
function Get-RunningState($app) {
    $a = Get-Revisions $app | Where-Object { $_.properties.active } | Select-Object -First 1
    if ($a) { return [string]$a.properties.runningState } else { return '?' }
}
function Get-HttpCode($url) {
    try   { return [int](Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 15).StatusCode }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } else { return 0 } }
}

# --- preflight ---------------------------------------------------------------
function Test-Setup {
    Step 'Checking your setup'
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) { Say "ERROR: the Azure CLI ('az') is not installed. See https://aka.ms/installazurecli"; exit 2 }
    $cur = Azq account show --query id -o tsv
    if (-not $cur) { Say 'ERROR: you are not logged in to Azure. Run:  az login'; exit 2 }
    if ($cur -ne $Subscription) {
        & az account set --subscription $Subscription 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Say ("ERROR: cannot select subscription $Subscription with this login (" + (Azq account show --query user.name -o tsv) + ').')
            Say '       Log in with an account that has access to it (az login), or set HELPDESK_SUBSCRIPTION_ID.'
            exit 2
        }
    }
    Say ('  account:      ' + (Azq account show --query user.name -o tsv))
    Say ('  subscription: ' + (Azq account show --query name -o tsv) + " ($Subscription)")
    if ((Azq extension show -n containerapp --query name -o tsv) -ne 'containerapp') {
        Say "  installing the Azure CLI 'containerapp' extension..."
        & az extension add -n containerapp --upgrade -y 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { Say 'ERROR: could not install the containerapp extension.'; exit 2 }
    }
    if ((Azq group show -n $ResourceGroup --query name -o tsv) -ne $ResourceGroup) { Say "ERROR: resource group '$ResourceGroup' not found in this subscription."; exit 2 }
    $script:Domain = Azq containerapp env show -g $ResourceGroup -n $Environment --query properties.defaultDomain -o tsv
    if (-not $script:Domain) { Say "ERROR: Container Apps environment '$Environment' not found in '$ResourceGroup'."; exit 2 }
    Say "  resource group: $ResourceGroup   environment domain: $($script:Domain)"
    if ($DryRun) { Say '  DRY RUN: nothing will be changed.' }
}

# --- MySQL -------------------------------------------------------------------
function Start-MySql {
    Step "MySQL server ($MySql)"
    $started = $false
    for ($i = 0; $i -lt 60; $i++) {        # up to ~15 minutes
        $s = Azq mysql flexible-server show -g $ResourceGroup -n $MySql --query state -o tsv
        switch -Regex ($s) {
            '^Ready$' { Say '  Ready.'; return $true }
            '^(Stopped|Disabled)$' {
                if (-not $started) {
                    Say "  state: $s -> starting it"
                    Invoke-Az mysql flexible-server start -g $ResourceGroup -n $MySql --no-wait | Out-Null
                    $started = $true
                    if ($DryRun) { return $true }
                }
            }
            '^$' { Say "  ! cannot read the state of MySQL server '$MySql' (does it exist?)"; Fail 'mysql'; return $false }
            default { Say "  state: $s (waiting)" }
        }
        Start-Sleep -Seconds 15
    }
    Say "  ! MySQL did not become Ready in time (last state: $s)"; Fail 'mysql'; return $false
}

# --- one ordinary container app ----------------------------------------------
function Start-App($app) {
    if (-not (Test-AppExists $app)) { Say "  - ${app}: not found in $ResourceGroup, skipping"; return }
    # an app whose revisions were all deactivated cannot run: activate the newest one
    $revs = Get-Revisions $app -All
    if (-not ($revs | Where-Object { $_.properties.active })) {
        $newest = $revs | Sort-Object { [datetime]$_.properties.createdTime } | Select-Object -Last 1
        if ($newest) {
            Say "  - ${app}: no active revision, activating $($newest.name)"
            Invoke-Az containerapp revision activate -g $ResourceGroup -n $app --revision $newest.name | Out-Null
        }
    }
    if ((Get-MinReplicas $app) -ne 1) {
        Say "  - ${app}: setting min replicas to 1"
        Invoke-Az containerapp update -g $ResourceGroup -n $app --min-replicas 1 --no-wait | Out-Null
    } else {
        Say "  - ${app}: already set to run"
    }
}

# --- the WSO2 gateway: ONE revision, never "update" ---------------------------
function Start-Gateway {
    Step "WSO2 gateway ($GatewayApp)"
    if (-not (Test-AppExists $GatewayApp)) { Say "  ! $GatewayApp not found"; Fail 'gateway'; return }
    $all    = Get-Revisions $GatewayApp -All
    $active = @($all | Where-Object { $_.properties.active } | ForEach-Object { $_.name })
    $want   = $all | Where-Object { $_.properties.template.scale.minReplicas -eq 1 -and $_.properties.template.scale.maxReplicas -eq 1 } |
              Sort-Object { [datetime]$_.properties.createdTime } | Select-Object -Last 1
    if (-not $want) {
        Say "  ! no revision of $GatewayApp is configured min=1 / max=1. Fix it in the portal (see scripts/README.md)."
        Fail 'gateway'; return
    }
    $wantName = $want.name
    $wantIsActive = $active -contains $wantName
    if ($active.Count -gt 1 -or ($active.Count -eq 1 -and -not $wantIsActive)) {
        Say ("  more than one (or the wrong) revision is active: " + ($active -join ' ') + " - keeping only $wantName")
        foreach ($r in $active) { if ($r -ne $wantName) { Invoke-Az containerapp revision deactivate -g $ResourceGroup -n $GatewayApp --revision $r | Out-Null } }
        if (-not $wantIsActive -and -not $DryRun) { Say '  waiting 90s for the database file lock to be released...'; Start-Sleep -Seconds 90 }
    }
    if ($wantIsActive) { Say "  $wantName is already active"; return }
    # if it was switched off a moment ago, give the file lock on its database time to clear
    if ($want.properties.lastActiveTime) {
        $age = ([datetime]::UtcNow - ([datetime]$want.properties.lastActiveTime).ToUniversalTime()).TotalSeconds
        if ($age -ge 0 -and $age -lt 120) {
            $wait = [int](120 - $age)
            Say "  it was active $([int]$age) s ago; waiting ${wait}s so its database lock is released"
            if (-not $DryRun) { Start-Sleep -Seconds $wait }
        }
    }
    Say "  activating revision $wantName"
    Invoke-Az containerapp revision activate -g $ResourceGroup -n $GatewayApp --revision $wantName | Out-Null
}

# --- waiting and checking ------------------------------------------------------
function Wait-UntilUp {
    Step "Waiting for everything to answer (up to $TimeoutMinutes minutes)"
    $gw = "https://$GatewayApp.$($script:Domain)"
    $fe = "https://$FrontendApp.$($script:Domain)"
    # name -> url that must return 200. Through the gateway, so the API routes are tested too.
    $checks = [ordered]@{
        'gateway'                   = "$gw/"
        'auth (via gateway)'        = "$gw/auth/v1/health"
        'ticket (via gateway)'      = "$gw/ticket/v1/health"
        'assignment (via gateway)'  = "$gw/assignment/v1/health"
        'sla (via gateway)'         = "$gw/sla/v1/health"
        'notification (via gateway)'= "$gw/notification/v1/health"
        'frontend'                  = "$fe/"
    }
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    while ($true) {
        $pending = @()
        foreach ($name in $checks.Keys) {
            $code = Get-HttpCode $checks[$name]
            if ($code -ne 200) { $pending += "[$name=$code]" }
        }
        if ($pending.Count -eq 0) { Say '  everything answers 200.'; return $true }
        if ((Get-Date) -ge $deadline) { Say ("  ! still not ready after $TimeoutMinutes minutes: " + ($pending -join ' ')); Fail 'health'; return $false }
        Say ('  waiting... ' + ($pending -join ' '))
        Start-Sleep -Seconds 20
    }
}

function Show-Summary {
    Step 'Summary'
    foreach ($a in (@($KafkaApp) + $BackendApps + @($FrontendApp, $GatewayApp))) {
        if (-not (Test-AppExists $a)) { continue }
        '  {0,-22} replicas={1,-2} state={2}' -f $a, (Get-ActiveReplicas $a), (Get-RunningState $a) | Write-Host
    }
    Say ("  MySQL ${MySql}: " + (Azq mysql flexible-server show -g $ResourceGroup -n $MySql --query state -o tsv))
    Say ''
    Say "  Frontend:  https://$FrontendApp.$($script:Domain)"
    Say "  Gateway:   https://$GatewayApp.$($script:Domain)"
    Say ''
    Say '  Everything is now running and costing money. When you are done run:  .\scripts\stop-all.ps1'
}

# --- main ----------------------------------------------------------------------
Test-Setup
if (-not (Start-MySql)) { Say ''; Say 'Cannot continue without the database.'; exit 1 }

Step 'Kafka'
Start-App $KafkaApp
Step 'Backend services and frontend'
foreach ($a in ($BackendApps + @($FrontendApp))) { Start-App $a }
Start-Gateway

if (-not $NoWait -and -not $DryRun) { Wait-UntilUp | Out-Null }
elseif (-not $DryRun) { Say '  (-NoWait: not waiting. Check the Azure portal or re-run this script later.)' }
if (-not $DryRun) { Show-Summary }
if ($script:Failed.Count -gt 0) {
    Write-Host ''
    Write-Host ('FINISHED WITH PROBLEMS: ' + ($script:Failed -join ' ') + '  (see messages above and scripts/README.md, section Troubleshooting)') -ForegroundColor Red
    exit 1
}
exit 0
