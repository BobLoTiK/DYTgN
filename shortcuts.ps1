param(
    [switch]$NoDesktop,
    [switch]$NoAutostart,
    [switch]$RemoveAll
)
# Creates (or removes) desktop shortcuts and the autostart entry for net-suite.
# This file is UTF-8 WITH BOM on purpose: PowerShell 5.1 needs the BOM to read Cyrillic names.

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sh = New-Object -ComObject WScript.Shell
$desk = [Environment]::GetFolderPath('Desktop')
$startup = [Environment]::GetFolderPath('Startup')

function New-Link {
    param($Path, $Target, $Arguments, $Icon, $Description, $WindowStyle = 7)
    $lnk = $sh.CreateShortcut($Path)
    $lnk.TargetPath = $Target
    $lnk.Arguments = $Arguments
    $lnk.WorkingDirectory = $root
    $lnk.IconLocation = $Icon
    $lnk.Description = $Description
    $lnk.WindowStyle = $WindowStyle
    $lnk.Save()
    Write-Host ("created: {0}" -f $Path)
}

$cmd = "$env:WINDIR\System32\cmd.exe"
$ff = 'C:\Program Files\Mozilla Firefox\firefox.exe,0'
if (-not (Test-Path 'C:\Program Files\Mozilla Firefox\firefox.exe')) { $ff = "$env:WINDIR\System32\shell32.dll,13" }

$desktopLinks = @{
    'Сеть вкл.lnk'    = @{ Args = "/c `"`"$root\on.bat`"`"";     Desc = 'zapret + TgWsProxy + туннель Notion' }
    'Сеть выкл.lnk'   = @{ Args = "/c `"`"$root\off.bat`"`"`"";  Desc = 'Погасить TgWsProxy и туннель Notion' }
    'Сеть статус.lnk' = @{ Args = "/c `"`"$root\status.bat`"`""; Desc = 'Показать состояние компонентов' }
    'Notion.lnk'      = @{ Args = "/c `"`"$root\notion-window.bat`"`""; Desc = 'Notion в Firefox через туннель' }
}
$autostartLink = Join-Path $startup 'net-suite.lnk'
$oldStartup = Join-Path $startup 'Notion tunnel.lnk'

if ($RemoveAll) {
    foreach ($name in $desktopLinks.Keys) {
        $p = Join-Path $desk $name
        if (Test-Path $p) { Remove-Item $p -Force; Write-Host ("removed: {0}" -f $p) }
    }
    if (Test-Path $autostartLink) { Remove-Item $autostartLink -Force; Write-Host ("removed: {0}" -f $autostartLink) }
    return
}

if (-not $NoDesktop) {
    foreach ($name in $desktopLinks.Keys) {
        $info = $desktopLinks[$name]
        New-Link -Path (Join-Path $desk $name) -Target $cmd -Arguments $info.Args -Icon $ff -Description $info.Desc
    }
}
else {
    foreach ($name in $desktopLinks.Keys) {
        $p = Join-Path $desk $name
        if (Test-Path $p) { Remove-Item $p -Force; Write-Host ("removed: {0}" -f $p) }
    }
}

if (-not $NoAutostart) {
    New-Link -Path $autostartLink -Target "$env:WINDIR\System32\wscript.exe" -Arguments "`"$root\autostart.vbs`"" -Icon $ff -Description 'Запуск сетевого набора при входе'
}
else {
    if (Test-Path $autostartLink) { Remove-Item $autostartLink -Force; Write-Host ("removed: {0}" -f $autostartLink) }
    else { Write-Host 'autostart entry: not present' }
}

# the old single-purpose autostart entry is superseded by net-suite
if (Test-Path $oldStartup) { Remove-Item $oldStartup -Force; Write-Host 'removed old startup link: Notion tunnel.lnk' }
