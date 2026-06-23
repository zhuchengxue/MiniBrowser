@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-MiniBrowser.ps1" -Launch
pause
