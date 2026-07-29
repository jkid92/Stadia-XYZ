using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StadiaX.ControlCenter;

internal sealed record PreparedUpdate(string Version, string InstallerPath, string ExpectedSha256, string ReleaseUrl);
internal sealed record UpdateState(string CurrentVersion, string TargetVersion, string BackupPath, string Status, DateTimeOffset UpdatedAt);

internal sealed class UpdateService
{
    private readonly AppPaths _paths;
    private readonly bool _windowsNative;
    private readonly string _dataDirectory;
    private readonly string _statePath;
    private readonly string _helperPath;
    private readonly SemaphoreSlim _prepareGate = new(1, 1);

    internal UpdateService(AppPaths paths, bool windowsNative)
    {
        _paths = paths;
        _windowsNative = windowsNative;
        _dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Stadia X", "updates", windowsNative ? "windows-native" : "bridge");
        _statePath = Path.Combine(_dataDirectory, "update-state.json");
        _helperPath = Path.Combine(_dataDirectory, "apply-update.ps1");
    }

    internal bool CanInstallAutomatically { get { var programs = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")) + Path.DirectorySeparatorChar; var root = Path.GetFullPath(_paths.Root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar; return root.StartsWith(programs, StringComparison.OrdinalIgnoreCase) && File.Exists(_paths.AppExecutable); } }
    internal bool HasRollback { get { var state = ReadState(); return state is not null && Directory.Exists(state.BackupPath); } }
    internal bool IsUpdateAvailable(string installedVersion, string releaseTag) => CompareVersions(releaseTag, installedVersion) > 0;

    internal static void RunSelfTest()
    {
        if (CompareVersions("v0.5.40", "v0.5.39") <= 0 ||
            CompareVersions("windows-native-v0.5.20.24", "windows-native-v0.5.20.23") <= 0 ||
            CompareVersions("v1.2.3", "1.2.3") != 0 ||
            ParseExpectedHash(new string('a', 64)) != new string('A', 64))
        {
            throw new InvalidOperationException("Update version or checksum validation failed its self-test.");
        }

        var testDirectory = Path.Combine(Path.GetTempPath(), $"StadiaX-update-self-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDirectory);
        try
        {
            var destination = Path.Combine(testDirectory, "setup.exe");
            File.WriteAllText(destination, "stale");
            var payload = new byte[] { 0x53, 0x74, 0x61, 0x64, 0x69, 0x61, 0x58 };
            using var client = new HttpClient(new StaticResponseHandler(payload));
            DownloadAsync(
                client,
                "https://self-test.invalid/setup.exe",
                destination,
                progress: null,
                CancellationToken.None).GetAwaiter().GetResult();

            if (!File.ReadAllBytes(destination).SequenceEqual(payload) ||
                Directory.EnumerateFiles(testDirectory, "*.download.*.tmp").Any())
            {
                throw new InvalidOperationException("Atomic update download failed its self-test.");
            }

            using var exclusive = new FileStream(destination, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Directory.Delete(testDirectory, recursive: true);
        }
    }

    internal async Task<PreparedUpdate?> PrepareAsync(ReleaseInfo release, string installedVersion, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsUpdateAvailable(installedVersion, release.Tag)) return null;
        var enteredImmediately = await _prepareGate.WaitAsync(0, cancellationToken).ConfigureAwait(false);
        if (!enteredImmediately)
        {
            AppDiagnosticsLogger.Record(
                "UPDATE_PREPARE_QUEUED",
                ("installedVersion", installedVersion),
                ("targetVersion", release.Tag));
            await _prepareGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (!IsUpdateAvailable(installedVersion, release.Tag)) return null;
            return await PrepareCoreAsync(release, installedVersion, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _prepareGate.Release();
        }
    }

    private async Task<PreparedUpdate> PrepareCoreAsync(ReleaseInfo release, string installedVersion, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        AppDiagnosticsLogger.Record(
            "UPDATE_PREPARE_STARTED",
            ("installedVersion", installedVersion),
            ("targetVersion", release.Tag));
        var installer = release.Assets.FirstOrDefault(asset => asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase) && (_windowsNative ? asset.Name.Contains("Windows-Native", StringComparison.OrdinalIgnoreCase) : !asset.Name.Contains("Windows-Native", StringComparison.OrdinalIgnoreCase)));
        if (installer is null) throw new InvalidOperationException("The release does not contain the expected setup file.");
        var checksum = release.Assets.FirstOrDefault(asset => asset.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null) throw new InvalidOperationException("The release does not contain the setup checksum.");
        var releaseDirectory = Path.Combine(_dataDirectory, SanitizeFileName(release.Tag));
        Directory.CreateDirectory(releaseDirectory);
        var installerPath = Path.Combine(releaseDirectory, installer.Name);
        var checksumPath = installerPath + ".sha256";
        using var client = ReleaseChecker.CreateClient();
        AppDiagnosticsLogger.Record(
            "UPDATE_CHECKSUM_DOWNLOAD_STARTED",
            ("targetVersion", release.Tag),
            ("asset", checksum.Name));
        await DownloadAsync(client, checksum.DownloadUrl, checksumPath, null, cancellationToken).ConfigureAwait(false);
        var expectedHash = ParseExpectedHash(await File.ReadAllTextAsync(checksumPath, cancellationToken).ConfigureAwait(false));
        var canReuseInstaller = File.Exists(installerPath) && HashMatches(installerPath, expectedHash);
        if (canReuseInstaller)
        {
            AppDiagnosticsLogger.Record(
                "UPDATE_INSTALLER_REUSED",
                ("targetVersion", release.Tag),
                ("asset", installer.Name),
                ("sha256", expectedHash));
        }
        else
        {
            AppDiagnosticsLogger.Record(
                "UPDATE_INSTALLER_DOWNLOAD_STARTED",
                ("targetVersion", release.Tag),
                ("asset", installer.Name),
                ("sizeBytes", installer.Size.ToString()));
            await DownloadAsync(client, installer.DownloadUrl, installerPath, progress, cancellationToken).ConfigureAwait(false);
        }

        if (!HashMatches(installerPath, expectedHash))
        {
            TryDeleteFile(installerPath);
            throw new InvalidDataException("The downloaded setup did not pass SHA-256 verification.");
        }

        progress?.Report(100);
        AppDiagnosticsLogger.Record(
            "UPDATE_PREPARE_COMPLETED",
            ("targetVersion", release.Tag),
            ("asset", installer.Name),
            ("sha256", expectedHash),
            ("reused", canReuseInstaller.ToString()));
        return new PreparedUpdate(release.Tag, installerPath, expectedHash, release.Url);
    }

    internal void LaunchInstall(PreparedUpdate update, string currentVersion)
    {
        if (!CanInstallAutomatically) throw new InvalidOperationException("Automatic installation is available only for an installed copy of Stadia X.");
        Directory.CreateDirectory(_dataDirectory);
        var backupPath = Path.Combine(_dataDirectory, "rollback", SanitizeFileName(currentVersion));
        if (Directory.Exists(backupPath)) Directory.Delete(backupPath, recursive: true);
        Directory.CreateDirectory(backupPath);
        CopyApplicationForRollback(_paths.Root, backupPath);
        WriteState(new UpdateState(currentVersion, update.Version, backupPath, "pending", DateTimeOffset.UtcNow));
        WriteHelperScript();
        LaunchHelper("install", backupPath, update.InstallerPath);
    }

    internal void LaunchRollback()
    {
        var state = ReadState();
        if (state is null || !Directory.Exists(state.BackupPath)) throw new InvalidOperationException("No previous Stadia X version is available for rollback.");
        WriteHelperScript();
        LaunchHelper("rollback", state.BackupPath, "");
    }

    private void LaunchHelper(string mode, string backupPath, string installerPath)
    {
        var startInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = _dataDirectory };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", _helperPath, "-Mode", mode, "-ParentPid", Environment.ProcessId.ToString(), "-Root", _paths.Root, "-Backup", backupPath, "-Installer", installerPath, "-Executable", _paths.AppExecutable, "-StatePath", _statePath }) startInfo.ArgumentList.Add(argument);
        Process.Start(startInfo);
    }

    private static async Task DownloadAsync(HttpClient client, string url, string destination, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        var temporaryPath = destination + $".download.{Guid.NewGuid():N}.tmp";
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            long written = 0;

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    written += read;
                    if (total is > 0)
                    {
                        progress?.Report(Math.Clamp((int)(written * 100 / total.Value), 1, 99));
                    }
                }

                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (total is >= 0 && written != total.Value)
            {
                throw new EndOfStreamException($"The update download was incomplete: expected {total.Value} bytes, received {written}.");
            }

            await CommitDownloadedFileAsync(temporaryPath, destination, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static async Task CommitDownloadedFileAsync(string temporaryPath, string destination, CancellationToken cancellationToken)
    {
        const int attempts = 8;
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                File.Move(temporaryPath, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(75 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool HashMatches(string path, string expectedHash) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedHash, StringComparison.OrdinalIgnoreCase); }
    private static string ParseExpectedHash(string checksumText) { var match = Regex.Match(checksumText, "(?i)\\b[0-9a-f]{64}\\b"); return match.Success ? match.Value.ToUpperInvariant() : throw new InvalidDataException("The release checksum file is invalid."); }
    private static int CompareVersions(string left, string right) { var leftParts = Regex.Matches(left, "\\d+").Select(match => int.Parse(match.Value)).ToArray(); var rightParts = Regex.Matches(right, "\\d+").Select(match => int.Parse(match.Value)).ToArray(); for (var i = 0; i < Math.Max(leftParts.Length, rightParts.Length); i++) { var l = i < leftParts.Length ? leftParts[i] : 0; var r = i < rightParts.Length ? rightParts[i] : 0; if (l != r) return l.CompareTo(r); } return 0; }

    private static void CopyApplicationForRollback(string sourceRoot, string backupRoot)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".git", "artifacts", "dist", "logs", "support-bundles" };
        CopyDirectory(sourceRoot, backupRoot);

        void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                if (!excluded.Contains(Path.GetFileName(directory)))
                {
                    CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
                }
            }
        }
    }

