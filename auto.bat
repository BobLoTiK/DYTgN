@echo off
cd /d "%~dp0"
rem One button: check everything, fix zapret profile if it died, start all parts.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0netsuite.ps1" -Action auto
echo.
pause
