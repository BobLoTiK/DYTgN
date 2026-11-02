param([switch]$SkipStart)
# One-time (or repeatable) setup: makes this folder self-sufficient and starts everything.
# Safe to re-run: it only fills in what is missing.

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = Join-Path $root 'bin'
$notion = Join-Path $root 'notion'
New-Item -ItemType Directory -Force -Path $bin, $notion | Out-Null

function Say([string]$m) { Write-Host $m }

function Get-GithubAsset {
    param([string]$Repo, [string]$Pattern, [string]$OutFile)
    try {
        $rel = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'ps' } -TimeoutSec 30
        $asset = $rel.assets | Where-Object { $_.name -match $Pattern } | Select-Object -First 1
        if (-not $asset) { Say "  no asset matching $Pattern in $Repo"; return $false }
        Say ("  downloading {0} ({1} MB)" -f $asset.name, [math]::Round($asset.size / 1MB, 1))
        $zip = Join-Path $env:TEMP $asset.name
        Invoke-WebRequest $asset.browser_download_url -OutFile $zip -TimeoutSec 600
        $tmpDir = Join-Path $env:TEMP ("ns_" + [guid]::NewGuid().ToString('N'))
        Expand-Archive $zip -DestinationPath $tmpDir -Force
        $exe = Get-ChildItem $tmpDir -Recurse -Filter (Split-Path -Leaf $OutFile) | Select-Object -First 1
        if (-not $exe) { Say "  $OutFile not found inside the archive"; return $false }
        Copy-Item $exe.FullName $OutFile -Force
        Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $zip -Force -ErrorAction SilentlyContinue
        return $true
    }
    catch { Say ("  download failed: {0}" -f $_.Exception.Message); return $false }
}

Say '=== 1/5 sing-box ==='
$sbExe = Join-Path $bin 'sing-box.exe'
if (Test-Path $sbExe) { Say '  ok, already present' }
else {
    Say '  not found, downloading from SagerNet/sing-box'
    $null = Get-GithubAsset -Repo 'SagerNet/sing-box' -Pattern 'windows-amd64\.zip$' -OutFile $sbExe
}

Say '=== 2/5 xray (fallback engine) ==='
$xrayExe = Join-Path $bin 'xray.exe'
if (Test-Path $xrayExe) { Say '  ok, already present' }
else {
    Say '  not found, downloading from XTLS/Xray-core'
    $null = Get-GithubAsset -Repo 'XTLS/Xray-core' -Pattern 'Xray-windows-64\.zip$' -OutFile $xrayExe
}

Say '=== 3/5 TgWsProxy ==='
$tgExe = Join-Path $bin 'TgWsProxy_windows.exe'
if (Test-Path $tgExe) { Say '  ok, already present' }
else {
    $candidates = @()
    foreach ($dir in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:USERPROFILE 'Downloads'), [Environment]::GetFolderPath('MyDocuments'))) {
        if ($dir -and (Test-Path $dir)) { $candidates += Get-ChildItem $dir -Filter 'TgWsProxy*.exe' -File -ErrorAction SilentlyContinue }
    }
    if ($candidates) {
        Copy-Item $candidates[0].FullName $tgExe -Force
        Say ("  copied from {0}" -f $candidates[0].FullName)
    }
    else {
        Say '  NOT FOUND. Put TgWsProxy_windows.exe next to this script or into bin\, then re-run install.bat.'
    }
}

Say '=== 4/5 node for the Notion tunnel ==='
$cfg = Join-Path $notion 'sb-config.json'
if (Test-Path $cfg) { Say '  ok, sb-config.json already present' }
else {
    Say '  no config, searching for a working free node (1-3 minutes)'
    $builder = Join-Path $notion 'build-tuic.ps1'
    if (Test-Path $builder) { & powershell -NoProfile -ExecutionPolicy Bypass -File $builder -MaxNodes 80 }
    if (Test-Path $cfg) { Say '  node found and saved' } else { Say '  no working node right now - the tunnel will retry later' }
}

Say '=== 5/5 shortcuts, autostart, scheduled task ==='
$sc = Join-Path $root 'shortcuts.ps1'
if (Test-Path $sc) { & powershell -NoProfile -ExecutionPolicy Bypass -File $sc }

$taskAction = "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$notion\watch.ps1`""
& schtasks /create /tn 'NotionTunnelKeepalive' /tr $taskAction /sc minute /mo 10 /f 2>&1 | Out-Null
Say '  keepalive task: every 10 minutes'

if (-not $SkipStart) {
    Say ''
    Say '=== starting everything ==='
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'netsuite.ps1') -Action on
}

Say ''
Say 'Done. Desktop shortcuts: "Net ON", "Net OFF", "Net STATUS" (or their Russian names).'
