@echo off
cd /d "%~dp0"
"%ProgramFiles%\Git\bin\bash.exe" manager.sh test
exit /b %errorlevel%
