param(
    [ValidateSet('on', 'off', 'status', 'ensure', 'restart', 'zapret-fix')]
    [string]$Action = 'status',
    [switch]$IncludeZapret,
    [switch]$FixZapret
)

# One switchboard for three separate things:
#   zapret  - winws.exe (WinDivert DPI bypass), runs as the "zapret" service
#   TgWsProxy - local MTProto-to-WebSocket bridge for Telegram
#   notion  - sing-box TUIC tunnel + local PAC server for Notion
#
# Usage:  netsuite.ps1 -Action on|off|status|ensure|restart [-IncludeZapret]

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$cfg = Get-Content (Join-Path $root 'netsuite.json') -Raw | ConvertFrom-Json

# Config keeps %TOKENS% instead of absolute paths, so the repo stays ASCII-only
# and works regardless of the Cyrillic user/profile names on disk.
function Expand-Path([string]$p) {
    if (-not $p) { return $p }
    $p = $p.Replace('%REPO%', $root)
    $p = $p.Replace('%LOCALAPPDATA%', $env:LOCALAPPDATA)
    $p = $p.Replace('%APPDATA%', $env:APPDATA)
    $p = $p.Replace('%USERPROFILE%', $env:USERPROFILE)
    $p = $p.Replace('%DOCUMENTS%', [Environment]::GetFolderPath('MyDocuments'))
    return $p
}

foreach ($cname in @($cfg.components.PSObject.Properties.Name)) {
    $comp = $cfg.components.$cname
    foreach ($prop in @($comp.PSObject.Properties.Name)) {
        if ($comp.$prop -is [string]) { $comp.$prop = Expand-Path $comp.$prop }
    }
}

$logDir = Join-Path $root 'logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$logFile = Join-Path $logDir ("netsuite-{0}.log" -f (Get-Date -Format 'yyyyMMdd'))

function Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "{0}  {1,-5} {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    if ($Action -ne 'ensure') { Write-Host $line }
}

function Test-Process([string]$name) {
    return ((Get-Process -Name $name -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0)
}

function Test-Port([int]$port) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $ok = $client.ConnectAsync('127.0.0.1', $port).Wait(1500)
        return ($ok -and $client.Connected)
    }
    catch { return $false }
    finally { $client.Close() }
}

function Test-Url([string]$url, [int]$timeoutSec = 10) {
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time $timeoutSec $url 2>$null
    return ($code -match '^(200|204|30[1278])$')
}

# ---------------- zapret ----------------

function Get-ZapretState {
    if (Test-Process $cfg.components.zapret.process) { return 'running' }
    return 'stopped'
}

# Real check: winws may be alive while its profile no longer defeats DPI.
function Get-ZapretHealth {
    $check = Join-Path $root 'zapret\check.ps1'
    if (-not (Test-Path $check)) { return $null }
    try { return (& $check) } catch { return $null }
}

