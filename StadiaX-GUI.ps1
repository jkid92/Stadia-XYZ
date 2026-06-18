Set-StrictMode -Version 2.0

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$Script:Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Script:ConfigPath = Join-Path $Script:Root "stadia_buttons.ini"
$Script:StartScript = Join-Path $Script:Root "Start-Stadia.bat"
$Script:StopScript = Join-Path $Script:Root "Stop-Stadia.bat"
$Script:ReceiverPath = Join-Path $Script:Root "stadia_receiver.exe"
$Script:ViGEmClientPath = Join-Path $Script:Root "ViGEmClient.dll"
$Script:BridgePath = Join-Path $Script:Root "stadia_bridge"
$Script:StartShPath = Join-Path $Script:Root "start.sh"
$Script:LogDir = Join-Path $Script:Root "logs"
$Script:StatusLogPath = Join-Path $Script:LogDir "status.log"
$Script:LinuxStatusLogPath = Join-Path $Script:LogDir "linux-status.log"
$Script:LinuxLogPath = Join-Path $Script:LogDir "linux.log"
$Script:BluetoothDiagPath = Join-Path $Script:LogDir "bluetooth-diagnostics.txt"
$Script:ControllerStatePath = Join-Path $Script:LogDir "controller-state.json"
$Script:SelectedControllerMacsPath = Join-Path $Script:Root "selected_controller_macs.txt"
$Script:ControllerProfilesPath = Join-Path $Script:Root "controller_profiles.json"
$Script:SupportBundleDir = Join-Path $Script:Root "support-bundles"
$Script:SelectedWslDistroPath = Join-Path $Script:Root "selected_wsl_distro.txt"
$Script:VersionPath = Join-Path $Script:Root "VERSION.txt"
$Script:ReleasesUrl = "https://github.com/jkid92/Stadia-XYZ/releases"
$Script:LatestReleaseApiUrl = "https://api.github.com/repos/jkid92/Stadia-XYZ/releases/latest"
$Script:MaxControllers = 4
$Script:ButtonIndicators = @{}
$Script:WizardList = $null
$Script:WizardSummaryLabel = $null
$Script:WizardDetailBox = $null
$Script:SetupList = $null
$Script:LiveStatusList = $null
$Script:LiveSummaryLabel = $null
$Script:LinuxLogBox = $null
$Script:HumanTimelineList = $null
$Script:TelemetryLabel = $null
$Script:HealthSummaryLabel = $null
$Script:LeftTriggerBar = $null
$Script:RightTriggerBar = $null
$Script:AxesLabel = $null
$Script:ControllerSelector = $null
$Script:ControllerStatsLabel = $null
$Script:ControllerDeadzoneLabel = $null
$Script:ControllerRawBox = $null
$Script:UpdateStatusLabel = $null
$Script:DeviceList = $null
$Script:SelectedBusIdText = $null
$Script:DeviceDetailsBox = $null
$Script:BluetoothStatusList = $null
$Script:BluetoothAdapterList = $null
$Script:BluetoothDeviceList = $null
$Script:LinuxBluetoothDeviceList = $null
$Script:WslDistroCombo = $null
$Script:SelectedWslDistroLabel = $null
$Script:BluetoothInfoBox = $null
$Script:BluetoothSummaryLabel = $null
$Script:LinuxControllerSelectionLabel = $null
$Script:BatteryStatusLabel = $null
$Script:BatteryOverlayForm = $null
$Script:BatteryOverlayLabel = $null
$Script:LastBatterySnapshot = @()
$Script:LastBatteryRefresh = [datetime]::MinValue
$Script:BatteryOverlayThreshold = 30
$Script:ControllerProfileList = $null
$Script:ProfileNameText = $null
$Script:ProfileMacText = $null
$Script:ProfileSlotCombo = $null
$Script:ProfileAutoCheck = $null
$Script:MacroMappingList = $null
$Script:MacroChordCombo = $null
$Script:MacroShortcutText = $null
$Script:NotifyIcon = $null

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-CommandAvailable {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Test-ViGEmBusInstalled {
    try {
        $service = Get-Service -Name "ViGEmBus" -ErrorAction SilentlyContinue
        if ($service) {
            return $true
        }
    } catch {}

    try {
        $devices = Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
            Where-Object { $_.Name -match "ViGEm|Virtual Gamepad Emulation" }
        return [bool]($devices | Select-Object -First 1)
    } catch {
        return $false
    }
}

function Ensure-LogDir {
    if (-not (Test-Path $Script:LogDir)) {
        New-Item -ItemType Directory -Path $Script:LogDir -Force | Out-Null
    }
}

function Write-AtomicText {
    param(
        [string]$Path,
        [string]$Text,
        [System.Text.Encoding]$Encoding = [System.Text.Encoding]::UTF8
    )

    $directory = Split-Path -Parent $Path
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $tempPath = "$Path.tmp"
    [System.IO.File]::WriteAllText($tempPath, $Text, $Encoding)
    Move-Item -LiteralPath $tempPath -Destination $Path -Force
}

function Add-Log {
    param([string]$Message)
    $stamp = Get-Date -Format "HH:mm:ss"
    $line = "[$stamp] $Message"

    try {
        Ensure-LogDir
        Add-Content -Path (Join-Path $Script:LogDir "gui.log") -Encoding UTF8 -Value $line
    } catch {}

    if ($Script:LogBox) {
        $Script:LogBox.AppendText("$line`r`n")
        $Script:LogBox.SelectionStart = $Script:LogBox.TextLength
        $Script:LogBox.ScrollToCaret()
    }
}

function Set-StatusText {
    param(
        [string]$Text,
        [System.Drawing.Color]$Color
    )
    $Script:StatusLabel.Text = $Text
    $Script:StatusLabel.ForeColor = $Color
}

function Add-CheckRow {
    param(
        [string]$Name,
        [string]$State,
        [string]$Details
    )
    $item = New-Object System.Windows.Forms.ListViewItem($Name)
    [void]$item.SubItems.Add($State)
    [void]$item.SubItems.Add($Details)
    switch ($State) {
        "OK" { $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72) }
        "WARN" { $item.ForeColor = [System.Drawing.Color]::FromArgb(170, 104, 0) }
        "MISSING" { $item.ForeColor = [System.Drawing.Color]::FromArgb(180, 45, 45) }
        default { $item.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70) }
    }
    [void]$Script:ChecksList.Items.Add($item)
}

function Add-ListRow {
    param(
        [System.Windows.Forms.ListView]$List,
        [string]$Name,
        [string]$State,
        [string]$Details
    )
    $item = New-Object System.Windows.Forms.ListViewItem($Name)
    [void]$item.SubItems.Add($State)
    [void]$item.SubItems.Add($Details)
    switch ($State) {
        "OK" { $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72) }
        "WARN" { $item.ForeColor = [System.Drawing.Color]::FromArgb(170, 104, 0) }
        "FAIL" { $item.ForeColor = [System.Drawing.Color]::FromArgb(180, 45, 45) }
        "MISSING" { $item.ForeColor = [System.Drawing.Color]::FromArgb(180, 45, 45) }
        "LIVE" { $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 80, 150) }
        default { $item.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70) }
    }
    [void]$List.Items.Add($item)
}

function Invoke-CapturedCommand {
    param(
        [string]$FileName,
        [string[]]$Arguments = @(),
        [int]$TimeoutMs = 10000
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FileName
    foreach ($arg in $Arguments) {
        [void]$psi.ArgumentList.Add($arg)
    }
    $psi.WorkingDirectory = $Script:Root
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()

    if (-not $process.WaitForExit($TimeoutMs)) {
        try { $process.Kill() } catch {}
        return [pscustomobject]@{
            ExitCode = -1
            Output = ""
            Error = "Timed out after $TimeoutMs ms"
        }
    }

    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Output = $process.StandardOutput.ReadToEnd()
        Error = $process.StandardError.ReadToEnd()
    }
}

function Get-WslDistroInfos {
    if (-not (Test-CommandAvailable "wsl")) {
        return @()
    }

    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments @("-l", "-v") -TimeoutMs 8000
    if ($result.ExitCode -ne 0) {
        return @()
    }

    $distros = New-Object System.Collections.Generic.List[object]
    foreach ($rawLine in ($result.Output -replace "`0", "" -split "`r?`n")) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line -match "^\s*NAME\s+STATE\s+VERSION") {
            continue
        }
        if ($line.StartsWith("*")) {
            $line = $line.Substring(1).Trim()
        }
        if ($line -match "^(?<name>\S+)\s+(?<state>Running|Stopped)\s+(?<version>[12])$") {
            [void]$distros.Add([pscustomobject]@{
                Name = $Matches.name
                State = $Matches.state
                Version = [int]$Matches.version
                IsUbuntu = ($Matches.name -match "(?i)^Ubuntu")
            })
        }
    }

    return @($distros)
}

function Get-SavedWslDistro {
    if (-not (Test-Path $Script:SelectedWslDistroPath)) {
        return ""
    }

    $name = (Get-Content -Raw -Path $Script:SelectedWslDistroPath -ErrorAction SilentlyContinue).Trim()
    if ($name -match "^[A-Za-z0-9_.-]+$") {
        return $name
    }
    return ""
}

function Resolve-StadiaWslDistro {
    $distros = @(Get-WslDistroInfos)
    if ($distros.Count -eq 0) {
        return ""
    }

    $saved = Get-SavedWslDistro
    if ($saved) {
        $match = @($distros | Where-Object { $_.Name -eq $saved } | Select-Object -First 1)
        if ($match.Count -gt 0) {
            return $match[0].Name
        }
    }

    $ubuntu = @($distros | Where-Object { $_.IsUbuntu -and $_.Version -eq 2 } | Sort-Object Name | Select-Object -First 1)
    if ($ubuntu.Count -gt 0) {
        return $ubuntu[0].Name
    }

    $wsl2 = @($distros | Where-Object { $_.Version -eq 2 } | Sort-Object Name | Select-Object -First 1)
    if ($wsl2.Count -gt 0) {
        return $wsl2[0].Name
    }

    return $distros[0].Name
}

function Test-WslDistroAvailable {
    param([string]$Name = "")
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = Resolve-StadiaWslDistro
    }
    if ([string]::IsNullOrWhiteSpace($Name) -or -not (Test-CommandAvailable "wsl")) {
        return $false
    }
    $check = Invoke-CapturedCommand -FileName "wsl" -Arguments @("-d", $Name, "echo", "ok") -TimeoutMs 8000
    return ($check.ExitCode -eq 0)
}

function Test-WslDistroWsl2 {
    param([string]$Name = "")
    if ([string]::IsNullOrWhiteSpace($Name)) {
        $Name = Resolve-StadiaWslDistro
    }
    if ([string]::IsNullOrWhiteSpace($Name)) {
        return $false
    }
    $info = @(Get-WslDistroInfos | Where-Object { $_.Name -eq $Name } | Select-Object -First 1)
    return ($info.Count -gt 0 -and $info[0].Version -eq 2)
}

function Get-WslArgs {
    param(
        [string[]]$Arguments,
        [switch]$Root
    )

    $resolved = Resolve-StadiaWslDistro
    $args = @()
    if ($resolved) {
        $args += @("-d", $resolved)
    }
    if ($Root) {
        $args += @("-u", "root")
    }
    return @($args + $Arguments)
}

function Set-SelectedWslDistro {
    param([string]$Name)

    $clean = $Name.Trim()
    if ([string]::IsNullOrWhiteSpace($clean)) {
        if (Test-Path $Script:SelectedWslDistroPath) {
            Remove-Item -LiteralPath $Script:SelectedWslDistroPath -Force
        }
    } elseif ($clean -match "^[A-Za-z0-9_.-]+$") {
        Write-AtomicText -Path $Script:SelectedWslDistroPath -Text ($clean + "`r`n") -Encoding ([System.Text.Encoding]::ASCII)
    } else {
        Add-Log "Ignored invalid WSL distro name: $Name"
        return
    }

    Update-WslDistroSelectionLabel
}

function Update-WslDistroSelectionLabel {
    if (-not $Script:SelectedWslDistroLabel) {
        return
    }
    $saved = Get-SavedWslDistro
    $resolved = Resolve-StadiaWslDistro
    if ($saved) {
        $Script:SelectedWslDistroLabel.Text = "WSL distro: $saved"
    } elseif ($resolved) {
        $Script:SelectedWslDistroLabel.Text = "WSL distro: automatic -> $resolved"
    } else {
        $Script:SelectedWslDistroLabel.Text = "WSL distro: automatic (Ubuntu will be installed if needed)"
    }
}

function Refresh-WslDistroList {
    if (-not $Script:WslDistroCombo) {
        return
    }

    $Script:WslDistroCombo.Items.Clear()
    [void]$Script:WslDistroCombo.Items.Add("Automatic")
    $distros = @(Get-WslDistroInfos)
    foreach ($distro in $distros) {
        [void]$Script:WslDistroCombo.Items.Add("$($distro.Name)  (WSL$($distro.Version), $($distro.State))")
    }

    $saved = Get-SavedWslDistro
    $Script:WslDistroCombo.SelectedIndex = 0
    if ($saved) {
        for ($i = 0; $i -lt $Script:WslDistroCombo.Items.Count; $i++) {
            if ([string]$Script:WslDistroCombo.Items[$i] -match "^$([regex]::Escape($saved))\s") {
                $Script:WslDistroCombo.SelectedIndex = $i
                break
            }
        }
    }

    Update-WslDistroSelectionLabel
}

function Test-PortsAvailable {
    $busy = @()
    $tcpConnections = Get-NetTCPConnection -ErrorAction SilentlyContinue | Where-Object { $_.LocalPort -eq 45495 }
    foreach ($connection in $tcpConnections) {
        $label = "TCP:$($connection.LocalPort)"
        if ($busy -notcontains $label) {
            $busy += $label
        }
    }

    $udpPorts = @(45493, 45494, 45499)
    $udpEndpoints = Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Where-Object { $udpPorts -contains $_.LocalPort }
    foreach ($endpoint in $udpEndpoints) {
        $label = "UDP:$($endpoint.LocalPort)"
        if ($busy -notcontains $label) {
            $busy += $label
        }
    }

    if ($busy.Count -gt 0) {
        return "Busy: $($busy -join ', ')"
    }
    return "Available"
}

function Test-UsbipdDistributionAttachSupport {
    if (-not (Test-CommandAvailable "usbipd")) {
        return $false
    }
    $result = Invoke-CapturedCommand -FileName "usbipd" -Arguments @("attach", "--help") -TimeoutMs 8000
    return (($result.Output + "`n" + $result.Error) -match "(?i)distribution")
}

function Get-StatusEvents {
    Ensure-LogDir
    $events = New-Object System.Collections.Generic.List[object]
    $paths = @(
        @{ Path = $Script:StatusLogPath; Source = "Windows" },
        @{ Path = $Script:LinuxStatusLogPath; Source = "Linux" }
    )

    foreach ($entry in $paths) {
        if (-not (Test-Path $entry.Path)) {
            continue
        }
        foreach ($line in (Get-Content -Path $entry.Path -Tail 250 -ErrorAction SilentlyContinue)) {
            if ($line -match "^(?:\[(?<time>[^\]]+)\]\s+)?STATUS:(?<code>[^|]+)\|(?<message>.*)$") {
                $events.Add([pscustomobject]@{
                    Source = $entry.Source
                    Time = $Matches.time
                    Code = $Matches.code
                    Message = $Matches.message
                    Raw = $line
                })
            }
        }
    }

    return @($events | Select-Object -Last 150)
}

function Get-LatestStatusSummary {
    $events = @(Get-StatusEvents)
    if ($events.Count -eq 0) {
        return "No live status yet."
    }
    $last = $events[-1]
    return "$($last.Source): $($last.Message)"
}

function Convert-StatusToHuman {
    param([object]$Event)

    $code = [string]$Event.Code
    $message = [string]$Event.Message
    switch ($code) {
        "START_REQUESTED" { return "Avvio richiesto: preparo Windows, WSL e Bluetooth." }
        "WSL_DISTRO_REQUESTED" { return "Uso la distro WSL scelta dall'utente." }
        "WSL_DISTRO_INVALID" { return "Nome distro WSL non valido: torno alla scelta automatica." }
        "WSL_DISTRO_MISSING" { return "Nessuna distro WSL pronta: avvio installazione Ubuntu." }
        "WSL_DISTRO_START_FAILED" { return "La distro WSL scelta non parte correttamente." }
        "WSL_DISTRO_SELECTED" { return "Distro WSL selezionata per questa sessione." }
        "BT_RESTORE_INVALID" { return "BUSID Bluetooth di ripristino non valido: lo ignoro per sicurezza." }
        "LINUX_INIT" { return "Linux si sta preparando e inizializza i servizi Bluetooth." }
        "BLUEZ_MISSING" { return "BlueZ non e presente: provo a installare gli strumenti Bluetooth." }
        "BLUEZ_INSTALLED" { return "BlueZ installato correttamente." }
        "BLUEZ_INSTALL_FAILED" { return "Installazione BlueZ fallita: serve controllare rete o permessi in WSL." }
        "BLUEZ_OK" { return "Gli strumenti Bluetooth Linux sono disponibili." }
        "KERNEL_MODULES_CHECKED" { return "Moduli Bluetooth/HID controllati in WSL." }
        "HID_MISSING" { return "WSL non espone il sottosistema HID: aggiorna WSL prima di riprovare." }
        "HID_OK" { return "Il sottosistema HID Linux e disponibile." }
        "DBUS_START" { return "Avvio D-Bus, necessario per bluetoothd." }
        "DBUS_OK" { return "D-Bus e gia attivo." }
        "BLUETOOTHD_START" { return "Avvio bluetoothd dentro Linux." }
        "ADAPTER_POWERED" { return "Bluetooth acceso in Linux: l'adattatore e stato passato correttamente." }
        "ADAPTER_POWER_FAILED" { return "Linux vede l'adattatore, ma non riesce ad accenderlo." }
        "BT_ATTACH_DISTRO_FALLBACK" { return "Attach alla distro WSL scelta fallito: provo il metodo compatibile." }
        "BT_ATTACH_DISTRO_UNSUPPORTED" { return "Questa versione di usbipd non supporta attach esplicito alla distro: uso il default." }
        "BT_DIAG_WRITTEN" { return "Diagnostica Bluetooth aggiornata." }
        "SCAN_START" { return "Ricerca controller Stadia in corso." }
        "CONTROLLER_MANUAL_CONFIG" { return "Windows ha una selezione manuale di controller Stadia da passare a Linux." }
        "CONTROLLER_MANUAL_INVALID" { return "Selezione manuale controller non valida: torno alla modalita automatica." }
        "CONTROLLER_MANUAL_SELECTION" { return "Linux sta usando i MAC controller scelti manualmente." }
        "PAIR_WAIT" { return "Controller non ancora trovato: mettilo in pairing con Stadia + Y." }
        "CONTROLLER_SEEN" { return "Controller rilevato: provo a usarlo." }
        "CONNECT_START" { return "Tentativo di connessione al controller." }
        "CONNECT_COMMAND_OK" { return "Comando di connessione accettato, verifico se il controller risponde." }
        "CONNECT_COMMAND_FAILED" { return "Linux vede il controller ma il comando di connessione fallisce." }
        "CONTROLLER_CONNECTED" { return "Controller connesso via Bluetooth." }
        "CONTROLLER_NOT_CONNECTED" { return "Controller rilevato, ma non risulta connesso." }
        "CONTROLLER_NOT_FOUND" { return "Nessun controller Stadia trovato durante la scansione." }
        "CONTROLLERS_READY" { return "Controller pronti: almeno un pad Stadia e connesso." }
        "RECOVERY_START" { return "Auto-recovery attivo: Linux controllera e riconnettera i controller se cadono." }
        "RECOVERY_RECONNECT" { return "Controller disconnesso: provo la riconnessione Bluetooth." }
        "RECOVERY_RECONNECT_OK" { return "Controller riconnesso automaticamente." }
        "RECOVERY_RECONNECT_FAILED" { return "Tentativo automatico di riconnessione fallito." }
        "INPUT_WAIT" { return "Bluetooth connesso: aspetto il device input Linux." }
        "INPUT_READY" { return "Input Linux pronto: i pulsanti possono essere inoltrati a Windows." }
        "INPUT_TIMEOUT" { return "Controller connesso ma nessun device input e apparso." }
        "HOST_IP_MISSING" { return "Non riesco a trovare l'IP Windows da Linux." }
        "HOST_IP_READY" { return "IP Windows rilevato: il ponte UDP puo partire." }
        "BRIDGE_START" { return "Avvio del core nativo Linux." }
        "BRIDGE_RESTART" { return "Il core Linux e uscito: provo a riavviarlo." }
        "BRIDGE_RESTART_LIMIT" { return "Il core Linux e uscito piu volte: stop ai riavvii automatici." }
        "BRIDGE_EXIT" { return "Il core Linux si e chiuso." }
        default {
            if ([string]::IsNullOrWhiteSpace($message)) {
                return "$($Event.Source): $code"
            }
            return "$($Event.Source): $message"
        }
    }
}

function Get-HumanStatusEvents {
    $events = @(Get-StatusEvents)
    return @($events | Select-Object -Last 80 | ForEach-Object {
        [pscustomobject]@{
            Time = $_.Time
            Source = $_.Source
            Event = $_.Code
            Human = Convert-StatusToHuman $_
        }
    })
}

function Refresh-LiveStatus {
    if (-not $Script:LiveStatusList) {
        return
    }

    $events = @(Get-StatusEvents)
    $Script:LiveStatusList.Items.Clear()
    foreach ($event in $events) {
        Add-ListRow $Script:LiveStatusList $event.Source $event.Code $event.Message
    }

    if ($Script:HumanTimelineList) {
        $Script:HumanTimelineList.Items.Clear()
        foreach ($event in @(Get-HumanStatusEvents)) {
            $item = New-Object System.Windows.Forms.ListViewItem($(if ($event.Time) { $event.Time } else { "-" }))
            [void]$item.SubItems.Add($event.Source)
            [void]$item.SubItems.Add($event.Human)
            switch ($event.Event) {
                { $_ -match "FAILED|MISSING|TIMEOUT|NOT_FOUND|NOT_CONNECTED|POWER_FAILED" } {
                    $item.ForeColor = [System.Drawing.Color]::FromArgb(180, 45, 45)
                    break
                }
                { $_ -match "CONNECTED|READY|OK|POWERED|INSTALLED" } {
                    $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
                    break
                }
                default {
                    $item.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
                }
            }
            [void]$Script:HumanTimelineList.Items.Add($item)
        }
    }

    if ($events.Count -gt 0) {
        $last = $events[-1]
        $Script:LiveSummaryLabel.Text = "$($last.Source): $($last.Message)"
    } else {
        $Script:LiveSummaryLabel.Text = "Waiting for Start-Stadia.bat or Linux core status events."
    }

    if (Test-Path $Script:LinuxLogPath) {
        $tail = Get-Content -Path $Script:LinuxLogPath -Tail 160 -ErrorAction SilentlyContinue
        $Script:LinuxLogBox.Text = ($tail -join "`r`n")
        $Script:LinuxLogBox.SelectionStart = $Script:LinuxLogBox.TextLength
        $Script:LinuxLogBox.ScrollToCaret()
    } else {
        $Script:LinuxLogBox.Text = "No Linux log yet. Start the bridge to create logs\linux.log."
    }
}

function Add-WizardRow {
    param(
        [string]$Name,
        [string]$State,
        [string]$Details
    )

    Add-ListRow $Script:WizardList $Name $State $Details
}

