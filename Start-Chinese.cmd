@echo off
cd /d "%~dp0"
set "PATCH_VERSION=0.3.0"
if not exist "runtime\Solbrand.exe" goto prepare
if not exist "runtime\zh-patch-version.txt" goto prepare
set /p "INSTALLED_VERSION="<"runtime\zh-patch-version.txt"
if not "%INSTALLED_VERSION%"=="%PATCH_VERSION%" goto prepare
start "" /D "%~dp0runtime" "%~dp0runtime\Solbrand.exe"
exit /b 0
:prepare
echo Please prepare patch %PATCH_VERSION% with PowerShell 7 first:
echo pwsh -NoProfile -File .\Prepare-Chinese.ps1
pause
exit /b 1
