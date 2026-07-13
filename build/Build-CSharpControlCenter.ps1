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

$dotnetCandidates = @(
    (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -Unique

$dotnet = $dotnetCandidates | Where-Object {
    $sdks = & $_ --list-sdks 2>$null
    $LASTEXITCODE -eq 0 -and $sdks
} | Select-Object -First 1

if (-not $dotnet) {
    throw "A .NET SDK is required to build the C# control center."
}

& $dotnet publish $project `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained $selfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugType=embedded `
    --output $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

if ($CopyToRoot) {
    $exe = Join-Path $OutputDirectory "StadiaX.exe"
    if (-not (Test-Path $exe)) {
        throw "Published executable not found: $exe"
    }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $repoRoot "StadiaX.exe") -Force
}

Write-Host "C# Control Center: $OutputDirectory"
