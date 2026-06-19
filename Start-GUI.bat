@echo off
set "SCRIPT_DIR=%~dp0"
if exist "%SCRIPT_DIR%StadiaX.exe" (
    start "" "%SCRIPT_DIR%StadiaX.exe"
    exit /b
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%StadiaX-GUI.ps1"