    private UpdateState? ReadState() { try { return File.Exists(_statePath) ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(_statePath)) : null; } catch { return null; } }
    private void WriteState(UpdateState state) { Directory.CreateDirectory(_dataDirectory); File.WriteAllText(_statePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true })); }
    private void WriteHelperScript() { Directory.CreateDirectory(_dataDirectory); File.WriteAllText(_helperPath, HelperScript); }
    private static string SanitizeFileName(string value) => string.Concat(value.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));

    private sealed class StaticResponseHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            };
            return Task.FromResult(response);
        }
    }

    private const string HelperScript = """
param([ValidateSet('install','rollback')][string]$Mode,[int]$ParentPid,[string]$Root,[string]$Backup,[string]$Installer,[string]$Executable,[string]$StatePath)
$ErrorActionPreference = 'Stop'
function Write-State([string]$status) {
    $previous = $null
    if (Test-Path -LiteralPath $StatePath) { try { $previous = Get-Content -LiteralPath $StatePath -Raw | ConvertFrom-Json } catch { $previous = $null } }
    $state = [ordered]@{
        currentVersion = if ($null -ne $previous) { $previous.currentVersion } else { '' }
        targetVersion = if ($null -ne $previous) { $previous.targetVersion } else { '' }
        backupPath = if ($null -ne $previous) { $previous.backupPath } else { $Backup }
        status = $status
        updatedAt = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $state | ConvertTo-Json | Set-Content -LiteralPath $StatePath -Encoding UTF8
}
function Restore-Backup { if (-not (Test-Path -LiteralPath $Backup)) { throw 'Rollback backup is missing.' }; Get-ChildItem -LiteralPath $Backup -Force | Copy-Item -Destination $Root -Recurse -Force }
try { Wait-Process -Id $ParentPid -Timeout 60 -ErrorAction SilentlyContinue; if ($Mode -eq 'rollback') { Restore-Backup; Write-State 'rolled-back'; Start-Process -FilePath $Executable -ArgumentList '--update-rollback-restored'; exit 0 }; $setup = Start-Process -FilePath $Installer -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS' -Wait -PassThru; if ($setup.ExitCode -ne 0) { throw "Setup exited with code $($setup.ExitCode)." }; $newApp = Start-Process -FilePath $Executable -ArgumentList '--update-health-check' -PassThru; Start-Sleep -Seconds 15; if ($newApp.HasExited) { throw 'The updated application exited during its health check.' }; Write-State 'healthy' }
catch { try { Restore-Backup; Write-State 'auto-rolled-back'; Start-Process -FilePath $Executable -ArgumentList '--update-rollback-restored' } catch { Write-State 'rollback-failed' }; exit 1 }
""";
}
