[CmdletBinding()]
param(
    [string]$Version = $env:GITHUB_REF_NAME,
    [string]$OutputDirectory,
    [switch]$AllowMissingBinaries
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "dist"
}
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = "local-windows-native-" + (Get-Date -Format "yyyyMMdd-HHmmss")
}

$safeVersion = $Version -replace '[^\w\.\-]+', '-'
$distRoot = New-Item -ItemType Directory -Force -Path $OutputDirectory
$packageName = "Stadia-X-Windows-Native-$safeVersion"
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

& (Join-Path $PSScriptRoot "Download-WindowsNativeDependencies.ps1") -OutputDirectory (Join-Path $repoRoot "dependencies")

$requiredFiles = @(
    "VERSION.txt",
    "README-WINDOWS-NATIVE.md",
    "LICENSE.txt",
    "assets\StadiaX-WindowsNative.ico",
    "assets\StadiaX-WindowsNative-icon.png",
    "assets\StadiaControllerPhoto.png",
    "assets\ATTRIBUTION.md",
    "dependencies\HidHide_1.5.230_x64.exe",
    "dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe",
    "dependencies\THIRD-PARTY-NOTICES.txt"
)

$binaryFiles = @(
    "StadiaX.exe",
    "ViGEmClient.dll"
)

foreach ($relativePath in $requiredFiles) {
    $source = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $source)) {
        throw "Required Windows Native package file missing: $relativePath"
    }

    $destination = Join-Path $packageRoot $relativePath
    $destinationDirectory = Split-Path -Parent $destination
    if (-not (Test-Path $destinationDirectory)) {
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    }
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

foreach ($relativePath in $binaryFiles) {
    $source = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $source)) {
        if ($AllowMissingBinaries) {
            Write-Warning "Binary missing, skipping: $relativePath"
            continue
        }
        throw "Required Windows Native binary missing: $relativePath. Build artifacts first or pass -AllowMissingBinaries for a source-only dry run."
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $packageRoot $relativePath) -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -Path $zipPath
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Set-Content -Encoding ASCII -Path $shaPath

Write-Host "Windows Native package: $zipPath"
Write-Host "SHA256:                 $shaPath"
