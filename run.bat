@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  DeskPins - Run: run scripts/run.ps1
rem  Pure PowerShell entry; no Git Bash required.
rem  Mirrors: sh manager.sh run
rem ============================================================

pushd "%~dp0"
if errorlevel 1 exit /b 1

where pwsh >nul 2>&1
if errorlevel 1 goto :TryWindowsPowerShell
set "PS=pwsh"
goto :Run
:TryWindowsPowerShell
where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell
set "PS=powershell"

:Run
echo ============================================================
echo  Run  ^|  scripts\run.ps1
echo ============================================================
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" %*
set "RC=%ERRORLEVEL%"
popd
if not "%RC%"=="0" goto :Fail

echo.
echo [SUCCESS] App started.
exit /b 0

:NoPowerShell
echo [ERROR] PowerShell not found. Install PowerShell 7 or use Windows PowerShell 5.1.
popd
exit /b 1

:Fail
echo.
echo [FAILED] run failed ^(exit code %RC%^).
if not "%DESKPINS_NO_PAUSE%"=="1" pause
exit /b %RC%
