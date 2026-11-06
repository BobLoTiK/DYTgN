# Points the existing "zapret" service at the copy that ships inside the repo.
# Needs administrator rights (service registry value + WinDivert).
#
#   powershell -ExecutionPolicy Bypass -File relink.ps1

param([string]$ServiceName = 'zapret')

$ErrorActionPreference = 'Continue'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $dir

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'administrator rights required (use zapret\relink.bat)' -ForegroundColor Yellow
    exit 2
}

$cfg = Get-Content (Join-Path $root 'netsuite.json') -Raw | ConvertFrom-Json
function Expand-Path([string]$p) {
    return $p.Replace('%REPO%', $root).Replace('%LOCALAPPDATA%', $env:LOCALAPPDATA).Replace('%APPDATA%', $env:APPDATA).Replace('%DOCUMENTS%', [Environment]::GetFolderPath('MyDocuments'))
}
$newDir = Expand-Path $cfg.components.zapret.folder
if (-not (Test-Path (Join-Path $newDir 'bin\winws.exe'))) { Write-Host "package not found: $newDir" -ForegroundColor Yellow; exit 1 }

$key = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
$imagePath = (Get-ItemProperty -Path $key -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
if (-not $imagePath) { Write-Host 'cannot read service ImagePath' -ForegroundColor Yellow; exit 1 }

if ($imagePath -like ('*' + $newDir + '*')) { Write-Host 'already points at the repo copy, nothing to do'; exit 0 }

# the service stores the whole command line; swap the directory that holds winws.exe
$exeMatch = [regex]::Match($imagePath, '"([^"]*winws\.exe)"')
if (-not $exeMatch.Success) { Write-Host 'unexpected ImagePath format' -ForegroundColor Yellow; exit 1 }
$oldExe = $exeMatch.Groups[1].Value
$oldDir = Split-Path -Parent (Split-Path -Parent $oldExe)
Write-Host "old dir: $oldDir"

$updated = $imagePath.Replace($oldDir, $newDir)
Set-ItemProperty -Path $key -Name ImagePath -Value $updated -Type ExpandString
Write-Host "new exe: $newDir\bin\winws.exe"

Restart-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 4

$alive = (Get-Process winws -ErrorAction SilentlyContinue | Measure-Object).Count -gt 0
$cur = (Get-ItemProperty -Path $key -Name ImagePath -ErrorAction SilentlyContinue).ImagePath
Write-Host ("winws running: {0}" -f $alive)
Write-Host ("points at repo: {0}" -f ($cur -like ('*' + $newDir + '*')))
if ($alive -and $cur -like ('*' + $newDir + '*')) { Write-Host 'ok: service now runs from the repo copy'; exit 0 }
Write-Host 'warning: verify manually' -ForegroundColor Yellow
exit 3
