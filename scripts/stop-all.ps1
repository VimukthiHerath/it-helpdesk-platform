<#
.SYNOPSIS
    Shut the whole IT Helpdesk platform down on Azure and PROVE it.

.DESCRIPTION
    What it does, in order:
      1. checks the Azure CLI, your login, the subscription and the resource group
      2. sets every container app to "minimum replicas 0" (scale to zero)
      3. deactivates the WSO2 gateway revision (it is never changed with "update")
      4. waits and CHECKS that no replica is left running anywhere; if an app does not
         go down by itself (Kafka sometimes does not), its revision is deactivated
      5. stops the MySQL server (unless -KeepMySql); the request is sent early so it runs
         in parallel with the apps shutting down, and is checked at the end

    Why the check matters: "az ... update --min-replicas 0" only tells Azure that zero is
    allowed. Replicas are removed a few minutes later, and a stuck app keeps running and
    billing. This script only reports success when it has seen zero replicas.

    Exit code: 0 = verified stopped, 1 = something is still running, 2 = could not start.
    Azure restarts a stopped MySQL server by itself after 7 days. Run this again then.

.PARAMETER KeepMySql
    Leave the MySQL server running (it keeps billing about $12-15 a month).

.PARAMETER DryRun
    Show what would be done, change nothing.

.PARAMETER NoWait
    Send the stop commands and return without checking.

.PARAMETER TimeoutMinutes
    How long to wait for replicas to disappear (default 15).

.PARAMETER NudgeMinutes
    After this many minutes, an app that did not scale down by itself is deactivated (default 4).

.EXAMPLE
    .\scripts\stop-all.ps1
    .\scripts\stop-all.ps1 -KeepMySql
    .\scripts\stop-all.ps1 -DryRun

.NOTES
    Settings can also be given as environment variables: HELPDESK_RG,
    HELPDESK_SUBSCRIPTION_ID, HELPDESK_MYSQL, HELPDESK_TIMEOUT_MIN.
#>
[CmdletBinding()]
param(
    [switch]$KeepMySql,
    [switch]$DryRun,
    [switch]$NoWait,
    [string]$ResourceGroup  = $(if ($env:HELPDESK_RG)              { $env:HELPDESK_RG }              else { 'it-helpdesk-rg' }),
    [string]$Subscription   = $(if ($env:HELPDESK_SUBSCRIPTION_ID) { $env:HELPDESK_SUBSCRIPTION_ID } else { '7e76da9f-fdb8-43bd-9551-28bc90afcd2f' }),
    [string]$MySql          = $(if ($env:HELPDESK_MYSQL)           { $env:HELPDESK_MYSQL }           else { 'it-helpdesk-mysql02' }),
    [int]$TimeoutMinutes    = $(if ($env:HELPDESK_TIMEOUT_MIN)     { [int]$env:HELPDESK_TIMEOUT_MIN } else { 15 }),
    [int]$NudgeMinutes      = $(if ($env:HELPDESK_NUDGE_MIN)       { [int]$env:HELPDESK_NUDGE_MIN }   else { 4 })
)

$ErrorActionPreference = 'Continue'      # az writes warnings to stderr; they must not stop the script

$GatewayApp = 'wso2-gateway'
$script:Failed = @()

# --- helpers (same as start-all.ps1) --------------------------------------------
function Azq {
    $out = & az @args 2>$null
    if ($LASTEXITCODE -ne 0) { return '' }
    return (($out | Out-String)).Trim()
}
function AzJson {
    $out = & az @args -o json 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $out) { return $null }
    return ($out | Out-String | ConvertFrom-Json)
}
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

function Get-Revisions($app) { return @(AzJson containerapp revision list -g $ResourceGroup -n $app) }
function Get-AppNames { return @((Azq containerapp list -g $ResourceGroup --query '[].name' -o tsv) -split "\r?\n" | Where-Object { $_ }) }
function Get-MinReplicas($app) {
    $v = Azq containerapp show -g $ResourceGroup -n $app --query properties.template.scale.minReplicas -o tsv
    if ($v -match '^\d+$') { return [int]$v } else { return 0 }
}
function Get-ActiveReplicas($app) {
    $n = 0
    foreach ($r in (Get-Revisions $app)) { if ($r.properties.active -and $r.properties.replicas) { $n += [int]$r.properties.replicas } }
    return $n
}
function Get-ActiveNames($app) { return @(Get-Revisions $app | Where-Object { $_.properties.active } | ForEach-Object { $_.name }) }

# --- preflight ---------------------------------------------------------------------
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
    Say "  resource group: $ResourceGroup"
    if ($DryRun) { Say '  DRY RUN: nothing will be changed.' }
}

# --- stopping ------------------------------------------------------------------------
function Stop-Gateway {
    Step "WSO2 gateway ($GatewayApp)"
    $active = Get-ActiveNames $GatewayApp
    if ($active.Count -eq 0) { Say '  no active revision - already stopped'; return }
    foreach ($r in $active) {
        Say "  deactivating revision $r"
        Invoke-Az containerapp revision deactivate -g $ResourceGroup -n $GatewayApp --revision $r | Out-Null
    }
}

