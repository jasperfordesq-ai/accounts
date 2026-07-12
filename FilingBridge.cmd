@echo off
setlocal
set "FILINGBRIDGE_ROOT=%~dp0"
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%FILINGBRIDGE_ROOT%scripts\private-server.ps1" %*
set "FILINGBRIDGE_EXIT=%ERRORLEVEL%"
endlocal & exit /b %FILINGBRIDGE_EXIT%
