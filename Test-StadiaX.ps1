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

function Test-HidHideInstalled {
    $path = Join-Path ${env:ProgramFiles} "Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"
    return Test-Path -LiteralPath $path
}

$requiredFiles = @(
    "Test-StadiaX.ps1",
    "VERSION.txt",
    "README-WINDOWS-NATIVE.md",
    "LICENSE.txt",
    "assets\StadiaX-WindowsNative.ico",
    "assets\StadiaX-WindowsNative-icon.png",
    "assets\StadiaControllerCutout.png",
    "assets\ATTRIBUTION.md",
    "dependencies\THIRD-PARTY-NOTICES.txt"
)

foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $root $relativePath
    Add-Result "File: $relativePath" ($(if (Test-Path $path) { "OK" } else { "MISSING" })) ($(if (Test-Path $path) { $path } else { "Required file is missing" }))
}

foreach ($relativePath in @("StadiaX.exe", "ViGEmClient.dll")) {
    $path = Join-Path $root $relativePath
    if (Test-Path $path) {
        Add-Result "Runtime: $relativePath" "OK" $path
    } elseif ($AllowMissingBinaries) {
        Add-Result "Runtime: $relativePath" "WARN" "Missing, but allowed for source/package dry run"
    } else {
        Add-Result "Runtime: $relativePath" "MISSING" "Build or download the release runtime artifact"
    }
}

$dependencies = @(
    @{
        Path = "dependencies\HidHide_1.5.230_x64.exe"
        Sha256 = "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6"
    },
    @{
        Path = "dependencies\ViGEmBus_1.22.0_x64_x86_arm64.exe"
        Sha256 = "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A"
    }
)
foreach ($dependency in $dependencies) {
    $path = Join-Path $root $dependency.Path
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Result "Dependency: $($dependency.Path)" "MISSING" "Bundled signed installer is missing"
        continue
    }

    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    Add-Result "Dependency: $($dependency.Path)" ($(if ($actualHash -eq $dependency.Sha256) { "OK" } else { "MISSING" })) ($(if ($actualHash -eq $dependency.Sha256) { "Pinned SHA-256 verified" } else { "SHA-256 mismatch" }))
}

Add-Result "ViGEmBus driver" ($(if (Test-ViGEmBusInstalled) { "OK" } else { "WARN" })) "Start installs the bundled driver automatically when needed"
Add-Result "HidHide driver" ($(if (Test-HidHideInstalled) { "OK" } else { "WARN" })) "Start installs the bundled driver automatically when needed"

$runtimePath = Join-Path $root "StadiaX.exe"
if (Test-Path -LiteralPath $runtimePath) {
    try {
        $process = Start-Process -FilePath $runtimePath -ArgumentList "--internal-self-test" -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(60000)) {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch {}
            Add-Result "Internal runtime self-test" "MISSING" "Timed out after 60 seconds"
        } else {
            Add-Result "Internal runtime self-test" ($(if ($process.ExitCode -eq 0) { "OK" } else { "MISSING" })) "Exit code $($process.ExitCode)"
        }
        $process.Dispose()
    } catch {
        Add-Result "Internal runtime self-test" "MISSING" $_.Exception.Message
    }
} elseif ($AllowMissingBinaries) {
    Add-Result "Internal runtime self-test" "WARN" "Skipped because StadiaX.exe is absent in a source-only check"
}

$missing = @($results | Where-Object { $_.State -eq "MISSING" })
$warn = @($results | Where-Object { $_.State -eq "WARN" })
$overall = if ($missing.Count -gt 0) { "FAIL" } elseif ($warn.Count -gt 0) { "WARN" } else { "OK" }

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("Stadia X Windows Native self-test")
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