function Refresh-FirstRunWizard {
    if (-not $Script:WizardList) {
        return
    }

    $Script:WizardList.Items.Clear()

    $runtimeMissing = @()
    if (-not (Test-Path $Script:ReceiverPath)) { $runtimeMissing += "stadia_receiver.exe" }
    if (-not (Test-Path $Script:ViGEmClientPath)) { $runtimeMissing += "ViGEmClient.dll" }
    if (-not (Test-Path $Script:BridgePath)) { $runtimeMissing += "stadia_bridge" }
    if (-not (Test-Path $Script:StartShPath)) { $runtimeMissing += "start.sh" }
    if (-not (Test-Path $Script:ConfigPath)) { $runtimeMissing += "stadia_buttons.ini" }

    $releaseOk = ($runtimeMissing.Count -eq 0)
    Add-WizardRow "Release files" ($(if ($releaseOk) { "OK" } else { "MISSING" })) ($(if ($releaseOk) { "Runtime package is complete" } else { "Missing: $($runtimeMissing -join ', ')" }))

    $localVersion = Get-LocalVersion
    Add-WizardRow "Version" "INFO" $localVersion
    if ($Script:UpdateStatusLabel -and -not [string]::IsNullOrWhiteSpace($Script:UpdateStatusLabel.Text)) {
        $updateState = if ($Script:UpdateStatusLabel.Text -match "Update available") { "WARN" } elseif ($Script:UpdateStatusLabel.Text -match "Up to date") { "OK" } else { "INFO" }
        Add-WizardRow "Updates" $updateState $Script:UpdateStatusLabel.Text
    }

    $installerPresent = (Test-Path (Join-Path $Script:Root "Install-StadiaX.ps1"))
    Add-WizardRow "Installer" ($(if ($installerPresent) { "OK" } else { "WARN" })) ($(if ($installerPresent) { "Portable installer is available" } else { "Installer script not found" }))

    $vigemOk = Test-ViGEmBusInstalled
    Add-WizardRow "ViGEmBus driver" ($(if ($vigemOk) { "OK" } else { "MISSING" })) ($(if ($vigemOk) { "Virtual Xbox bus is installed" } else { "Install ViGEmBus before playing" }))

    $usbipdOk = Test-CommandAvailable "usbipd"
    Add-WizardRow "usbipd" ($(if ($usbipdOk) { "OK" } else { "MISSING" })) ($(if ($usbipdOk) { "USB/IP command is available" } else { "Start flow can install it with winget" }))

    $wslOk = Test-CommandAvailable "wsl"
    Add-WizardRow "WSL" ($(if ($wslOk) { "OK" } else { "MISSING" })) ($(if ($wslOk) { "wsl.exe is available" } else { "Windows Subsystem for Linux is missing" }))

    $resolvedDistro = if ($wslOk) { Resolve-StadiaWslDistro } else { "" }
    $distroOk = if ($resolvedDistro) { Test-WslDistroAvailable $resolvedDistro } else { $false }
    Add-WizardRow "WSL distro" ($(if ($distroOk) { "OK" } else { "WARN" })) ($(if ($resolvedDistro) { "$resolvedDistro selected/resolved" } else { "Start flow can install Ubuntu" }))
    Add-WizardRow "WSL distro version" ($(if ($resolvedDistro -and (Test-WslDistroWsl2 $resolvedDistro)) { "OK" } else { "WARN" })) ($(if ($resolvedDistro) { "USB/IP works best with WSL2" } else { "No distro detected yet" }))

    $devices = @(Get-UsbipdDevices)
    $btDevices = @($devices | Where-Object { $_.IsBluetooth })
    $selectedBusId = Get-SelectedBusId
    $selectedKnown = $false
    if ($selectedBusId) {
        $selectedKnown = [bool]($devices | Where-Object { $_.BusId -eq $selectedBusId } | Select-Object -First 1)
    }
    Add-WizardRow "Bluetooth adapter" ($(if ($btDevices.Count -gt 0) { "OK" } else { "WARN" })) "$($btDevices.Count) likely adapter(s) detected by usbipd"
    Add-WizardRow "Selected BUSID" ($(if ($selectedBusId -and $selectedKnown) { "OK" } elseif ($selectedBusId) { "WARN" } else { "MISSING" })) ($(if ($selectedBusId) { $selectedBusId } else { "Select one in Setup" }))
    $manualMacs = @(Get-SelectedLinuxControllerMacs)
    Add-WizardRow "Stadia controller" ($(if ($manualMacs.Count -gt 0) { "OK" } else { "INFO" })) ($(if ($manualMacs.Count -gt 0) { "Manual: $($manualMacs -join ', ')" } else { "Automatic Linux selection" }))

    $latest = Get-LatestStatusSummary
    Add-WizardRow "Startup timeline" ($(if ($latest -ne "No live status yet.") { "LIVE" } else { "INFO" })) $latest

    $receiverRunning = [bool](Get-Process -Name "stadia_receiver" -ErrorAction SilentlyContinue)
    Add-WizardRow "Receiver process" ($(if ($receiverRunning) { "OK" } else { "INFO" })) ($(if ($receiverRunning) { "stadia_receiver.exe is running" } else { "Not running" }))

    $telemetryOk = Test-Path $Script:ControllerStatePath
    Add-WizardRow "Controller test data" ($(if ($telemetryOk) { "OK" } else { "INFO" })) ($(if ($telemetryOk) { "Telemetry file exists" } else { "Created after bridge receives input" }))

    $problem = $Script:WizardList.Items | Where-Object { $_.SubItems[1].Text -in @("MISSING", "WARN") } | Select-Object -First 1
    if ($problem) {
        $Script:WizardSummaryLabel.Text = "Next: $($problem.Text) - $($problem.SubItems[2].Text)"
    } else {
        $Script:WizardSummaryLabel.Text = "Ready: start the bridge, pair the controller, then open Controller Test."
    }

    $Script:WizardDetailBox.Text = "Install folder: $Script:Root`r`nVersion: $localVersion`r`nWSL distro: $(if ($resolvedDistro) { $resolvedDistro } else { 'automatic / not installed yet' })`r`nSelected BUSID: $(if ($selectedBusId) { $selectedBusId } else { 'none' })`r`nSelected controller MAC(s): $(if ($manualMacs.Count -gt 0) { $manualMacs -join ', ' } else { 'automatic' })`r`nLatest status: $latest"
}

function Run-SetupAudit {
    Ensure-LogDir
    $Script:SetupList.Items.Clear()

    $usbipdDistributionAttach = Test-UsbipdDistributionAttachSupport
    Add-ListRow $Script:SetupList "PowerShell elevation" ($(if (Test-IsAdmin) { "OK" } else { "WARN" })) ($(if (Test-IsAdmin) { "Running as Administrator" } else { "Start/Stop will request UAC elevation" }))
    Add-ListRow $Script:SetupList "usbipd" ($(if (Test-CommandAvailable "usbipd") { "OK" } else { "MISSING" })) "Required for Bluetooth pass-through"
    Add-ListRow $Script:SetupList "usbipd distro attach" ($(if ($usbipdDistributionAttach) { "OK" } else { "INFO" })) ($(if ($usbipdDistributionAttach) { "Explicit WSL distro attach is supported" } else { "Older usbipd fallback will use the default WSL distro" }))
    Add-ListRow $Script:SetupList "wsl" ($(if (Test-CommandAvailable "wsl") { "OK" } else { "MISSING" })) "Required for Linux bridge"
    Add-ListRow $Script:SetupList "ViGEmBus driver" ($(if (Test-ViGEmBusInstalled) { "OK" } else { "MISSING" })) "Required for virtual Xbox 360 controller"
    $resolvedDistro = Resolve-StadiaWslDistro
    Add-ListRow $Script:SetupList "WSL distro" ($(if ($resolvedDistro -and (Test-WslDistroAvailable $resolvedDistro)) { "OK" } else { "WARN" })) ($(if ($resolvedDistro) { $resolvedDistro } else { "No distro detected; Start can install Ubuntu" }))
    Add-ListRow $Script:SetupList "WSL distro version" ($(if ($resolvedDistro -and (Test-WslDistroWsl2 $resolvedDistro)) { "OK" } else { "WARN" })) "USB/IP requires WSL2"
    Add-ListRow $Script:SetupList "Runtime: receiver" ($(if (Test-Path $Script:ReceiverPath) { "OK" } else { "MISSING" })) "stadia_receiver.exe"
    Add-ListRow $Script:SetupList "Runtime: ViGEm DLL" ($(if (Test-Path $Script:ViGEmClientPath) { "OK" } else { "MISSING" })) "ViGEmClient.dll next to receiver"
    Add-ListRow $Script:SetupList "Runtime: bridge" ($(if (Test-Path $Script:BridgePath) { "OK" } else { "MISSING" })) "stadia_bridge"
    Add-ListRow $Script:SetupList "Linux startup script" ($(if (Test-Path $Script:StartShPath) { "OK" } else { "MISSING" })) "start.sh"
    Add-ListRow $Script:SetupList "Macro config" ($(if (Test-Path $Script:ConfigPath) { "OK" } else { "MISSING" })) "stadia_buttons.ini"

    $devices = Get-UsbipdDevices
    $btCount = @($devices | Where-Object { $_.IsBluetooth }).Count
    Add-ListRow $Script:SetupList "Bluetooth candidates" ($(if ($btCount -gt 0) { "OK" } else { "WARN" })) "$btCount likely Bluetooth adapter(s) found"
    $btSnapshot = Get-BluetoothSnapshot
    $btCapacity = Get-BluetoothControllerCapacityEstimate $btSnapshot
    $btGuidance = Get-BluetoothAdapterGuidance -Snapshot $btSnapshot -Capacity $btCapacity
    Add-ListRow $Script:SetupList "Stadia capacity estimate" $btCapacity.State $btCapacity.Details
    Add-ListRow $Script:SetupList "Adapter advice" $btGuidance.State "$($btGuidance.Summary). $($btGuidance.Details)"

    $ports = Test-PortsAvailable
    Add-ListRow $Script:SetupList "Bridge ports" ($(if ($ports -eq "Available") { "OK" } else { "WARN" })) $ports

    Add-Log "Setup audit completed."
}

function Run-PostStartAudit {
    Ensure-LogDir
    $Script:SetupList.Items.Clear()

    $receiverRunning = [bool](Get-Process -Name "stadia_receiver" -ErrorAction SilentlyContinue)
    Add-ListRow $Script:SetupList "Receiver process" ($(if ($receiverRunning) { "OK" } else { "WARN" })) ($(if ($receiverRunning) { "stadia_receiver.exe is running" } else { "Receiver is not running" }))

    $wslIp = Get-WslIp
    Add-ListRow $Script:SetupList "WSL IP" ($(if ($wslIp) { "OK" } else { "WARN" })) ($(if ($wslIp) { $wslIp } else { "Not detected" }))

    $summary = Get-LatestStatusSummary
    Add-ListRow $Script:SetupList "Latest status" "LIVE" $summary

    Add-ListRow $Script:SetupList "Linux status log" ($(if (Test-Path $Script:LinuxStatusLogPath) { "OK" } else { "WARN" })) $Script:LinuxStatusLogPath
    Add-ListRow $Script:SetupList "Linux raw log" ($(if (Test-Path $Script:LinuxLogPath) { "OK" } else { "WARN" })) $Script:LinuxLogPath
    Add-ListRow $Script:SetupList "Controller telemetry" ($(if (Test-Path $Script:ControllerStatePath) { "OK" } else { "WARN" })) "Created by a rebuilt stadia_receiver.exe"
    $health = Get-ControllerHealthSummary
    Add-ListRow $Script:SetupList "Controller health" $health.State "$($health.Summary). $($health.Details)"

    if (Test-CommandAvailable "wsl") {
        $bluez = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", "command -v bluetoothctl >/dev/null && echo ok || echo missing")) -TimeoutMs 8000
        Add-ListRow $Script:SetupList "BlueZ in WSL" ($(if ($bluez.Output.Trim() -eq "ok") { "OK" } else { "WARN" })) $bluez.Output.Trim()
        $events = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", "ls /dev/input/event* 2>/dev/null | tr '\n' ' '")) -TimeoutMs 8000
        Add-ListRow $Script:SetupList "Linux input events" ($(if ($events.Output.Trim()) { "OK" } else { "WARN" })) ($(if ($events.Output.Trim()) { $events.Output.Trim() } else { "No /dev/input/event* found" }))
    }

    Add-Log "Post-start audit completed."
}

function Get-BluetoothPnPDevices {
    $devices = @()
    try {
        $devices = @(Get-PnpDevice -Class Bluetooth -ErrorAction Stop | ForEach-Object {
            [pscustomobject]@{
                Name = $_.FriendlyName
                Status = $_.Status
                InstanceId = $_.InstanceId
                Class = $_.Class
            }
        })
    } catch {
        try {
            $devices = @(Get-CimInstance Win32_PnPEntity -ErrorAction Stop |
                Where-Object { $_.Name -match "(?i)bluetooth" } |
                ForEach-Object {
                    [pscustomobject]@{
                        Name = $_.Name
                        Status = $_.Status
                        InstanceId = $_.PNPDeviceID
                        Class = "Bluetooth"
                    }
                })
        } catch {
            $devices = @()
        }
    }

    return @($devices | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Name) })
}

function Test-BluetoothAdapterCandidate {
    param([object]$Device)

    if (-not $Device) {
        return $false
    }

    $name = [string]$Device.Name
    $id = [string]$Device.InstanceId

    if ($name -match "(?i)enumerator|rfcomm|protocol|avrcp|hands-free|le generic attribute") {
        return $false
    }

    return (
        $id -match "^(?i:USB|PCI|BTHUSB)\\" -or
        $name -match "(?i)adapter|radio|wireless bluetooth|intel.*bluetooth|realtek.*bluetooth|mediatek.*bluetooth|qualcomm.*bluetooth|broadcom.*bluetooth"
    )
}

function Get-BluetoothDriverInfo {
    param([string]$InstanceId)

    if ([string]::IsNullOrWhiteSpace($InstanceId)) {
        return "Driver info unavailable"
    }

    try {
        $escaped = $InstanceId.Replace("\", "\\")
        $driver = Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceID='$escaped'" -ErrorAction Stop | Select-Object -First 1
        if ($driver) {
            return "Driver $($driver.DriverVersion) by $($driver.Manufacturer)"
        }
    } catch {}

    return "Driver info unavailable"
}

function Get-BluetoothSnapshot {
    $devices = @(Get-BluetoothPnPDevices)
    $adapters = @($devices | Where-Object { Test-BluetoothAdapterCandidate $_ })
    $activeDevices = @($devices | Where-Object {
        $_.Status -eq "OK" -and
        -not (Test-BluetoothAdapterCandidate $_) -and
        $_.Name -notmatch "(?i)enumerator|rfcomm|protocol"
    })
    $pairedOrKnown = @($devices | Where-Object { -not (Test-BluetoothAdapterCandidate $_) })

    $service = $null
    try { $service = Get-Service -Name bthserv -ErrorAction Stop } catch {}

    $primaryAdapter = $adapters | Select-Object -First 1
    $driverInfo = if ($primaryAdapter) { Get-BluetoothDriverInfo $primaryAdapter.InstanceId } else { "No Bluetooth adapter found" }

    return [pscustomobject]@{
        Devices = $devices
        Adapters = $adapters
        ActiveDevices = $activeDevices
        PairedOrKnown = $pairedOrKnown
        Service = $service
        DriverInfo = $driverInfo
    }
}

function Get-BluetoothControllerCapacityEstimate {
    param([object]$Snapshot)

    if (-not $Snapshot -or $Snapshot.Adapters.Count -eq 0) {
        return [pscustomobject]@{
            State = "MISSING"
            Estimated = 0
            AdapterLimit = 0
            Confidence = "low"
            Details = "No Bluetooth adapter detected. Stadia X supports up to $Script:MaxControllers controllers when hardware allows it."
        }
    }

    $adapter = $Snapshot.Adapters | Select-Object -First 1
    $text = "$($adapter.Name) $($adapter.InstanceId) $($Snapshot.DriverInfo)"
    $adapterLimit = 2
    $confidence = "low"
    $reasons = New-Object System.Collections.Generic.List[string]

    if ($text -match "(?i)bluetooth\s*5|bt\s*5|5\.[0-9]") {
        $adapterLimit = 4
        $confidence = "medium"
        [void]$reasons.Add("Bluetooth 5.x hint found")
    } elseif ($text -match "(?i)4\.2") {
        $adapterLimit = 3
        $confidence = "medium"
        [void]$reasons.Add("Bluetooth 4.2 hint found")
    } elseif ($text -match "(?i)4\.0|4\.1") {
        $adapterLimit = 2
        $confidence = "medium"
        [void]$reasons.Add("Bluetooth 4.x hint found")
    }

    if ($text -match "(?i)\bAX\d+|Wi-?Fi\s*6|Wi-?Fi\s*7|Intel\(R\).*Wireless|Intel.*Bluetooth|Qualcomm|MediaTek|Broadcom") {
        $adapterLimit = [Math]::Max($adapterLimit, 4)
        $confidence = "medium"
        [void]$reasons.Add("modern integrated chipset hint")
    } elseif ($text -match "(?i)Realtek") {
        $adapterLimit = [Math]::Max($adapterLimit, 3)
        [void]$reasons.Add("Realtek adapter hint")
    } elseif ($text -match "(?i)CSR|generic bluetooth radio|dongle") {
        $adapterLimit = [Math]::Min($adapterLimit, 2)
        [void]$reasons.Add("generic/dongle hint")
    }

    $activeCount = [int]$Snapshot.ActiveDevices.Count
    $estimated = [Math]::Min($Script:MaxControllers, $adapterLimit)
    if ($activeCount -ge 3) {
        $estimated = [Math]::Max(1, $estimated - 1)
        [void]$reasons.Add("$activeCount other active Bluetooth device(s) may reduce headroom")
    }

    $state = if ($estimated -ge $Script:MaxControllers) { "OK" } elseif ($estimated -ge 2) { "WARN" } else { "INFO" }
    if ($reasons.Count -eq 0) {
        [void]$reasons.Add("Windows exposes limited adapter capability data")
    }

    return [pscustomobject]@{
        State = $state
        Estimated = $estimated
        AdapterLimit = $adapterLimit
        Confidence = $confidence
        Details = "Estimated Stadia capacity: $estimated/$Script:MaxControllers controller(s). Adapter: $($adapter.Name). Confidence: $confidence. Hints: $($reasons -join '; ')."
    }
}

function Get-BluetoothAdapterGuidance {
    param(
        [object]$Snapshot,
        [object]$Capacity
    )

    if (-not $Snapshot -or $Snapshot.Adapters.Count -eq 0) {
        return [pscustomobject]@{
            State = "MISSING"
            Summary = "No Bluetooth adapter available"
            Details = "Enable an internal Bluetooth radio or use a USB Bluetooth 5.x adapter before trying four Stadia controllers."
        }
    }

    $adapter = $Snapshot.Adapters | Select-Object -First 1
    $text = "$($adapter.Name) $($adapter.InstanceId) $($Snapshot.DriverInfo)"
    $advice = New-Object System.Collections.Generic.List[string]

    if ($text -match "(?i)\bAX\d+|Wi-?Fi\s*6|Wi-?Fi\s*7|Intel\(R\).*Wireless|Intel.*Bluetooth|Qualcomm|MediaTek|Broadcom|bluetooth\s*5|bt\s*5|5\.[0-9]") {
        [void]$advice.Add("Good candidate for four controllers.")
    } elseif ($text -match "(?i)4\.2|Realtek") {
        [void]$advice.Add("Likely usable, but test three/four controllers before a long session.")
    } else {
        [void]$advice.Add("Treat this as a basic adapter until tested with real controller input.")
    }

    $audioDevices = @($Snapshot.ActiveDevices | Where-Object { $_.Name -match "(?i)audio|headphone|headset|speaker|buds|hands-free|avrcp" })
    if ($audioDevices.Count -gt 0) {
        [void]$advice.Add("Move Bluetooth audio to another radio or wired audio while using multiple controllers.")
    }

    if ($text -match "(?i)CSR|generic bluetooth radio|dongle") {
        [void]$advice.Add("Generic dongles often become unstable above one or two gamepads.")
    }

    $state = if ($Capacity.Estimated -ge $Script:MaxControllers) { "OK" } elseif ($Capacity.Estimated -ge 2) { "WARN" } else { "INFO" }
    return [pscustomobject]@{
        State = $state
        Summary = "$($adapter.Name): $($Capacity.Estimated)/$Script:MaxControllers estimated"
        Details = ($advice -join " ")
    }
}

function Add-BluetoothDeviceRow {
    param(
        [System.Windows.Forms.ListView]$List,
        [object]$Device,
        [string]$Role
    )

    $item = New-Object System.Windows.Forms.ListViewItem($Device.Name)
    [void]$item.SubItems.Add($Device.Status)
    [void]$item.SubItems.Add($Role)
    [void]$item.SubItems.Add($Device.InstanceId)
    $item.Tag = $Device.InstanceId
    if ($Device.Status -eq "OK") {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
    } elseif ($Device.Status -eq "Error") {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(180, 45, 45)
    } else {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
    }
    [void]$List.Items.Add($item)
}

function Get-SelectedLinuxControllerMacs {
    if (-not (Test-Path $Script:SelectedControllerMacsPath)) {
        return @()
    }

    $raw = (Get-Content -Raw -Path $Script:SelectedControllerMacsPath -ErrorAction SilentlyContinue).Trim()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return @()
    }

    return @($raw -split "[,;\s]+" | Where-Object { $_ -match "^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$" } | Select-Object -First $Script:MaxControllers)
}

function Update-LinuxControllerSelectionLabel {
    if (-not $Script:LinuxControllerSelectionLabel) {
        return
    }

    $macs = @(Get-SelectedLinuxControllerMacs)
    if ($macs.Count -gt 0) {
        $Script:LinuxControllerSelectionLabel.Text = "Manual Stadia selection: $($macs -join ', ')"
        $Script:LinuxControllerSelectionLabel.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
    } else {
        $Script:LinuxControllerSelectionLabel.Text = "Manual Stadia selection: automatic"
        $Script:LinuxControllerSelectionLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
    }
}

function Set-SelectedLinuxControllerMacs {
    param([string[]]$Macs)

    $clean = @($Macs | Where-Object { $_ -match "^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$" } | Select-Object -First $Script:MaxControllers)
    if ($clean.Count -eq 0) {
        if (Test-Path $Script:SelectedControllerMacsPath) {
            Remove-Item -LiteralPath $Script:SelectedControllerMacsPath -Force
        }
        Update-LinuxControllerSelectionLabel
        return
    }

    Write-AtomicText -Path $Script:SelectedControllerMacsPath -Text (($clean -join ",") + "`r`n") -Encoding ([System.Text.Encoding]::ASCII)
    Update-LinuxControllerSelectionLabel
    Add-Log "Manual Linux controller selection saved: $($clean -join ', ')"
}

function Get-LinuxBluetoothDevices {
    param([int]$ScanSeconds = 0)

    if (-not (Test-CommandAvailable "wsl")) {
        return @()
    }

    $scanCommand = ""
    if ($ScanSeconds -gt 0) {
        $safeSeconds = [Math]::Max(1, [Math]::Min(20, $ScanSeconds))
        $scanCommand = "timeout $($safeSeconds + 2) bluetoothctl --timeout $safeSeconds scan on >/dev/null 2>&1 || true;"
    }

    $script = @"
if ! command -v bluetoothctl >/dev/null 2>&1; then
  echo "__ERROR__|bluetoothctl missing"
  exit 0
fi
$scanCommand
bluetoothctl devices 2>/dev/null | while read -r kind mac name; do
  [ -z "`$mac" ] && continue
  info="`$(bluetoothctl info "`$mac" 2>/dev/null || true)"
  paired="`$(printf "%s\n" "`$info" | awk -F': ' '/Paired:/ {print `$2; exit}')"
  trusted="`$(printf "%s\n" "`$info" | awk -F': ' '/Trusted:/ {print `$2; exit}')"
  connected="`$(printf "%s\n" "`$info" | awk -F': ' '/Connected:/ {print `$2; exit}')"
  battery="`$(printf "%s\n" "`$info" | awk -F'[()]' '/Battery Percentage:/ {print `$2; exit}')"
  printf '%s|%s|%s|%s|%s|%s\n' "`$mac" "`$name" "`$paired" "`$trusted" "`$connected" "`$battery"
done
"@

    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", $script)) -TimeoutMs ([Math]::Max(8000, ($ScanSeconds + 5) * 1000))
    $devices = New-Object System.Collections.Generic.List[object]

    foreach ($line in ($result.Output -split "`r?`n")) {
        if ($line -match "^__ERROR__\|(?<message>.*)$") {
            Add-Log "Linux Bluetooth query failed: $($Matches.message)"
            continue
        }
        if ($line -match "^(?<mac>[0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})\|(?<name>[^|]*)\|(?<paired>[^|]*)\|(?<trusted>[^|]*)\|(?<connected>[^|]*)\|(?<battery>.*)$") {
            $name = $Matches.name.Trim()
            [void]$devices.Add([pscustomobject]@{
                Mac = $Matches.mac.ToUpperInvariant()
                Name = if ($name) { $name } else { "(unknown)" }
                Paired = $Matches.paired.Trim()
                Trusted = $Matches.trusted.Trim()
                Connected = $Matches.connected.Trim()
                Battery = $Matches.battery.Trim()
                IsStadia = ($name -match "(?i)stadia")
            })
        }
    }

    if ($result.ExitCode -ne 0 -and $devices.Count -eq 0) {
        Add-Log "Linux Bluetooth query returned exit code $($result.ExitCode): $($result.Error.Trim())"
    }

    return $devices.ToArray()
}

function Add-LinuxBluetoothDeviceRow {
    param([object]$Device)

    $item = New-Object System.Windows.Forms.ListViewItem($Device.Mac)
    [void]$item.SubItems.Add($Device.Name)
    [void]$item.SubItems.Add($Device.Connected)
    [void]$item.SubItems.Add($Device.Paired)
    [void]$item.SubItems.Add($Device.Trusted)
    [void]$item.SubItems.Add($(if ($Device.Battery) { "$($Device.Battery)%" } else { "-" }))
    [void]$item.SubItems.Add($(if ($Device.IsStadia) { "yes" } else { "no" }))
    $item.Tag = $Device
    if ($Device.IsStadia) {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
    } elseif ($Device.Connected -eq "yes") {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 80, 150)
    } else {
        $item.ForeColor = [System.Drawing.Color]::FromArgb(70, 70, 70)
    }
    [void]$Script:LinuxBluetoothDeviceList.Items.Add($item)
}

