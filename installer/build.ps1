# Builds the installer: dist\net-suite-setup.exe from installer\Bootstrapper.cs.
# Same compiler as gui\build.ps1 - no .NET SDK, no NuGet.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $PSScriptRoot 'Bootstrapper.cs'
$outDir = Join-Path $root 'dist'
$out = Join-Path $outDir 'net-suite-setup.exe'
$icon = Join-Path $root 'icons\E-power-wifi.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }
if (-not (Test-Path $src)) { throw "source not found: $src" }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$args = @('/nologo', '/target:exe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001',
    ('/out:"{0}"' -f $out))
if (Test-Path $icon) { $args += ('/win32icon:"{0}"' -f $icon) }
$args += ('"{0}"' -f $src)

Write-Host ('compiling {0}' -f (Split-Path -Leaf $src))
& $csc @args
if ($LASTEXITCODE -ne 0) { throw ("csc failed with exit code {0}" -f $LASTEXITCODE) }
$size = (Get-Item $out).Length
Write-Host ('built: {0} ({1} KB)' -f $out, [math]::Round($size / 1KB))
