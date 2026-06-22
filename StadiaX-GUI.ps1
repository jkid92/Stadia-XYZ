[CmdletBinding()]
param(
    [switch]$StartBridge,
    [switch]$StopBridge
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = Join-Path $root "StadiaX.exe"
$project = Join-Path $root "src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj"
if (-not (Test-Path $project)) {
    $project = Join-Path $root "source\src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj"
}

$arguments = @()
if ($StartBridge) { $arguments += "--start-bridge" }
if ($StopBridge) { $arguments += "--stop-bridge" }

if (Test-Path $app) {
    Start-Process -FilePath $app -ArgumentList $arguments -WorkingDirectory $root
    return
}

if (Test-Path $project) {
    $dotnetArgs = @("run", "--project", $project, "--no-launch-profile")
    if ($arguments.Count -gt 0) {
        $dotnetArgs += "--"
        $dotnetArgs += $arguments
    }

    & dotnet @dotnetArgs
    return
}

throw "Could not find StadiaX.exe or the C# control center project."
