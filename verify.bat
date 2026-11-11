@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  DeskPins - Verify: build + tests, then check-docs.sh
rem  Build/tests use PowerShell directly; check-docs needs bash
rem  (Git for Windows). Mirrors: sh manager.sh verify
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
echo  Verify  ^|  scripts\build.ps1 -Test + check-docs.sh
echo ============================================================
"%PS%" -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -Test
set "RC=%ERRORLEVEL%"
if not "%RC%"=="0" goto :AfterDocs

:TryProgramFiles
if exist "%ProgramFiles%\Git\bin\bash.exe" goto :BashProgramFiles
goto :TryLocalAppData

:BashProgramFiles
"%ProgramFiles%\Git\bin\bash.exe" check-docs.sh
set "RC=%ERRORLEVEL%"
goto :AfterDocs

:TryLocalAppData
if exist "%LocalAppData%\Programs\Git\bin\bash.exe" goto :BashLocalAppData
goto :TryPathBash

:BashLocalAppData
"%LocalAppData%\Programs\Git\bin\bash.exe" check-docs.sh
set "RC=%ERRORLEVEL%"
goto :AfterDocs

:TryPathBash
where bash >nul 2>&1
if errorlevel 1 goto :NoBash
bash check-docs.sh
set "RC=%ERRORLEVEL%"
goto :AfterDocs

:NoBash
echo [ERROR] bash not found. Install Git for Windows to run check-docs.
set "RC=1"

:AfterDocs
popd
if not "%RC%"=="0" goto :Fail

echo.
echo [SUCCESS] Verify passed.
exit /b 0

:NoPowerShell
echo [ERROR] PowerShell not found. Install PowerShell 7 or use Windows PowerShell 5.1.
popd
exit /b 1

:Fail
echo.
echo [FAILED] verify failed ^(exit code %RC%^).
if not "%DESKPINS_NO_PAUSE%"=="1" pause
exit /b %RC%
