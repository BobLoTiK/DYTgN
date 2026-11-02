@echo off
cd /d "%~dp0"
rem Start the zapret service (needs elevation: UAC prompt appears once).
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath cmd.exe -Verb RunAs -ArgumentList '/c','sc start zapret & timeout /t 3 /nobreak >nul & sc query zapret & pause'"
