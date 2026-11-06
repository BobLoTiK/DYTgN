# Fast repair of the zapret side: make sure the service runs and, if its profile
# no longer defeats DPI, test profiles and install the FIRST one that works.
# Requires administrator rights (WinDivert + service).
#
#   powershell -ExecutionPolicy Bypass -File fix.ps1

param(
    [string]$ServiceName = 'zapret',
    [int]$SettleSeconds = 3,
    [int]$MaxWaitSeconds = 14
)

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir 'zapret-fix.log'

function Log([string]$m) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { Write-Host 'administrator rights required (use zapret\fix.bat)' -ForegroundColor Yellow; exit 2 }

function Get-Health {
    return (& (Join-Path $dir 'check.ps1'))
}

$h = Get-Health
Log ("start: state={0} ({1})" -f $h.State, $h.Targets)
if ($h.State -eq 'ok') { Log 'nothing to do: zapret works'; exit 0 }
if ($h.State -eq 'no-internet') { Log 'no internet at all - not a zapret problem'; exit 4 }

# 1. try to start the service
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Log "service $ServiceName not found - run service.bat once"; exit 5 }
if ($svc.Status -ne 'Running') {
    Log 'starting the service'
    try { Start-Service -Name $ServiceName -ErrorAction Stop } catch { Log ("start failed: {0}" -f $_.Exception.Message) }
}
$deadline = (Get-Date).AddSeconds($MaxWaitSeconds)
while ((Get-Date) -lt $deadline) {
    if ((Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) { break }
    Start-Sleep -Milliseconds 700
}
$h = Get-Health
Log ("after start: state={0} ({1})" -f $h.State, $h.Targets)
if ($h.State -eq 'ok') { Log 'ok: service started, profile works'; exit 0 }

# 2. profile is dead - test profiles and take the first working one
Log 'profile does not work, testing profiles (stops at the first working one)'
& (Join-Path $dir 'pick.ps1') -First -SettleSeconds $SettleSeconds
$h = Get-Health
Log ("after pick: state={0} ({1})" -f $h.State, $h.Targets)
if ($h.State -eq 'ok') { Log 'ok: working profile installed'; exit 0 }
Log 'could not fix zapret automatically' -ForegroundColor Yellow
exit 3
