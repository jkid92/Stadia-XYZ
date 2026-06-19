[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDirectory,
    [switch]$CopyToRoot,
    [switch]$FrameworkDependent
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

$selfContained = if ($FrameworkDependent) { "false" } else { "true" }

dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded `
    --output $OutputDirectory

if ($CopyToRoot) {
    $exe = Join-Path $OutputDirectory "StadiaX.exe"
    if (-not (Test-Path $exe)) {
        throw "Published executable not found: $exe"
    }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $repoRoot "StadiaX.exe") -Force
}

Write-Host "C# Control Center: $OutputDirectory"
