@echo off
cd /d "%~dp0"
rem Pick a working zapret profile. Needs elevation: UAC prompt appears once.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','\"%~dp0pick.ps1\"'"
