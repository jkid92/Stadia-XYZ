[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory,
    [switch]$CopyToRoot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src\StadiaX.ControlCenter\StadiaX.ControlCenter.csproj"
if (-not (Test-Path $project)) {
    throw "C# control center project not found: $project"
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\csharp-control-center"
}

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:DebugType=embedded `
    --output $OutputDirectory

if ($CopyToRoot) {
    $exe = Join-Path $OutputDirectory "StadiaX.ControlCenter.exe"
    if (-not (Test-Path $exe)) {
        throw "Published executable not found: $exe"
    }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $repoRoot "StadiaX.ControlCenter.exe") -Force
}

Write-Host "C# Control Center: $OutputDirectory"
