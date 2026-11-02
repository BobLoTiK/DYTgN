@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0netsuite.ps1" -Action status
if "%~1"=="" pause
