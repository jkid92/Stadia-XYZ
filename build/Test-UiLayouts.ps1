param(
    [string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) "StadiaX.exe")
)

$ErrorActionPreference = "Stop"
$ExecutablePath = (Resolve-Path $ExecutablePath).Path
$modes = @("--compact-ui", "--constrained-ui", "--comfortable-ui")

foreach ($mode in $modes) {
    $process = Start-Process `
        -FilePath $ExecutablePath `
        -ArgumentList @("--ui-layout-test", $mode) `
        -WindowStyle Hidden `
        -PassThru

    if (-not $process.WaitForExit(30000)) {
        try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
        throw "UI layout audit timed out for $mode."
    }

    if ($process.ExitCode -ne 0) {
        $density = $mode -replace '^--', '' -replace '-ui$', ''
        $report = Join-Path (Split-Path -Parent $PSScriptRoot) "logs\ui-layout-audit-$density.txt"
        if (Test-Path $report) {
            Get-Content $report | Write-Host
        }
        throw "UI layout audit failed for $mode with exit code $($process.ExitCode)."
    }
}

Write-Host "UI layout audit passed for compact, constrained, and comfortable modes."
