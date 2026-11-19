# Scans every zapret profile and ranks them by how well they actually work.
# Written for the net-suite GUI, but it runs fine from an admin console:
#
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1 -Apply "general (ALT4).bat"
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1 -SettleSeconds 3
#
# It kills winws, tries each general*.bat one by one, probes the blocked sites,
# records latency, and finally brings the service back exactly as it was.
# -Apply installs the chosen profile into the `zapret` service and restarts it.
#
# NOTE: keep this file pure ASCII - PowerShell 5.1 reads it as ANSI and chokes
# on Cyrillic literals.

param(
    [int]$SettleSeconds = 5,
    [string]$OutFile = '',
    [string]$ProgressFile = '',
    [string]$Apply = '',
    [string[]]$Targets = @('https://www.youtube.com/', 'https://discord.com/'),
    [string]$ServiceName = 'zapret'
)

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir
$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
if (-not $OutFile) { $OutFile = Join-Path $logDir 'scan-results.json' }
if (-not $ProgressFile) { $ProgressFile = Join-Path $logDir 'scan-progress.json' }
$logFile = Join-Path $logDir 'zapret-scan.log'

function Log([string]$m) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Get-ZapretFolder {
    $cfgPath = Join-Path $root 'netsuite.json'
    $cfg = Get-Content $cfgPath -Raw | ConvertFrom-Json
    $p = $cfg.components.zapret.folder
    $p = $p.Replace('%REPO%', $root).Replace('%LOCALAPPDATA%', $env:LOCALAPPDATA)
    $p = $p.Replace('%APPDATA%', $env:APPDATA).Replace('%DOCUMENTS%', [Environment]::GetFolderPath('MyDocuments'))
    return $p
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'scan.ps1 needs administrator rights (WinDivert + service).' -ForegroundColor Yellow
    exit 2
}

$zdir = Get-ZapretFolder
if (-not (Test-Path $zdir)) { Log "zapret folder not found: $zdir"; exit 1 }
$exe = Join-Path $zdir 'bin\winws.exe'

function Test-Probe([string]$url, [int]$timeoutSec = 8) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time $timeoutSec $url 2>$null
    $sw.Stop()
    if (-not $code) { $code = '000' }
    return [pscustomobject]@{ code = "$code"; ms = [int]$sw.ElapsedMilliseconds }
}

function Stop-Winws {
    Get-Process winws -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
}