# Runs the picker with a UAC prompt (it needs admin to touch WinDivert and the service).
function Invoke-ZapretFix {
    $bat = Join-Path $root 'zapret\pick.bat'
    if (-not (Test-Path $bat)) { Log 'zapret: picker not found' 'WARN'; return }
    Log 'zapret: launching profile picker (confirm the UAC prompt)'
    try { Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', ('"' + $bat + '"')) -Verb RunAs | Out-Null }
    catch { Log ("zapret: elevation denied ({0})" -f $_.Exception.Message) 'WARN' }
}

function Start-Zapret {
    if (Get-ZapretState -eq 'running') { Log 'zapret: already running'; return $true }
    $svc = $cfg.components.zapret.service
    try {
        $s = Get-Service -Name $svc -ErrorAction Stop
        if ($s.Status -ne 'Running') { Start-Service -Name $svc -ErrorAction Stop }
        Start-Sleep -Seconds 2
        if (Get-ZapretState -eq 'running') { Log 'zapret: started via service'; return $true }
    }
    catch {
        Log ("zapret: cannot start service ({0}). Run service.bat as administrator once." -f $_.Exception.Message) 'WARN'
    }
    Log 'zapret: NOT running' 'WARN'
    return $false
}

# Called after zapret is up: if the profile stopped working, offer the picker.
function Test-ZapretWorking {
    $h = Get-ZapretHealth
    if (-not $h) { return $true }
    if ($h.State -eq 'ok') { Log ("zapret: healthy ({0})" -f $h.Targets); return $true }
    Log ("zapret: {0} ({1})" -f $h.State, $h.Targets) 'WARN'
    if ($h.State -eq 'dead') {
        Log 'zapret: profile stopped defeating DPI, a different one is needed'
        if ($FixZapret) { Invoke-ZapretFix } else { Log 'zapret: run zapret\pick.bat to test the profiles' 'WARN' }
    }
    return $false
}

# ---------------- TgWsProxy ----------------

function Start-Tgws {
    $c = $cfg.components.tgws
    if (-not $c.enabled) { return $true }
    if (Test-Process $c.process) {
        Log ("tgws: already running, port {0}: {1}" -f $c.port, (Test-Port $c.port))
        return $true
    }
    if (-not (Test-Path $c.exe)) { Log ("tgws: exe not found: {0}" -f $c.exe) 'ERROR'; return $false }
    Start-Process -FilePath $c.exe -WorkingDirectory (Split-Path -Parent $c.exe) -WindowStyle Minimized
    $deadline = (Get-Date).AddSeconds($c.waitSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Port $c.port) { Log ("tgws: started, port {0} is up" -f $c.port); return $true }
        Start-Sleep -Milliseconds 700
    }
    Log ("tgws: started but port {0} is still closed" -f $c.port) 'WARN'
    return $false
}

# ---------------- notion tunnel + PAC ----------------

