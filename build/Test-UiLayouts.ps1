param(
    [string]$ExecutablePath = (Join-Path (Split-Path -Parent $PSScriptRoot) "StadiaX.exe")
)

$ErrorActionPreference = "Stop"
$ExecutablePath = (Resolve-Path $ExecutablePath).Path
$modes = @("--compact-ui", "--constrained-ui", "--comfortable-ui")
$dpiScales = @(100, 125, 150, 200)
$languages = @("it", "en")

foreach ($mode in $modes) {
  foreach ($dpiScale in $dpiScales) {
    foreach ($language in $languages) {
        $process = Start-Process `
            -FilePath $ExecutablePath `
            -ArgumentList @("--ui-layout-test", $mode, "--dpi-preview=$dpiScale", "--language=$language") `
            -WindowStyle Hidden `
            -PassThru

        if (-not $process.WaitForExit(30000)) {
            try { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue } catch { }
            throw "UI layout audit timed out for $mode in $language at $dpiScale%."
        }

        if ($process.ExitCode -ne 0) {
            $density = $mode -replace '^--', '' -replace '-ui$', ''
            $report = Join-Path (Split-Path -Parent $PSScriptRoot) "logs\ui-layout-audit-$density-$language-dpi$dpiScale.txt"
            if (Test-Path $report) {
                Get-Content $report | Write-Host
            }
            throw "UI layout audit failed for $mode in $language at $dpiScale% with exit code $($process.ExitCode)."
        }
    }
  }
}

Write-Host "UI layout audit passed in Italian and English for compact, constrained, and comfortable modes at 100%, 125%, 150%, and 200%."
