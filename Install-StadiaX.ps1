[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$NoShortcut,
    [switch]$StartAfterInstall
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$sourceRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    if ($isAdmin) {
        $InstallDirectory = Join-Path $env:ProgramFiles "Stadia X"
    } else {
        $InstallDirectory = Join-Path $env:LOCALAPPDATA "Programs\Stadia X"
    }
}

function Test-CommandAvailable {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-ViGEmBusInstalled {
    try {
        $service = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue
        if ($service) { return $true }
    } catch {}

    try {
        $devices = Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
            Where-Object { $_.Name -match "ViGEm|Virtual Gamepad Emulation" }
        return [bool]($devices | Select-Object -First 1)
    } catch {
        return $false
    }
}

function New-StadiaShortcut {
    param(
        [string]$ShortcutPath,
        [string]$TargetPath,
        [string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$env:SystemRoot\System32\shell32.dll,220"
    $shortcut.Description = "Launch Stadia X Control Center"
    $shortcut.Save()
}

$installRoot = New-Item -ItemType Directory -Force -Path $InstallDirectory
$sourceFull = (Resolve-Path $sourceRoot).Path.TrimEnd('\')
$targetFull = $installRoot.FullName.TrimEnd('\')

if ($sourceFull -ne $targetFull) {
    $excludeDirs = @(".git", ".deps", "artifacts", "dist", "logs")
    $items = Get-ChildItem -LiteralPath $sourceRoot -Force | Where-Object {
        $excludeDirs -notcontains $_.Name
    }

    foreach ($item in $items) {
        $destination = Join-Path $installRoot.FullName $item.Name
        if ($item.PSIsContainer) {
            Copy-Item -LiteralPath $item.FullName -Destination $destination -Recurse -Force
        } else {
            Copy-Item -LiteralPath $item.FullName -Destination $destination -Force
        }
    }
}

$launcher = Join-Path $installRoot.FullName "Start-GUI.bat"
if (-not (Test-Path $launcher)) {
    throw "Start-GUI.bat was not found in $($installRoot.FullName). The package looks incomplete."
}

if (-not $NoShortcut) {
    $desktop = [Environment]::GetFolderPath("Desktop")
    $startMenu = Join-Path ([Environment]::GetFolderPath("StartMenu")) "Programs"
    New-Item -ItemType Directory -Force -Path $startMenu | Out-Null
    New-StadiaShortcut -ShortcutPath (Join-Path $desktop "Stadia X.lnk") -TargetPath $launcher -WorkingDirectory $installRoot.FullName
    New-StadiaShortcut -ShortcutPath (Join-Path $startMenu "Stadia X.lnk") -TargetPath $launcher -WorkingDirectory $installRoot.FullName
}

$checks = [ordered]@{
    "Install folder" = $installRoot.FullName
    "ViGEmBus driver" = if (Test-ViGEmBusInstalled) { "OK" } else { "Missing - install ViGEmBus before playing" }
    "usbipd" = if (Test-CommandAvailable "usbipd") { "OK" } else { "Missing - Start-Stadia can install it with winget" }
    "wsl" = if (Test-CommandAvailable "wsl") { "OK" } else { "Missing - Start-Stadia can install Ubuntu/WSL" }
}

Write-Host ""
Write-Host "Stadia X installed."
Write-Host ""
foreach ($entry in $checks.GetEnumerator()) {
    Write-Host ("{0}: {1}" -f $entry.Key, $entry.Value)
}
Write-Host ""

if ($StartAfterInstall) {
    Start-Process -FilePath $launcher -WorkingDirectory $installRoot.FullName
}