function Refresh-LinuxBluetoothDevices {
    param([int]$ScanSeconds = 0)

    if (-not $Script:LinuxBluetoothDeviceList) {
        return
    }

    $Script:LinuxBluetoothDeviceList.Items.Clear()
    Update-LinuxControllerSelectionLabel
    $devices = @(Get-LinuxBluetoothDevices -ScanSeconds $ScanSeconds)
    foreach ($device in $devices) {
        Add-LinuxBluetoothDeviceRow $device
    }
    Add-Log "Linux Bluetooth devices refreshed: $($devices.Count) device(s)."
}

function Use-SelectedLinuxStadiaControllers {
    if (-not $Script:LinuxBluetoothDeviceList -or $Script:LinuxBluetoothDeviceList.SelectedItems.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select up to $Script:MaxControllers Linux Bluetooth devices first.", "Linux Bluetooth devices", "OK", "Warning") | Out-Null
        return
    }

    $selected = @($Script:LinuxBluetoothDeviceList.SelectedItems | ForEach-Object { $_.Tag } | Select-Object -First $Script:MaxControllers)
    $nonStadia = @($selected | Where-Object { -not $_.IsStadia })
    if ($nonStadia.Count -gt 0) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "At least one selected device does not look like a Stadia controller.`r`n`r`nSave it anyway?",
            "Manual controller selection",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }
    }

    Set-SelectedLinuxControllerMacs @($selected | ForEach-Object { $_.Mac })
}

function Invoke-LinuxBluetoothCommand {
    param(
        [string]$Mac,
        [string[]]$Commands,
        [int]$TimeoutMs = 30000
    )

    if ($Mac -notmatch "^[0-9A-Fa-f]{2}(:[0-9A-Fa-f]{2}){5}$") {
        return [pscustomobject]@{ ExitCode = -1; Output = ""; Error = "Invalid MAC: $Mac" }
    }
    if (-not (Test-CommandAvailable "wsl")) {
        return [pscustomobject]@{ ExitCode = -1; Output = ""; Error = "WSL is not available" }
    }

    $safeMac = $Mac.ToUpperInvariant()
    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($command in $Commands) {
        switch ($command) {
            "trust" { [void]$lines.Add("bluetoothctl trust '$safeMac' 2>&1") }
            "pair" { [void]$lines.Add("timeout 20 bluetoothctl pair '$safeMac' 2>&1 || true") }
            "connect" { [void]$lines.Add("timeout 20 bluetoothctl connect '$safeMac' 2>&1 || true") }
            "disconnect" { [void]$lines.Add("bluetoothctl disconnect '$safeMac' 2>&1 || true") }
            "remove" { [void]$lines.Add("bluetoothctl remove '$safeMac' 2>&1 || true") }
            default {}
        }
    }
    [void]$lines.Add("bluetoothctl info '$safeMac' 2>&1 || true")
    $script = $lines -join "; "
    return Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", $script)) -TimeoutMs $TimeoutMs
}

function Pair-SelectedLinuxBluetoothDevices {
    if (-not $Script:LinuxBluetoothDeviceList -or $Script:LinuxBluetoothDeviceList.SelectedItems.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select up to $Script:MaxControllers Linux Bluetooth devices first.", "Linux pairing", "OK", "Warning") | Out-Null
        return
    }

    $selected = @($Script:LinuxBluetoothDeviceList.SelectedItems | ForEach-Object { $_.Tag } | Select-Object -First $Script:MaxControllers)
    foreach ($device in $selected) {
        Add-Log "Linux pairing flow for $($device.Name) [$($device.Mac)]"
        $result = Invoke-LinuxBluetoothCommand -Mac $device.Mac -Commands @("trust", "pair", "connect") -TimeoutMs 45000
        Add-Log (($result.Output + "`n" + $result.Error).Trim())
    }
    Refresh-LinuxBluetoothDevices
}

function Connect-SelectedLinuxBluetoothDevices {
    if (-not $Script:LinuxBluetoothDeviceList -or $Script:LinuxBluetoothDeviceList.SelectedItems.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select up to $Script:MaxControllers Linux Bluetooth devices first.", "Linux connect", "OK", "Warning") | Out-Null
        return
    }

    foreach ($item in $Script:LinuxBluetoothDeviceList.SelectedItems) {
        $device = $item.Tag
        Add-Log "Linux connect request for $($device.Name) [$($device.Mac)]"
        $result = Invoke-LinuxBluetoothCommand -Mac $device.Mac -Commands @("trust", "connect") -TimeoutMs 30000
        Add-Log (($result.Output + "`n" + $result.Error).Trim())
    }
    Refresh-LinuxBluetoothDevices
}

function Start-PairingWizard {
    $tabs.SelectedTab = $bluetoothPage
    [System.Windows.Forms.MessageBox]::Show(
        "1. Make sure the Bluetooth adapter BUSID is selected.`r`n2. Start the bridge once so Linux owns the adapter, or attach it through the normal Start flow.`r`n3. Put each Stadia controller in pairing mode with Stadia + Y.`r`n4. Use Scan Linux, select up to $Script:MaxControllers controllers, then Pair + connect.`r`n`r`nLeave Automatic selected if you want Stadia X to keep choosing controllers by itself.",
        "Pair Stadia controller",
        "OK",
        "Information"
    ) | Out-Null
    Refresh-LinuxBluetoothDevices -ScanSeconds 8
}

function Update-LinuxBluetoothDeviceRows {
    param([object[]]$Devices)

    if (-not $Script:LinuxBluetoothDeviceList) {
        return
    }

    $Script:LinuxBluetoothDeviceList.Items.Clear()
    foreach ($device in $Devices) {
        Add-LinuxBluetoothDeviceRow $device
    }
    Update-LinuxControllerSelectionLabel
}

function Start-ControllerCapacityWizard {
    $tabs.SelectedTab = $bluetoothPage
    Add-Log "Controller capacity wizard started."
    Refresh-BluetoothPanel

    $snapshot = Get-BluetoothSnapshot
    $capacity = Get-BluetoothControllerCapacityEstimate $snapshot
    $guidance = Get-BluetoothAdapterGuidance -Snapshot $snapshot -Capacity $capacity
    $linuxDevices = @(Get-LinuxBluetoothDevices -ScanSeconds 10)
    Update-LinuxBluetoothDeviceRows $linuxDevices

    $stadiaDevices = @($linuxDevices | Where-Object { $_.IsStadia } | Select-Object -First $Script:MaxControllers)
    $connectedStadia = @($stadiaDevices | Where-Object { $_.Connected -eq "yes" })
    $manualMacs = @(Get-SelectedLinuxControllerMacs)
    $profiles = @(Get-ControllerProfiles | Where-Object { $_.AutoConnect -and $_.Mac } | Sort-Object Slot | Select-Object -First $Script:MaxControllers)
    $health = Get-ControllerHealthSummary

    $lines = @(
        "Adapter estimate: $($capacity.Estimated)/$Script:MaxControllers controller(s)",
        "Adapter advice: $($guidance.Details)",
        "Linux sees Stadia controllers: $($stadiaDevices.Count)",
        "Linux connected Stadia controllers: $($connectedStadia.Count)",
        "Manual selection: $(if ($manualMacs.Count -gt 0) { $manualMacs -join ', ' } else { 'automatic' })",
        "Auto-connect profiles: $($profiles.Count)",
        "$($health.Summary)",
        "",
        "Target for four controllers:",
        "- Use a Bluetooth 5.x or modern integrated adapter.",
        "- Keep Bluetooth audio devices off the same radio.",
        "- Pair and press buttons on each controller, then check Controller Test."
    )

    Ensure-LogDir
    $reportPath = Join-Path $Script:LogDir "capacity-wizard.txt"
    Write-AtomicText -Path $reportPath -Text (($lines -join "`r`n") + "`r`n")
    Add-Log "Controller capacity wizard completed. Report: $reportPath"

    [System.Windows.Forms.MessageBox]::Show(($lines -join "`r`n"), "Stadia X controller capacity", "OK", "Information") | Out-Null
}

function Enable-PartyMode {
    $tabs.SelectedTab = $bluetoothPage
    Add-Log "Party mode requested for up to $Script:MaxControllers controllers."

    $linuxDevices = @(Get-LinuxBluetoothDevices -ScanSeconds 8)
    Update-LinuxBluetoothDeviceRows $linuxDevices
    $stadiaDevices = @($linuxDevices | Where-Object { $_.IsStadia } | Select-Object -First $Script:MaxControllers)

    if ($stadiaDevices.Count -gt 0) {
        Set-SelectedLinuxControllerMacs @($stadiaDevices | ForEach-Object { $_.Mac })
        foreach ($device in $stadiaDevices) {
            if ($device.Connected -ne "yes") {
                Add-Log "Party mode connect request for $($device.Name) [$($device.Mac)]"
                $result = Invoke-LinuxBluetoothCommand -Mac $device.Mac -Commands @("trust", "connect") -TimeoutMs 30000
                Add-Log (($result.Output + "`n" + $result.Error).Trim())
            }
        }
        Refresh-LinuxBluetoothDevices
        [System.Windows.Forms.MessageBox]::Show(
            "Party Mode enabled with $($stadiaDevices.Count) detected Stadia controller(s).`r`n`r`nSaved MACs:`r`n$(@($stadiaDevices | ForEach-Object { $_.Mac }) -join "`r`n")",
            "Party Mode",
            "OK",
            "Information"
        ) | Out-Null
        return
    }

    $profiles = @(Get-ControllerProfiles | Where-Object { $_.AutoConnect -and $_.Mac } | Sort-Object Slot | Select-Object -First $Script:MaxControllers)
    if ($profiles.Count -gt 0) {
        Set-SelectedLinuxControllerMacs @($profiles | ForEach-Object { $_.Mac })
        [System.Windows.Forms.MessageBox]::Show(
            "Party Mode enabled from auto-connect profiles.`r`n`r`nSaved MACs:`r`n$(@($profiles | ForEach-Object { $_.Mac }) -join "`r`n")",
            "Party Mode",
            "OK",
            "Information"
        ) | Out-Null
        return
    }

    Set-SelectedLinuxControllerMacs @()
    Add-Log "Party mode left automatic because no Stadia devices or auto-connect profiles were found."
    [System.Windows.Forms.MessageBox]::Show(
        "Party Mode is ready, but no Stadia controllers were visible to Linux yet.`r`n`r`nThe startup flow will stay automatic. Put the controllers in pairing mode, run Scan Linux, then use Party Mode again or select them manually.",
        "Party Mode",
        "OK",
        "Information"
    ) | Out-Null
}

function Invoke-StadiaRepairFlow {
    if (-not (Test-CommandAvailable "wsl")) {
        [System.Windows.Forms.MessageBox]::Show("WSL is not available, so the Linux Bluetooth repair flow cannot run.", "Repair Bluetooth", "OK", "Warning") | Out-Null
        return
    }

    $answer = [System.Windows.Forms.MessageBox]::Show(
        "This will refresh the Linux Bluetooth stack, power-cycle BlueZ, and reconnect the selected Stadia controller MACs when available.`r`n`r`nContinue?",
        "Repair Bluetooth",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question
    )
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
        return
    }

    Add-Log "Repair flow started."
    $script = @'
set +e
if command -v rfkill >/dev/null 2>&1; then rfkill unblock bluetooth 2>&1 || true; fi
if command -v systemctl >/dev/null 2>&1; then systemctl restart bluetooth 2>&1 || true; fi
if command -v service >/dev/null 2>&1; then service bluetooth restart 2>&1 || true; fi
if command -v bluetoothctl >/dev/null 2>&1; then
  bluetoothctl scan off 2>&1 || true
  bluetoothctl power off 2>&1 || true
  sleep 1
  bluetoothctl power on 2>&1 || true
  bluetoothctl agent on 2>&1 || true
  bluetoothctl default-agent 2>&1 || true
  bluetoothctl show 2>&1 || true
else
  echo "bluetoothctl missing"
fi
'@

    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Root -Arguments @("bash", "-lc", $script)) -TimeoutMs 35000
    Add-Log (($result.Output + "`n" + $result.Error).Trim())

    $macs = @(Get-SelectedLinuxControllerMacs)
    if ($macs.Count -eq 0 -and $Script:LinuxBluetoothDeviceList -and $Script:LinuxBluetoothDeviceList.SelectedItems.Count -gt 0) {
        $macs = @($Script:LinuxBluetoothDeviceList.SelectedItems | ForEach-Object { $_.Tag.Mac } | Select-Object -First $Script:MaxControllers)
    }

    foreach ($mac in $macs) {
        Add-Log "Repair reconnect request for $mac"
        $connectResult = Invoke-LinuxBluetoothCommand -Mac $mac -Commands @("trust", "connect") -TimeoutMs 30000
        Add-Log (($connectResult.Output + "`n" + $connectResult.Error).Trim())
    }

    Refresh-LinuxBluetoothDevices
    Refresh-BluetoothPanel
    Update-ControllerBatteryStatus
    Add-Log "Repair flow completed."
    [System.Windows.Forms.MessageBox]::Show("Repair flow completed. Check Linux devices and Controller Test for live input.", "Repair Bluetooth", "OK", "Information") | Out-Null
}

function Get-ControllerProfiles {
    if (-not (Test-Path $Script:ControllerProfilesPath)) {
        return @(1..$Script:MaxControllers | ForEach-Object {
            [pscustomobject]@{ Name = "Player $_"; Mac = ""; Slot = $_; AutoConnect = $false }
        })
    }

    try {
        $profiles = @(Get-Content -Raw -Path $Script:ControllerProfilesPath | ConvertFrom-Json)
        if ($profiles.Count -gt 0) {
            return @($profiles | ForEach-Object {
                [pscustomobject]@{
                    Name = if ($_.Name) { [string]$_.Name } else { "Controller" }
                    Mac = if ($_.Mac) { [string]$_.Mac } else { "" }
                    Slot = if ($_.Slot) { [Math]::Max(1, [Math]::Min($Script:MaxControllers, [int]$_.Slot)) } else { 1 }
                    AutoConnect = if ($_.PSObject.Properties["AutoConnect"]) { [bool]$_.AutoConnect } else { $false }
                }
            })
        }
    } catch {
        Add-Log "Could not read controller profiles: $($_.Exception.Message)"
        try {
            $backupPath = "$Script:ControllerProfilesPath.bad-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Move-Item -LiteralPath $Script:ControllerProfilesPath -Destination $backupPath -Force
            Add-Log "Invalid controller profiles were backed up to $(Split-Path -Leaf $backupPath)"
        } catch {}
    }

    return @(1..$Script:MaxControllers | ForEach-Object {
        [pscustomobject]@{ Name = "Player $_"; Mac = ""; Slot = $_; AutoConnect = $false }
    })
}

function Save-ControllerProfiles {
    param([object[]]$Profiles)
    $json = (@($Profiles) | ConvertTo-Json -Depth 4)
    Write-AtomicText -Path $Script:ControllerProfilesPath -Text ($json + "`r`n")
}

function Refresh-ControllerProfiles {
    if (-not $Script:ControllerProfileList) {
        return
    }

    $Script:ControllerProfileList.Items.Clear()
    foreach ($profile in @(Get-ControllerProfiles)) {
        $item = New-Object System.Windows.Forms.ListViewItem($profile.Name)
        [void]$item.SubItems.Add($profile.Mac)
        [void]$item.SubItems.Add("Pad $($profile.Slot)")
        [void]$item.SubItems.Add($(if ($profile.AutoConnect) { "yes" } else { "no" }))
        $item.Tag = $profile
        if ($profile.Mac) {
            $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
        }
        [void]$Script:ControllerProfileList.Items.Add($item)
    }
}

function Load-SelectedControllerProfileIntoForm {
    if (-not $Script:ControllerProfileList -or $Script:ControllerProfileList.SelectedItems.Count -eq 0) {
        return
    }

    $profile = $Script:ControllerProfileList.SelectedItems[0].Tag
    $Script:ProfileNameText.Text = $profile.Name
    $Script:ProfileMacText.Text = $profile.Mac
    $Script:ProfileSlotCombo.SelectedIndex = [Math]::Max(0, [Math]::Min($Script:MaxControllers - 1, [int]$profile.Slot - 1))
    $Script:ProfileAutoCheck.Checked = [bool]$profile.AutoConnect
}

function Save-ProfileFromForm {
    $name = $Script:ProfileNameText.Text.Trim()
    $mac = $Script:ProfileMacText.Text.Trim().ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($name)) {
        $name = "Controller"
    }
    if ($mac -and $mac -notmatch "^[0-9A-F]{2}(:[0-9A-F]{2}){5}$") {
        [System.Windows.Forms.MessageBox]::Show("MAC address is not valid.", "Controller profile", "OK", "Warning") | Out-Null
        return
    }

    $slot = $Script:ProfileSlotCombo.SelectedIndex + 1
    if ($slot -lt 1) { $slot = 1 }
    if ($slot -gt $Script:MaxControllers) { $slot = $Script:MaxControllers }
    $profiles = @(Get-ControllerProfiles)
    $existingIndex = -1
    for ($i = 0; $i -lt $profiles.Count; $i++) {
        if ($profiles[$i].Slot -eq $slot -or ($mac -and $profiles[$i].Mac -eq $mac)) {
            $existingIndex = $i
            break
        }
    }
    $profile = [pscustomobject]@{
        Name = $name
        Mac = $mac
        Slot = $slot
        AutoConnect = [bool]$Script:ProfileAutoCheck.Checked
    }

    if ($existingIndex -ge 0) {
        $profiles[$existingIndex] = $profile
    } else {
        $profiles += $profile
    }
    Save-ControllerProfiles $profiles
    Refresh-ControllerProfiles
    Add-Log "Controller profile saved: $name [$mac] -> Pad $slot"
}

function Use-SelectedLinuxDeviceAsProfile {
    if (-not $Script:LinuxBluetoothDeviceList -or $Script:LinuxBluetoothDeviceList.SelectedItems.Count -eq 0) {
        [System.Windows.Forms.MessageBox]::Show("Select a Linux Bluetooth device first.", "Controller profile", "OK", "Warning") | Out-Null
        return
    }
    $device = $Script:LinuxBluetoothDeviceList.SelectedItems[0].Tag
    $Script:ProfileNameText.Text = $device.Name
    $Script:ProfileMacText.Text = $device.Mac
    if ($device.IsStadia) {
        $Script:ProfileAutoCheck.Checked = $true
    }
}

function Apply-ControllerProfilesToStartup {
    $profiles = @(Get-ControllerProfiles | Where-Object { $_.AutoConnect -and $_.Mac } | Sort-Object Slot | Select-Object -First $Script:MaxControllers)
    if ($profiles.Count -eq 0) {
        Set-SelectedLinuxControllerMacs @()
        Add-Log "No auto-connect controller profile is enabled; automatic selection restored."
        return
    }

    Set-SelectedLinuxControllerMacs @($profiles | ForEach-Object { $_.Mac })
    Add-Log "Controller profiles applied to startup: $($profiles.Mac -join ', ')"
}

function Get-StadiaChordDefinitions {
    $defs = New-Object System.Collections.Generic.List[object]
    [void]$defs.Add([pscustomobject]@{ Code = "A"; Label = "Assistant solo" })
    [void]$defs.Add([pscustomobject]@{ Code = "C"; Label = "Capture solo" })
    $buttons = @(
        @("A", "A"), @("B", "B"), @("X", "X"), @("Y", "Y"),
        @("UP", "D-Pad Up"), @("DOWN", "D-Pad Down"), @("LEFT", "D-Pad Left"), @("RIGHT", "D-Pad Right"),
        @("LB", "LB"), @("RB", "RB"), @("L2", "L2"), @("R2", "R2"),
        @("L3", "L3"), @("R3", "R3"), @("SELECT", "Select"), @("START", "Start"), @("STADIA", "Stadia")
    )
    foreach ($button in $buttons) {
        [void]$defs.Add([pscustomobject]@{ Code = "A_$($button[0])"; Label = "Assistant + $($button[1])" })
    }
    foreach ($button in $buttons) {
        [void]$defs.Add([pscustomobject]@{ Code = "C_$($button[0])"; Label = "Capture + $($button[1])" })
    }
    return $defs.ToArray()
}

function Get-MacroMappingHash {
    $map = @{}
    $inButtons = $false
    $text = if ($Script:MacroBox) { $Script:MacroBox.Text } elseif (Test-Path $Script:ConfigPath) { [System.IO.File]::ReadAllText($Script:ConfigPath) } else { "" }
    foreach ($line in ($text -split "`r?`n")) {
        $trim = $line.Trim()
        if ($trim -match "^\[(.+)\]$") {
            $inButtons = ($Matches[1] -ieq "Buttons")
            continue
        }
        if ($inButtons -and $trim -match "^(?<code>[A-Z0-9_]+)\s*=\s*(?<shortcut>.*)$") {
            $map[$Matches.code] = $Matches.shortcut.Trim()
        }
    }
    return $map
}

function Refresh-MacroMappingList {
    if (-not $Script:MacroMappingList) {
        return
    }

    $Script:MacroMappingList.Items.Clear()
    $map = Get-MacroMappingHash
    foreach ($def in Get-StadiaChordDefinitions) {
        $shortcut = if ($map.ContainsKey($def.Code)) { $map[$def.Code] } else { "" }
        $item = New-Object System.Windows.Forms.ListViewItem($def.Label)
        [void]$item.SubItems.Add($def.Code)
        [void]$item.SubItems.Add($shortcut)
        $item.Tag = $def
        if ($shortcut) {
            $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
        }
        [void]$Script:MacroMappingList.Items.Add($item)
    }
}

function Load-SelectedMacroMappingIntoForm {
    if (-not $Script:MacroMappingList -or $Script:MacroMappingList.SelectedItems.Count -eq 0) {
        return
    }
    $def = $Script:MacroMappingList.SelectedItems[0].Tag
    $Script:MacroChordCombo.SelectedItem = "$($def.Code) - $($def.Label)"
    $Script:MacroShortcutText.Text = $Script:MacroMappingList.SelectedItems[0].SubItems[2].Text
}

function Set-MacroMappingInEditor {
    param(
        [string]$Code = "",
        [string]$Shortcut = $null
    )

    if (-not $Script:MacroBox) {
        return
    }

    $code = $Code.Trim().ToUpperInvariant()
    if ([string]::IsNullOrWhiteSpace($code)) {
        $selected = [string]$Script:MacroChordCombo.SelectedItem
        if ($selected -notmatch "^(?<code>[A-Z0-9_]+)\s+-") {
            [System.Windows.Forms.MessageBox]::Show("Select a controller chord first.", "Macro mapping", "OK", "Warning") | Out-Null
            return
        }
        $code = $Matches.code
    }

    $knownCodes = @{}
    foreach ($def in Get-StadiaChordDefinitions) { $knownCodes[$def.Code] = $true }
    if (-not $knownCodes.ContainsKey($code)) {
        [System.Windows.Forms.MessageBox]::Show("Unknown controller chord: $code", "Macro mapping", "OK", "Warning") | Out-Null
        return
    }

    $shortcut = if ($null -ne $Shortcut) { $Shortcut.Trim() } else { $Script:MacroShortcutText.Text.Trim() }
    $lines = New-Object System.Collections.Generic.List[string]
    $sourceLines = @($Script:MacroBox.Text -split "`r?`n")
    $inButtons = $false
    $buttonsSeen = $false
    $updated = $false

    foreach ($line in $sourceLines) {
        $trim = $line.Trim()
        if ($trim -match "^\[(.+)\]$") {
            if ($inButtons -and -not $updated) {
                [void]$lines.Add("$code=$shortcut")
                $updated = $true
            }
            $inButtons = ($Matches[1] -ieq "Buttons")
            if ($inButtons) { $buttonsSeen = $true }
            [void]$lines.Add($line)
            continue
        }

        if ($inButtons -and $trim -match "^$([regex]::Escape($code))\s*=") {
            [void]$lines.Add("$code=$shortcut")
            $updated = $true
        } else {
            [void]$lines.Add($line)
        }
    }

    if (-not $buttonsSeen) {
        [void]$lines.Add("")
        [void]$lines.Add("[Buttons]")
    }
    if (-not $updated) {
        [void]$lines.Add("$code=$shortcut")
    }

    $Script:MacroBox.Text = ($lines -join "`r`n")
    Refresh-MacroMappingList
    Add-Log "Macro mapping applied in editor: $code=$shortcut"
}

