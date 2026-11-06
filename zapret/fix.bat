@echo off
cd /d "%~dp0"
rem Fast zapret repair: start the service, and if the profile is dead pick the first working one.
rem Needs elevation: UAC prompt appears once.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-NoExit','-File','\"%~dp0fix.ps1\"'"
