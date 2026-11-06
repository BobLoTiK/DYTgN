# Picks a working zapret profile by actually trying them.
# Requires administrator rights (WinDivert driver + service change).
#
#   powershell -ExecutionPolicy Bypass -File pick.ps1            # test all profiles and install the best
#   powershell -ExecutionPolicy Bypass -File pick.ps1 -DryRun    # only list what would be tried
#   powershell -ExecutionPolicy Bypass -File pick.ps1 -NoService # test profiles, do not touch the service
#
# For each general*.bat: kill winws, run the bat, read the real winws.exe command line,
# probe the blocked sites, score. The winner's command line is written into the
# existing `zapret` service (sc config binPath), so it survives reboots.

param(
    [switch]$DryRun,
    [switch]$NoService,
    [switch]$First,
    [int]$SettleSeconds = 5,
    [string[]]$Targets = @('https://www.youtube.com/', 'https://discord.com/'),
    [string]$ServiceName = 'zapret'
)

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir 'zapret-pick.log'

function Log([string]$m) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Test-Http([string]$url, [int]$timeoutSec = 8) {
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time $timeoutSec $url 2>$null
    return ($code -match '^(200|204)$')
}

function Get-ZapretFolder {
    $cfgPath = Join-Path $root 'netsuite.json'
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    $p = $cfg.components.zapret.folder
    $p = $p.Replace('%REPO%', $root).Replace('%LOCALAPPDATA%', $env:LOCALAPPDATA).Replace('%APPDATA%', $env:APPDATA).Replace('%DOCUMENTS%', [Environment]::GetFolderPath('MyDocuments'))
    return $p
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
Log ("start: admin={0}, dryrun={1}" -f $isAdmin, $DryRun)
if (-not $isAdmin -and -not $DryRun) {
    Write-Host ''
    Write-Host 'This script needs administrator rights: it starts WinDivert filters and reconfigures a service.' -ForegroundColor Yellow
    Write-Host 'Run zapret\pick.bat (it asks for elevation) or open PowerShell as administrator.' -ForegroundColor Yellow
    exit 2
}

$zdir = Get-ZapretFolder
if (-not (Test-Path $zdir)) { Log "zapret folder not found: $zdir"; exit 1 }

$profiles = Get-ChildItem -LiteralPath $zdir -Filter '*.bat' |
    Where-Object { $_.Name -notlike 'service*' } |
    Sort-Object { [regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) }

Log ("profiles found: {0} in {1}" -f $profiles.Count, $zdir)
foreach ($p in $profiles) { Log ("  candidate: " + $p.Name) }

if ($DryRun) {
    Log 'dry run, nothing executed'
    foreach ($p in $profiles) { Write-Host ("would try: {0}" -f $p.Name) }
    exit 0
}

function Stop-Winws {
    Get-Process winws -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 700
}

# ---- baseline: how bad is it with zapret fully off ----
try { & sc.exe stop $ServiceName | Out-Null } catch { }
Start-Sleep -Seconds 2
Stop-Winws
$baselineOk = @($Targets | Where-Object { Test-Http $_ })
Log ("baseline (no zapret): reachable {0}/{1}" -f $baselineOk.Count, $Targets.Count)
if ($baselineOk.Count -eq $Targets.Count) {
    Log 'nothing is blocked without zapret - no profile tuning needed'
    try { & sc.exe start $ServiceName | Out-Null } catch { }
    exit 0
}

# ---- try every profile ----
$results = @()
$i = 0
foreach ($p in $profiles) {
    $i++
    Log ("[{0}/{1}] trying {2}" -f $i, $profiles.Count, $p.Name)
    Stop-Winws
    try {
        Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', ('"' + $p.FullName + '"')) -WorkingDirectory $zdir -WindowStyle Hidden | Out-Null
    }
    catch {
        Log ("  cannot run: {0}" -f $_.Exception.Message)
        continue
    }
    Start-Sleep -Seconds $SettleSeconds

    $proc = Get-CimInstance Win32_Process -Filter "Name='winws.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) {
        Log '  winws did not start'
        $results += [pscustomobject]@{ Profile = $p.Name; Score = 0; Cmd = ''; Detail = 'no process' }
        continue
    }
    Start-Sleep -Seconds 1
    $ok = @($Targets | Where-Object { Test-Http $_ })
    Log ("  winws pid {0}, targets {1}/{2}" -f $proc.ProcessId, $ok.Count, $Targets.Count)
    $results += [pscustomobject]@{ Profile = $p.Name; Score = $ok.Count; Cmd = $proc.CommandLine; Detail = ($ok -join ' ') }
    if ($First -and $ok.Count -eq $Targets.Count) { Log '  first working profile found, stopping the loop'; break }
}

Stop-Winws
$results | Sort-Object Score -Descending | Format-Table Profile, Score, Detail -AutoSize | Out-String | Write-Host

$best = $results | Where-Object { $_.Score -gt 0 } | Sort-Object Score -Descending | Select-Object -First 1
if (-not $best) {
    Log 'no profile worked at all - leaving the current configuration as is'
    try { & sc.exe start $ServiceName | Out-Null } catch { }
    exit 3
}

Log ("best: {0} ({1}/{2})" -f $best.Profile, $best.Score, $Targets.Count)
$stateFile = Join-Path $dir 'current.json'
@{ profile = $best.Profile; score = $best.Score; checked = (Get-Date).ToString('s'); command = $best.Cmd } |
    ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8

if ($NoService) {
    Log 'NoService: service left untouched'
    exit 0
}

# ---- install the winner into the existing service ----
$exe = Join-Path $zdir 'bin\winws.exe'
$argsOnly = $best.Cmd -replace '^\s*"[^"]*winws\.exe"\s*', '' -replace '^\s*\S*winws\.exe\s*', ''
$binPath = ('"' + $exe + '" ' + $argsOnly).Trim()
Log ("reconfiguring service {0}" -f $ServiceName)
$bat = Join-Path $env:TEMP 'ns_set_zapret_service.bat'
$batBody = @(
    '@echo off',
    ('sc config {0} binPath= "{1}" start= auto' -f $ServiceName, $binPath),
    ('sc stop {0}' -f $ServiceName),
    'timeout /t 2 >nul',
    ('sc start {0}' -f $ServiceName),
    'timeout /t 2 >nul'
) -join "`r`n"
[System.IO.File]::WriteAllText($bat, $batBody, (New-Object System.Text.UTF8Encoding($false)))
& cmd.exe /c "`"$bat`"" | Out-Null
Start-Sleep -Seconds 3

$alive = (Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
$okNow = @($Targets | Where-Object { Test-Http $_ })
Log ("after switch: winws={0}, targets {1}/{2}" -f $alive, $okNow.Count, $Targets.Count)
if ($alive -and $okNow.Count -eq $Targets.Count) {
    Log 'done: profile installed and verified'
    exit 0
}
Log 'warning: service did not verify after switching, check logs'
exit 4