function Get-StadiaSessionReportLines {
    $snapshot = Get-BluetoothSnapshot
    $capacity = Get-BluetoothControllerCapacityEstimate $snapshot
    $guidance = Get-BluetoothAdapterGuidance -Snapshot $snapshot -Capacity $capacity
    $health = Get-ControllerHealthSummary
    $battery = Format-BatterySnapshot @(Get-LinuxControllerBatterySnapshot)
    $manualMacs = @(Get-SelectedLinuxControllerMacs)
    $profiles = @(Get-ControllerProfiles | Where-Object { $_.AutoConnect -and $_.Mac } | Sort-Object Slot | Select-Object -First $Script:MaxControllers)
    $runtimeMissing = @()
    foreach ($path in @($Script:ReceiverPath, $Script:ViGEmClientPath, $Script:BridgePath, $Script:StartShPath)) {
        if (-not (Test-Path $path)) {
            $runtimeMissing += (Split-Path -Leaf $path)
        }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("# Stadia X Session Report")
    [void]$lines.Add("")
    [void]$lines.Add("- Created: $(Get-Date -Format o)")
    [void]$lines.Add("- Version: $(Get-LocalVersion)")
    [void]$lines.Add("- Install folder: $Script:Root")
    [void]$lines.Add("- WSL distro: $(if (Resolve-StadiaWslDistro) { Resolve-StadiaWslDistro } else { 'automatic / not resolved' })")
    [void]$lines.Add("- Selected BUSID: $(if (Get-SelectedBusId) { Get-SelectedBusId } else { 'none' })")
    [void]$lines.Add("- Manual controller MACs: $(if ($manualMacs.Count -gt 0) { $manualMacs -join ', ' } else { 'automatic' })")
    [void]$lines.Add("- Auto-connect profiles: $(if ($profiles.Count -gt 0) { ($profiles | ForEach-Object { "$($_.Name)=$($_.Mac)" }) -join ', ' } else { 'none' })")
    [void]$lines.Add("- Runtime files: $(if ($runtimeMissing.Count -eq 0) { 'present' } else { 'missing ' + ($runtimeMissing -join ', ') })")
    [void]$lines.Add("")
    [void]$lines.Add("## Bluetooth")
    [void]$lines.Add("")
    [void]$lines.Add("- Windows adapters: $($snapshot.Adapters.Count)")
    [void]$lines.Add("- Active Windows Bluetooth devices: $($snapshot.ActiveDevices.Count)")
    [void]$lines.Add("- Known Windows Bluetooth devices: $($snapshot.PairedOrKnown.Count)")
    [void]$lines.Add("- Driver: $($snapshot.DriverInfo)")
    [void]$lines.Add("- Capacity: $($capacity.Estimated)/$Script:MaxControllers ($($capacity.State), confidence $($capacity.Confidence))")
    [void]$lines.Add("- Advice: $($guidance.Details)")
    [void]$lines.Add("")
    [void]$lines.Add("## Controllers")
    [void]$lines.Add("")
    [void]$lines.Add("- $($health.Summary)")
    [void]$lines.Add("- Health details: $($health.Details)")
    [void]$lines.Add("- $battery")
    [void]$lines.Add("- Latest status: $(Get-LatestStatusSummary)")
    [void]$lines.Add("")
    [void]$lines.Add("## Recent Linux status")
    [void]$lines.Add("")
    if (Test-Path $Script:LinuxStatusLogPath) {
        foreach ($line in Get-Content -Path $Script:LinuxStatusLogPath -Tail 30 -ErrorAction SilentlyContinue) {
            [void]$lines.Add("    $line")
        }
    } else {
        [void]$lines.Add("    linux-status.log not created yet")
    }

    return $lines.ToArray()
}

function Export-StadiaSessionReport {
    param([switch]$Silent)

    Ensure-LogDir
    if (-not (Test-Path $Script:SupportBundleDir)) {
        New-Item -ItemType Directory -Force -Path $Script:SupportBundleDir | Out-Null
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $reportPath = Join-Path $Script:SupportBundleDir "StadiaX-session-$stamp.md"
    Write-AtomicText -Path $reportPath -Text (((Get-StadiaSessionReportLines) -join "`r`n") + "`r`n")
    Add-Log "Session report created: $reportPath"

    if (-not $Silent) {
        [System.Windows.Forms.MessageBox]::Show("Session report created:`r`n$reportPath", "Stadia X report", "OK", "Information") | Out-Null
    }

    return $reportPath
}

function Invoke-StadiaSelfTest {
    $testScript = Join-Path $Script:Root "Test-StadiaX.ps1"
    if (-not (Test-Path $testScript)) {
        [System.Windows.Forms.MessageBox]::Show("Test-StadiaX.ps1 was not found.", "Stadia X self-test", "OK", "Warning") | Out-Null
        return
    }

    Add-Log "Self-test started."
    $result = Invoke-CapturedCommand -FileName "powershell.exe" -Arguments @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $testScript) -TimeoutMs 60000
    $text = ($result.Output + "`r`n" + $result.Error).Trim()
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        Add-Log $text
    }
    $reportPath = Join-Path $Script:LogDir "self-test.txt"
    $message = "Self-test completed with exit code $($result.ExitCode).`r`n`r`nReport: $reportPath"
    Add-Log $message
    [System.Windows.Forms.MessageBox]::Show($message, "Stadia X self-test", "OK", $(if ($result.ExitCode -eq 0) { "Information" } else { "Warning" })) | Out-Null
}

function Export-StadiaSupportBundle {
    Ensure-LogDir
    if (-not (Test-Path $Script:SupportBundleDir)) {
        New-Item -ItemType Directory -Force -Path $Script:SupportBundleDir | Out-Null
    }

    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $workDir = Join-Path $Script:SupportBundleDir "StadiaX-support-$stamp"
    $zipPath = "$workDir.zip"
    if (Test-Path $workDir) { Remove-Item -LiteralPath $workDir -Recurse -Force }
    if (Test-Path $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    New-Item -ItemType Directory -Force -Path $workDir | Out-Null

    try {
        $files = @(
            $Script:StatusLogPath,
            $Script:LinuxStatusLogPath,
            $Script:LinuxLogPath,
            $Script:BluetoothDiagPath,
            $Script:ControllerStatePath,
            $Script:ConfigPath,
            $Script:SelectedControllerMacsPath,
            $Script:ControllerProfilesPath,
            $Script:SelectedWslDistroPath,
            (Join-Path $Script:Root "bt_busid.txt"),
            $Script:VersionPath
        )
        foreach ($file in $files) {
            if (Test-Path $file) {
                Copy-Item -LiteralPath $file -Destination (Join-Path $workDir (Split-Path -Leaf $file)) -Force
            }
        }

        Write-AtomicText -Path (Join-Path $workDir "session-report.md") -Text (((Get-StadiaSessionReportLines) -join "`r`n") + "`r`n")

        $commandReport = Join-Path $workDir "environment.txt"
        $report = New-Object System.Collections.Generic.List[string]
        [void]$report.Add("Stadia X support bundle")
        [void]$report.Add("Created: $(Get-Date -Format o)")
        [void]$report.Add("Root: $Script:Root")
        [void]$report.Add("Version: $(Get-LocalVersion)")
        [void]$report.Add("Resolved WSL distro: $(if (Resolve-StadiaWslDistro) { Resolve-StadiaWslDistro } else { 'none' })")
        [void]$report.Add("")
        foreach ($cmd in @(
            @{ Name = "usbipd list"; File = "usbipd"; Args = @("list") },
            @{ Name = "wsl -l -v"; File = "wsl"; Args = @("-l", "-v") },
            @{ Name = "resolved wsl hostname -I"; File = "wsl"; Args = (Get-WslArgs -Arguments @("bash", "-lc", "hostname -I 2>/dev/null || true")) },
            @{ Name = "resolved wsl bluetoothctl devices"; File = "wsl"; Args = (Get-WslArgs -Arguments @("bash", "-lc", "bluetoothctl devices 2>&1 || true")) }
        )) {
            [void]$report.Add("== $($cmd.Name) ==")
            if (Test-CommandAvailable $cmd.File) {
                $result = Invoke-CapturedCommand -FileName $cmd.File -Arguments $cmd.Args -TimeoutMs 10000
                [void]$report.Add($result.Output.Trim())
                if (-not [string]::IsNullOrWhiteSpace($result.Error)) { [void]$report.Add($result.Error.Trim()) }
            } else {
                [void]$report.Add("$($cmd.File) is not available")
            }
            [void]$report.Add("")
        }
        Write-AtomicText -Path $commandReport -Text (($report -join "`r`n") + "`r`n")

        Compress-Archive -Path (Join-Path $workDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
        Add-Log "Support bundle created: $zipPath"
        [System.Windows.Forms.MessageBox]::Show("Support bundle created:`r`n$zipPath", "Stadia X diagnostics", "OK", "Information") | Out-Null
    } catch {
        Add-Log "Could not create support bundle: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show("Could not create support bundle:`r`n$($_.Exception.Message)", "Stadia X diagnostics", "OK", "Error") | Out-Null
    } finally {
        if (Test-Path $workDir) {
            Remove-Item -LiteralPath $workDir -Recurse -Force
        }
    }
}

function Refresh-BluetoothPanel {
    if (-not $Script:BluetoothStatusList) {
        return
    }

    $snapshot = Get-BluetoothSnapshot
    $capacity = Get-BluetoothControllerCapacityEstimate $snapshot
    $guidance = Get-BluetoothAdapterGuidance -Snapshot $snapshot -Capacity $capacity
    $Script:BluetoothStatusList.Items.Clear()
    $Script:BluetoothAdapterList.Items.Clear()
    $Script:BluetoothDeviceList.Items.Clear()

    $serviceState = if ($snapshot.Service) { $snapshot.Service.Status.ToString() } else { "Not found" }
    Add-ListRow $Script:BluetoothStatusList "Bluetooth service" ($(if ($serviceState -eq "Running") { "OK" } else { "WARN" })) "bthserv: $serviceState"
    Add-ListRow $Script:BluetoothStatusList "Bluetooth adapters" ($(if ($snapshot.Adapters.Count -gt 0) { "OK" } else { "MISSING" })) "$($snapshot.Adapters.Count) adapter(s) detected"
    Add-ListRow $Script:BluetoothStatusList "Active devices" ($(if ($snapshot.ActiveDevices.Count -gt 0) { "OK" } else { "INFO" })) "$($snapshot.ActiveDevices.Count) Bluetooth device(s) with PnP Status OK"
    Add-ListRow $Script:BluetoothStatusList "Known devices" ($(if ($snapshot.PairedOrKnown.Count -gt 0) { "OK" } else { "INFO" })) "$($snapshot.PairedOrKnown.Count) non-adapter Bluetooth device(s) known to Windows"
    Add-ListRow $Script:BluetoothStatusList "Bluetooth version" "INFO" "Windows does not reliably expose the Bluetooth spec version here; $($snapshot.DriverInfo)"
    Add-ListRow $Script:BluetoothStatusList "Stadia capacity estimate" $capacity.State $capacity.Details
    Add-ListRow $Script:BluetoothStatusList "Adapter advice" $guidance.State "$($guidance.Summary). $($guidance.Details)"
    Add-ListRow $Script:BluetoothStatusList "Software controller limit" "INFO" "Stadia X can bridge up to $Script:MaxControllers controllers; the adapter/driver decides how many are stable."
    Add-ListRow $Script:BluetoothStatusList "Linux diagnostics" ($(if (Test-Path $Script:BluetoothDiagPath) { "OK" } else { "INFO" })) ($(if (Test-Path $Script:BluetoothDiagPath) { $Script:BluetoothDiagPath } else { "Created after Linux core starts" }))

    foreach ($adapter in $snapshot.Adapters) {
        Add-BluetoothDeviceRow $Script:BluetoothAdapterList $adapter "Adapter"
    }

    foreach ($device in $snapshot.PairedOrKnown) {
        $role = if ($device.Status -eq "OK") { "Active/OK" } else { "Known" }
        Add-BluetoothDeviceRow $Script:BluetoothDeviceList $device $role
    }

    if ($snapshot.Adapters.Count -gt 0) {
        $Script:BluetoothSummaryLabel.Text = "Bluetooth adapter present. Estimated Stadia capacity: $($capacity.Estimated)/$Script:MaxControllers. $($snapshot.ActiveDevices.Count) active/OK device(s)."
    } else {
        $Script:BluetoothSummaryLabel.Text = "No Bluetooth adapter detected by Windows."
    }

    $diagText = ""
    if (Test-Path $Script:BluetoothDiagPath) {
        $diagText = "`r`n`r`nLatest Linux / BlueZ diagnostics:`r`n" + ((Get-Content -Path $Script:BluetoothDiagPath -Tail 90 -ErrorAction SilentlyContinue) -join "`r`n")
    }
    $Script:BluetoothInfoBox.Text = "Bluetooth version: Windows standard PnP APIs usually expose driver version, not the Bluetooth radio specification version.`r`n`r`nStadia capacity estimate: $($capacity.Estimated)/$Script:MaxControllers controller(s). $($capacity.Details)`r`n`r`nAdapter advice: $($guidance.Details)`r`n`r`nFor four Stadia controllers, prefer a modern Bluetooth 5.x adapter/chipset and keep headphones/audio devices off the same radio while playing.`r`n`r`nConnected count: this panel counts Bluetooth devices with PnP Status OK as active/available; some paired devices may still appear here differently depending on driver behavior.$diagText"
    Update-LinuxControllerSelectionLabel
}

function Get-SelectedBluetoothAdapterInstanceId {
    if (-not $Script:BluetoothAdapterList -or $Script:BluetoothAdapterList.SelectedItems.Count -eq 0) {
        return ""
    }
    return [string]$Script:BluetoothAdapterList.SelectedItems[0].Tag
}

function Invoke-BluetoothAdapterPower {
    param([bool]$Enable)

    $instanceId = Get-SelectedBluetoothAdapterInstanceId
    if ([string]::IsNullOrWhiteSpace($instanceId)) {
        [System.Windows.Forms.MessageBox]::Show("Select a Bluetooth adapter first.", "Bluetooth adapter", "OK", "Warning") | Out-Null
        return
    }

    $verb = if ($Enable) { "enable" } else { "disable" }
    $answer = [System.Windows.Forms.MessageBox]::Show(
        "This will $verb the selected Bluetooth adapter.`r`n`r`nBluetooth keyboards, mice, headphones, or controllers may disconnect. Continue?",
        "Bluetooth adapter power",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
        return
    }

    $escapedId = $instanceId.Replace("'", "''")
    $command = if ($Enable) {
        "Enable-PnpDevice -InstanceId '$escapedId' -Confirm:`$false"
    } else {
        "Disable-PnpDevice -InstanceId '$escapedId' -Confirm:`$false"
    }

    try {
        if (Test-IsAdmin) {
            if ($Enable) {
                Enable-PnpDevice -InstanceId $instanceId -Confirm:$false
            } else {
                Disable-PnpDevice -InstanceId $instanceId -Confirm:$false
            }
        } else {
            Start-Process powershell.exe -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $command)
        }
        Add-Log "Requested Bluetooth adapter $verb for $instanceId"
    } catch {
        Add-Log "Bluetooth adapter $verb failed: $($_.Exception.Message)"
    }
}

function New-ButtonIndicator {
    param(
        [string]$Key,
        [string]$Text
    )
    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.TextAlign = "MiddleCenter"
    $label.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
    $label.Size = New-Object System.Drawing.Size(92, 34)
    $label.Margin = New-Object System.Windows.Forms.Padding(6)
    $label.BackColor = [System.Drawing.Color]::FromArgb(226, 232, 240)
    $label.ForeColor = [System.Drawing.Color]::FromArgb(45, 55, 72)
    $label.BorderStyle = "FixedSingle"
    $Script:ButtonIndicators[$Key] = $label
    return $label
}

function Set-ButtonIndicator {
    param(
        [string]$Key,
        [bool]$Pressed
    )
    if (-not $Script:ButtonIndicators.ContainsKey($Key)) {
        return
    }

    $label = $Script:ButtonIndicators[$Key]
    if ($Pressed) {
        $label.BackColor = [System.Drawing.Color]::FromArgb(45, 125, 90)
        $label.ForeColor = [System.Drawing.Color]::White
    } else {
        $label.BackColor = [System.Drawing.Color]::FromArgb(226, 232, 240)
        $label.ForeColor = [System.Drawing.Color]::FromArgb(45, 55, 72)
    }
}

function Reset-ControllerIndicators {
    foreach ($key in $Script:ButtonIndicators.Keys) {
        Set-ButtonIndicator $key $false
    }
    if ($Script:LeftTriggerBar) { $Script:LeftTriggerBar.Value = 0 }
    if ($Script:RightTriggerBar) { $Script:RightTriggerBar.Value = 0 }
    if ($Script:AxesLabel) { $Script:AxesLabel.Text = "Sticks: waiting for telemetry" }
    if ($Script:ControllerStatsLabel) { $Script:ControllerStatsLabel.Text = "Packets: -    Rate: -    Last seen: -" }
    if ($Script:ControllerDeadzoneLabel) { $Script:ControllerDeadzoneLabel.Text = "Deadzone: waiting for stick data" }
    if ($Script:ControllerRawBox) { $Script:ControllerRawBox.Text = "" }
}

function Get-ControllerHealthSummary {
    if (-not (Test-Path $Script:ControllerStatePath)) {
        return [pscustomobject]@{
            State = "INFO"
            ActiveCount = 0
            Summary = "Health: no controller input yet"
            Details = "Start the bridge and press a button on each controller."
        }
    }

    try {
        $state = Get-Content -Raw -Path $Script:ControllerStatePath | ConvertFrom-Json
        $fileAge = (Get-Date) - (Get-Item $Script:ControllerStatePath).LastWriteTime
    } catch {
        return [pscustomobject]@{
            State = "WARN"
            ActiveCount = 0
            Summary = "Health: telemetry could not be parsed"
            Details = $_.Exception.Message
        }
    }

    $controllers = @()
    if ($state.PSObject.Properties["controllers"]) {
        $controllers = @($state.controllers)
    }
    if ($controllers.Count -eq 0) {
        $controllers = @([pscustomobject]@{
            index = 0
            active = $true
            packets = 0
            pps = 0
            last_seen_age_ms = [int]$fileAge.TotalMilliseconds
        })
    }

    $activeCount = 0
    $staleCount = 0
    $totalRate = 0.0
    $worstLastSeenMs = 0
    foreach ($controller in $controllers) {
        $isActive = if ($controller.PSObject.Properties["active"]) { [bool]$controller.active } else { $true }
        $pps = if ($controller.PSObject.Properties["pps"]) { [double]$controller.pps } else { 0.0 }
        $lastSeenMs = if ($controller.PSObject.Properties["last_seen_age_ms"]) { [int64]$controller.last_seen_age_ms } else { [int64]$fileAge.TotalMilliseconds }

        if ($isActive) {
            $activeCount++
            $totalRate += $pps
            $worstLastSeenMs = [Math]::Max($worstLastSeenMs, $lastSeenMs)
            if ($lastSeenMs -gt 3000 -or $pps -lt 1) {
                $staleCount++
            }
        }
    }

    $stateText = "OK"
    if ($fileAge.TotalSeconds -gt 12 -or $activeCount -eq 0 -or $staleCount -gt 0) {
        $stateText = "WARN"
    } elseif ($activeCount -lt $Script:MaxControllers) {
        $stateText = "INFO"
    }

    $summary = "Health: $activeCount/$Script:MaxControllers active"
    if ($activeCount -gt 0) {
        $summary += "    Rate: $([Math]::Round($totalRate, 1))/s    Worst last seen: $([Math]::Round($worstLastSeenMs / 1000, 1))s"
    }

    $details = if ($staleCount -gt 0) {
        "$staleCount active controller(s) are stale or below 1 packet/sec."
    } elseif ($activeCount -gt 0) {
        "Controller input is flowing."
    } else {
        "No active controller input detected."
    }

    return [pscustomobject]@{
        State = $stateText
        ActiveCount = $activeCount
        Summary = $summary
        Details = $details
    }
}

function Update-ControllerHealthLabel {
    if (-not $Script:HealthSummaryLabel) {
        return
    }

    $health = Get-ControllerHealthSummary
    $Script:HealthSummaryLabel.Text = $health.Summary
    switch ($health.State) {
        "OK" { $Script:HealthSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72) }
        "WARN" { $Script:HealthSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(170, 104, 0) }
        default { $Script:HealthSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95) }
    }
}

function Refresh-ControllerTelemetry {
    if (-not $Script:TelemetryLabel) {
        return
    }

    if (-not (Test-Path $Script:ControllerStatePath)) {
        $Script:TelemetryLabel.Text = "No controller telemetry yet. Rebuild/use the updated stadia_receiver.exe, start the bridge, then press buttons."
        Reset-ControllerIndicators
        Update-ControllerHealthLabel
        return
    }

    try {
        $state = Get-Content -Raw -Path $Script:ControllerStatePath | ConvertFrom-Json
    } catch {
        $Script:TelemetryLabel.Text = "Controller telemetry is present but could not be parsed yet."
        Update-ControllerHealthLabel
        return
    }

    $age = (Get-Date) - (Get-Item $Script:ControllerStatePath).LastWriteTime

    $controllers = @()
    if ($state.PSObject.Properties["controllers"]) {
        $controllers = @($state.controllers)
    }
    if ($controllers.Count -eq 0) {
        $controllers = @([pscustomobject]@{
            index = 0
            active = $true
            packets = 0
            pps = 0
            last_seen_age_ms = [int]($age.TotalMilliseconds)
            buttons = $state.buttons
            axes = $state.axes
        })
    }

    $selectedIndex = 0
    if ($state.PSObject.Properties["active_controller"]) {
        $selectedIndex = [int]$state.active_controller
    }
    if ($Script:ControllerSelector) {
        $previous = $Script:ControllerSelector.SelectedIndex
        $Script:ControllerSelector.Items.Clear()
        foreach ($controller in $controllers) {
            $displayIndex = if ($controller.PSObject.Properties["index"]) { [int]$controller.index } else { $Script:ControllerSelector.Items.Count }
            $label = "Controller $($displayIndex + 1)"
            if ($controller.PSObject.Properties["active"] -and [bool]$controller.active) {
                $label += " (active)"
            }
            [void]$Script:ControllerSelector.Items.Add($label)
        }
        if ($previous -ge 0 -and $previous -lt $Script:ControllerSelector.Items.Count) {
            $selectedIndex = $previous
        }
        if ($Script:ControllerSelector.Items.Count -gt 0) {
            $Script:ControllerSelector.SelectedIndex = [Math]::Min([Math]::Max(0, $selectedIndex), $Script:ControllerSelector.Items.Count - 1)
            $selectedIndex = $Script:ControllerSelector.SelectedIndex
        }
    }

    $selected = @($controllers | Where-Object {
        $_.PSObject.Properties["index"] -and [int]$_.index -eq $selectedIndex
    } | Select-Object -First 1)
    if ($selected.Count -eq 0) {
        $selected = @($controllers | Select-Object -First 1)
    }
    if ($selected.Count -eq 0) {
        Reset-ControllerIndicators
        return
    }

    $controller = $selected[0]
    $controllerNumber = if ($controller.PSObject.Properties["index"]) { [int]$controller.index + 1 } else { $selectedIndex + 1 }
    $isActive = if ($controller.PSObject.Properties["active"]) { [bool]$controller.active } else { $true }
    $buttons = $controller.buttons
    $axes = $controller.axes

    $getBool = {
        param($Object, [string]$Name)
        if ($Object -and $Object.PSObject.Properties[$Name]) { return [bool]$Object.$Name }
        return $false
    }
    $getInt = {
        param($Object, [string]$Name, [int]$Default = 0)
        if ($Object -and $Object.PSObject.Properties[$Name]) { return [int]$Object.$Name }
        return $Default
    }

    $Script:TelemetryLabel.Text = "Controller $controllerNumber telemetry: updated $([math]::Round($age.TotalSeconds, 1)) seconds ago"

    Set-ButtonIndicator "a" (& $getBool $buttons "a")
    Set-ButtonIndicator "b" (& $getBool $buttons "b")
    Set-ButtonIndicator "x" (& $getBool $buttons "x")
    Set-ButtonIndicator "y" (& $getBool $buttons "y")
    Set-ButtonIndicator "lb" (& $getBool $buttons "lb")
    Set-ButtonIndicator "rb" (& $getBool $buttons "rb")
    Set-ButtonIndicator "select" (& $getBool $buttons "select")
    Set-ButtonIndicator "start" (& $getBool $buttons "start")
    Set-ButtonIndicator "stadia" (& $getBool $buttons "stadia")
    Set-ButtonIndicator "assistant" (& $getBool $buttons "assistant")
    Set-ButtonIndicator "l3" (& $getBool $buttons "l3")
    Set-ButtonIndicator "r3" (& $getBool $buttons "r3")
    Set-ButtonIndicator "dpad_up" (& $getBool $buttons "dpad_up")
    Set-ButtonIndicator "dpad_down" (& $getBool $buttons "dpad_down")
    Set-ButtonIndicator "dpad_left" (& $getBool $buttons "dpad_left")
    Set-ButtonIndicator "dpad_right" (& $getBool $buttons "dpad_right")

    $lt = [Math]::Max(0, [Math]::Min(255, (& $getInt $axes "trigger_left" 0)))
    $rt = [Math]::Max(0, [Math]::Min(255, (& $getInt $axes "trigger_right" 0)))
    $lx = & $getInt $axes "stick_lx" 0
    $ly = & $getInt $axes "stick_ly" 0
    $rx = & $getInt $axes "stick_rx" 0
    $ry = & $getInt $axes "stick_ry" 0

    $Script:LeftTriggerBar.Value = $lt
    $Script:RightTriggerBar.Value = $rt
    $Script:AxesLabel.Text = "LX $lx  LY $ly`r`nRX $rx  RY $ry`r`nLT $lt  RT $rt"

    $packets = if ($controller.PSObject.Properties["packets"]) { [int64]$controller.packets } else { 0 }
    $pps = if ($controller.PSObject.Properties["pps"]) { [double]$controller.pps } else { 0.0 }
    $lastSeenMs = if ($controller.PSObject.Properties["last_seen_age_ms"]) { [int64]$controller.last_seen_age_ms } else { [int64]$age.TotalMilliseconds }
    if ($Script:ControllerStatsLabel) {
        $Script:ControllerStatsLabel.Text = "Active: $isActive    Packets: $packets    Rate: $([math]::Round($pps, 1))/s    Last seen: $([math]::Round($lastSeenMs / 1000, 1))s"
    }

    $deadzone = 2500
    $centered = ([Math]::Abs($lx) -le $deadzone -and [Math]::Abs($ly) -le $deadzone -and [Math]::Abs($rx) -le $deadzone -and [Math]::Abs($ry) -le $deadzone)
    if ($Script:ControllerDeadzoneLabel) {
        $Script:ControllerDeadzoneLabel.Text = if ($centered) { "Deadzone: centered within +/-$deadzone" } else { "Deadzone: movement outside +/-$deadzone" }
        $Script:ControllerDeadzoneLabel.ForeColor = if ($centered) { [System.Drawing.Color]::FromArgb(34, 120, 72) } else { [System.Drawing.Color]::FromArgb(170, 104, 0) }
    }

    if ($Script:ControllerRawBox) {
        $pressed = @()
        foreach ($name in @("a","b","x","y","lb","rb","select","start","stadia","assistant","l3","r3","dpad_up","dpad_down","dpad_left","dpad_right")) {
            if (& $getBool $buttons $name) { $pressed += $name }
        }
        if ($pressed.Count -eq 0) { $pressed = @("none") }
        $Script:ControllerRawBox.Text = @(
            "Controller: $controllerNumber"
            "Active: $isActive"
            "Pressed: $($pressed -join ', ')"
            "Left stick:  $lx, $ly"
            "Right stick: $rx, $ry"
            "Triggers:    LT $lt / RT $rt"
            "Packets:     $packets"
            "Rate:        $([math]::Round($pps, 2)) packets/sec"
        ) -join "`r`n"
    }
    Update-ControllerHealthLabel
}

