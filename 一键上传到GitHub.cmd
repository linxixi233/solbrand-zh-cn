@echo off
chcp 65001 >nul
title Solbrand - GitHub Sync
cd /d "%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Sync-GitHub.ps1"
set "SYNC_EXIT=%ERRORLEVEL%"
echo.
if not "%SYNC_EXIT%"=="0" echo Upload stopped. Read the message above, then run this file again.
pause
exit /b %SYNC_EXIT%
