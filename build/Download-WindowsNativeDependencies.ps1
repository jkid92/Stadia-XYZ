[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "dependencies"
}
$output = New-Item -ItemType Directory -Force -Path $OutputDirectory

$dependencies = @(
    @{
        Name = "HidHide_1.5.230_x64.exe"
        Url = "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe"
        Sha256 = "F4BBBCB82E6258641B887C74BC81C4C5F66E4AA811808DFC304347687B7605F6"
    },
    @{
        Name = "ViGEmBus_1.22.0_x64_x86_arm64.exe"
        Url = "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe"
        Sha256 = "89220A7865076B342892F98865F3499FB7C4CFD673159E89D352C360FD014C6A"
    }
)

foreach ($dependency in $dependencies) {
    $path = Join-Path $output.FullName $dependency.Name
    $valid = $false
    if (Test-Path -LiteralPath $path) {
        $valid = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $dependency.Sha256
    }
    if (-not $valid) {
        $temporaryPath = "$path.download"
        Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        Invoke-WebRequest -Uri $dependency.Url -OutFile $temporaryPath
        $actualHash = (Get-FileHash -LiteralPath $temporaryPath -Algorithm SHA256).Hash
        if ($actualHash -ne $dependency.Sha256) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
            throw "SHA-256 verification failed for $($dependency.Name)."
        }
        Move-Item -LiteralPath $temporaryPath -Destination $path -Force
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $signature.SignerCertificate.Subject -notlike "*Nefarius Software Solutions*") {
        throw "Authenticode verification failed for $($dependency.Name): $($signature.Status)"
    }
    Write-Host "Verified dependency: $($dependency.Name)"
}

@"
Bundled Windows Native dependencies

HidHide 1.5.230 - MIT
https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0

ViGEmBus 1.22.0 - BSD-3-Clause
https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0

The original signed setup files are included unchanged. Their SHA-256 hashes are pinned in build/Download-WindowsNativeDependencies.ps1.
"@ | Set-Content -LiteralPath (Join-Path $output.FullName "THIRD-PARTY-NOTICES.txt") -Encoding UTF8

Write-Host "Windows Native dependencies: $($output.FullName)"
