# Creates desktop shortcuts and the autostart entry for net-suite.
# Run once after cloning/moving the repo:  powershell -ExecutionPolicy Bypass -File shortcuts.ps1
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
$icon = 'C:\Program Files\Yandex\YandexBrowser\Application\browser.exe,0'

New-Link -Path (Join-Path $desk 'Сеть вкл.lnk')    -Target $cmd -Arguments "/c `"`"$root\on.bat`"`""     -Icon $icon -Description 'zapret + TgWsProxy + туннель Notion'
New-Link -Path (Join-Path $desk 'Сеть выкл.lnk')   -Target $cmd -Arguments "/c `"`"$root\off.bat`"`"`"" -Icon $icon -Description 'Погасить TgWsProxy и туннель Notion'
New-Link -Path (Join-Path $desk 'Сеть статус.lnk') -Target $cmd -Arguments "/c `"`"$root\status.bat`"`"" -Icon $icon -Description 'Показать состояние компонентов'

New-Link -Path (Join-Path $startup 'net-suite.lnk') -Target "$env:WINDIR\System32\wscript.exe" -Arguments "`"$root\autostart.vbs`"" -Icon $icon -Description 'Запуск сетевого набора при входе'

# the old single-purpose autostart entry is superseded by net-suite
$old = Join-Path $startup 'Notion tunnel.lnk'
if (Test-Path $old) { Remove-Item $old -Force; Write-Host 'removed old startup link: Notion tunnel.lnk' }
