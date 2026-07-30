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
        Publisher = "Nefarius Software Solutions"
    },
    @{
        Name = "USBip-0.9.7.8-x64.exe"
        Url = "https://github.com/vadimgrn/usbip-win2/releases/download/v.0.9.7.8/USBip-0.9.7.8-x64.exe"
        Sha256 = "44451FE06F4186125C2A5ECD25B099C5560A61A60B1E56F5A0758E77A60AFA44"
        Publisher = "Cloudyne Systems"
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
        $signature.SignerCertificate.Subject -notlike "*$($dependency.Publisher)*") {
        throw "Authenticode verification failed for $($dependency.Name): $($signature.Status)"
    }
    Write-Host "Verified dependency: $($dependency.Name)"
}

$viiperDirectory = New-Item -ItemType Directory -Force -Path (Join-Path $output.FullName "VIIPER")
$viiperArchive = Join-Path $output.FullName "viiper-windows-amd64-v0.7.0.zip"
$viiperArchiveUrl = "https://github.com/Alia5/VIIPER/releases/download/v0.7.0/viiper-windows-amd64.zip"
$viiperArchiveSha256 = "A02B06751D64E43E7700ABA8EE1F7E3E4F5F4E7F370A11722FF922AB075C1629"
$viiperExeSha256 = "1868D682F4CC6D62349BBCCBF0727B05D3EB6E22027AC34F0F1D9B1DE56F2DDC"
if (-not (Test-Path -LiteralPath $viiperArchive) -or
    (Get-FileHash -LiteralPath $viiperArchive -Algorithm SHA256).Hash -ne $viiperArchiveSha256) {
    $temporaryArchive = "$viiperArchive.download"
    Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
    Invoke-WebRequest -Uri $viiperArchiveUrl -OutFile $temporaryArchive
    if ((Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA256).Hash -ne $viiperArchiveSha256) {
        Remove-Item -LiteralPath $temporaryArchive -Force -ErrorAction SilentlyContinue
        throw "SHA-256 verification failed for VIIPER 0.7.0."
    }
    Move-Item -LiteralPath $temporaryArchive -Destination $viiperArchive -Force
}

$viiperExtract = Join-Path $output.FullName ".viiper-extract"
Remove-Item -LiteralPath $viiperExtract -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive -LiteralPath $viiperArchive -DestinationPath $viiperExtract -Force
$viiperExe = Join-Path $viiperExtract "viiper.exe"
if (-not (Test-Path -LiteralPath $viiperExe) -or
    (Get-FileHash -LiteralPath $viiperExe -Algorithm SHA256).Hash -ne $viiperExeSha256) {
    throw "The extracted VIIPER executable failed SHA-256 verification."
}
Copy-Item -LiteralPath $viiperExe -Destination (Join-Path $viiperDirectory.FullName "viiper.exe") -Force
Copy-Item -LiteralPath (Join-Path $viiperExtract "licenses.txt") -Destination (Join-Path $viiperDirectory.FullName "licenses.txt") -Force
Remove-Item -LiteralPath $viiperExtract -Recurse -Force
Write-Host "Verified dependency: VIIPER 0.7.0"

@"
Bundled Windows Native dependencies

HidHide 1.5.230 - MIT
https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0

VIIPER 0.7.0 standalone server - GPL-3.0
https://github.com/Alia5/VIIPER/releases/tag/v0.7.0

VIIPER C# Client 0.7.0 - MIT
https://www.nuget.org/packages/Viiper.Client/0.7.0

usbip-win2 0.9.7.8 - BSD-2-Clause
https://github.com/vadimgrn/usbip-win2/releases/tag/v.0.9.7.8

The signed driver setup and the original VIIPER release files are included unchanged. Their SHA-256 hashes are pinned in build/Download-WindowsNativeDependencies.ps1.
"@ | Set-Content -LiteralPath (Join-Path $output.FullName "THIRD-PARTY-NOTICES.txt") -Encoding UTF8

Write-Host "Windows Native dependencies: $($output.FullName)"
