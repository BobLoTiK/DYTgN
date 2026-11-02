@echo off
cd /d "%~dp0"

rem tunnel + PAC, silent and idempotent
powershell -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0netsuite.ps1" -Action ensure

set "CODE="
for /f %%i in ('curl.exe -s -o NUL -w "%%{http_code}" --max-time 8 --socks5-hostname 127.0.0.1:10808 https://www.notion.com/') do set "CODE=%%i"

if not "%CODE%"=="200" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0notion\watch.ps1"
)

rem Firefox uses the system proxy settings by default (network.proxy.type=5),
rem so the PAC served at 127.0.0.1:10810 routes only Notion through the tunnel.
set "FF=C:\Program Files\Mozilla Firefox\firefox.exe"
if not exist "%FF%" set "FF=C:\Program Files (x86)\Mozilla Firefox\firefox.exe"

start "" "%FF%" "https://app.notion.com/"
exit /b 0
