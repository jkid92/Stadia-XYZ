[CmdletBinding()]
param(
    [string]$DependencyDirectory,
    [string]$LogPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version 2.0

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($DependencyDirectory)) {
    $DependencyDirectory = Join-Path $root "dependencies"
}
if ([string]::IsNullOrWhiteSpace($LogPath)) {
    $LogPath = Join-Path (Join-Path $root "logs") "prerequisite-install.log"
}
$logDirectory = Split-Path -Parent $LogPath
$restartRequired = $false

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null

function Write-PrerequisiteLog {
    param([Parameter(Mandatory = $true)][string]$Message)

    $line = "$(Get-Date -Format o) $Message"
    Add-Content -LiteralPath $LogPath -Encoding UTF8 -Value $line
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-HidHideInstalled {
    $programFiles64 = if (${env:ProgramW6432}) { ${env:ProgramW6432} } else { ${env:ProgramFiles} }
    $cli = Join-Path $programFiles64 "Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
    return Test-Path -LiteralPath $cli
}

function Test-UsbipInstalled {
    $servicePath = "HKLM:\SYSTEM\CurrentControlSet\Services\usbip2_ude"
    $programFiles64 = if (${env:ProgramW6432}) { ${env:ProgramW6432} } else { ${env:ProgramFiles} }
    $clientPath = Join-Path $programFiles64 "USBip\usbip.exe"
    if (-not (Test-Path -LiteralPath $servicePath) -or -not (Test-Path -LiteralPath $clientPath)) {
        return $false
    }

    try {
        $versionText = (Get-Item -LiteralPath $clientPath).VersionInfo.FileVersion
        return ([version]$versionText -ge [version]"0.9.7.8")
    } catch {
        return $false
    }
}

function Invoke-PinnedInstaller {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$FileName,
        [Parameter(Mandatory = $true)][string]$Sha256,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $path = Join-Path $DependencyDirectory $FileName
    if (-not (Test-Path -LiteralPath $path)) {
        throw "$Name installer is missing: $path"
    }

    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if (-not $actualHash.Equals($Sha256, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name installer failed SHA-256 verification."
    }

    Write-PrerequisiteLog "$Name installer verified; starting."
    $process = Start-Process -FilePath $path -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    $exitCode = $process.ExitCode
    $process.Dispose()
    Write-PrerequisiteLog "$Name installer completed with exit code $exitCode."
    if ($exitCode -notin @(0, 1641, 3010)) {
        throw "$Name installer failed with exit code $exitCode."
    }

    return $exitCode
}

try {
    Write-PrerequisiteLog "Prerequisite check started."
    if (-not (Test-Administrator)) {
        throw "Administrator privileges were not granted."
    }

    if (-not (Test-HidHideInstalled)) {
        $hidHideExit = Invoke-PinnedInstaller `
            -Name "HidHide" `
            -FileName "HidHide_1.5.230_x64.exe" `
            -Sha256 "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6" `
            -Arguments @("/quiet", "/norestart")
        if ($hidHideExit -in @(1641, 3010)) {
            $restartRequired = $true
        }
    } else {
        Write-PrerequisiteLog "HidHide is already installed."
    }

    if (-not (Test-HidHideInstalled)) {
        throw "HidHide was not detected after installation."
    }

    if (-not (Test-UsbipInstalled)) {
        $usbipExit = Invoke-PinnedInstaller `
            -Name "usbip-win2" `
            -FileName "USBip-0.9.7.8-x64.exe" `
            -Sha256 "44451FE06F4186125C2A5ECD25B099C5560A61A60B1E56F5A0758E77A60AFA44" `
            -Arguments @("/S")
        $restartRequired = $true
        if ($usbipExit -in @(1641, 3010)) {
            $restartRequired = $true
        }
    } else {
        Write-PrerequisiteLog "usbip-win2 0.9.7.8 or newer is already installed."
    }

    if (-not (Test-UsbipInstalled)) {
        throw "usbip-win2 0.9.7.8 or newer was not detected after installation."
    }

    Write-PrerequisiteLog "Prerequisite check completed successfully. restartRequired=$restartRequired"
    exit $(if ($restartRequired) { 3010 } else { 0 })
} catch {
    Write-PrerequisiteLog "Prerequisite check failed: $($_.Exception.Message)"
    exit 1
}
