# Starts the zapret Windows service. Needs administrator rights (UAC).
# Called by start.bat and by netsuite.ps1 on "Сеть вкл" when zapret is down.

param([string]$ServiceName = 'zapret')

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'administrator rights required (use zapret\start.bat)' -ForegroundColor Yellow
    exit 2
}

$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $svc) { Write-Host "service $ServiceName not found" -ForegroundColor Yellow; exit 1 }

if ($svc.Status -ne 'Running') {
    Write-Host "starting service $ServiceName"
    try { Start-Service -Name $ServiceName -ErrorAction Stop } catch { Write-Host ("failed: {0}" -f $_.Exception.Message) -ForegroundColor Yellow; exit 1 }
}

$deadline = (Get-Date).AddSeconds(10)
while ((Get-Date) -lt $deadline) {
    if ((Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0) { break }
    Start-Sleep -Milliseconds 500
}

$alive = (Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
$svc.Refresh()
Write-Host ("state: {0}, winws: {1}" -f (Get-Service -Name $ServiceName).Status, $alive)
if ($alive) { exit 0 } else { exit 1 }
