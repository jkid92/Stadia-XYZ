param(
    [string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) "StadiaX.exe")
)

$ErrorActionPreference = "Stop"
$ExecutablePath = (Resolve-Path $ExecutablePath).Path
$mode = "--comfortable-ui"
$dpiScale = 100
$language = "it"
$process = Start-Process `
    -FilePath $ExecutablePath `
    -ArgumentList @("--ui-layout-test", $mode, "--dpi-preview=$dpiScale", "--language=$language") `
    -WindowStyle Hidden `
    -PassThru

if (-not $process.WaitForExit(30000)) {
    try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
    throw "UI layout audit timed out at 1280x820 and $dpiScale% DPI."
}

if ($process.ExitCode -ne 0) {
    $report = Join-Path (Split-Path -Parent $PSScriptRoot) "logs\ui-layout-audit-comfortable-$language-dpi$dpiScale.txt"
    if (Test-Path $report) {
        Get-Content $report | Write-Host
    }
    throw "UI layout audit failed at 1280x820 and $dpiScale% DPI with exit code $($process.ExitCode)."
}

Write-Host "UI layout audit passed at 1280x820 and 100% DPI."
