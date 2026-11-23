param(
    [switch]$NoDesktop,
    [switch]$NoAutostart,
    [switch]$RemoveAll,
    [string]$Icon,
    [string]$DesktopDir,
    [string]$StartupDir
)
# The desktop gets exactly one shortcut: "Сеть (пульт)" - the window with every button.
# The old per-action shortcuts (Сеть вкл / выкл / статус / Notion / Запрет подобрать) are
# removed here, so an update does not leave six icons behind.
# This file is UTF-8 WITH BOM on purpose: PowerShell 5.1 needs the BOM to read Cyrillic names.

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sh = New-Object -ComObject WScript.Shell

$desk = $DesktopDir
if (-not $desk) { $desk = [Environment]::GetFolderPath('Desktop') }
$startup = $StartupDir
if (-not $startup) { $startup = [Environment]::GetFolderPath('Startup') }

# WScript.Shell.Save() dies on a .lnk path outside the system ANSI codepage - on an English
# Windows "Сеть (пульт).lnk" is simply not created ("Unable to save shortcut"). IShellLinkW
# takes real Unicode strings, so the name works on any locale.
$unicodeLink = $true
try {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    public class ShellLinkObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    public interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public static class NsLink
    {
        public static void Create(string lnkPath, string target, string arguments, string workDir,
                                  string iconPath, int iconIndex, string description, int showCmd)
        {
            IShellLinkW link = (IShellLinkW)new ShellLinkObject();
            link.SetPath(target);
            if (arguments != null) link.SetArguments(arguments);
            if (!string.IsNullOrEmpty(workDir)) link.SetWorkingDirectory(workDir);
            if (!string.IsNullOrEmpty(description)) link.SetDescription(description);
            if (!string.IsNullOrEmpty(iconPath)) link.SetIconLocation(iconPath, iconIndex);
            link.SetShowCmd(showCmd);
            ((IPersistFile)link).Save(lnkPath, false);
        }
    }
'@ -ErrorAction Stop
}
catch {
    Write-Host ("unicode shortcut writer unavailable ({0}), falling back to WScript.Shell" -f $_.Exception.Message.Split([char]10)[0])
    $unicodeLink = $false
}

function New-Link {
    param($Path, $Target, $Arguments, $Icon, $Description, $WindowStyle = 7)

    $iconPath = $Icon
    $iconIndex = 0
    if ($iconPath -match '^(.*),(-?\d+)$') { $iconPath = $matches[1]; $iconIndex = [int]$matches[2] }

    if ($unicodeLink) {
        try {
            [NsLink]::Create($Path, $Target, $Arguments, $root, $iconPath, $iconIndex, $Description, $WindowStyle)
            Write-Host ("created: {0}" -f $Path)
            return
        }
        catch {
            Write-Host ("unicode write failed ({0}), falling back" -f $_.Exception.Message.Split([char]10)[0])
        }
    }

    # fallback: WScript.Shell writes the file under an ASCII temp name, then the .lnk is
    # renamed into place, so the Cyrillic name never goes through the ANSI codepage
    $tmp = Join-Path $env:TEMP ("ns_" + [guid]::NewGuid().ToString('N') + ".lnk")
    $lnk = $sh.CreateShortcut($tmp)
    $lnk.TargetPath = $Target
    $lnk.Arguments = $Arguments
    $lnk.WorkingDirectory = $root
    $lnk.IconLocation = $Icon
    $lnk.Description = $Description
    $lnk.WindowStyle = $WindowStyle
    $lnk.Save()
    Move-Item -LiteralPath $tmp -Destination $Path -Force
    Write-Host ("created: {0}" -f $Path)
}

$cmd = "$env:WINDIR\System32\cmd.exe"

# default icon ships in the repo (icons\*.ico); override with -Icon "<path>,0"
$appIcon = $Icon
if (-not $appIcon) {
    $candidate = Join-Path $root 'icons\E-power-wifi.ico'
    if (Test-Path $candidate) { $appIcon = $candidate }
}
if (-not $appIcon) { $appIcon = "$env:WINDIR\System32\shell32.dll,13" }

$panelName = 'Сеть (пульт).lnk'
$panelPath = Join-Path $desk $panelName
$legacy = @('Сеть вкл.lnk', 'Сеть выкл.lnk', 'Сеть статус.lnk', 'Notion.lnk', 'Запрет подобрать.lnk')
$autostartLink = Join-Path $startup 'net-suite.lnk'
$oldStartup = Join-Path $startup 'Notion tunnel.lnk'

if ($RemoveAll) {
    if (Test-Path -LiteralPath $panelPath) { Remove-Item -LiteralPath $panelPath -Force; Write-Host ("removed: {0}" -f $panelPath) }
    foreach ($name in $legacy) {
        $p = Join-Path $desk $name
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; Write-Host ("removed: {0}" -f $p) }
    }
    if (Test-Path -LiteralPath $autostartLink) { Remove-Item -LiteralPath $autostartLink -Force; Write-Host ("removed: {0}" -f $autostartLink) }
    return
}

# the five single-action icons are obsolete: the panel does all of it
foreach ($name in $legacy) {
    $p = Join-Path $desk $name
    if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; Write-Host ("removed old shortcut: {0}" -f $p) }
}

# the one shortcut worth having: a real exe, so the window opens with no console flash
if (-not $NoDesktop) {
    $guiExe = Join-Path $root 'gui\NetSuite.exe'
    if (Test-Path $guiExe) {
        New-Link -Path $panelPath -Target $guiExe -Arguments '' -Icon $appIcon -WindowStyle 1 `
            -Description 'net-suite: состояние сети и все кнопки в одном окне'
    }
    else {
        New-Link -Path $panelPath -Target $cmd -Arguments "/c `"`"$root\auto.bat`"`"" -Icon $appIcon `
            -Description 'net-suite: включить всё (окно ещё не собрано)'
    }
}
else {
    Write-Host ("desktop shortcut: not created ({0} left as it is)" -f $panelName)
}

if (-not $NoAutostart) {
    New-Link -Path $autostartLink -Target "$env:WINDIR\System32\wscript.exe" -Arguments "`"$root\autostart.vbs`"" -Icon $appIcon -Description 'Запуск сетевого набора при входе'
}
else {
    if (Test-Path -LiteralPath $autostartLink) { Remove-Item -LiteralPath $autostartLink -Force; Write-Host ("removed: {0}" -f $autostartLink) }
    else { Write-Host 'autostart entry: not present' }
}

# the old single-purpose autostart entry is superseded by net-suite
if (Test-Path -LiteralPath $oldStartup) { Remove-Item -LiteralPath $oldStartup -Force; Write-Host 'removed old startup link: Notion tunnel.lnk' }
