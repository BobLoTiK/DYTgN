@echo off
cd /d "%~dp0"
rem Start the zapret service. Needs elevation: UAC prompt appears once.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','\"%~dp0start.ps1\"'"
