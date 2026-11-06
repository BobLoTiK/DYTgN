@echo off
cd /d "%~dp0"
rem Point the zapret service at the copy inside the repo. Needs elevation: UAC prompt appears once.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','\"%~dp0relink.ps1\"'"
