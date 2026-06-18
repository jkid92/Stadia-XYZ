[CmdletBinding()]
param(
    [string]$RequestedDistro = "",
    [string]$SelectedDistroPath = ""
)

$ErrorActionPreference = "SilentlyContinue"
Set-StrictMode -Version Latest

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
if ([string]::IsNullOrWhiteSpace($SelectedDistroPath)) {
    $SelectedDistroPath = Join-Path $scriptRoot "selected_wsl_distro.txt"
}

function Test-DistroName {
    param([string]$Name)
    return (-not [string]::IsNullOrWhiteSpace($Name) -and $Name.Trim() -match '^[A-Za-z0-9_.-]+$')
}

function Get-WslDistros {
    $raw = (& wsl.exe -l -v 2>$null) -join "`n"
    $raw = $raw -replace [char]0, ""
    $distros = New-Object System.Collections.Generic.List[object]

    foreach ($line in ($raw -split "`r?`n")) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -match '^NAME\s+STATE\s+VERSION') {
            continue
        }
        if ($trimmed.StartsWith("*")) {
            $trimmed = $trimmed.Substring(1).Trim()
        }
        if ($trimmed -match '^(?<name>\S+)\s+(?<state>Running|Stopped)\s+(?<version>[12])$') {
            [void]$distros.Add([pscustomobject]@{
                Name = $Matches.name
                State = $Matches.state
                Version = [int]$Matches.version
                IsUbuntu = ($Matches.name -match '(?i)^Ubuntu')
            })
        }
    }

    return @($distros)
}

$distros = @(Get-WslDistros)
if ($distros.Count -eq 0) {
    exit 2
}

$candidates = New-Object System.Collections.Generic.List[string]
if (Test-DistroName $RequestedDistro) {
    [void]$candidates.Add($RequestedDistro.Trim())
}
if (Test-Path $SelectedDistroPath) {
    $saved = (Get-Content -Raw -Path $SelectedDistroPath).Trim()
    if (Test-DistroName $saved) {
        [void]$candidates.Add($saved)
    }
}

foreach ($candidate in $candidates) {
    $match = @($distros | Where-Object { $_.Name -eq $candidate } | Select-Object -First 1)
    if ($match.Count -gt 0) {
        Write-Output $match[0].Name
        exit 0
    }
}

$preferred = @($distros | Where-Object { $_.IsUbuntu -and $_.Version -eq 2 } | Sort-Object Name | Select-Object -First 1)
if ($preferred.Count -eq 0) {
    $preferred = @($distros | Where-Object { $_.Version -eq 2 } | Sort-Object Name | Select-Object -First 1)
}
if ($preferred.Count -eq 0) {
    $preferred = @($distros | Where-Object { $_.IsUbuntu } | Sort-Object Name | Select-Object -First 1)
}
if ($preferred.Count -eq 0) {
    $preferred = @($distros | Sort-Object Name | Select-Object -First 1)
}

if ($preferred.Count -gt 0) {
    Write-Output $preferred[0].Name
    exit 0
}

exit 2
