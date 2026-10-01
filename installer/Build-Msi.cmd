@echo off
setlocal
cd /d "%~dp0.."

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build-Msi.ps1" -OutputDir "%~dp0output"

echo.
if errorlevel 1 (
    echo MSI generation failed. Review the error above.
) else (
    echo MSI generation completed.
)
pause