function Get-UsbipdDevices {
    if (-not (Test-CommandAvailable "usbipd")) {
        return @()
    }

    $result = Invoke-CapturedCommand -FileName "usbipd" -Arguments @("list") -TimeoutMs 8000
    if ($result.ExitCode -ne 0 -and [string]::IsNullOrWhiteSpace($result.Output)) {
        return @()
    }

    $devices = New-Object System.Collections.Generic.List[object]
    foreach ($line in ($result.Output -split "`r?`n")) {
        if ($line -match "^\s*(?<busid>\d+-\d+)\s+(?<vidpid>[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4})\s+(?<name>.+?)\s{2,}(?<state>.+?)\s*$") {
            $name = $Matches.name.Trim()
            $state = $Matches.state.Trim()
            $isBluetooth = $name -match "(?i)bluetooth|wireless|intel\(r\)|realtek|mediatek|qualcomm"
            $devices.Add([pscustomobject]@{
                BusId = $Matches.busid
                VidPid = $Matches.vidpid
                Name = $name
                State = $state
                IsBluetooth = $isBluetooth
                Display = "$($Matches.busid) - $name [$state]"
            })
        }
    }

    return @($devices | Sort-Object @{ Expression = "IsBluetooth"; Descending = $true }, BusId)
}

function Get-SelectedBusId {
    if ($Script:SelectedBusIdText -and -not [string]::IsNullOrWhiteSpace($Script:SelectedBusIdText.Text)) {
        return $Script:SelectedBusIdText.Text.Trim()
    }

    if ($Script:BluetoothCombo.SelectedIndex -lt 0) {
        return ""
    }
    $selected = [string]$Script:BluetoothCombo.SelectedItem
    if ($selected -match "^(\d+-\d+)") {
        return $Matches[1]
    }
    return ""
}

function Set-SelectedBluetoothDevice {
    param([string]$BusId)

    if ([string]::IsNullOrWhiteSpace($BusId)) {
        return
    }

    $BusId = $BusId.Trim()
    if ($Script:SelectedBusIdText) {
        $Script:SelectedBusIdText.Text = $BusId
    }

    if ($Script:BluetoothCombo) {
        for ($i = 0; $i -lt $Script:BluetoothCombo.Items.Count; $i++) {
            if ([string]$Script:BluetoothCombo.Items[$i] -match "^$([regex]::Escape($BusId))\b") {
                $Script:BluetoothCombo.SelectedIndex = $i
                break
            }
        }
    }

    if ($Script:DeviceList) {
        foreach ($item in $Script:DeviceList.Items) {
            $item.Selected = ($item.Text -eq $BusId)
            if ($item.Selected) {
                $item.EnsureVisible()
            }
        }
    }

    $device = @(Get-UsbipdDevices | Where-Object { $_.BusId -eq $BusId } | Select-Object -First 1)
    if ($Script:DeviceDetailsBox) {
        if ($device.Count -gt 0) {
            $d = $device[0]
            $Script:DeviceDetailsBox.Text = "Selected BUSID: $($d.BusId)`r`nVID:PID: $($d.VidPid)`r`nName: $($d.Name)`r`nUSB/IP state: $($d.State)`r`nLikely Bluetooth: $($d.IsBluetooth)"
        } else {
            $Script:DeviceDetailsBox.Text = "Selected BUSID: $BusId`r`nThis BUSID is not present in the latest usbipd list output. Refresh devices before starting."
        }
    }
}

function Refresh-BluetoothList {
    $Script:BluetoothCombo.Items.Clear()
    if ($Script:DeviceList) {
        $Script:DeviceList.Items.Clear()
    }

    $previousBusId = ""
    if ($Script:SelectedBusIdText) {
        $previousBusId = $Script:SelectedBusIdText.Text.Trim()
    }

    $devices = Get-UsbipdDevices
    foreach ($device in $devices) {
        [void]$Script:BluetoothCombo.Items.Add($device.Display)
        if ($Script:DeviceList) {
            $item = New-Object System.Windows.Forms.ListViewItem($device.BusId)
            [void]$item.SubItems.Add($device.VidPid)
            [void]$item.SubItems.Add($device.Name)
            [void]$item.SubItems.Add($device.State)
            [void]$item.SubItems.Add($(if ($device.IsBluetooth) { "yes" } else { "no" }))
            if ($device.IsBluetooth) {
                $item.ForeColor = [System.Drawing.Color]::FromArgb(34, 120, 72)
            }
            [void]$Script:DeviceList.Items.Add($item)
        }
    }
    if ($Script:BluetoothCombo.Items.Count -gt 0) {
        if ($previousBusId) {
            Set-SelectedBluetoothDevice $previousBusId
        } else {
            $Script:BluetoothCombo.SelectedIndex = 0
            Set-SelectedBluetoothDevice (Get-SelectedBusId)
        }
        Add-Log "Loaded $($Script:BluetoothCombo.Items.Count) USB/IP device(s)."
    } else {
        Add-Log "No USB/IP devices found, or usbipd is unavailable."
    }
}

function Get-WslIp {
    if (-not (Test-CommandAvailable "wsl")) {
        return ""
    }
    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", "hostname -I 2>/dev/null")) -TimeoutMs 8000
    if ($result.ExitCode -ne 0) {
        return ""
    }
    $parts = ($result.Output.Trim() -split "\s+") | Where-Object { $_ }
    if ($parts.Count -gt 0) {
        return $parts[0]
    }
    return ""
}

function ConvertTo-BatteryPercent {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }
    if ($Value -match "([0-9]{1,3})") {
        return [Math]::Max(0, [Math]::Min(100, [int]$Matches[1]))
    }
    return $null
}

function Get-LinuxControllerBatterySnapshot {
    if (-not (Test-CommandAvailable "wsl")) {
        return @()
    }

    $selectedMacs = @(Get-SelectedLinuxControllerMacs | ForEach-Object { $_.ToUpperInvariant() })
    $devices = @(Get-LinuxBluetoothDevices)
    $controllers = @($devices | Where-Object {
        $_.IsStadia -or ($selectedMacs -contains $_.Mac.ToUpperInvariant())
    } | Select-Object -First $Script:MaxControllers)

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($device in $controllers) {
        [void]$rows.Add([pscustomobject]@{
            Name = $device.Name
            Mac = $device.Mac.ToUpperInvariant()
            Connected = $device.Connected
            BatteryPercent = ConvertTo-BatteryPercent $device.Battery
        })
    }

    foreach ($mac in $selectedMacs) {
        $known = @($rows | Where-Object { $_.Mac -eq $mac } | Select-Object -First 1)
        if ($known.Count -eq 0) {
            [void]$rows.Add([pscustomobject]@{
                Name = "Selected Stadia controller"
                Mac = $mac
                Connected = "unknown"
                BatteryPercent = $null
            })
        }
    }

    return @($rows | Select-Object -First $Script:MaxControllers)
}

function Format-BatterySnapshot {
    param([object[]]$Snapshot)

    if (-not $Snapshot -or $Snapshot.Count -eq 0) {
        if (-not (Test-CommandAvailable "wsl")) {
            return "Battery: WSL is not available."
        }
        return "Battery: not available yet. Start Stadia X and connect a controller."
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $index = 1
    foreach ($controller in $Snapshot) {
        $battery = if ($null -ne $controller.BatteryPercent) { "$($controller.BatteryPercent)%" } else { "unknown" }
        $connected = if ($controller.Connected) { $controller.Connected } else { "unknown" }
        [void]$parts.Add("P$index $battery ($connected)")
        $index++
    }

    return "Battery: " + ($parts -join "   ")
}

function Ensure-BatteryOverlay {
    if ($Script:BatteryOverlayForm -and -not $Script:BatteryOverlayForm.IsDisposed) {
        return
    }

    $overlay = New-Object System.Windows.Forms.Form
    $overlay.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
    $overlay.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
    $overlay.ShowInTaskbar = $false
    $overlay.TopMost = $true
    $overlay.BackColor = [System.Drawing.Color]::FromArgb(180, 45, 45)
    $overlay.Opacity = 0.94
    $overlay.Size = New-Object System.Drawing.Size(188, 44)

    $label = New-Object System.Windows.Forms.Label
    $label.Dock = "Fill"
    $label.TextAlign = "MiddleCenter"
    $label.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
    $label.ForeColor = [System.Drawing.Color]::White
    $overlay.Controls.Add($label)

    $Script:BatteryOverlayForm = $overlay
    $Script:BatteryOverlayLabel = $label
}

function Show-BatteryOverlay {
    param([object[]]$LowControllers)

    if (-not $LowControllers -or $LowControllers.Count -eq 0) {
        Hide-BatteryOverlay
        return
    }

    Ensure-BatteryOverlay
    $text = if ($LowControllers.Count -eq 1) {
        "Stadia battery $($LowControllers[0].BatteryPercent)%"
    } else {
        "Low batteries: " + (@($LowControllers | ForEach-Object { "$($_.BatteryPercent)%" }) -join " / ")
    }

    $Script:BatteryOverlayLabel.Text = $text
    $area = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $Script:BatteryOverlayForm.Location = New-Object System.Drawing.Point(($area.Right - $Script:BatteryOverlayForm.Width - 16), ($area.Top + 16))
    if (-not $Script:BatteryOverlayForm.Visible) {
        $Script:BatteryOverlayForm.Show($form)
    }
}

function Hide-BatteryOverlay {
    if ($Script:BatteryOverlayForm -and -not $Script:BatteryOverlayForm.IsDisposed -and $Script:BatteryOverlayForm.Visible) {
        $Script:BatteryOverlayForm.Hide()
    }
}

function Update-ControllerBatteryStatus {
    $snapshot = @(Get-LinuxControllerBatterySnapshot)
    $Script:LastBatterySnapshot = $snapshot
    $Script:LastBatteryRefresh = Get-Date
    $message = Format-BatterySnapshot $snapshot

    if ($Script:BatteryStatusLabel) {
        $Script:BatteryStatusLabel.Text = $message
        $Script:BatteryStatusLabel.ForeColor = if ($snapshot | Where-Object { $null -ne $_.BatteryPercent -and $_.BatteryPercent -le $Script:BatteryOverlayThreshold }) {
            [System.Drawing.Color]::FromArgb(180, 45, 45)
        } else {
            [System.Drawing.Color]::FromArgb(70, 80, 95)
        }
    }

    $lowControllers = @($snapshot | Where-Object { $null -ne $_.BatteryPercent -and $_.BatteryPercent -le $Script:BatteryOverlayThreshold })
    Show-BatteryOverlay $lowControllers
    return $message
}

function Get-BatteryInfo {
    $snapshot = @(Get-LinuxControllerBatterySnapshot)
    $message = Format-BatterySnapshot $snapshot
    if ($snapshot.Count -gt 0) {
        return $message
    }

    if (-not (Test-CommandAvailable "wsl")) {
        return $message
    }

    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments (Get-WslArgs -Arguments @("bash", "-lc", "bluetoothctl info 2>/dev/null | grep -i Battery")) -TimeoutMs 10000
    $text = ($result.Output + "`n" + $result.Error).Trim()
    if ($text -match "\(([0-9]{1,3})\)") {
        return "Battery: controller $($Matches[1])%"
    }
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        return $text
    }
    return $message
}

function Refresh-Status {
    $Script:ChecksList.Items.Clear()

    Add-CheckRow "PowerShell elevation" ($(if (Test-IsAdmin) { "OK" } else { "WARN" })) ($(if (Test-IsAdmin) { "Running as Administrator" } else { "Start/Stop will ask for UAC elevation" }))
    Add-CheckRow "usbipd" ($(if (Test-CommandAvailable "usbipd") { "OK" } else { "MISSING" })) "Required to attach the Bluetooth adapter to WSL"
    Add-CheckRow "wsl" ($(if (Test-CommandAvailable "wsl") { "OK" } else { "MISSING" })) "Required for the Linux bridge"
    Add-CheckRow "ViGEmBus driver" ($(if (Test-ViGEmBusInstalled) { "OK" } else { "MISSING" })) "Required for Xbox 360 controller emulation"

    $resolvedDistro = Resolve-StadiaWslDistro
    Add-CheckRow "WSL distro" ($(if ($resolvedDistro -and (Test-WslDistroAvailable $resolvedDistro)) { "OK" } else { "WARN" })) ($(if ($resolvedDistro) { $resolvedDistro } else { "No distro detected; Start can install Ubuntu" }))
    Add-CheckRow "WSL distro version" ($(if ($resolvedDistro -and (Test-WslDistroWsl2 $resolvedDistro)) { "OK" } else { "WARN" })) "USB/IP requires WSL2"

    Add-CheckRow "stadia_receiver.exe" ($(if (Test-Path $Script:ReceiverPath) { "OK" } else { "MISSING" })) "Windows ViGEm receiver runtime"
    Add-CheckRow "ViGEmClient.dll" ($(if (Test-Path $Script:ViGEmClientPath) { "OK" } else { "MISSING" })) "Native ViGEm client DLL beside the receiver"
    Add-CheckRow "stadia_bridge" ($(if (Test-Path $Script:BridgePath) { "OK" } else { "MISSING" })) "Linux bridge runtime copied into WSL"
    Add-CheckRow "start.sh" ($(if (Test-Path $Script:StartShPath) { "OK" } else { "MISSING" })) "Linux setup and controller pairing script"
    Add-CheckRow "stadia_buttons.ini" ($(if (Test-Path $Script:ConfigPath) { "OK" } else { "MISSING" })) "Macro shortcut configuration"

    $receiverRunning = [bool](Get-Process -Name "stadia_receiver" -ErrorAction SilentlyContinue)
    Add-CheckRow "Receiver process" ($(if ($receiverRunning) { "OK" } else { "INFO" })) ($(if ($receiverRunning) { "stadia_receiver.exe is running" } else { "Not running" }))

    $wslIp = Get-WslIp
    Add-CheckRow "WSL IP" ($(if ($wslIp) { "OK" } else { "INFO" })) ($(if ($wslIp) { $wslIp } else { "Not detected" }))

    $missingRuntime = -not (Test-Path $Script:ReceiverPath) -or -not (Test-Path $Script:ViGEmClientPath) -or -not (Test-Path $Script:BridgePath)
    if ($receiverRunning) {
        Set-StatusText "Running" ([System.Drawing.Color]::FromArgb(34, 120, 72))
    } elseif ($missingRuntime) {
        Set-StatusText "Runtime files missing" ([System.Drawing.Color]::FromArgb(180, 45, 45))
    } else {
        Set-StatusText "Ready" ([System.Drawing.Color]::FromArgb(34, 80, 150))
    }
}

function Load-MacroConfig {
    if (Test-Path $Script:ConfigPath) {
        $Script:MacroBox.Text = [System.IO.File]::ReadAllText($Script:ConfigPath)
        Add-Log "Loaded macro config."
    } else {
        $Script:MacroBox.Text = ""
        Add-Log "Macro config not found: $Script:ConfigPath"
    }
    Refresh-MacroMappingList
}

function Save-MacroConfig {
    if (-not (Test-Path $Script:ConfigPath)) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "stadia_buttons.ini does not exist. Create it now?",
            "Save macro config",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question
        )
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }
    } else {
        $backup = Join-Path $Script:Root ("stadia_buttons.ini." + (Get-Date -Format "yyyyMMdd-HHmmss") + ".bak")
        Copy-Item -LiteralPath $Script:ConfigPath -Destination $backup -Force
        Add-Log "Backup saved: $(Split-Path -Leaf $backup)"
    }

    Write-AtomicText -Path $Script:ConfigPath -Text $Script:MacroBox.Text
    Add-Log "Saved macro config."
    Refresh-MacroMappingList
    Refresh-Status
}

