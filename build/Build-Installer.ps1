[CmdletBinding()]
param(
    [string]$Version = $env:GITHUB_REF_NAME,
    [string]$PackageDirectory,
    [string]$OutputDirectory,
    [string]$InnoSetupCompiler
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

if ([string]::IsNullOrWhiteSpace($PackageDirectory)) {
    $PackageDirectory = Join-Path $distRoot.FullName "Stadia-X-$safeVersion"
}

if (-not (Test-Path $PackageDirectory)) {
    throw "Package directory not found: $PackageDirectory. Run Package-Release.ps1 first."
}

function Find-InnoSetupCompiler {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        if (Test-Path $RequestedPath) { return (Resolve-Path $RequestedPath).Path }
        throw "Inno Setup compiler not found at requested path: $RequestedPath"
    }

    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates += (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe")
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates += (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6, or let GitHub Actions install it."
}

$sourceDir = (Resolve-Path $PackageDirectory).Path
$outputDir = $distRoot.FullName
$issPath = Join-Path $PSScriptRoot "StadiaX.iss"
$iscc = Find-InnoSetupCompiler $InnoSetupCompiler

$requiredFiles = @(
    "Start-GUI.bat",
    "Start-GUI-CSharp.bat",
    "VERSION.txt",
    "StadiaX-GUI.ps1",
    "Start-Stadia.bat",
    "Stop-Stadia.bat",
    "Check-Battery.bat",
    "Resolve-WslDistro.ps1",
    "Test-StadiaX.ps1",
    "StadiaX.exe",
    "start.sh",
    "stadia_buttons.ini",
    "stadia_receiver.exe",
    "ViGEmClient.dll",
    "stadia_bridge",
    "LICENSE.txt"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $sourceDir $relativePath
    if (-not (Test-Path $path)) {
        throw "Installer source is incomplete. Missing: $relativePath"
    }
}

$setupPath = Join-Path $outputDir "Stadia-X-$safeVersion-Setup.exe"
$setupHashPath = "$setupPath.sha256"
if (Test-Path $setupPath) { Remove-Item -LiteralPath $setupPath -Force }
if (Test-Path $setupHashPath) { Remove-Item -LiteralPath $setupHashPath -Force }

Write-Host "Building installer from $sourceDir"
& $iscc `
    "/DMyAppVersion=$safeVersion" `
    "/DSourceDir=$sourceDir" `
    "/DOutputDir=$outputDir" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup build failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $setupPath)) {
    throw "Installer was not created: $setupPath"
}

$hash = Get-FileHash -Algorithm SHA256 -Path $setupPath
"$($hash.Hash)  $(Split-Path -Leaf $setupPath)" | Set-Content -Encoding ASCII -Path $setupHashPath

Write-Host "Installer: $setupPath"
Write-Host "SHA256:    $setupHashPath"