function Save-Json($obj, [string]$path) {
    $json = $obj | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# Probe a list of urls and return {ok, codes, ms, detail}
function Invoke-Probes {
    $codes = @{}
    $total = 0
    $ok = 0
    foreach ($t in $Targets) {
        $r = Test-Probe $t
        $codes[$t] = $r
        $total += $r.ms
        if ($r.code -match '^(200|204)$') { $ok++ }
    }
    $detail = ($Targets | ForEach-Object { "{0}={1}" -f ($_ -replace '^https?://(www\.)?', '' -replace '/$', ''), $codes[$_].code }) -join ' '
    $avg = 0
    if ($Targets.Count -gt 0) { $avg = [int]($total / $Targets.Count) }
    return [pscustomobject]@{ ok = $ok; total = $Targets.Count; avgMs = $avg; detail = $detail; codes = $codes }
}

# ---------------- apply mode ----------------

if ($Apply) {
    if (-not (Test-Path $OutFile)) { Log "no scan results in $OutFile - scan first"; exit 5 }
    $data = Get-Content $OutFile -Raw | ConvertFrom-Json
    $hit = $data.results | Where-Object { $_.profile -eq $Apply } | Select-Object -First 1
    if (-not $hit) { Log "profile not found in results: $Apply"; exit 6 }
    if (-not $hit.args) { Log "profile has no captured command line: $Apply"; exit 7 }

    $argsOnly = $hit.args -replace '^\s*"[^"]*winws\.exe"\s*', '' -replace '^\s*\S*winws\.exe\s*', ''
    if (-not $argsOnly) { Log 'empty command line, refusing to touch the service'; exit 8 }
    $binPath = ('"' + $exe + '" ' + $argsOnly).Trim()

    Log ("applying {0}" -f $Apply)
    $bat = Join-Path $env:TEMP 'ns_apply_profile.bat'
    $body = @(
        '@echo off',
        ('sc config {0} binPath= "{1}" start= auto' -f $ServiceName, $binPath),
        ('sc stop {0}' -f $ServiceName),
        'timeout /t 2 >nul',
        ('sc start {0}' -f $ServiceName),
        'timeout /t 2 >nul'
    ) -join "`r`n"
    [System.IO.File]::WriteAllText($bat, $body, (New-Object System.Text.UTF8Encoding($false)))
    & cmd.exe /c "`"$bat`"" | Out-Null
    Start-Sleep -Seconds 3

    $alive = (Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
    $probe = Invoke-Probes
    Log ("after apply: winws={0}, {1}" -f $alive, $probe.detail)

    $stateFile = Join-Path $dir 'current.json'
    @{ profile = $hit.profile; score = $hit.score; checked = (Get-Date).ToString('s'); command = $hit.args } |
        ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8
    if ($alive -and $probe.ok -eq $probe.total) { Log 'done: profile installed and verified'; exit 0 }
    Log 'warning: profile applied but did not verify cleanly'
    exit 4
}

# ---------------- scan mode ----------------

$profiles = Get-ChildItem -LiteralPath $zdir -Filter '*.bat' |
    Where-Object { $_.Name -notlike 'service*' } |
    Sort-Object { [regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) }

Log ("scan start: {0} profiles in {1}, settle {2}s" -f $profiles.Count, $zdir, $SettleSeconds)

$result = [pscustomobject]@{
    generated = (Get-Date).ToString('s')
    targets   = $Targets
    settle    = $SettleSeconds
    results   = @()
    best      = ''
}
Save-Json $result $OutFile
Save-Json ([pscustomobject]@{ total = $profiles.Count; index = 0; current = ''; done = $false; results = @() }) $ProgressFile

# baseline: what works with zapret out of the picture
& sc.exe stop $ServiceName | Out-Null
Start-Sleep -Seconds 2
Stop-Winws
$baseline = Invoke-Probes
Log ("baseline (no zapret): {0}/{1} {2}" -f $baseline.ok, $baseline.total, $baseline.detail)
$nothingBlocked = ($baseline.ok -eq $baseline.total)

$all = @()
$i = 0
foreach ($p in $profiles) {
    $i++
    Save-Json ([pscustomobject]@{ total = $profiles.Count; index = $i; current = $p.Name; done = $false; results = $all }) $ProgressFile
    Log ("[{0}/{1}] {2}" -f $i, $profiles.Count, $p.Name)

    Stop-Winws
    try {
        Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', ('"' + $p.FullName + '"')) -WorkingDirectory $zdir -WindowStyle Hidden | Out-Null
    } catch {
        Log ("  cannot run: {0}" -f $_.Exception.Message)
        $all += [pscustomobject]@{ profile = $p.Name; score = 0; ok = $false; ms = 0; detail = 'cannot run'; args = '' }
        continue
    }
    Start-Sleep -Seconds $SettleSeconds

    $proc = Get-CimInstance Win32_Process -Filter "Name='winws.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $proc) {
        Log '  winws did not start'
        $all += [pscustomobject]@{ profile = $p.Name; score = 0; ok = $false; ms = 0; detail = 'winws not running'; args = '' }
        continue
    }
    Start-Sleep -Milliseconds 900
    $probe = Invoke-Probes
    Log ("  pid {0}, {1}/{2} {3}" -f $proc.ProcessId, $probe.ok, $probe.total, $probe.detail)
    $all += [pscustomobject]@{
        profile = $p.Name
        score   = $probe.ok
        ok      = ($probe.ok -eq $probe.total)
        ms      = $probe.avgMs
        detail  = $probe.detail
        args    = $proc.CommandLine
    }
}

Stop-Winws
$sorted = $all | Sort-Object @{ Expression = 'score'; Descending = $true }, @{ Expression = 'ms'; Descending = $false }
$best = $sorted | Where-Object { $_.score -gt 0 } | Select-Object -First 1

$result.results = $sorted
if ($best) { $result.best = $best.profile }
Save-Json $result $OutFile
Save-Json ([pscustomobject]@{ total = $profiles.Count; index = $profiles.Count; current = ''; done = $true; results = $sorted }) $ProgressFile

# bring the previous service back so the user is not left without the bypass
try { & sc.exe start $ServiceName | Out-Null } catch { }
Start-Sleep -Seconds 2
$after = Invoke-Probes
Log ("service restarted: {0}/{1} {2}" -f $after.ok, $after.total, $after.detail)
if ($best) { Log ("best profile: {0} ({1}/{2}, {3} ms avg)" -f $best.profile, $best.score, $best.total, $best.ms) }
else { Log 'no profile worked at all' }
if ($nothingBlocked) { Log 'note: nothing was blocked even without zapret' }
Log ("results: {0}" -f $OutFile)
exit 0
