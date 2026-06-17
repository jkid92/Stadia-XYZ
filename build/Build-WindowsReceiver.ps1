[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$OutputDirectory = (Join-Path $PSScriptRoot ".."),
    [switch]$SkipDependencyDownload
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Platform -ne "x64") {
    throw "Only x64 builds are supported right now."
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$depsRoot = Join-Path $repoRoot ".deps"
$vigemRoot = Join-Path $depsRoot "ViGEmClient"
$vigemInclude = Join-Path $vigemRoot "include"
$vigemLibDir = Join-Path $vigemRoot "lib\x64"
$vigemBinDir = Join-Path $vigemRoot "bin\x64"
$receiverSource = Join-Path $PSScriptRoot "stadia_receiver.cpp"
$outDir = New-Item -ItemType Directory -Force -Path $OutputDirectory
$receiverExe = Join-Path $outDir.FullName "stadia_receiver.exe"
$vigemDllOut = Join-Path $outDir.FullName "ViGEmClient.dll"

$vigemBuildBase = "https://buildbot.nefarius.at/builds/ViGEmClient/master/1.16.106.0/bin/release/x64"
$vigemHeaderUrl = "https://raw.githubusercontent.com/nefarius/ViGEmClient/master/include/ViGEm/Client.h"

function Get-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$Url,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $parent = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    if (Test-Path $Destination) {
        return
    }

    Write-Host "Downloading $Url"
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Destination
}

if (-not $SkipDependencyDownload) {
    Get-RequiredFile -Url $vigemHeaderUrl -Destination (Join-Path $vigemInclude "ViGEm\Client.h")
    Get-RequiredFile -Url "$vigemBuildBase/ViGEmClient.lib" -Destination (Join-Path $vigemLibDir "ViGEmClient.lib")
    Get-RequiredFile -Url "$vigemBuildBase/ViGEmClient.dll" -Destination (Join-Path $vigemBinDir "ViGEmClient.dll")
}

$requiredDeps = @(
    (Join-Path $vigemInclude "ViGEm\Client.h"),
    (Join-Path $vigemLibDir "ViGEmClient.lib"),
    (Join-Path $vigemBinDir "ViGEmClient.dll")
)

foreach ($dep in $requiredDeps) {
    if (-not (Test-Path $dep)) {
        throw "Missing dependency: $dep"
    }
}

$cl = Get-Command cl.exe -ErrorAction SilentlyContinue
if (-not $cl) {
    throw "cl.exe was not found. Run this from a Developer PowerShell, or use the GitHub Actions workflow."
}

Push-Location $PSScriptRoot
try {
    $compileArgs = @(
        "/nologo",
        "/std:c++17",
        "/EHsc",
        "/O2",
        "/MD",
        "/I", $vigemInclude,
        $receiverSource,
        "/Fe:$receiverExe",
        "/link",
        "/LIBPATH:$vigemLibDir",
        "ViGEmClient.lib",
        "ws2_32.lib",
        "setupapi.lib",
        "user32.lib"
    )

    Write-Host "Building stadia_receiver.exe ($Configuration/$Platform)"
    & $cl.Source @compileArgs
    if ($LASTEXITCODE -ne 0) {
        throw "MSVC build failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Copy-Item -Force (Join-Path $vigemBinDir "ViGEmClient.dll") $vigemDllOut
Write-Host "Built $receiverExe"
Write-Host "Copied $vigemDllOut"