function Start-Notion {
    $c = $cfg.components.notion
    if (-not $c.enabled) { return $true }
    $ok = $false
    if (Test-Port $c.socksPort) {
        $ok = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 12 --socks5-hostname ("127.0.0.1:{0}" -f $c.socksPort) $c.checkUrl 2>$null
        $ok = ($ok -eq '200')
    }
    if ($ok) { Log 'notion: tunnel is fine' }
    else {
        Log 'notion: tunnel dead, starting/repairing'
        $boot = Join-Path $c.folder $c.boot
        if (Test-Path $boot) {
            Start-Process -FilePath 'wscript.exe' -ArgumentList @("`"$boot`"") -WindowStyle Hidden
            Start-Sleep -Seconds 8
        }
    }
    # PAC server
    if (-not (Test-Url $c.pacUrl 5)) {
        Log 'notion: PAC server is down, restarting'
        $boot = Join-Path $c.folder $c.boot
        if (Test-Path $boot) {
            Start-Process -FilePath 'wscript.exe' -ArgumentList @("`"$boot`"") -WindowStyle Hidden
            Start-Sleep -Seconds 5
        }
    }
    Ensure-SystemPac
    return (Test-Port $c.socksPort)
}

function Repair-Notion {
    $c = $cfg.components.notion
    $code = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 12 --socks5-hostname ("127.0.0.1:{0}" -f $c.socksPort) $c.checkUrl 2>$null
    if ($code -eq '200') { return $true }
    $watch = Join-Path $c.folder $c.watch
    if (Test-Path $watch) {
        Log 'notion: searching for a fresh node (can take a few minutes)'
        & powershell -NoProfile -ExecutionPolicy Bypass -File $watch *>> $logFile
    }
    $code = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 15 --socks5-hostname ("127.0.0.1:{0}" -f $c.socksPort) $c.checkUrl 2>$null
    return ($code -eq '200')
}

# Firefox (network.proxy.type=5, the default) and WinINET apps read the PAC from
# this registry value, so it has to stay pointed at our local PAC server.
function Ensure-SystemPac {
    $want = $cfg.components.notion.pacUrl
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    try {
        $cur = (Get-ItemProperty -Path $key -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL
        if ($cur -ne $want) {
            Set-ItemProperty -Path $key -Name AutoConfigURL -Value $want -ErrorAction Stop
            Log ("pac: AutoConfigURL -> {0} (was '{1}')" -f $want, $cur)
        }
    }
    catch { Log ("pac: cannot set AutoConfigURL: {0}" -f $_.Exception.Message) 'WARN' }
}

# ---------------- actions ----------------

function Show-Status {
    $z = Get-ZapretState
    $t = Test-Process $cfg.components.tgws.process
    $tp = Test-Port $cfg.components.tgws.port
    $s = Test-Port $cfg.components.notion.socksPort
    $pac = Test-Url $cfg.components.notion.pacUrl 5
    $zh = Get-ZapretHealth
    $zDetail = 'service zapret'
    if ($zh) { $zDetail = ("{0} | {1}" -f $zh.State, $zh.Targets) }
    $notion = ''
    if ($s) {
        $notion = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 12 --socks5-hostname ("127.0.0.1:{0}" -f $cfg.components.notion.socksPort) $cfg.components.notion.checkUrl 2>$null
    }
    $rows = @(
        [pscustomobject]@{ Component = 'zapret (winws)'; State = $z; Detail = $zDetail }
        [pscustomobject]@{ Component = 'TgWsProxy'; State = $(if ($t) { 'running' } else { 'stopped' }); Detail = ("port {0}: {1}" -f $cfg.components.tgws.port, $(if ($tp) { 'up' } else { 'closed' })) }
        [pscustomobject]@{ Component = 'notion tunnel'; State = $(if ($notion -eq '200') { 'ok' } elseif ($s) { 'degraded' } else { 'stopped' }); Detail = ("notion.com -> {0}" -f $(if ($notion) { $notion } else { 'n/a' })) }
        [pscustomobject]@{ Component = 'PAC server'; State = $(if ($pac) { 'ok' } else { 'down' }); Detail = $cfg.components.notion.pacUrl }
    )
    $rows | Format-Table -AutoSize | Out-String | Write-Host
    return $rows
}

switch ($Action) {
    'status' {
        $null = Show-Status
    }
    'on' {
        Log '=== bringing everything up ==='
        $null = Start-Zapret
        $null = Test-ZapretWorking
        $null = Start-Tgws
        $null = Start-Notion
        if (-not (Repair-Notion)) { Log 'notion: still not working after repair attempt' 'WARN' }
        Log '=== done ==='
        $null = Show-Status
    }
    'ensure' {
        # silent, idempotent - for autostart and the scheduled task
        $null = Start-Zapret
        $null = Test-ZapretWorking
        $null = Start-Tgws
        $null = Start-Notion
        $null = Repair-Notion
        Log 'ensure: pass finished'
    }
    'restart' {
        Log '=== restarting local parts ==='
        Get-Process -Name $cfg.components.tgws.process -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Get-Process -Name 'sing-box', 'xray' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $null = Start-Zapret
        $null = Test-ZapretWorking
        $null = Start-Tgws
        $null = Start-Notion
        Log '=== done ==='
        $null = Show-Status
    }
    'zapret-fix' {
        Invoke-ZapretFix
    }
    'off' {
        Log '=== shutting down ==='
        Get-Process -Name $cfg.components.tgws.process -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Log 'tgws: stopped'
        Get-Process -Name 'sing-box', 'xray' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        $me = $PID
        Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessId -ne $me -and $_.CommandLine -like '*-File*pacsrv.ps1*' } |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Log 'notion: tunnel and PAC server stopped'
        if ($IncludeZapret) {
            try { Stop-Service -Name $cfg.components.zapret.service -ErrorAction Stop; Log 'zapret: service stopped' }
            catch { Log 'zapret: cannot stop service without administrator' 'WARN' }
        }
        else { Log 'zapret: left running (use -IncludeZapret to stop it)' }
    }
}
