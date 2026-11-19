# Builds gui\NetSuite.exe with the csc.exe that ships with .NET Framework 4.8.
# No SDK, no NuGet, no Visual Studio: one exe you can copy anywhere.
#
#   powershell -ExecutionPolicy Bypass -File gui\build.ps1
#
# The GUI is plain C# 5 on purpose - that is what this csc understands.

$ErrorActionPreference = 'Stop'
$gui = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $gui 'src'
$out = Join-Path $gui 'NetSuite.exe'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found (is .NET Framework 4.x installed?)' }

$icon = Join-Path (Split-Path -Parent $gui) 'icons\E-power-wifi.ico'
$files = Get-ChildItem -Path $src -Recurse -Filter '*.cs' | Sort-Object FullName | ForEach-Object { '"' + $_.FullName + '"' }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/utf8output'
    '/codepage:65001'
    '/out:"' + $out + '"'
    '/win32manifest:"' + (Join-Path $gui 'app.manifest') + '"'
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    '/r:System.Web.Extensions.dll'
    '/r:System.ServiceProcess.dll'
    '/r:System.Management.dll'
    '/r:System.Net.Http.dll'
) 
if (Test-Path $icon) { $cscArgs += ('/win32icon:"' + $icon + '"') }
$cscArgs += $files

"compiling {0} files with csc" -f $files.Count
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw ("csc failed with exit code {0}" -f $LASTEXITCODE) }
"built: {0} ({1:N0} KB)" -f $out, ((Get-Item $out).Length / 1KB)
