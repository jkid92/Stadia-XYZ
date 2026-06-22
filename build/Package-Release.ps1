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
    "Start-GUI-CSharp.bat",
    "VERSION.txt",
    "Install-StadiaX.bat",
    "Install-StadiaX.ps1",
    "StadiaX-GUI.ps1",
    "Start-Stadia.bat",
    "Stop-Stadia.bat",
    "Check-Battery.bat",
    "Resolve-WslDistro.ps1",
    "Test-StadiaX.ps1",
    "start.sh",
    "stadia_buttons.ini",
    "assets\StadiaX.ico",
    "assets\StadiaX-icon.png",
    "assets\StadiaControllerPhoto.png",
    "assets\ATTRIBUTION.md",
    "README.md",
    "BUILD.md",
    "LICENSE.txt"
)

$binaryFiles = @(
    "StadiaX.exe",
    "ViGEmClient.dll",
    "stadia_bridge"
)

foreach ($relativePath in $requiredFiles) {
    $source = Join-Path $repoRoot $relativePath
    if (-not (Test-Path $source)) {
        throw "Required package file missing: $relativePath"
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
        throw "Required binary missing: $relativePath. Build artifacts first or pass -AllowMissingBinaries for a source-only dry run."
    }
    Copy-Item -LiteralPath $source -Destination (Join-Path $packageRoot $relativePath) -Force
}

$sourceDir = Join-Path $packageRoot "source"
New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "build") -Destination $sourceDir -Recurse -Force
if (Test-Path (Join-Path $repoRoot "src")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "src") -Destination $sourceDir -Recurse -Force
    Get-ChildItem -LiteralPath (Join-Path $sourceDir "src") -Directory -Recurse |
        Where-Object { $_.Name -in @("bin", "obj") } |
        Remove-Item -Recurse -Force
}

Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -Algorithm SHA256 -Path $zipPath
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Set-Content -Encoding ASCII -Path $shaPath

Write-Host "Package: $zipPath"
Write-Host "SHA256:  $shaPath"
