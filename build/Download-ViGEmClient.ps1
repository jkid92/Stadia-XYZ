[CmdletBinding()]
param(
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $OutputDirectory = Join-Path $scriptRoot ".."
}

$outDir = New-Item -ItemType Directory -Force -Path $OutputDirectory
$destination = Join-Path $outDir.FullName "ViGEmClient.dll"
$url = "https://buildbot.nefarius.at/builds/ViGEmClient/master/1.16.106.0/bin/release/x64/ViGEmClient.dll"

if (-not (Test-Path $destination)) {
    Write-Host "Downloading ViGEmClient.dll"
    Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $destination
}

Write-Host "ViGEmClient.dll: $destination"
