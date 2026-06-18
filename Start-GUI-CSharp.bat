@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj"
if not exist "%PROJECT%" set "PROJECT=%SCRIPT_DIR%source\src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj"

if not exist "%PROJECT%" (
    echo Could not find the C# control center project.
    echo This preview launcher is available only from a source checkout or a package that includes source\src.
    pause
    exit /b 1
)

dotnet run --project "%PROJECT%" --no-launch-profile
