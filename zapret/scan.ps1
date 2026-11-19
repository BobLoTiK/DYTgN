# Scans every zapret profile and ranks them by how well they actually work.
# Written for the net-suite GUI, but it runs fine from an admin console:
#
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1 -Apply "general (ALT4).bat"
#   powershell -ExecutionPolicy Bypass -File zapret\scan.ps1 -SettleSeconds 3 -MaxProfiles 3
#
# It stops the service, kills winws, tries each general*.bat one by one, probes the
# blocked sites, records latency, and then puts the service back EXACTLY as it was
# (binPath, start mode, running/stopped) - including when the scan is interrupted.
# -Apply installs the chosen profile into the `zapret` service and verifies it.
#
# NOTE: keep this file pure ASCII - PowerShell 5.1 reads it as ANSI and chokes
# on Cyrillic literals.

param(
    [int]$SettleSeconds = 5,
    [string]$OutFile = '',
    [string]$ProgressFile = '',
    [string]$Apply = '',
    [int]$MaxProfiles = 0,
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
$svcReg = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$stateFile = Join-Path $dir 'current.json'
$backupFile = Join-Path $logDir 'zapret-service-backup.json'

function Log([string]$m) {
    $line = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

function Get-ZapretFolder {
    $cfgPath = Join-Path $root 'netsuite.json'
    $cfg = Get-Content $cfgPath -Raw -Encoding UTF8 | ConvertFrom-Json
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

function Save-Json($obj, [string]$path) {
    $json = $obj | ConvertTo-Json -Depth 6
    [System.IO.File]::WriteAllText($path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# ---------------- service state ----------------

function Get-SvcState {
    $s = [pscustomobject]@{ status = 'absent'; imagePath = ''; startValue = ''; startName = 'demand' }
    if (-not (Test-Path $svcReg)) { return $s }
    $s.imagePath = (Get-ItemProperty -Path $svcReg -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
    $start = (Get-ItemProperty -Path $svcReg -Name Start -ErrorAction SilentlyContinue).Start
    $s.startValue = "$start"
    switch ("$start") {
        '2' { $s.startName = 'auto' }
        '3' { $s.startName = 'demand' }
        '4' { $s.startName = 'disabled' }
        default { $s.startName = 'demand' }
    }
    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) { $s.status = "$($svc.Status)" } else { $s.status = 'missing' }
    return $s
}

function Set-SvcImagePath([string]$binPath, [string]$startName) {
    # sc.exe config binPath= "..." mangles the quotes around winws.exe, so the value
    # goes straight into the registry - that is what the SCM reads anyway.
    try {
        Set-ItemProperty -Path $svcReg -Name ImagePath -Value $binPath
        $startNum = 3
        if ($startName -eq 'auto') { $startNum = 2 }
        if ($startName -eq 'disabled') { $startNum = 4 }
        Set-ItemProperty -Path $svcReg -Name Start -Value $startNum -Type DWord
        $now = (Get-ItemProperty -Path $svcReg -Name ImagePath).ImagePath
        $ok = ($now -eq $binPath)
        return [pscustomobject]@{ code = $(if ($ok) { 0 } else { 1 }); output = ('registry write, matches=' + $ok) }
    } catch {
        return [pscustomobject]@{ code = 1; output = $_.Exception.Message }
    }
}

function Restore-Svc($snap) {
    if (-not $snap -or -not $snap.imagePath) { return }
    $now = Get-SvcState
    if ($now.imagePath -ne $snap.imagePath -or $now.startName -ne $snap.startName) {
        $r = Set-SvcImagePath $snap.imagePath $snap.startName
        Log ("restore config: code {0} start={1}" -f $r.code, $snap.startName)
    }
    if ($snap.status -eq 'Running') {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -ne 'Running') { & sc.exe start $ServiceName | Out-Null; Start-Sleep -Seconds 2 }
    } elseif ($snap.status -eq 'Stopped') {
        $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($svc -and $svc.Status -ne 'Stopped') { & sc.exe stop $ServiceName | Out-Null; Start-Sleep -Seconds 1 }
    }
}

# ---------------- probes ----------------

function Test-Probe([string]$url, [int]$timeoutSec = 8) {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time $timeoutSec $url 2>$null
    $sw.Stop()
    if (-not $code) { $code = '000' }
    return [pscustomobject]@{ code = "$code"; ms = [int]$sw.ElapsedMilliseconds }
}

# only winws that lives in OUR zapret folder; a foreign build stays untouched
function Stop-Winws {
    Get-CimInstance Win32_Process -Filter "Name='winws.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
        $path = $_.ExecutablePath
        if ($path -and ($path.StartsWith($zdir, 'OrdinalIgnoreCase'))) {
            Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
        }
    }
    Start-Sleep -Milliseconds 700
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
    if (-not (Test-Path $OutFile)) { Log 'no scan results yet - scan first'; exit 5 }
    $data = Get-Content $OutFile -Raw -Encoding UTF8 | ConvertFrom-Json
    $hit = $data.results | Where-Object { $_.profile -eq $Apply } | Select-Object -First 1
    if (-not $hit) { Log "profile not found in results: $Apply"; exit 6 }
    if (-not $hit.args) { Log "profile has no captured command line: $Apply"; exit 7 }

    $argsOnly = $hit.args -replace '^\s*"[^"]*winws\.exe"\s*', '' -replace '^\s*\S*winws\.exe\s*', ''
    if (-not $argsOnly) { Log 'empty command line, refusing to touch the service'; exit 8 }
    $binPath = ('"' + $exe + '" ' + $argsOnly).Trim()

    $before = Get-SvcState
    Save-Json ([pscustomobject]@{ imagePath = $before.imagePath; startName = $before.startName; status = $before.status; saved = (Get-Date).ToString('s') }) $backupFile
    Log ("applying {0}" -f $Apply)
    Log 'previous binPath saved to logs\zapret-service-backup.json'

    # keep the start mode the service already had - scan must not change boot policy
    $r = Set-SvcImagePath $binPath $before.startName
    if ($r.code -ne 0) {
        Log ("sc config failed (code {0}): {1}" -f $r.code, $r.output)
        Restore-Svc $before
        exit 4
    }
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Stop-Winws
    & sc.exe start $ServiceName | Out-Null
    Start-Sleep -Seconds 3

    $alive = (Get-CimInstance Win32_Process -Filter "Name='winws.exe'" -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
    $probe = Invoke-Probes
    $installed = (Get-SvcState).imagePath
    $written = ($installed -eq $binPath)
    Log ("after apply: winws={0}, binPath written={1}, {2}" -f $alive, $written, $probe.detail)

    if ($alive -and $written -and $probe.ok -eq $probe.total) {
        @{ profile = $hit.profile; score = $hit.score; checked = (Get-Date).ToString('s'); command = $hit.args } |
            ConvertTo-Json | Set-Content -Path $stateFile -Encoding UTF8
        Log 'done: profile installed and verified'
        exit 0
    }
    Log 'warning: profile applied but did not verify cleanly - rolling back to the previous binPath'
    Restore-Svc $before
    Start-Sleep -Seconds 2
    $back = Invoke-Probes
    Log ("after rollback: {0}" -f $back.detail)
    exit 4
}

# ---------------- scan mode ----------------

$profiles = @(Get-ChildItem -LiteralPath $zdir -Filter '*.bat' |
    Where-Object { $_.Name -notlike 'service*' } |
    Sort-Object { [regex]::Replace($_.Name, '(\d+)', { $args[0].Value.PadLeft(8, '0') }) })
if ($MaxProfiles -gt 0 -and $profiles.Count -gt $MaxProfiles) { $profiles = $profiles[0..($MaxProfiles - 1)] }

$snap = Get-SvcState
Save-Json ([pscustomobject]@{ imagePath = $snap.imagePath; startName = $snap.startName; status = $snap.status; saved = (Get-Date).ToString('s') }) $backupFile
Log ("scan start: {0} profiles, settle {1}s" -f $profiles.Count, $SettleSeconds)
Log ("service before: {0}, start={1}" -f $snap.status, $snap.startName)

$result = [pscustomobject]@{
    generated = (Get-Date).ToString('s')
    targets   = $Targets
    settle    = $SettleSeconds
    results   = @()
    best      = ''
    serviceBefore = $snap.status
}
Save-Json $result $OutFile
Save-Json ([pscustomobject]@{ total = $profiles.Count; index = 0; current = ''; done = $false; results = @() }) $ProgressFile

$all = @()
try {
    # baseline: what works with zapret out of the picture
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    Stop-Winws
    $baseline = Invoke-Probes
    Log ("baseline (no zapret): {0}/{1} {2}" -f $baseline.ok, $baseline.total, $baseline.detail)
    $nothingBlocked = ($baseline.ok -eq $baseline.total)

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
            $all += [pscustomobject]@{ profile = $p.Name; score = 0; total = $Targets.Count; ok = $false; ms = 0; detail = 'cannot run'; args = '' }
            continue
        }
        Start-Sleep -Seconds $SettleSeconds

        $proc = Get-CimInstance Win32_Process -Filter "Name='winws.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $proc) {
            Log '  winws did not start'
            $all += [pscustomobject]@{ profile = $p.Name; score = 0; total = $Targets.Count; ok = $false; ms = 0; detail = 'winws not running'; args = '' }
            continue
        }
        Start-Sleep -Milliseconds 900
        $probe = Invoke-Probes
        Log ("  pid {0}, {1}/{2} {3}" -f $proc.ProcessId, $probe.ok, $probe.total, $probe.detail)
        $all += [pscustomobject]@{
            profile = $p.Name
            score   = $probe.ok
            total   = $probe.total
            ok      = ($probe.ok -eq $probe.total)
            ms      = $probe.avgMs
            detail  = $probe.detail
            args    = $proc.CommandLine
        }
    }
} finally {
    Stop-Winws
    Restore-Svc $snap
    $after = Invoke-Probes
    Log ("service restored ({0}): {1}/{2} {3}" -f (Get-SvcState).status, $after.ok, $after.total, $after.detail)
}

$sorted = @($all | Sort-Object @{ Expression = 'score'; Descending = $true }, @{ Expression = 'ms'; Descending = $false })
$best = $sorted | Where-Object { $_.score -gt 0 } | Select-Object -First 1

$result.results = $sorted
if ($best) { $result.best = $best.profile }
Save-Json $result $OutFile
Save-Json ([pscustomobject]@{ total = $profiles.Count; index = $profiles.Count; current = ''; done = $true; results = $sorted }) $ProgressFile

if ($best) { Log ("best profile: {0} ({1}/{2}, {3} ms avg)" -f $best.profile, $best.score, $best.total, $best.ms) }
else { Log 'no profile worked at all' }
if ($nothingBlocked) { Log 'note: nothing was blocked even without zapret' }
Log 'results: logs\scan-results.json'
exit 0