function Start-StadiaBridge {
    if (-not (Test-Path $Script:StartScript)) {
        [System.Windows.Forms.MessageBox]::Show("Start-Stadia.bat was not found.", "Stadia X", "OK", "Error") | Out-Null
        return
    }

    $missing = @()
    if (-not (Test-Path $Script:ReceiverPath)) { $missing += "stadia_receiver.exe" }
    if (-not (Test-Path $Script:ViGEmClientPath)) { $missing += "ViGEmClient.dll" }
    if (-not (Test-Path $Script:BridgePath)) { $missing += "stadia_bridge" }
    if (-not (Test-Path $Script:StartShPath)) { $missing += "start.sh" }
    if ($missing.Count -gt 0) {
        $message = "These runtime file(s) are missing:`r`n`r`n" + ($missing -join "`r`n") + "`r`n`r`nStart-Stadia.bat will probably stop until the release/build artifacts are added. Continue anyway?"
        $answer = [System.Windows.Forms.MessageBox]::Show($message, "Runtime files missing", [System.Windows.Forms.MessageBoxButtons]::YesNo, [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }
    }

    $busId = Get-SelectedBusId
    if (-not $busId) {
        [System.Windows.Forms.MessageBox]::Show("Select or type the Bluetooth USB/IP BUSID before starting.", "Bluetooth adapter required", "OK", "Warning") | Out-Null
        return
    }
    if ($busId -notmatch "^\d+-\d+$") {
        [System.Windows.Forms.MessageBox]::Show("The selected BUSID '$busId' is not valid. Expected format looks like 1-13.", "Invalid Bluetooth BUSID", "OK", "Warning") | Out-Null
        return
    }

    $knownDevice = @(Get-UsbipdDevices | Where-Object { $_.BusId -eq $busId } | Select-Object -First 1)
    if ($knownDevice.Count -eq 0) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "BUSID '$busId' was not found in the latest usbipd list output.`r`n`r`nRefresh adapters, or continue only if you are sure this BUSID is correct.",
            "BUSID not found",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )
        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            return
        }
    }

    $quotedScript = '"' + $Script:StartScript + '"'
    if ($busId) {
        $arguments = "/k $quotedScript `"$busId`""
        Add-Log "Starting bridge with Bluetooth BUSID $busId."
    } else {
        $arguments = "/k $quotedScript"
        Add-Log "Starting bridge with script auto-detection."
    }

    $manualMacs = @(Get-SelectedLinuxControllerMacs)
    if ($manualMacs.Count -gt 0) {
        Add-Log "Linux will use manual Stadia controller MAC(s): $($manualMacs -join ', ')"
    }

    $startParams = @{
        FilePath = "cmd.exe"
        ArgumentList = $arguments
        WorkingDirectory = $Script:Root
        WindowStyle = "Normal"
    }
    if (-not (Test-IsAdmin)) {
        $startParams.Verb = "RunAs"
        Add-Log "Requesting Administrator elevation for Start-Stadia.bat."
    }

    try {
        Start-Process @startParams
        $tabs.SelectedTab = $livePage
        Refresh-LiveStatus
    } catch {
        Add-Log "Start cancelled or failed: $($_.Exception.Message)"
    }
}

function Stop-StadiaBridge {
    if (-not (Test-Path $Script:StopScript)) {
        [System.Windows.Forms.MessageBox]::Show("Stop-Stadia.bat was not found.", "Stadia X", "OK", "Error") | Out-Null
        return
    }

    $quotedScript = '"' + $Script:StopScript + '"'
    $startParams = @{
        FilePath = "cmd.exe"
        ArgumentList = "/k $quotedScript"
        WorkingDirectory = $Script:Root
        WindowStyle = "Normal"
    }
    if (-not (Test-IsAdmin)) {
        $startParams.Verb = "RunAs"
        Add-Log "Requesting Administrator elevation for Stop-Stadia.bat."
    }

    try {
        Start-Process @startParams
        Add-Log "Stop script launched."
        $tabs.SelectedTab = $livePage
    } catch {
        Add-Log "Stop cancelled or failed: $($_.Exception.Message)"
    }
}

function Open-ProjectFolder {
    Start-Process explorer.exe -ArgumentList ('"' + $Script:Root + '"')
}

function Start-StadiaInstaller {
    $installer = Join-Path $Script:Root "Install-StadiaX.bat"
    if (-not (Test-Path $installer)) {
        [System.Windows.Forms.MessageBox]::Show("Install-StadiaX.bat was not found.", "Stadia X installer", "OK", "Warning") | Out-Null
        return
    }

    try {
        Start-Process -FilePath $installer -WorkingDirectory $Script:Root
        Add-Log "Installer launched."
    } catch {
        Add-Log "Installer launch failed: $($_.Exception.Message)"
    }
}

function Get-LocalVersion {
    if (Test-Path $Script:VersionPath) {
        $version = (Get-Content -Raw -Path $Script:VersionPath -ErrorAction SilentlyContinue).Trim()
        if (-not [string]::IsNullOrWhiteSpace($version)) {
            return $version
        }
    }
    return "local"
}

function ConvertTo-StadiaVersion {
    param([string]$Tag)
    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return $null
    }

    $clean = $Tag.Trim()
    if ($clean.StartsWith("v")) {
        $clean = $clean.Substring(1)
    }
    if ($clean -match "^(\d+)\.(\d+)\.(\d+)") {
        try {
            return [version]"$($Matches[1]).$($Matches[2]).$($Matches[3])"
        } catch {
            return $null
        }
    }
    return $null
}

function Test-StadiaUpdateAvailable {
    param(
        [string]$Local,
        [string]$Latest
    )

    $localVersion = ConvertTo-StadiaVersion $Local
    $latestVersion = ConvertTo-StadiaVersion $Latest
    if ($localVersion -and $latestVersion) {
        return ($latestVersion -gt $localVersion)
    }
    return ($Local -ne $Latest -and $Latest -notmatch "(?i)local|dev")
}

function Check-StadiaUpdate {
    $local = Get-LocalVersion
    if ($Script:UpdateStatusLabel) {
        $Script:UpdateStatusLabel.Text = "Checking latest release..."
    }

    try {
        $headers = @{ "User-Agent" = "StadiaX-GUI" }
        $release = Invoke-RestMethod -Uri $Script:LatestReleaseApiUrl -Headers $headers -TimeoutSec 10
        $latest = [string]$release.tag_name
        $setupAsset = @($release.assets | Where-Object { $_.name -like "*Setup.exe" } | Select-Object -First 1)
        $downloadUrl = if ($setupAsset.Count -gt 0) { [string]$setupAsset[0].browser_download_url } else { [string]$release.html_url }
        $needsUpdate = Test-StadiaUpdateAvailable -Local $local -Latest $latest

        if ($needsUpdate) {
            $message = "Update available: $latest (installed $local)"
            if ($Script:UpdateStatusLabel) { $Script:UpdateStatusLabel.Text = "$message - open Releases to download." }
            Add-Log "$message. Download: $downloadUrl"
            return [pscustomobject]@{ Local = $local; Latest = $latest; NeedsUpdate = $true; Url = $downloadUrl }
        }

        $message = "Up to date: $local"
        if ($Script:UpdateStatusLabel) { $Script:UpdateStatusLabel.Text = $message }
        Add-Log "Update check completed. $message"
        return [pscustomobject]@{ Local = $local; Latest = $latest; NeedsUpdate = $false; Url = [string]$release.html_url }
    } catch {
        $message = "Update check failed: $($_.Exception.Message)"
        if ($Script:UpdateStatusLabel) { $Script:UpdateStatusLabel.Text = $message }
        Add-Log $message
        return $null
    }
}

function Open-StadiaReleases {
    Start-Process $Script:ReleasesUrl
}

function Get-SelectedControllerIndex {
    if ($Script:ControllerSelector -and $Script:ControllerSelector.SelectedIndex -ge 0) {
        return [Math]::Max(0, $Script:ControllerSelector.SelectedIndex)
    }
    return 0
}

function Send-TestRumble {
    $controllerIndex = Get-SelectedControllerIndex
    $wslIp = Get-WslIp
    if ([string]::IsNullOrWhiteSpace($wslIp)) {
        [System.Windows.Forms.MessageBox]::Show("WSL IP not detected. Start the bridge before testing rumble.", "Rumble test", "OK", "Warning") | Out-Null
        return
    }

    try {
        $client = New-Object System.Net.Sockets.UdpClient
        $bytes = [byte[]](0x53, 0x01, [byte]$controllerIndex, 0x00, 180, 120)
        [void]$client.Send($bytes, $bytes.Length, $wslIp, 45494)
        Start-Sleep -Milliseconds 280
        $stop = [byte[]](0x53, 0x01, [byte]$controllerIndex, 0x00, 0, 0)
        [void]$client.Send($stop, $stop.Length, $wslIp, 45494)
        $client.Close()
        Add-Log "Sent rumble test to controller $($controllerIndex + 1) through $wslIp:45494."
    } catch {
        Add-Log "Rumble test failed: $($_.Exception.Message)"
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Stadia X Control Center"
$form.StartPosition = "CenterScreen"
$form.Size = New-Object System.Drawing.Size(980, 700)
$form.MinimumSize = New-Object System.Drawing.Size(900, 620)
$form.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)

$header = New-Object System.Windows.Forms.Panel
$header.Dock = "Top"
$header.Height = 82
$header.BackColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$form.Controls.Add($header)

$title = New-Object System.Windows.Forms.Label
$title.Text = "Stadia X"
$title.Font = New-Object System.Drawing.Font("Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
$title.ForeColor = [System.Drawing.Color]::White
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(18, 12)
$header.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = "Native bridge control panel"
$subtitle.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(202, 213, 225)
$subtitle.AutoSize = $true
$subtitle.Location = New-Object System.Drawing.Point(22, 51)
$header.Controls.Add($subtitle)

$Script:StatusLabel = New-Object System.Windows.Forms.Label
$Script:StatusLabel.Text = "Checking..."
$Script:StatusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
$Script:StatusLabel.ForeColor = [System.Drawing.Color]::White
$Script:StatusLabel.TextAlign = "MiddleRight"
$Script:StatusLabel.Anchor = "Top,Right"
$Script:StatusLabel.Size = New-Object System.Drawing.Size(320, 32)
$Script:StatusLabel.Location = New-Object System.Drawing.Point(($form.ClientSize.Width - 350), 24)
$header.Controls.Add($Script:StatusLabel)

$tabs = New-Object System.Windows.Forms.TabControl
$tabs.Dock = "Fill"
$tabs.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$form.Controls.Add($tabs)

$firstRunPage = New-Object System.Windows.Forms.TabPage
$firstRunPage.Text = "First Run"
$firstRunPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($firstRunPage)

$setupPage = New-Object System.Windows.Forms.TabPage
$setupPage.Text = "Setup"
$setupPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($setupPage)

$bluetoothPage = New-Object System.Windows.Forms.TabPage
$bluetoothPage.Text = "Bluetooth"
$bluetoothPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($bluetoothPage)

$statusPage = New-Object System.Windows.Forms.TabPage
$statusPage.Text = "Control"
$statusPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($statusPage)

$livePage = New-Object System.Windows.Forms.TabPage
$livePage.Text = "Live Status"
$livePage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($livePage)

$controllerPage = New-Object System.Windows.Forms.TabPage
$controllerPage.Text = "Controller Test"
$controllerPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($controllerPage)

$profilesPage = New-Object System.Windows.Forms.TabPage
$profilesPage.Text = "Profiles"
$profilesPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($profilesPage)

$macroPage = New-Object System.Windows.Forms.TabPage
$macroPage.Text = "Macros"
$macroPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($macroPage)

$logPage = New-Object System.Windows.Forms.TabPage
$logPage.Text = "Log"
$logPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($logPage)

$firstRunTop = New-Object System.Windows.Forms.Panel
$firstRunTop.Dock = "Top"
$firstRunTop.Height = 104
$firstRunTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$firstRunPage.Controls.Add($firstRunTop)

$Script:WizardSummaryLabel = New-Object System.Windows.Forms.Label
$Script:WizardSummaryLabel.Text = "Run the checklist to prepare Stadia X."
$Script:WizardSummaryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:WizardSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:WizardSummaryLabel.AutoSize = $false
$Script:WizardSummaryLabel.Size = New-Object System.Drawing.Size(780, 24)
$Script:WizardSummaryLabel.Location = New-Object System.Drawing.Point(14, 10)
$firstRunTop.Controls.Add($Script:WizardSummaryLabel)

$Script:UpdateStatusLabel = New-Object System.Windows.Forms.Label
$Script:UpdateStatusLabel.Text = "Installed version: $(Get-LocalVersion)"
$Script:UpdateStatusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$Script:UpdateStatusLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$Script:UpdateStatusLabel.AutoSize = $false
$Script:UpdateStatusLabel.Size = New-Object System.Drawing.Size(780, 22)
$Script:UpdateStatusLabel.Location = New-Object System.Drawing.Point(14, 38)
$firstRunTop.Controls.Add($Script:UpdateStatusLabel)

$runWizardButton = New-Object System.Windows.Forms.Button
$runWizardButton.Text = "Run checklist"
$runWizardButton.Size = New-Object System.Drawing.Size(110, 30)
$runWizardButton.Location = New-Object System.Drawing.Point(14, 68)
$runWizardButton.Add_Click({
    Refresh-BluetoothList
    Refresh-FirstRunWizard
})
$firstRunTop.Controls.Add($runWizardButton)

$installerButton = New-Object System.Windows.Forms.Button
$installerButton.Text = "Install"
$installerButton.Size = New-Object System.Drawing.Size(90, 30)
$installerButton.Location = New-Object System.Drawing.Point(132, 68)
$installerButton.Add_Click({ Start-StadiaInstaller })
$firstRunTop.Controls.Add($installerButton)

$firstRunStartButton = New-Object System.Windows.Forms.Button
$firstRunStartButton.Text = "Start"
$firstRunStartButton.Size = New-Object System.Drawing.Size(90, 30)
$firstRunStartButton.Location = New-Object System.Drawing.Point(230, 68)
$firstRunStartButton.Add_Click({ Start-StadiaBridge })
$firstRunTop.Controls.Add($firstRunStartButton)

$firstRunSetupButton = New-Object System.Windows.Forms.Button
$firstRunSetupButton.Text = "Setup"
$firstRunSetupButton.Size = New-Object System.Drawing.Size(90, 30)
$firstRunSetupButton.Location = New-Object System.Drawing.Point(328, 68)
$firstRunSetupButton.Add_Click({ $tabs.SelectedTab = $setupPage })
$firstRunTop.Controls.Add($firstRunSetupButton)

$firstRunBluetoothButton = New-Object System.Windows.Forms.Button
$firstRunBluetoothButton.Text = "Bluetooth"
$firstRunBluetoothButton.Size = New-Object System.Drawing.Size(90, 30)
$firstRunBluetoothButton.Location = New-Object System.Drawing.Point(426, 68)
$firstRunBluetoothButton.Add_Click({ $tabs.SelectedTab = $bluetoothPage })
$firstRunTop.Controls.Add($firstRunBluetoothButton)

$firstRunTestButton = New-Object System.Windows.Forms.Button
$firstRunTestButton.Text = "Test"
$firstRunTestButton.Size = New-Object System.Drawing.Size(90, 30)
$firstRunTestButton.Location = New-Object System.Drawing.Point(524, 68)
$firstRunTestButton.Add_Click({ $tabs.SelectedTab = $controllerPage })
$firstRunTop.Controls.Add($firstRunTestButton)

$checkUpdateButton = New-Object System.Windows.Forms.Button
$checkUpdateButton.Text = "Check updates"
$checkUpdateButton.Size = New-Object System.Drawing.Size(118, 30)
$checkUpdateButton.Location = New-Object System.Drawing.Point(622, 68)
$checkUpdateButton.Add_Click({
    Check-StadiaUpdate | Out-Null
    Refresh-FirstRunWizard
})
$firstRunTop.Controls.Add($checkUpdateButton)

$openReleasesButton = New-Object System.Windows.Forms.Button
$openReleasesButton.Text = "Releases"
$openReleasesButton.Size = New-Object System.Drawing.Size(90, 30)
$openReleasesButton.Location = New-Object System.Drawing.Point(748, 68)
$openReleasesButton.Add_Click({ Open-StadiaReleases })
$firstRunTop.Controls.Add($openReleasesButton)

$firstRunSplit = New-Object System.Windows.Forms.SplitContainer
$firstRunSplit.Dock = "Fill"
$firstRunSplit.Orientation = "Horizontal"
$firstRunSplit.SplitterDistance = 360
$firstRunSplit.Panel1.Padding = New-Object System.Windows.Forms.Padding(14, 10, 14, 6)
$firstRunSplit.Panel2.Padding = New-Object System.Windows.Forms.Padding(14, 6, 14, 14)
$firstRunPage.Controls.Add($firstRunSplit)
$firstRunTop.BringToFront()

$wizardGroup = New-Object System.Windows.Forms.GroupBox
$wizardGroup.Text = "First-run checklist"
$wizardGroup.Dock = "Fill"
$wizardGroup.Padding = New-Object System.Windows.Forms.Padding(12)
$wizardGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$firstRunSplit.Panel1.Controls.Add($wizardGroup)

$Script:WizardList = New-Object System.Windows.Forms.ListView
$Script:WizardList.View = "Details"
$Script:WizardList.FullRowSelect = $true
$Script:WizardList.GridLines = $true
$Script:WizardList.Dock = "Fill"
[void]$Script:WizardList.Columns.Add("Step", 190)
[void]$Script:WizardList.Columns.Add("Status", 90)
[void]$Script:WizardList.Columns.Add("Details", 650)
$wizardGroup.Controls.Add($Script:WizardList)

$wizardDetailGroup = New-Object System.Windows.Forms.GroupBox
$wizardDetailGroup.Text = "Current context"
$wizardDetailGroup.Dock = "Fill"
$wizardDetailGroup.Padding = New-Object System.Windows.Forms.Padding(12)
$wizardDetailGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$firstRunSplit.Panel2.Controls.Add($wizardDetailGroup)

$Script:WizardDetailBox = New-Object System.Windows.Forms.TextBox
$Script:WizardDetailBox.Multiline = $true
$Script:WizardDetailBox.ReadOnly = $true
$Script:WizardDetailBox.ScrollBars = "Vertical"
$Script:WizardDetailBox.BorderStyle = "None"
$Script:WizardDetailBox.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$Script:WizardDetailBox.Font = New-Object System.Drawing.Font("Consolas", 9)
$Script:WizardDetailBox.Dock = "Fill"
$wizardDetailGroup.Controls.Add($Script:WizardDetailBox)

$setupTop = New-Object System.Windows.Forms.Panel
$setupTop.Dock = "Top"
$setupTop.Height = 78
$setupTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$setupPage.Controls.Add($setupTop)

$preAuditButton = New-Object System.Windows.Forms.Button
$preAuditButton.Text = "Run pre-start audit"
$preAuditButton.Size = New-Object System.Drawing.Size(150, 32)
$preAuditButton.Location = New-Object System.Drawing.Point(14, 12)
$preAuditButton.Add_Click({ Run-SetupAudit })
$setupTop.Controls.Add($preAuditButton)

$postAuditButton = New-Object System.Windows.Forms.Button
$postAuditButton.Text = "Run post-start audit"
$postAuditButton.Size = New-Object System.Drawing.Size(155, 32)
$postAuditButton.Location = New-Object System.Drawing.Point(176, 12)
$postAuditButton.Add_Click({ Run-PostStartAudit })
$setupTop.Controls.Add($postAuditButton)

$wslRefreshButton = New-Object System.Windows.Forms.Button
$wslRefreshButton.Text = "Refresh WSL"
$wslRefreshButton.Size = New-Object System.Drawing.Size(96, 32)
$wslRefreshButton.Location = New-Object System.Drawing.Point(348, 12)
$wslRefreshButton.Add_Click({ Refresh-WslDistroList; Run-SetupAudit; Refresh-FirstRunWizard })
$setupTop.Controls.Add($wslRefreshButton)

$Script:WslDistroCombo = New-Object System.Windows.Forms.ComboBox
$Script:WslDistroCombo.DropDownStyle = "DropDownList"
$Script:WslDistroCombo.Size = New-Object System.Drawing.Size(210, 28)
$Script:WslDistroCombo.Location = New-Object System.Drawing.Point(456, 14)
$setupTop.Controls.Add($Script:WslDistroCombo)

$wslUseButton = New-Object System.Windows.Forms.Button
$wslUseButton.Text = "Use distro"
$wslUseButton.Size = New-Object System.Drawing.Size(92, 32)
$wslUseButton.Location = New-Object System.Drawing.Point(676, 12)
$wslUseButton.Add_Click({
    if (-not $Script:WslDistroCombo -or $Script:WslDistroCombo.SelectedIndex -lt 0 -or $Script:WslDistroCombo.SelectedIndex -eq 0) {
        Set-SelectedWslDistro ""
        Add-Log "WSL distro selection reset to automatic."
    } else {
        $selected = [string]$Script:WslDistroCombo.SelectedItem
        if ($selected -match "^(?<name>[A-Za-z0-9_.-]+)\s") {
            Set-SelectedWslDistro $Matches.name
            Add-Log "WSL distro selected: $($Matches.name)"
        }
    }
    Refresh-WslDistroList
    Refresh-FirstRunWizard
    Run-SetupAudit
})
$setupTop.Controls.Add($wslUseButton)

$Script:SelectedWslDistroLabel = New-Object System.Windows.Forms.Label
$Script:SelectedWslDistroLabel.AutoSize = $true
$Script:SelectedWslDistroLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8.5)
$Script:SelectedWslDistroLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$Script:SelectedWslDistroLabel.Location = New-Object System.Drawing.Point(784, 19)
$setupTop.Controls.Add($Script:SelectedWslDistroLabel)

$setupHint = New-Object System.Windows.Forms.Label
$setupHint.Text = "Use this before and after starting Stadia X to catch missing requirements early."
$setupHint.AutoSize = $true
$setupHint.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$setupHint.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$setupHint.Location = New-Object System.Drawing.Point(14, 44)
$setupTop.Controls.Add($setupHint)

$setupSplit = New-Object System.Windows.Forms.SplitContainer
$setupSplit.Dock = "Fill"
$setupSplit.Orientation = "Horizontal"
$setupSplit.SplitterDistance = 250
$setupSplit.Panel1.Padding = New-Object System.Windows.Forms.Padding(14, 8, 14, 6)
$setupSplit.Panel2.Padding = New-Object System.Windows.Forms.Padding(14, 6, 14, 14)
$setupPage.Controls.Add($setupSplit)
$setupTop.BringToFront()

$setupGroup = New-Object System.Windows.Forms.GroupBox
$setupGroup.Text = "Installation and health checks"
$setupGroup.Dock = "Fill"
$setupGroup.Padding = New-Object System.Windows.Forms.Padding(12)
$setupGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$setupSplit.Panel1.Controls.Add($setupGroup)

$Script:SetupList = New-Object System.Windows.Forms.ListView
$Script:SetupList.View = "Details"
$Script:SetupList.FullRowSelect = $true
$Script:SetupList.GridLines = $true
$Script:SetupList.Dock = "Fill"
[void]$Script:SetupList.Columns.Add("Check", 220)
[void]$Script:SetupList.Columns.Add("Status", 90)
[void]$Script:SetupList.Columns.Add("Details", 560)
$setupGroup.Controls.Add($Script:SetupList)

$devicesGroup = New-Object System.Windows.Forms.GroupBox
$devicesGroup.Text = "USB/IP devices - select the Bluetooth adapter to pass fully to Linux"
$devicesGroup.Dock = "Fill"
$devicesGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$setupSplit.Panel2.Controls.Add($devicesGroup)

$Script:DeviceList = New-Object System.Windows.Forms.ListView
$Script:DeviceList.View = "Details"
$Script:DeviceList.FullRowSelect = $true
$Script:DeviceList.GridLines = $true
$Script:DeviceList.Dock = "Fill"
[void]$Script:DeviceList.Columns.Add("BUSID", 80)
[void]$Script:DeviceList.Columns.Add("VID:PID", 90)
[void]$Script:DeviceList.Columns.Add("Device", 430)
[void]$Script:DeviceList.Columns.Add("USB/IP state", 140)
[void]$Script:DeviceList.Columns.Add("Bluetooth?", 90)
$Script:DeviceList.Add_SelectedIndexChanged({
    if ($Script:DeviceList.SelectedItems.Count -gt 0) {
        Set-SelectedBluetoothDevice $Script:DeviceList.SelectedItems[0].Text
    }
})
$devicesGroup.Controls.Add($Script:DeviceList)

$bluetoothTop = New-Object System.Windows.Forms.Panel
$bluetoothTop.Dock = "Top"
$bluetoothTop.Height = 94
$bluetoothTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$bluetoothPage.Controls.Add($bluetoothTop)

$Script:BluetoothSummaryLabel = New-Object System.Windows.Forms.Label
$Script:BluetoothSummaryLabel.Text = "Bluetooth status not loaded yet."
$Script:BluetoothSummaryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:BluetoothSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:BluetoothSummaryLabel.AutoSize = $false
$Script:BluetoothSummaryLabel.Size = New-Object System.Drawing.Size(420, 30)
$Script:BluetoothSummaryLabel.Location = New-Object System.Drawing.Point(14, 16)
$bluetoothTop.Controls.Add($Script:BluetoothSummaryLabel)

$pairWizardButton = New-Object System.Windows.Forms.Button
$pairWizardButton.Text = "Pair wizard"
$pairWizardButton.Size = New-Object System.Drawing.Size(95, 30)
$pairWizardButton.Location = New-Object System.Drawing.Point(455, 14)
$pairWizardButton.Add_Click({ Start-PairingWizard })
$bluetoothTop.Controls.Add($pairWizardButton)

$refreshBluetoothButton = New-Object System.Windows.Forms.Button
$refreshBluetoothButton.Text = "Refresh"
$refreshBluetoothButton.Size = New-Object System.Drawing.Size(85, 30)
$refreshBluetoothButton.Location = New-Object System.Drawing.Point(560, 14)
$refreshBluetoothButton.Add_Click({ Refresh-BluetoothPanel })
$bluetoothTop.Controls.Add($refreshBluetoothButton)

$enableBluetoothButton = New-Object System.Windows.Forms.Button
$enableBluetoothButton.Text = "Enable adapter"
$enableBluetoothButton.Size = New-Object System.Drawing.Size(120, 30)
$enableBluetoothButton.Location = New-Object System.Drawing.Point(655, 14)
$enableBluetoothButton.Add_Click({
    Invoke-BluetoothAdapterPower $true
    Start-Sleep -Milliseconds 600
    Refresh-BluetoothPanel
})
$bluetoothTop.Controls.Add($enableBluetoothButton)

$disableBluetoothButton = New-Object System.Windows.Forms.Button
$disableBluetoothButton.Text = "Disable adapter"
$disableBluetoothButton.Size = New-Object System.Drawing.Size(125, 30)
$disableBluetoothButton.Location = New-Object System.Drawing.Point(785, 14)
$disableBluetoothButton.Add_Click({
    Invoke-BluetoothAdapterPower $false
    Start-Sleep -Milliseconds 600
    Refresh-BluetoothPanel
})
$bluetoothTop.Controls.Add($disableBluetoothButton)

$capacityWizardButton = New-Object System.Windows.Forms.Button
$capacityWizardButton.Text = "Capacity wizard"
$capacityWizardButton.Size = New-Object System.Drawing.Size(125, 28)
$capacityWizardButton.Location = New-Object System.Drawing.Point(455, 52)
$capacityWizardButton.Add_Click({ Start-ControllerCapacityWizard })
$bluetoothTop.Controls.Add($capacityWizardButton)

$partyModeButton = New-Object System.Windows.Forms.Button
$partyModeButton.Text = "Party mode"
$partyModeButton.Size = New-Object System.Drawing.Size(100, 28)
$partyModeButton.Location = New-Object System.Drawing.Point(590, 52)
$partyModeButton.Add_Click({ Enable-PartyMode })
$bluetoothTop.Controls.Add($partyModeButton)

$repairBluetoothButton = New-Object System.Windows.Forms.Button
$repairBluetoothButton.Text = "Repair"
$repairBluetoothButton.Size = New-Object System.Drawing.Size(90, 28)
$repairBluetoothButton.Location = New-Object System.Drawing.Point(700, 52)
$repairBluetoothButton.Add_Click({ Invoke-StadiaRepairFlow })
$bluetoothTop.Controls.Add($repairBluetoothButton)

$bluetoothSplit = New-Object System.Windows.Forms.SplitContainer
$bluetoothSplit.Dock = "Fill"
$bluetoothSplit.Orientation = "Horizontal"
$bluetoothSplit.SplitterDistance = 230
$bluetoothSplit.Panel1.Padding = New-Object System.Windows.Forms.Padding(14, 8, 14, 6)
$bluetoothSplit.Panel2.Padding = New-Object System.Windows.Forms.Padding(14, 6, 14, 14)
$bluetoothPage.Controls.Add($bluetoothSplit)
$bluetoothTop.BringToFront()

$bluetoothUpperSplit = New-Object System.Windows.Forms.SplitContainer
$bluetoothUpperSplit.Dock = "Fill"
$bluetoothUpperSplit.Orientation = "Vertical"
$bluetoothUpperSplit.SplitterDistance = 430
$bluetoothSplit.Panel1.Controls.Add($bluetoothUpperSplit)

$bluetoothStatusGroup = New-Object System.Windows.Forms.GroupBox
$bluetoothStatusGroup.Text = "Bluetooth status"
$bluetoothStatusGroup.Dock = "Fill"
$bluetoothStatusGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothUpperSplit.Panel1.Controls.Add($bluetoothStatusGroup)

$Script:BluetoothStatusList = New-Object System.Windows.Forms.ListView
$Script:BluetoothStatusList.View = "Details"
$Script:BluetoothStatusList.FullRowSelect = $true
$Script:BluetoothStatusList.GridLines = $true
$Script:BluetoothStatusList.Dock = "Fill"
[void]$Script:BluetoothStatusList.Columns.Add("Item", 160)
[void]$Script:BluetoothStatusList.Columns.Add("Status", 90)
[void]$Script:BluetoothStatusList.Columns.Add("Details", 360)
$bluetoothStatusGroup.Controls.Add($Script:BluetoothStatusList)

$bluetoothInfoGroup = New-Object System.Windows.Forms.GroupBox
$bluetoothInfoGroup.Text = "Version and capacity notes"
$bluetoothInfoGroup.Dock = "Fill"
$bluetoothInfoGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothUpperSplit.Panel2.Controls.Add($bluetoothInfoGroup)

$Script:BluetoothInfoBox = New-Object System.Windows.Forms.TextBox
$Script:BluetoothInfoBox.Multiline = $true
$Script:BluetoothInfoBox.ReadOnly = $true
$Script:BluetoothInfoBox.ScrollBars = "Vertical"
$Script:BluetoothInfoBox.BorderStyle = "None"
$Script:BluetoothInfoBox.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$Script:BluetoothInfoBox.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$Script:BluetoothInfoBox.Dock = "Fill"
$bluetoothInfoGroup.Controls.Add($Script:BluetoothInfoBox)

$bluetoothLowerSplit = New-Object System.Windows.Forms.SplitContainer
$bluetoothLowerSplit.Dock = "Fill"
$bluetoothLowerSplit.Orientation = "Vertical"
$bluetoothLowerSplit.SplitterDistance = 430
$bluetoothSplit.Panel2.Controls.Add($bluetoothLowerSplit)

$adapterGroup = New-Object System.Windows.Forms.GroupBox
$adapterGroup.Text = "Bluetooth adapters"
$adapterGroup.Dock = "Fill"
$adapterGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothLowerSplit.Panel1.Controls.Add($adapterGroup)

$Script:BluetoothAdapterList = New-Object System.Windows.Forms.ListView
$Script:BluetoothAdapterList.View = "Details"
$Script:BluetoothAdapterList.FullRowSelect = $true
$Script:BluetoothAdapterList.GridLines = $true
$Script:BluetoothAdapterList.Dock = "Fill"
[void]$Script:BluetoothAdapterList.Columns.Add("Name", 250)
[void]$Script:BluetoothAdapterList.Columns.Add("Status", 80)
[void]$Script:BluetoothAdapterList.Columns.Add("Role", 80)
[void]$Script:BluetoothAdapterList.Columns.Add("Instance ID", 420)
$adapterGroup.Controls.Add($Script:BluetoothAdapterList)

$bluetoothDeviceRightSplit = New-Object System.Windows.Forms.SplitContainer
$bluetoothDeviceRightSplit.Dock = "Fill"
$bluetoothDeviceRightSplit.Orientation = "Horizontal"
$bluetoothDeviceRightSplit.SplitterDistance = 165
$bluetoothLowerSplit.Panel2.Controls.Add($bluetoothDeviceRightSplit)

$bluetoothDevicesGroup = New-Object System.Windows.Forms.GroupBox
$bluetoothDevicesGroup.Text = "Known / active Bluetooth devices"
$bluetoothDevicesGroup.Dock = "Fill"
$bluetoothDevicesGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothDeviceRightSplit.Panel1.Controls.Add($bluetoothDevicesGroup)

$Script:BluetoothDeviceList = New-Object System.Windows.Forms.ListView
$Script:BluetoothDeviceList.View = "Details"
$Script:BluetoothDeviceList.FullRowSelect = $true
$Script:BluetoothDeviceList.GridLines = $true
$Script:BluetoothDeviceList.Dock = "Fill"
[void]$Script:BluetoothDeviceList.Columns.Add("Name", 250)
[void]$Script:BluetoothDeviceList.Columns.Add("Status", 80)
[void]$Script:BluetoothDeviceList.Columns.Add("Role", 90)
[void]$Script:BluetoothDeviceList.Columns.Add("Instance ID", 420)
$bluetoothDevicesGroup.Controls.Add($Script:BluetoothDeviceList)

$linuxBluetoothGroup = New-Object System.Windows.Forms.GroupBox
$linuxBluetoothGroup.Text = "Linux / BlueZ devices"
$linuxBluetoothGroup.Dock = "Fill"
$linuxBluetoothGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothDeviceRightSplit.Panel2.Controls.Add($linuxBluetoothGroup)

$linuxBluetoothActions = New-Object System.Windows.Forms.Panel
$linuxBluetoothActions.Dock = "Top"
$linuxBluetoothActions.Height = 40
$linuxBluetoothActions.Padding = New-Object System.Windows.Forms.Padding(8, 8, 8, 4)
$linuxBluetoothGroup.Controls.Add($linuxBluetoothActions)

$scanLinuxButton = New-Object System.Windows.Forms.Button
$scanLinuxButton.Text = "Scan Linux"
$scanLinuxButton.Size = New-Object System.Drawing.Size(88, 26)
$scanLinuxButton.Location = New-Object System.Drawing.Point(8, 7)
$scanLinuxButton.Add_Click({ Refresh-LinuxBluetoothDevices -ScanSeconds 8 })
$linuxBluetoothActions.Controls.Add($scanLinuxButton)

$refreshLinuxButton = New-Object System.Windows.Forms.Button
$refreshLinuxButton.Text = "Refresh"
$refreshLinuxButton.Size = New-Object System.Drawing.Size(75, 26)
$refreshLinuxButton.Location = New-Object System.Drawing.Point(104, 7)
$refreshLinuxButton.Add_Click({ Refresh-LinuxBluetoothDevices })
$linuxBluetoothActions.Controls.Add($refreshLinuxButton)

$useLinuxControllerButton = New-Object System.Windows.Forms.Button
$pairLinuxButton = New-Object System.Windows.Forms.Button
$pairLinuxButton.Text = "Pair"
$pairLinuxButton.Size = New-Object System.Drawing.Size(70, 26)
$pairLinuxButton.Location = New-Object System.Drawing.Point(186, 7)
$pairLinuxButton.Add_Click({ Pair-SelectedLinuxBluetoothDevices })
$linuxBluetoothActions.Controls.Add($pairLinuxButton)

$connectLinuxButton = New-Object System.Windows.Forms.Button
$connectLinuxButton.Text = "Connect"
$connectLinuxButton.Size = New-Object System.Drawing.Size(80, 26)
$connectLinuxButton.Location = New-Object System.Drawing.Point(264, 7)
$connectLinuxButton.Add_Click({ Connect-SelectedLinuxBluetoothDevices })
$linuxBluetoothActions.Controls.Add($connectLinuxButton)

$useLinuxControllerButton = New-Object System.Windows.Forms.Button
$useLinuxControllerButton.Text = "Use selected"
$useLinuxControllerButton.Size = New-Object System.Drawing.Size(100, 26)
$useLinuxControllerButton.Location = New-Object System.Drawing.Point(352, 7)
$useLinuxControllerButton.Add_Click({ Use-SelectedLinuxStadiaControllers })
$linuxBluetoothActions.Controls.Add($useLinuxControllerButton)

$autoLinuxControllerButton = New-Object System.Windows.Forms.Button
$autoLinuxControllerButton.Text = "Automatic"
$autoLinuxControllerButton.Size = New-Object System.Drawing.Size(82, 26)
$autoLinuxControllerButton.Location = New-Object System.Drawing.Point(460, 7)
$autoLinuxControllerButton.Add_Click({
    Set-SelectedLinuxControllerMacs @()
    Add-Log "Manual Linux controller selection cleared; automatic selection restored."
})
$linuxBluetoothActions.Controls.Add($autoLinuxControllerButton)

$Script:LinuxControllerSelectionLabel = New-Object System.Windows.Forms.Label
$Script:LinuxControllerSelectionLabel.Text = "Manual Stadia selection: automatic"
$Script:LinuxControllerSelectionLabel.AutoSize = $false
$Script:LinuxControllerSelectionLabel.Size = New-Object System.Drawing.Size(270, 22)
$Script:LinuxControllerSelectionLabel.Location = New-Object System.Drawing.Point(550, 10)
$linuxBluetoothActions.Controls.Add($Script:LinuxControllerSelectionLabel)

$Script:LinuxBluetoothDeviceList = New-Object System.Windows.Forms.ListView
$Script:LinuxBluetoothDeviceList.View = "Details"
$Script:LinuxBluetoothDeviceList.FullRowSelect = $true
$Script:LinuxBluetoothDeviceList.GridLines = $true
$Script:LinuxBluetoothDeviceList.MultiSelect = $true
$Script:LinuxBluetoothDeviceList.Dock = "Fill"
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("MAC", 130)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Name", 170)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Connected", 80)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Paired", 70)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Trusted", 70)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Battery", 70)
[void]$Script:LinuxBluetoothDeviceList.Columns.Add("Stadia?", 70)
$linuxBluetoothGroup.Controls.Add($Script:LinuxBluetoothDeviceList)
$linuxBluetoothActions.BringToFront()

$leftPanel = New-Object System.Windows.Forms.Panel
$leftPanel.Dock = "Left"
$leftPanel.Width = 310
$leftPanel.Padding = New-Object System.Windows.Forms.Padding(14)
$statusPage.Controls.Add($leftPanel)

$actions = New-Object System.Windows.Forms.GroupBox
$actions.Text = "Actions"
$actions.Dock = "Top"
$actions.Height = 290
$actions.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$leftPanel.Controls.Add($actions)

$startButton = New-Object System.Windows.Forms.Button
$startButton.Text = "Start bridge"
$startButton.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$startButton.Size = New-Object System.Drawing.Size(260, 38)
$startButton.Location = New-Object System.Drawing.Point(18, 28)
$startButton.BackColor = [System.Drawing.Color]::FromArgb(45, 125, 90)
$startButton.ForeColor = [System.Drawing.Color]::White
$startButton.FlatStyle = "Flat"
$startButton.Add_Click({ Start-StadiaBridge })
$actions.Controls.Add($startButton)

$stopButton = New-Object System.Windows.Forms.Button
$stopButton.Text = "Stop and restore Bluetooth"
$stopButton.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$stopButton.Size = New-Object System.Drawing.Size(260, 38)
$stopButton.Location = New-Object System.Drawing.Point(18, 76)
$stopButton.BackColor = [System.Drawing.Color]::FromArgb(178, 62, 62)
$stopButton.ForeColor = [System.Drawing.Color]::White
$stopButton.FlatStyle = "Flat"
$stopButton.Add_Click({ Stop-StadiaBridge })
$actions.Controls.Add($stopButton)

$batteryButton = New-Object System.Windows.Forms.Button
$batteryButton.Text = "Check battery"
$batteryButton.Size = New-Object System.Drawing.Size(260, 32)
$batteryButton.Location = New-Object System.Drawing.Point(18, 124)
$batteryButton.Add_Click({
    $info = Update-ControllerBatteryStatus
    Add-Log $info
    [System.Windows.Forms.MessageBox]::Show($info, "Stadia X Battery", "OK", "Information") | Out-Null
})
$actions.Controls.Add($batteryButton)

$Script:BatteryStatusLabel = New-Object System.Windows.Forms.Label
$Script:BatteryStatusLabel.Text = "Battery: not checked yet."
$Script:BatteryStatusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$Script:BatteryStatusLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$Script:BatteryStatusLabel.AutoSize = $false
$Script:BatteryStatusLabel.Size = New-Object System.Drawing.Size(260, 42)
$Script:BatteryStatusLabel.Location = New-Object System.Drawing.Point(18, 162)
$actions.Controls.Add($Script:BatteryStatusLabel)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh status"
$refreshButton.Size = New-Object System.Drawing.Size(125, 32)
$refreshButton.Location = New-Object System.Drawing.Point(18, 218)
$refreshButton.Add_Click({
    Refresh-BluetoothList
    Refresh-WslDistroList
    Refresh-Status
})
$actions.Controls.Add($refreshButton)

$folderButton = New-Object System.Windows.Forms.Button
$folderButton.Text = "Open folder"
$folderButton.Size = New-Object System.Drawing.Size(125, 32)
$folderButton.Location = New-Object System.Drawing.Point(153, 218)
$folderButton.Add_Click({ Open-ProjectFolder })
$actions.Controls.Add($folderButton)

$btGroup = New-Object System.Windows.Forms.GroupBox
$btGroup.Text = "Bluetooth adapter"
$btGroup.Dock = "Top"
$btGroup.Height = 248
$btGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$btGroup.Top = 260
$leftPanel.Controls.Add($btGroup)
$btGroup.BringToFront()

$btHelp = New-Object System.Windows.Forms.Label
$btHelp.Text = "Choose the exact USB/IP device to hand over to Linux. Use Setup for the full device table."
$btHelp.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$btHelp.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$btHelp.AutoSize = $false
$btHelp.Size = New-Object System.Drawing.Size(260, 34)
$btHelp.Location = New-Object System.Drawing.Point(18, 26)
$btGroup.Controls.Add($btHelp)

$Script:BluetoothCombo = New-Object System.Windows.Forms.ComboBox
$Script:BluetoothCombo.DropDownStyle = "DropDownList"
$Script:BluetoothCombo.Size = New-Object System.Drawing.Size(260, 28)
$Script:BluetoothCombo.Location = New-Object System.Drawing.Point(18, 64)
$Script:BluetoothCombo.Add_SelectedIndexChanged({
    $selected = [string]$Script:BluetoothCombo.SelectedItem
    if ($selected -match "^(\d+-\d+)") {
        Set-SelectedBluetoothDevice $Matches[1]
    }
})
$btGroup.Controls.Add($Script:BluetoothCombo)

$selectedBusLabel = New-Object System.Windows.Forms.Label
$selectedBusLabel.Text = "Selected BUSID"
$selectedBusLabel.AutoSize = $true
$selectedBusLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$selectedBusLabel.Location = New-Object System.Drawing.Point(18, 100)
$btGroup.Controls.Add($selectedBusLabel)

$Script:SelectedBusIdText = New-Object System.Windows.Forms.TextBox
$Script:SelectedBusIdText.Size = New-Object System.Drawing.Size(116, 24)
$Script:SelectedBusIdText.Location = New-Object System.Drawing.Point(18, 121)
$Script:SelectedBusIdText.Add_Leave({
    if (-not [string]::IsNullOrWhiteSpace($Script:SelectedBusIdText.Text)) {
        Set-SelectedBluetoothDevice $Script:SelectedBusIdText.Text
    }
})
$btGroup.Controls.Add($Script:SelectedBusIdText)

$useSelectedButton = New-Object System.Windows.Forms.Button
$useSelectedButton.Text = "Use selected"
$useSelectedButton.Size = New-Object System.Drawing.Size(132, 26)
$useSelectedButton.Location = New-Object System.Drawing.Point(146, 119)
$useSelectedButton.Add_Click({
    $busId = Get-SelectedBusId
    if ($busId) {
        Set-SelectedBluetoothDevice $busId
        Add-Log "Bluetooth BUSID selected manually: $busId"
    }
})
$btGroup.Controls.Add($useSelectedButton)

$Script:DeviceDetailsBox = New-Object System.Windows.Forms.TextBox
$Script:DeviceDetailsBox.Multiline = $true
$Script:DeviceDetailsBox.ReadOnly = $true
$Script:DeviceDetailsBox.ScrollBars = "Vertical"
$Script:DeviceDetailsBox.Size = New-Object System.Drawing.Size(260, 48)
$Script:DeviceDetailsBox.Location = New-Object System.Drawing.Point(18, 153)
$Script:DeviceDetailsBox.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$Script:DeviceDetailsBox.Text = "Refresh devices to select a Bluetooth adapter."
$btGroup.Controls.Add($Script:DeviceDetailsBox)

$refreshBtButton = New-Object System.Windows.Forms.Button
$refreshBtButton.Text = "Refresh adapters"
$refreshBtButton.Size = New-Object System.Drawing.Size(260, 28)
$refreshBtButton.Location = New-Object System.Drawing.Point(18, 209)
$refreshBtButton.Add_Click({ Refresh-BluetoothList })
$btGroup.Controls.Add($refreshBtButton)

$notes = New-Object System.Windows.Forms.GroupBox
$notes.Text = "Notes"
$notes.Dock = "Fill"
$notes.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$leftPanel.Controls.Add($notes)

$notesText = New-Object System.Windows.Forms.TextBox
$notesText.Multiline = $true
$notesText.ReadOnly = $true
$notesText.BorderStyle = "None"
$notesText.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$notesText.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$notesText.Text = "Start/Stop may need Administrator rights because usbipd bind, attach, detach, and unbind touch USB devices.`r`n`r`nThe source tree does not store generated runtime binaries. Use a release ZIP or build stadia_receiver.exe, ViGEmClient.dll, and stadia_bridge before expecting Start to succeed."
$notesText.Dock = "Fill"
$notesText.Margin = New-Object System.Windows.Forms.Padding(10)
$notes.Controls.Add($notesText)

$rightPanel = New-Object System.Windows.Forms.Panel
$rightPanel.Dock = "Fill"
$rightPanel.Padding = New-Object System.Windows.Forms.Padding(14)
$statusPage.Controls.Add($rightPanel)

$checksGroup = New-Object System.Windows.Forms.GroupBox
$checksGroup.Text = "Preflight checks"
$checksGroup.Dock = "Fill"
$checksGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$rightPanel.Controls.Add($checksGroup)

$Script:ChecksList = New-Object System.Windows.Forms.ListView
$Script:ChecksList.View = "Details"
$Script:ChecksList.FullRowSelect = $true
$Script:ChecksList.GridLines = $true
$Script:ChecksList.Dock = "Fill"
[void]$Script:ChecksList.Columns.Add("Item", 190)
[void]$Script:ChecksList.Columns.Add("Status", 90)
[void]$Script:ChecksList.Columns.Add("Details", 360)
$checksGroup.Controls.Add($Script:ChecksList)

$liveTop = New-Object System.Windows.Forms.Panel
$liveTop.Dock = "Top"
$liveTop.Height = 58
$liveTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$livePage.Controls.Add($liveTop)

$Script:LiveSummaryLabel = New-Object System.Windows.Forms.Label
$Script:LiveSummaryLabel.Text = "Waiting for status events."
$Script:LiveSummaryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:LiveSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:LiveSummaryLabel.AutoSize = $false
$Script:LiveSummaryLabel.Size = New-Object System.Drawing.Size(650, 30)
$Script:LiveSummaryLabel.Location = New-Object System.Drawing.Point(14, 16)
$liveTop.Controls.Add($Script:LiveSummaryLabel)

$refreshLiveButton = New-Object System.Windows.Forms.Button
$refreshLiveButton.Text = "Refresh now"
$refreshLiveButton.Size = New-Object System.Drawing.Size(110, 30)
$refreshLiveButton.Anchor = "Top,Right"
$refreshLiveButton.Location = New-Object System.Drawing.Point(800, 14)
$refreshLiveButton.Add_Click({ Refresh-LiveStatus })
$liveTop.Controls.Add($refreshLiveButton)

$liveSplit = New-Object System.Windows.Forms.SplitContainer
$liveSplit.Dock = "Fill"
$liveSplit.Orientation = "Horizontal"
$liveSplit.SplitterDistance = 260
$liveSplit.Panel1.Padding = New-Object System.Windows.Forms.Padding(14, 8, 14, 6)
$liveSplit.Panel2.Padding = New-Object System.Windows.Forms.Padding(14, 6, 14, 14)
$livePage.Controls.Add($liveSplit)
$liveTop.BringToFront()

$liveEventLayout = New-Object System.Windows.Forms.TableLayoutPanel
$liveEventLayout.Dock = "Fill"
$liveEventLayout.ColumnCount = 2
$liveEventLayout.RowCount = 1
$liveEventLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 48))) | Out-Null
$liveEventLayout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 52))) | Out-Null
$liveSplit.Panel1.Controls.Add($liveEventLayout)

$humanTimelineGroup = New-Object System.Windows.Forms.GroupBox
$humanTimelineGroup.Text = "Human timeline"
$humanTimelineGroup.Dock = "Fill"
$humanTimelineGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$liveEventLayout.Controls.Add($humanTimelineGroup, 0, 0)

$Script:HumanTimelineList = New-Object System.Windows.Forms.ListView
$Script:HumanTimelineList.View = "Details"
$Script:HumanTimelineList.FullRowSelect = $true
$Script:HumanTimelineList.GridLines = $true
$Script:HumanTimelineList.Dock = "Fill"
[void]$Script:HumanTimelineList.Columns.Add("Time", 130)
[void]$Script:HumanTimelineList.Columns.Add("Side", 70)
[void]$Script:HumanTimelineList.Columns.Add("What is happening", 360)
$humanTimelineGroup.Controls.Add($Script:HumanTimelineList)

$liveEventsGroup = New-Object System.Windows.Forms.GroupBox
$liveEventsGroup.Text = "Structured status events"
$liveEventsGroup.Dock = "Fill"
$liveEventsGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$liveEventLayout.Controls.Add($liveEventsGroup, 1, 0)

$Script:LiveStatusList = New-Object System.Windows.Forms.ListView
$Script:LiveStatusList.View = "Details"
$Script:LiveStatusList.FullRowSelect = $true
$Script:LiveStatusList.GridLines = $true
$Script:LiveStatusList.Dock = "Fill"
[void]$Script:LiveStatusList.Columns.Add("Source", 90)
[void]$Script:LiveStatusList.Columns.Add("Event", 180)
[void]$Script:LiveStatusList.Columns.Add("Message", 620)
$liveEventsGroup.Controls.Add($Script:LiveStatusList)

$linuxLogGroup = New-Object System.Windows.Forms.GroupBox
$linuxLogGroup.Text = "Linux core log"
$linuxLogGroup.Dock = "Fill"
$linuxLogGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$liveSplit.Panel2.Controls.Add($linuxLogGroup)

$Script:LinuxLogBox = New-Object System.Windows.Forms.TextBox
$Script:LinuxLogBox.Multiline = $true
$Script:LinuxLogBox.ReadOnly = $true
$Script:LinuxLogBox.ScrollBars = "Vertical"
$Script:LinuxLogBox.Font = New-Object System.Drawing.Font("Consolas", 9)
$Script:LinuxLogBox.BackColor = [System.Drawing.Color]::FromArgb(20, 24, 32)
$Script:LinuxLogBox.ForeColor = [System.Drawing.Color]::FromArgb(220, 230, 240)
$Script:LinuxLogBox.Dock = "Fill"
$linuxLogGroup.Controls.Add($Script:LinuxLogBox)

$controllerTop = New-Object System.Windows.Forms.Panel
$controllerTop.Dock = "Top"
$controllerTop.Height = 112
$controllerTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$controllerPage.Controls.Add($controllerTop)

$Script:TelemetryLabel = New-Object System.Windows.Forms.Label
$Script:TelemetryLabel.Text = "No controller telemetry yet."
$Script:TelemetryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:TelemetryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:TelemetryLabel.AutoSize = $false
$Script:TelemetryLabel.Size = New-Object System.Drawing.Size(520, 30)
$Script:TelemetryLabel.Location = New-Object System.Drawing.Point(14, 16)
$controllerTop.Controls.Add($Script:TelemetryLabel)

$controllerSelectLabel = New-Object System.Windows.Forms.Label
$controllerSelectLabel.Text = "Pad"
$controllerSelectLabel.AutoSize = $true
$controllerSelectLabel.Location = New-Object System.Drawing.Point(548, 20)
$controllerTop.Controls.Add($controllerSelectLabel)

$Script:ControllerSelector = New-Object System.Windows.Forms.ComboBox
$Script:ControllerSelector.DropDownStyle = "DropDownList"
$Script:ControllerSelector.Size = New-Object System.Drawing.Size(132, 28)
$Script:ControllerSelector.Location = New-Object System.Drawing.Point(584, 16)
$Script:ControllerSelector.Add_SelectedIndexChanged({ Reset-ControllerIndicators })
$controllerTop.Controls.Add($Script:ControllerSelector)

$rumbleButton = New-Object System.Windows.Forms.Button
$rumbleButton.Text = "Rumble test"
$rumbleButton.Size = New-Object System.Drawing.Size(100, 30)
$rumbleButton.Location = New-Object System.Drawing.Point(724, 14)
$rumbleButton.Add_Click({ Send-TestRumble })
$controllerTop.Controls.Add($rumbleButton)

$refreshControllerButton = New-Object System.Windows.Forms.Button
$refreshControllerButton.Text = "Refresh"
$refreshControllerButton.Size = New-Object System.Drawing.Size(90, 30)
$refreshControllerButton.Anchor = "Top,Right"
$refreshControllerButton.Location = New-Object System.Drawing.Point(832, 14)
$refreshControllerButton.Add_Click({ Refresh-ControllerTelemetry })
$controllerTop.Controls.Add($refreshControllerButton)

$Script:ControllerStatsLabel = New-Object System.Windows.Forms.Label
$Script:ControllerStatsLabel.Text = "Packets: -    Rate: -    Last seen: -"
$Script:ControllerStatsLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$Script:ControllerStatsLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$Script:ControllerStatsLabel.AutoSize = $false
$Script:ControllerStatsLabel.Size = New-Object System.Drawing.Size(760, 22)
$Script:ControllerStatsLabel.Location = New-Object System.Drawing.Point(14, 52)
$controllerTop.Controls.Add($Script:ControllerStatsLabel)

$Script:HealthSummaryLabel = New-Object System.Windows.Forms.Label
$Script:HealthSummaryLabel.Text = "Health: waiting for controller input"
$Script:HealthSummaryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 8, [System.Drawing.FontStyle]::Bold)
$Script:HealthSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$Script:HealthSummaryLabel.AutoSize = $false
$Script:HealthSummaryLabel.Size = New-Object System.Drawing.Size(760, 22)
$Script:HealthSummaryLabel.Location = New-Object System.Drawing.Point(14, 76)
$controllerTop.Controls.Add($Script:HealthSummaryLabel)

$controllerBody = New-Object System.Windows.Forms.TableLayoutPanel
$controllerBody.Dock = "Fill"
$controllerBody.ColumnCount = 2
$controllerBody.RowCount = 1
$controllerBody.Padding = New-Object System.Windows.Forms.Padding(14)
$controllerBody.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 58))) | Out-Null
$controllerBody.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 42))) | Out-Null
$controllerPage.Controls.Add($controllerBody)
$controllerTop.BringToFront()

$buttonGroup = New-Object System.Windows.Forms.GroupBox
$buttonGroup.Text = "Buttons"
$buttonGroup.Dock = "Fill"
$buttonGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$controllerBody.Controls.Add($buttonGroup, 0, 0)

$buttonFlow = New-Object System.Windows.Forms.FlowLayoutPanel
$buttonFlow.Dock = "Fill"
$buttonFlow.Padding = New-Object System.Windows.Forms.Padding(12)
$buttonFlow.AutoScroll = $true
$buttonGroup.Controls.Add($buttonFlow)

@(
    @("a", "A"), @("b", "B"), @("x", "X"), @("y", "Y"),
    @("lb", "LB"), @("rb", "RB"), @("select", "Select"), @("start", "Start"),
    @("stadia", "Stadia"), @("assistant", "Assistant"), @("l3", "L3"), @("r3", "R3"),
    @("dpad_up", "D-Up"), @("dpad_down", "D-Down"), @("dpad_left", "D-Left"), @("dpad_right", "D-Right")
) | ForEach-Object {
    [void]$buttonFlow.Controls.Add((New-ButtonIndicator -Key $_[0] -Text $_[1]))
}

$axesGroup = New-Object System.Windows.Forms.GroupBox
$axesGroup.Text = "Triggers and sticks"
$axesGroup.Dock = "Fill"
$axesGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$controllerBody.Controls.Add($axesGroup, 1, 0)

$axesPanel = New-Object System.Windows.Forms.Panel
$axesPanel.Dock = "Fill"
$axesPanel.Padding = New-Object System.Windows.Forms.Padding(18)
$axesGroup.Controls.Add($axesPanel)

$ltLabel = New-Object System.Windows.Forms.Label
$ltLabel.Text = "Left trigger"
$ltLabel.AutoSize = $true
$ltLabel.Location = New-Object System.Drawing.Point(18, 26)
$axesPanel.Controls.Add($ltLabel)

$Script:LeftTriggerBar = New-Object System.Windows.Forms.ProgressBar
$Script:LeftTriggerBar.Minimum = 0
$Script:LeftTriggerBar.Maximum = 255
$Script:LeftTriggerBar.Size = New-Object System.Drawing.Size(320, 24)
$Script:LeftTriggerBar.Location = New-Object System.Drawing.Point(18, 50)
$axesPanel.Controls.Add($Script:LeftTriggerBar)

$rtLabel = New-Object System.Windows.Forms.Label
$rtLabel.Text = "Right trigger"
$rtLabel.AutoSize = $true
$rtLabel.Location = New-Object System.Drawing.Point(18, 92)
$axesPanel.Controls.Add($rtLabel)

$Script:RightTriggerBar = New-Object System.Windows.Forms.ProgressBar
$Script:RightTriggerBar.Minimum = 0
$Script:RightTriggerBar.Maximum = 255
$Script:RightTriggerBar.Size = New-Object System.Drawing.Size(320, 24)
$Script:RightTriggerBar.Location = New-Object System.Drawing.Point(18, 116)
$axesPanel.Controls.Add($Script:RightTriggerBar)

$Script:AxesLabel = New-Object System.Windows.Forms.Label
$Script:AxesLabel.Text = "Sticks: waiting for telemetry"
$Script:AxesLabel.Font = New-Object System.Drawing.Font("Consolas", 10)
$Script:AxesLabel.AutoSize = $false
$Script:AxesLabel.Size = New-Object System.Drawing.Size(350, 70)
$Script:AxesLabel.Location = New-Object System.Drawing.Point(18, 158)
$axesPanel.Controls.Add($Script:AxesLabel)

$Script:ControllerDeadzoneLabel = New-Object System.Windows.Forms.Label
$Script:ControllerDeadzoneLabel.Text = "Deadzone: waiting for stick data"
$Script:ControllerDeadzoneLabel.AutoSize = $false
$Script:ControllerDeadzoneLabel.Size = New-Object System.Drawing.Size(350, 22)
$Script:ControllerDeadzoneLabel.Location = New-Object System.Drawing.Point(18, 232)
$axesPanel.Controls.Add($Script:ControllerDeadzoneLabel)

$Script:ControllerRawBox = New-Object System.Windows.Forms.TextBox
$Script:ControllerRawBox.Multiline = $true
$Script:ControllerRawBox.ReadOnly = $true
$Script:ControllerRawBox.ScrollBars = "Vertical"
$Script:ControllerRawBox.Font = New-Object System.Drawing.Font("Consolas", 9)
$Script:ControllerRawBox.Size = New-Object System.Drawing.Size(350, 100)
$Script:ControllerRawBox.Location = New-Object System.Drawing.Point(18, 260)
$axesPanel.Controls.Add($Script:ControllerRawBox)

$telemetryNote = New-Object System.Windows.Forms.TextBox
$telemetryNote.Multiline = $true
$telemetryNote.ReadOnly = $true
$telemetryNote.BorderStyle = "None"
$telemetryNote.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$telemetryNote.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$telemetryNote.Text = "This screen reads logs\controller-state.json. With the updated receiver it can show up to four Stadia controllers, packet rate, deadzone, and rumble routing."
$telemetryNote.Size = New-Object System.Drawing.Size(350, 70)
$telemetryNote.Location = New-Object System.Drawing.Point(18, 370)
$axesPanel.Controls.Add($telemetryNote)

$profilesTop = New-Object System.Windows.Forms.Panel
$profilesTop.Dock = "Top"
$profilesTop.Height = 58
$profilesTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$profilesPage.Controls.Add($profilesTop)

$profilesTitle = New-Object System.Windows.Forms.Label
$profilesTitle.Text = "Controller profiles"
$profilesTitle.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$profilesTitle.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$profilesTitle.AutoSize = $false
$profilesTitle.Size = New-Object System.Drawing.Size(360, 30)
$profilesTitle.Location = New-Object System.Drawing.Point(14, 16)
$profilesTop.Controls.Add($profilesTitle)

$applyProfilesButton = New-Object System.Windows.Forms.Button
$applyProfilesButton.Text = "Apply to startup"
$applyProfilesButton.Size = New-Object System.Drawing.Size(120, 30)
$applyProfilesButton.Location = New-Object System.Drawing.Point(610, 14)
$applyProfilesButton.Anchor = "Top,Right"
$applyProfilesButton.Add_Click({ Apply-ControllerProfilesToStartup })
$profilesTop.Controls.Add($applyProfilesButton)

$refreshProfilesButton = New-Object System.Windows.Forms.Button
$refreshProfilesButton.Text = "Refresh"
$refreshProfilesButton.Size = New-Object System.Drawing.Size(90, 30)
$refreshProfilesButton.Location = New-Object System.Drawing.Point(740, 14)
$refreshProfilesButton.Anchor = "Top,Right"
$refreshProfilesButton.Add_Click({ Refresh-ControllerProfiles })
$profilesTop.Controls.Add($refreshProfilesButton)

$profileFromLinuxButton = New-Object System.Windows.Forms.Button
$profileFromLinuxButton.Text = "Use Linux selected"
$profileFromLinuxButton.Size = New-Object System.Drawing.Size(130, 30)
$profileFromLinuxButton.Location = New-Object System.Drawing.Point(840, 14)
$profileFromLinuxButton.Anchor = "Top,Right"
$profileFromLinuxButton.Add_Click({ Use-SelectedLinuxDeviceAsProfile })
$profilesTop.Controls.Add($profileFromLinuxButton)

$profilesBody = New-Object System.Windows.Forms.TableLayoutPanel
$profilesBody.Dock = "Fill"
$profilesBody.ColumnCount = 2
$profilesBody.RowCount = 1
$profilesBody.Padding = New-Object System.Windows.Forms.Padding(14)
$profilesBody.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 58))) | Out-Null
$profilesBody.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent, 42))) | Out-Null
$profilesPage.Controls.Add($profilesBody)
$profilesTop.BringToFront()

$profileListGroup = New-Object System.Windows.Forms.GroupBox
$profileListGroup.Text = "Saved controllers"
$profileListGroup.Dock = "Fill"
$profileListGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$profilesBody.Controls.Add($profileListGroup, 0, 0)

$Script:ControllerProfileList = New-Object System.Windows.Forms.ListView
$Script:ControllerProfileList.View = "Details"
$Script:ControllerProfileList.FullRowSelect = $true
$Script:ControllerProfileList.GridLines = $true
$Script:ControllerProfileList.Dock = "Fill"
[void]$Script:ControllerProfileList.Columns.Add("Name", 190)
[void]$Script:ControllerProfileList.Columns.Add("MAC", 160)
[void]$Script:ControllerProfileList.Columns.Add("Slot", 80)
[void]$Script:ControllerProfileList.Columns.Add("Auto", 80)
$Script:ControllerProfileList.Add_SelectedIndexChanged({ Load-SelectedControllerProfileIntoForm })
$profileListGroup.Controls.Add($Script:ControllerProfileList)

$profileEditGroup = New-Object System.Windows.Forms.GroupBox
$profileEditGroup.Text = "Profile editor"
$profileEditGroup.Dock = "Fill"
$profileEditGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$profilesBody.Controls.Add($profileEditGroup, 1, 0)

$profileEditPanel = New-Object System.Windows.Forms.Panel
$profileEditPanel.Dock = "Fill"
$profileEditPanel.Padding = New-Object System.Windows.Forms.Padding(18)
$profileEditGroup.Controls.Add($profileEditPanel)

$profileNameLabel = New-Object System.Windows.Forms.Label
$profileNameLabel.Text = "Name"
$profileNameLabel.AutoSize = $true
$profileNameLabel.Location = New-Object System.Drawing.Point(18, 28)
$profileEditPanel.Controls.Add($profileNameLabel)

$Script:ProfileNameText = New-Object System.Windows.Forms.TextBox
$Script:ProfileNameText.Size = New-Object System.Drawing.Size(310, 24)
$Script:ProfileNameText.Location = New-Object System.Drawing.Point(18, 50)
$profileEditPanel.Controls.Add($Script:ProfileNameText)

$profileMacLabel = New-Object System.Windows.Forms.Label
$profileMacLabel.Text = "Bluetooth MAC"
$profileMacLabel.AutoSize = $true
$profileMacLabel.Location = New-Object System.Drawing.Point(18, 88)
$profileEditPanel.Controls.Add($profileMacLabel)

$Script:ProfileMacText = New-Object System.Windows.Forms.TextBox
$Script:ProfileMacText.Size = New-Object System.Drawing.Size(310, 24)
$Script:ProfileMacText.Location = New-Object System.Drawing.Point(18, 110)
$profileEditPanel.Controls.Add($Script:ProfileMacText)

$profileSlotLabel = New-Object System.Windows.Forms.Label
$profileSlotLabel.Text = "Preferred pad slot"
$profileSlotLabel.AutoSize = $true
$profileSlotLabel.Location = New-Object System.Drawing.Point(18, 148)
$profileEditPanel.Controls.Add($profileSlotLabel)

$Script:ProfileSlotCombo = New-Object System.Windows.Forms.ComboBox
$Script:ProfileSlotCombo.DropDownStyle = "DropDownList"
for ($slot = 1; $slot -le $Script:MaxControllers; $slot++) {
    [void]$Script:ProfileSlotCombo.Items.Add("Pad $slot")
}
$Script:ProfileSlotCombo.SelectedIndex = 0
$Script:ProfileSlotCombo.Size = New-Object System.Drawing.Size(130, 24)
$Script:ProfileSlotCombo.Location = New-Object System.Drawing.Point(18, 170)
$profileEditPanel.Controls.Add($Script:ProfileSlotCombo)

$Script:ProfileAutoCheck = New-Object System.Windows.Forms.CheckBox
$Script:ProfileAutoCheck.Text = "Use this profile at startup"
$Script:ProfileAutoCheck.AutoSize = $true
$Script:ProfileAutoCheck.Location = New-Object System.Drawing.Point(18, 212)
$profileEditPanel.Controls.Add($Script:ProfileAutoCheck)

$saveProfileButton = New-Object System.Windows.Forms.Button
$saveProfileButton.Text = "Save profile"
$saveProfileButton.Size = New-Object System.Drawing.Size(140, 32)
$saveProfileButton.Location = New-Object System.Drawing.Point(18, 252)
$saveProfileButton.Add_Click({ Save-ProfileFromForm })
$profileEditPanel.Controls.Add($saveProfileButton)

$applyProfileHint = New-Object System.Windows.Forms.TextBox
$applyProfileHint.Multiline = $true
$applyProfileHint.ReadOnly = $true
$applyProfileHint.BorderStyle = "None"
$applyProfileHint.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$applyProfileHint.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$applyProfileHint.Size = New-Object System.Drawing.Size(330, 110)
$applyProfileHint.Location = New-Object System.Drawing.Point(18, 306)
$applyProfileHint.Text = "Profiles save the controller MAC and desired startup order. Apply to startup writes selected MACs into selected_controller_macs.txt. Clear that file from Bluetooth > Automatic to return to fully automatic detection."
$profileEditPanel.Controls.Add($applyProfileHint)

$macroTop = New-Object System.Windows.Forms.Panel
$macroTop.Dock = "Top"
$macroTop.Height = 48
$macroTop.Padding = New-Object System.Windows.Forms.Padding(12, 10, 12, 6)
$macroPage.Controls.Add($macroTop)

$reloadMacroButton = New-Object System.Windows.Forms.Button
$reloadMacroButton.Text = "Reload"
$reloadMacroButton.Size = New-Object System.Drawing.Size(90, 28)
$reloadMacroButton.Location = New-Object System.Drawing.Point(12, 10)
$reloadMacroButton.Add_Click({ Load-MacroConfig })
$macroTop.Controls.Add($reloadMacroButton)

$saveMacroButton = New-Object System.Windows.Forms.Button
$saveMacroButton.Text = "Save"
$saveMacroButton.Size = New-Object System.Drawing.Size(90, 28)
$saveMacroButton.Location = New-Object System.Drawing.Point(112, 10)
$saveMacroButton.Add_Click({ Save-MacroConfig })
$macroTop.Controls.Add($saveMacroButton)

$openNotepadButton = New-Object System.Windows.Forms.Button
$openNotepadButton.Text = "Open in Notepad"
$openNotepadButton.Size = New-Object System.Drawing.Size(130, 28)
$openNotepadButton.Location = New-Object System.Drawing.Point(212, 10)
$openNotepadButton.Add_Click({
    if (Test-Path $Script:ConfigPath) {
        Start-Process notepad.exe -ArgumentList ('"' + $Script:ConfigPath + '"')
    }
})
$macroTop.Controls.Add($openNotepadButton)

$refreshVisualMacroButton = New-Object System.Windows.Forms.Button
$refreshVisualMacroButton.Text = "Refresh visual"
$refreshVisualMacroButton.Size = New-Object System.Drawing.Size(115, 28)
$refreshVisualMacroButton.Location = New-Object System.Drawing.Point(352, 10)
$refreshVisualMacroButton.Add_Click({ Refresh-MacroMappingList })
$macroTop.Controls.Add($refreshVisualMacroButton)

$macroSplit = New-Object System.Windows.Forms.SplitContainer
$macroSplit.Dock = "Fill"
$macroSplit.Orientation = "Horizontal"
$macroSplit.SplitterDistance = 285
$macroSplit.Panel1.Padding = New-Object System.Windows.Forms.Padding(14, 8, 14, 6)
$macroSplit.Panel2.Padding = New-Object System.Windows.Forms.Padding(14, 6, 14, 14)
$macroPage.Controls.Add($macroSplit)
$macroTop.BringToFront()

$macroVisualGroup = New-Object System.Windows.Forms.GroupBox
$macroVisualGroup.Text = "Visual macro mapping"
$macroVisualGroup.Dock = "Fill"
$macroVisualGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$macroSplit.Panel1.Controls.Add($macroVisualGroup)

$macroVisualTop = New-Object System.Windows.Forms.Panel
$macroVisualTop.Dock = "Top"
$macroVisualTop.Height = 46
$macroVisualTop.Padding = New-Object System.Windows.Forms.Padding(10, 10, 10, 6)
$macroVisualGroup.Controls.Add($macroVisualTop)

$Script:MacroChordCombo = New-Object System.Windows.Forms.ComboBox
$Script:MacroChordCombo.DropDownStyle = "DropDownList"
$Script:MacroChordCombo.Size = New-Object System.Drawing.Size(230, 26)
$Script:MacroChordCombo.Location = New-Object System.Drawing.Point(10, 10)
foreach ($def in Get-StadiaChordDefinitions) {
    [void]$Script:MacroChordCombo.Items.Add("$($def.Code) - $($def.Label)")
}
if ($Script:MacroChordCombo.Items.Count -gt 0) { $Script:MacroChordCombo.SelectedIndex = 0 }
$macroVisualTop.Controls.Add($Script:MacroChordCombo)

$Script:MacroShortcutText = New-Object System.Windows.Forms.TextBox
$Script:MacroShortcutText.Size = New-Object System.Drawing.Size(220, 24)
$Script:MacroShortcutText.Location = New-Object System.Drawing.Point(250, 10)
$macroVisualTop.Controls.Add($Script:MacroShortcutText)

$applyMacroButton = New-Object System.Windows.Forms.Button
$applyMacroButton.Text = "Apply to editor"
$applyMacroButton.Size = New-Object System.Drawing.Size(110, 26)
$applyMacroButton.Location = New-Object System.Drawing.Point(480, 9)
$applyMacroButton.Add_Click({ Set-MacroMappingInEditor })
$macroVisualTop.Controls.Add($applyMacroButton)

$macroVisualHint = New-Object System.Windows.Forms.Label
$macroVisualHint.Text = "Example shortcut: CTRL+ALT+DELETE, VOLUME_UP, MEDIA_PLAY_PAUSE, LWIN+SHIFT+S"
$macroVisualHint.AutoSize = $true
$macroVisualHint.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$macroVisualHint.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$macroVisualHint.Location = New-Object System.Drawing.Point(604, 13)
$macroVisualTop.Controls.Add($macroVisualHint)

$Script:MacroMappingList = New-Object System.Windows.Forms.ListView
$Script:MacroMappingList.View = "Details"
$Script:MacroMappingList.FullRowSelect = $true
$Script:MacroMappingList.GridLines = $true
$Script:MacroMappingList.Dock = "Fill"
[void]$Script:MacroMappingList.Columns.Add("Chord", 220)
[void]$Script:MacroMappingList.Columns.Add("Code", 90)
[void]$Script:MacroMappingList.Columns.Add("Shortcut", 360)
$Script:MacroMappingList.Add_SelectedIndexChanged({ Load-SelectedMacroMappingIntoForm })
$macroVisualGroup.Controls.Add($Script:MacroMappingList)
$macroVisualTop.BringToFront()

$macroEditorGroup = New-Object System.Windows.Forms.GroupBox
$macroEditorGroup.Text = "stadia_buttons.ini editor"
$macroEditorGroup.Dock = "Fill"
$macroEditorGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$macroSplit.Panel2.Controls.Add($macroEditorGroup)

$Script:MacroBox = New-Object System.Windows.Forms.TextBox
$Script:MacroBox.Multiline = $true
$Script:MacroBox.AcceptsTab = $true
$Script:MacroBox.AcceptsReturn = $true
$Script:MacroBox.ScrollBars = "Both"
$Script:MacroBox.WordWrap = $false
$Script:MacroBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$Script:MacroBox.Dock = "Fill"
$macroEditorGroup.Controls.Add($Script:MacroBox)

$logTop = New-Object System.Windows.Forms.Panel
$logTop.Dock = "Top"
$logTop.Height = 48
$logTop.Padding = New-Object System.Windows.Forms.Padding(12, 10, 12, 6)
$logPage.Controls.Add($logTop)

$supportBundleButton = New-Object System.Windows.Forms.Button
$supportBundleButton.Text = "Create support bundle"
$supportBundleButton.Size = New-Object System.Drawing.Size(165, 28)
$supportBundleButton.Location = New-Object System.Drawing.Point(12, 10)
$supportBundleButton.Add_Click({ Export-StadiaSupportBundle })
$logTop.Controls.Add($supportBundleButton)

$openSupportFolderButton = New-Object System.Windows.Forms.Button
$openSupportFolderButton.Text = "Open bundles"
$openSupportFolderButton.Size = New-Object System.Drawing.Size(115, 28)
$openSupportFolderButton.Location = New-Object System.Drawing.Point(188, 10)
$openSupportFolderButton.Add_Click({
    if (-not (Test-Path $Script:SupportBundleDir)) {
        New-Item -ItemType Directory -Force -Path $Script:SupportBundleDir | Out-Null
    }
    Start-Process explorer.exe -ArgumentList ('"' + $Script:SupportBundleDir + '"')
})
$logTop.Controls.Add($openSupportFolderButton)

$sessionReportButton = New-Object System.Windows.Forms.Button
$sessionReportButton.Text = "Session report"
$sessionReportButton.Size = New-Object System.Drawing.Size(125, 28)
$sessionReportButton.Location = New-Object System.Drawing.Point(314, 10)
$sessionReportButton.Add_Click({ Export-StadiaSessionReport | Out-Null })
$logTop.Controls.Add($sessionReportButton)

$selfTestButton = New-Object System.Windows.Forms.Button
$selfTestButton.Text = "Run self-test"
$selfTestButton.Size = New-Object System.Drawing.Size(115, 28)
$selfTestButton.Location = New-Object System.Drawing.Point(450, 10)
$selfTestButton.Add_Click({ Invoke-StadiaSelfTest })
$logTop.Controls.Add($selfTestButton)

$Script:LogBox = New-Object System.Windows.Forms.TextBox
$Script:LogBox.Multiline = $true
$Script:LogBox.ReadOnly = $true
$Script:LogBox.ScrollBars = "Vertical"
$Script:LogBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$Script:LogBox.BackColor = [System.Drawing.Color]::FromArgb(20, 24, 32)
$Script:LogBox.ForeColor = [System.Drawing.Color]::FromArgb(220, 230, 240)
$Script:LogBox.Dock = "Fill"
$logPage.Controls.Add($Script:LogBox)
$logTop.BringToFront()

$trayMenu = New-Object System.Windows.Forms.ContextMenuStrip
$trayOpen = New-Object System.Windows.Forms.ToolStripMenuItem("Open Stadia X")
$trayOpen.Add_Click({
    $form.Show()
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Activate()
})
[void]$trayMenu.Items.Add($trayOpen)

$trayStart = New-Object System.Windows.Forms.ToolStripMenuItem("Start bridge")
$trayStart.Add_Click({ Start-StadiaBridge })
[void]$trayMenu.Items.Add($trayStart)

$trayStop = New-Object System.Windows.Forms.ToolStripMenuItem("Stop and restore Bluetooth")
$trayStop.Add_Click({ Stop-StadiaBridge })
[void]$trayMenu.Items.Add($trayStop)

[void]$trayMenu.Items.Add((New-Object System.Windows.Forms.ToolStripSeparator))

$trayFolder = New-Object System.Windows.Forms.ToolStripMenuItem("Open folder")
$trayFolder.Add_Click({ Open-ProjectFolder })
[void]$trayMenu.Items.Add($trayFolder)

$trayExit = New-Object System.Windows.Forms.ToolStripMenuItem("Exit")
$trayExit.Add_Click({
    if ($Script:NotifyIcon) { $Script:NotifyIcon.Visible = $false }
    $form.Close()
})
[void]$trayMenu.Items.Add($trayExit)

$Script:NotifyIcon = New-Object System.Windows.Forms.NotifyIcon
$Script:NotifyIcon.Text = "Stadia X"
$Script:NotifyIcon.Icon = [System.Drawing.SystemIcons]::Application
$Script:NotifyIcon.ContextMenuStrip = $trayMenu
$Script:NotifyIcon.Visible = $true
$Script:NotifyIcon.Add_DoubleClick({
    $form.Show()
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $form.Activate()
})

$form.Add_Resize({
    if ($form.WindowState -eq [System.Windows.Forms.FormWindowState]::Minimized) {
        $form.Hide()
        if ($Script:NotifyIcon) {
            $Script:NotifyIcon.BalloonTipTitle = "Stadia X"
            $Script:NotifyIcon.BalloonTipText = "Still running in the tray."
            $Script:NotifyIcon.ShowBalloonTip(1200)
        }
    }
})

$form.Add_FormClosed({
    if ($batteryTimer) {
        $batteryTimer.Stop()
        $batteryTimer.Dispose()
    }
    if ($Script:BatteryOverlayForm -and -not $Script:BatteryOverlayForm.IsDisposed) {
        $Script:BatteryOverlayForm.Close()
        $Script:BatteryOverlayForm.Dispose()
    }
    if ($Script:NotifyIcon) {
        $Script:NotifyIcon.Visible = $false
        $Script:NotifyIcon.Dispose()
    }
})

$refreshTimer = New-Object System.Windows.Forms.Timer
$refreshTimer.Interval = 2000
$refreshTimer.Add_Tick({
    Refresh-LiveStatus
    Refresh-ControllerTelemetry
    if ($tabs.SelectedTab -eq $bluetoothPage) {
        Refresh-BluetoothPanel
    }
    if ($tabs.SelectedTab -eq $firstRunPage) {
        Refresh-FirstRunWizard
    }
})

$batteryTimer = New-Object System.Windows.Forms.Timer
$batteryTimer.Interval = 300000
$batteryTimer.Add_Tick({
    Update-ControllerBatteryStatus | Out-Null
})

$form.Add_Shown({
    Ensure-LogDir
    Add-Log "Stadia X GUI loaded from $Script:Root"
    Refresh-BluetoothList
    Refresh-Status
    Refresh-FirstRunWizard
    Run-SetupAudit
    Refresh-BluetoothPanel
    Refresh-LiveStatus
    Refresh-ControllerTelemetry
    Refresh-ControllerProfiles
    Update-LinuxControllerSelectionLabel
    Update-ControllerBatteryStatus | Out-Null
    Load-MacroConfig
    $refreshTimer.Start()
    $batteryTimer.Start()
})

if ($env:STADIA_X_GUI_TEST -eq "1") {
    Write-Output "Stadia X GUI initialized"
    return
}

[void][System.Windows.Forms.Application]::Run($form)
