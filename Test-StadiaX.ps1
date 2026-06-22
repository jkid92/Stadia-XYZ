[CmdletBinding()]
param(
    [switch]$AllowMissingBinaries,
    [switch]$Json
)

$ErrorActionPreference = "Continue"
Set-StrictMode -Version 2.0

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$logDir = Join-Path $root "logs"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
}

$results = New-Object System.Collections.Generic.List[object]

function Add-Result {
    param(
        [string]$Name,
        [string]$State,
        [string]$Details
    )

    [void]$results.Add([pscustomobject]@{
        Name = $Name
        State = $State
        Details = $Details
    })
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
        $device = Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
            Where-Object { $_.Name -match "ViGEm|Virtual Gamepad Emulation" } |
            Select-Object -First 1
        return [bool]$device
    } catch {
        return $false
    }
}

function Resolve-StadiaDistroForTest {
    $resolver = Join-Path $root "Resolve-WslDistro.ps1"
    if (-not (Test-Path $resolver) -or -not (Test-CommandAvailable "powershell.exe")) {
        return ""
    }

    try {
        $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $resolver 2>$null
        return [string]($output | Select-Object -First 1)
    } catch {
        return ""
    }
}

$requiredFiles = @(
    "Start-GUI.bat",
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
    "README.md",
    "LICENSE.txt"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $root $relativePath
    Add-Result "File: $relativePath" ($(if (Test-Path $path) { "OK" } else { "MISSING" })) ($(if (Test-Path $path) { $path } else { "Required file is missing" }))
}

foreach ($relativePath in @("StadiaX.exe", "ViGEmClient.dll", "stadia_bridge")) {
    $path = Join-Path $root $relativePath
    if (Test-Path $path) {
        Add-Result "Runtime: $relativePath" "OK" $path
    } elseif ($AllowMissingBinaries) {
        Add-Result "Runtime: $relativePath" "WARN" "Missing, but allowed for source/package dry run"
    } else {
        Add-Result "Runtime: $relativePath" "MISSING" "Build or download the release runtime artifact"
    }
}

Add-Result "Command: usbipd" ($(if (Test-CommandAvailable "usbipd") { "OK" } else { "MISSING" })) "Required for Bluetooth USB/IP handoff"
Add-Result "Command: wsl" ($(if (Test-CommandAvailable "wsl") { "OK" } else { "MISSING" })) "Required for the Linux Bluetooth bridge"
Add-Result "ViGEmBus driver" ($(if (Test-ViGEmBusInstalled) { "OK" } else { "MISSING" })) "Required for virtual Xbox 360 pads"

$distro = Resolve-StadiaDistroForTest
if ([string]::IsNullOrWhiteSpace($distro)) {
    Add-Result "WSL distro" "WARN" "No usable distro resolved yet; first start can install Ubuntu"
} else {
    Add-Result "WSL distro" "OK" $distro
}

$macroPath = Join-Path $root "stadia_buttons.ini"
if (Test-Path $macroPath) {
    $macroText = Get-Content -Raw -Path $macroPath -ErrorAction SilentlyContinue
    Add-Result "Macro config" ($(if ($macroText -match "(?im)^\s*\[Buttons\]\s*$") { "OK" } else { "WARN" })) "stadia_buttons.ini should contain a [Buttons] section"
}

$missing = @($results | Where-Object { $_.State -eq "MISSING" })
$warn = @($results | Where-Object { $_.State -eq "WARN" })
$overall = if ($missing.Count -gt 0) { "FAIL" } elseif ($warn.Count -gt 0) { "WARN" } else { "OK" }

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("Stadia X self-test")
[void]$lines.Add("Created: $(Get-Date -Format o)")
[void]$lines.Add("Root: $root")
[void]$lines.Add("Overall: $overall")
[void]$lines.Add("")
foreach ($result in $results) {
    [void]$lines.Add(("{0,-34} {1,-8} {2}" -f $result.Name, $result.State, $result.Details))
}

$textPath = Join-Path $logDir "self-test.txt"
Set-Content -Path $textPath -Encoding UTF8 -Value $lines

if ($Json) {
    $jsonPath = Join-Path $logDir "self-test.json"
    [pscustomobject]@{
        Created = Get-Date
        Root = $root
        Overall = $overall
        Results = $results
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding UTF8
}

$lines | Write-Output
exit $(if ($missing.Count -gt 0) { 1 } else { 0 })
