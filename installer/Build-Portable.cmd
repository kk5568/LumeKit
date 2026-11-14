@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Portable.ps1"
exit /b %ERRORLEVEL%