function Stop-Apps {
    Step 'Container apps (scale to zero)'
    foreach ($app in (Get-AppNames)) {
        if ($app -eq $GatewayApp) { continue }
        if ((Get-MinReplicas $app) -ne 0) {
            Say "  - ${app}: setting min replicas to 0"
            Invoke-Az containerapp update -g $ResourceGroup -n $app --min-replicas 0 --no-wait | Out-Null
        } else {
            Say "  - ${app}: already set to scale to zero"
        }
    }
}

# poll until every app has zero replicas; deactivate stragglers after half the time
function Confirm-Zero {
    Step "Checking that nothing is running (up to $TimeoutMinutes minutes)"
    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $nudgeAt  = (Get-Date).AddMinutes($NudgeMinutes)
    $nudged   = $false
    while ($true) {
        $up = @{}
        foreach ($app in (Get-AppNames)) {
            $n = Get-ActiveReplicas $app
            if ($n -ne 0) { $up[$app] = $n }
        }
        if ($up.Count -eq 0) { Say '  verified: every app has 0 replicas.'; return $true }
        $list = ($up.Keys | ForEach-Object { "$_=$($up[$_])" }) -join ' '
        if ((Get-Date) -ge $deadline) {
            Say "  ! still running after $TimeoutMinutes minutes: $list"
            foreach ($k in $up.Keys) { Fail $k }
            return $false
        }
        # an app that ignores "scale to zero" (Kafka's TCP connection can keep it awake): switch its revision off
        if (-not $nudged -and (Get-Date) -ge $nudgeAt) {
            $nudged = $true
            Say "  these did not go down by themselves: $list  -> deactivating their revisions"
            foreach ($k in $up.Keys) {
                foreach ($r in (Get-ActiveNames $k)) { Invoke-Az containerapp revision deactivate -g $ResourceGroup -n $k --revision $r | Out-Null }
            }
        }
        Say "  still running: $list"
        Start-Sleep -Seconds 20
    }
}

# Ask Azure to stop MySQL right away: it takes a few minutes, so it runs while the apps drain.
# (The apps are being stopped anyway; a few database errors while they wind down are harmless.)
function Request-MySqlStop {
    Step "MySQL server ($MySql)"
    if ($KeepMySql) { Say "  left running (-KeepMySql). It keeps billing; stop it later with: az mysql flexible-server stop -g $ResourceGroup -n $MySql"; return }
    $s = Azq mysql flexible-server show -g $ResourceGroup -n $MySql --query state -o tsv
    switch -Regex ($s) {
        '^Ready$'              { Say '  asking Azure to stop it...'; Invoke-Az mysql flexible-server stop -g $ResourceGroup -n $MySql --no-wait | Out-Null }
        '^(Stopped|Disabled)$' { Say '  already stopped.' }
        '^$'                   { Say "  ! cannot read the state of MySQL server '$MySql'"; Fail 'mysql' }
        default                { Say "  state: $s (it is changing already; will check at the end)" }
    }
}

function Wait-MySqlStopped {
    if ($KeepMySql) { return $true }
    Step 'Checking that MySQL is stopped'
    for ($i = 0; $i -lt 40; $i++) {        # up to ~10 minutes
        $s = Azq mysql flexible-server show -g $ResourceGroup -n $MySql --query state -o tsv
        switch -Regex ($s) {
            '^(Stopped|Disabled)$' { Say '  Stopped. (Azure starts it again by itself after 7 days - run this script again then.)'; return $true }
            '^Ready$' { Say '  still Ready, asking again...'; Invoke-Az mysql flexible-server stop -g $ResourceGroup -n $MySql --no-wait | Out-Null }
            '^$' { Say "  ! cannot read the state of MySQL server '$MySql'"; Fail 'mysql'; return $false }
            default { Say "  state: $s (waiting)" }
        }
        Start-Sleep -Seconds 15
    }
    Say "  ! MySQL did not stop in time (last state: $s)"; Fail 'mysql'; return $false
}

function Show-Summary {
    Step 'Summary'
    foreach ($app in (Get-AppNames)) { '  {0,-22} active replicas={1}' -f $app, (Get-ActiveReplicas $app) | Write-Host }
    Say ("  MySQL ${MySql}: " + (Azq mysql flexible-server show -g $ResourceGroup -n $MySql --query state -o tsv))
}

# --- main ------------------------------------------------------------------------------
Test-Setup
Stop-Apps
Stop-Gateway
Request-MySqlStop
if ($DryRun) {
    # nothing more to do: the commands above were only printed
}
elseif (-not $NoWait) {
    Confirm-Zero | Out-Null
    Wait-MySqlStopped | Out-Null
    Show-Summary
}
else {
    Say '  (-NoWait: stop commands sent, nothing was verified. Check the Azure portal, or run this script again.)'
}
Write-Host ''
if ($script:Failed.Count -gt 0) {
    Write-Host ('NOT FULLY STOPPED: ' + ($script:Failed -join ' ') + '  - these may still be billing. See scripts/README.md, section Troubleshooting.') -ForegroundColor Red
    exit 1
}
if (-not $DryRun) { Say 'Done. Everything is stopped (compute billing has ended; storage and the registry still bill a few cents a day).' }
exit 0
