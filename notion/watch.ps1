param([int]$MaxNodes = 60, [int]$Batch = 12)
# Keeps the background Notion route alive: local PAC server + tunnel.
# Nothing system-wide except HKCU AutoConfigURL pointing at the local PAC.
# The PAC sends only notion domains through the local SOCKS port; everything else is DIRECT.

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $dir 'watch.log'
$pacUrl = 'http://127.0.0.1:10810/proxy.pac'

function Note([string]$m) {
    Add-Content -Path $log -Value ("{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $m) -Encoding UTF8
}

function Test-Tunnel {
    $code = & curl.exe -s -L -o NUL -w "%{http_code}" --max-time 10 --socks5-hostname 127.0.0.1:10808 'https://www.notion.com/' 2>$null
    return ($code -eq '200')
}

function Ensure-Background {
    Start-Process -FilePath 'wscript.exe' -ArgumentList @("`"$dir\boot.vbs`"") -WindowStyle Hidden
    Start-Sleep -Seconds 5
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    $cur = (Get-ItemProperty $key -ErrorAction SilentlyContinue).AutoConfigURL
    if ($cur -ne $pacUrl) {
        Set-ItemProperty $key -Name AutoConfigURL -Value $pacUrl -Type String
        Note "AutoConfigURL restored: $pacUrl"
    }
}

Ensure-Background
if (Test-Tunnel) { exit 0 }

Note 'tunnel dead - searching for a fresh node'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'build-tuic.ps1') -MaxNodes $MaxNodes *>> $log
if (Test-Tunnel) { Note 'rebuilt (hy2/tuic) - tunnel works'; exit 0 }

Note 'hy2/tuic gave nothing - trying vless'
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'collect.ps1') *>> $log
$cand = Join-Path $env:TEMP 'vpnfree\candidates.txt'
if (Test-Path $cand) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $dir 'build.ps1') -Source $cand -MaxNodes 600 -Batch $Batch -WantWinners 5 *>> $log
    if (Test-Tunnel) { Note 'rebuilt (vless) - tunnel works'; exit 0 }
}

Note 'nothing worked this round'
exit 1
