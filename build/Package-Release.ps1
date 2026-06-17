[CmdletBinding()]
param(
    [string]$Version = $env:GITHUB_REF_NAME,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "..\dist"),
    [switch]$AllowMissingBinaries
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "local-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

$safeVersion = $Version -replace '[^\w\.\-]+', '-'
$distRoot = New-Item -ItemType Directory -Force -Path $OutputDirectory
$packageName = "Stadia-X-$safeVersion"
$packageRoot = Join-Path $distRoot.FullName $packageName
$zipPath = Join-Path $distRoot.FullName "$packageName.zip"
$shaPath = "$zipPath.sha256"

if (Test-Path $packageRoot) {
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
if (Test-Path $shaPath) {
    Remove-Item -LiteralPath $shaPath -Force
}

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null

$requiredFiles = @(
    "Start-GUI.bat",
    "Install-StadiaX.bat",
    "Install-StadiaX.ps1",
    "StadiaX-GUI.ps1",
    "Start-Stadia.bat",
    "Stop-Stadia.bat",
    "Check-Battery.bat",
    "start.sh",
    "stadia_buttons.ini",
    "README.md",
    "BUILD.md",
    "LICENSE.txt"
)

$binaryFiles = @(
    "stadia_receiver.exe",
    "ViGEmClient.dll",
    "stadia_bridge"
)

foreach ($relativePath in $requiredFiles) {
    $source = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $source)) {
        throw "Required package file missing: $relativePath"
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $packageRoot $relativePath) -Force
}

foreach ($relativePath in $binaryFiles) {
    $source = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $source)) {
        if ($AllowMissingBinaries) {
            Write-Warning "Binary missing, skipping: $relativePath"
            continue
        }
        throw "Required binary missing: $relativePath. Build artifacts first or pass -AllowMissingBinaries for a source-only dry run."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $packageRoot $relativePath) -Force
}

$sourceDir = Join-Path $packageRoot "source"
New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "build") -Destination $sourceDir -Recurse -Force

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -Path $zipPath
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Set-Content -Encoding ASCII -Path $shaPath

Write-Host "Package: $zipPath"
Write-Host "SHA256:  $shaPath"
