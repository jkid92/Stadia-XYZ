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
$Script:ControllerStatePath = Join-Path $Script:LogDir "controller-state.json"
$Script:ButtonIndicators = @{}
$Script:SetupList = $null
$Script:LiveStatusList = $null
$Script:LiveSummaryLabel = $null
$Script:LinuxLogBox = $null
$Script:TelemetryLabel = $null
$Script:LeftTriggerBar = $null
$Script:RightTriggerBar = $null
$Script:AxesLabel = $null
$Script:DeviceList = $null
$Script:SelectedBusIdText = $null
$Script:DeviceDetailsBox = $null
$Script:BluetoothStatusList = $null
$Script:BluetoothAdapterList = $null
$Script:BluetoothDeviceList = $null
$Script:BluetoothInfoBox = $null
$Script:BluetoothSummaryLabel = $null

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-CommandAvailable {
    param([string]$Name)
    return [bool](Get-Command $Name -ErrorAction SilentlyContinue)
}

function Ensure-LogDir {
    if (-not (Test-Path $Script:LogDir)) {
        New-Item -ItemType Directory -Path $Script:LogDir -Force | Out-Null
    }
}

function Add-Log {
    param([string]$Message)
    $stamp = Get-Date -Format "HH:mm:ss"
    $Script:LogBox.AppendText("[$stamp] $Message`r`n")
    $Script:LogBox.SelectionStart = $Script:LogBox.TextLength
    $Script:LogBox.ScrollToCaret()
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

function Test-UbuntuAvailable {
    if (-not (Test-CommandAvailable "wsl")) {
        return $false
    }
    $ubuntuCheck = Invoke-CapturedCommand -FileName "wsl" -Arguments @("-d", "Ubuntu", "echo", "ok") -TimeoutMs 8000
    return ($ubuntuCheck.ExitCode -eq 0)
}

function Test-UbuntuWsl2 {
    if (-not (Test-CommandAvailable "wsl")) {
        return $false
    }
    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments @("-l", "-v") -TimeoutMs 8000
    if ($result.ExitCode -ne 0) {
        return $false
    }
    return ($result.Output -match "Ubuntu\s+Running\s+2" -or $result.Output -match "Ubuntu\s+Stopped\s+2")
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

function Refresh-LiveStatus {
    if (-not $Script:LiveStatusList) {
        return
    }

    $events = @(Get-StatusEvents)
    $Script:LiveStatusList.Items.Clear()
    foreach ($event in $events) {
        Add-ListRow $Script:LiveStatusList $event.Source $event.Code $event.Message
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

function Run-SetupAudit {
    Ensure-LogDir
    $Script:SetupList.Items.Clear()

    Add-ListRow $Script:SetupList "PowerShell elevation" ($(if (Test-IsAdmin) { "OK" } else { "WARN" })) ($(if (Test-IsAdmin) { "Running as Administrator" } else { "Start/Stop will request UAC elevation" }))
    Add-ListRow $Script:SetupList "usbipd" ($(if (Test-CommandAvailable "usbipd") { "OK" } else { "MISSING" })) "Required for Bluetooth pass-through"
    Add-ListRow $Script:SetupList "wsl" ($(if (Test-CommandAvailable "wsl") { "OK" } else { "MISSING" })) "Required for Linux bridge"
    Add-ListRow $Script:SetupList "Ubuntu distro" ($(if (Test-UbuntuAvailable) { "OK" } else { "WARN" })) "Current scripts target distro name Ubuntu"
    Add-ListRow $Script:SetupList "Ubuntu WSL2" ($(if (Test-UbuntuWsl2) { "OK" } else { "WARN" })) "USB/IP requires WSL2"
    Add-ListRow $Script:SetupList "Runtime: receiver" ($(if (Test-Path $Script:ReceiverPath) { "OK" } else { "MISSING" })) "stadia_receiver.exe"
    Add-ListRow $Script:SetupList "Runtime: ViGEm DLL" ($(if (Test-Path $Script:ViGEmClientPath) { "OK" } else { "MISSING" })) "ViGEmClient.dll next to receiver"
    Add-ListRow $Script:SetupList "Runtime: bridge" ($(if (Test-Path $Script:BridgePath) { "OK" } else { "MISSING" })) "stadia_bridge"
    Add-ListRow $Script:SetupList "Linux startup script" ($(if (Test-Path $Script:StartShPath) { "OK" } else { "MISSING" })) "start.sh"
    Add-ListRow $Script:SetupList "Macro config" ($(if (Test-Path $Script:ConfigPath) { "OK" } else { "MISSING" })) "stadia_buttons.ini"

    $devices = Get-UsbipdDevices
    $btCount = @($devices | Where-Object { $_.IsBluetooth }).Count
    Add-ListRow $Script:SetupList "Bluetooth candidates" ($(if ($btCount -gt 0) { "OK" } else { "WARN" })) "$btCount likely Bluetooth adapter(s) found"

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

    if (Test-CommandAvailable "wsl") {
        $bluez = Invoke-CapturedCommand -FileName "wsl" -Arguments @("bash", "-lc", "command -v bluetoothctl >/dev/null && echo ok || echo missing") -TimeoutMs 8000
        Add-ListRow $Script:SetupList "BlueZ in WSL" ($(if ($bluez.Output.Trim() -eq "ok") { "OK" } else { "WARN" })) $bluez.Output.Trim()
        $events = Invoke-CapturedCommand -FileName "wsl" -Arguments @("bash", "-lc", "ls /dev/input/event* 2>/dev/null | tr '\n' ' '") -TimeoutMs 8000
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

function Refresh-BluetoothPanel {
    if (-not $Script:BluetoothStatusList) {
        return
    }

    $snapshot = Get-BluetoothSnapshot
    $Script:BluetoothStatusList.Items.Clear()
    $Script:BluetoothAdapterList.Items.Clear()
    $Script:BluetoothDeviceList.Items.Clear()

    $serviceState = if ($snapshot.Service) { $snapshot.Service.Status.ToString() } else { "Not found" }
    Add-ListRow $Script:BluetoothStatusList "Bluetooth service" ($(if ($serviceState -eq "Running") { "OK" } else { "WARN" })) "bthserv: $serviceState"
    Add-ListRow $Script:BluetoothStatusList "Bluetooth adapters" ($(if ($snapshot.Adapters.Count -gt 0) { "OK" } else { "MISSING" })) "$($snapshot.Adapters.Count) adapter(s) detected"
    Add-ListRow $Script:BluetoothStatusList "Active devices" ($(if ($snapshot.ActiveDevices.Count -gt 0) { "OK" } else { "INFO" })) "$($snapshot.ActiveDevices.Count) Bluetooth device(s) with PnP Status OK"
    Add-ListRow $Script:BluetoothStatusList "Known devices" ($(if ($snapshot.PairedOrKnown.Count -gt 0) { "OK" } else { "INFO" })) "$($snapshot.PairedOrKnown.Count) non-adapter Bluetooth device(s) known to Windows"
    Add-ListRow $Script:BluetoothStatusList "Bluetooth version" "INFO" "Windows does not reliably expose the Bluetooth spec version here; $($snapshot.DriverInfo)"
    Add-ListRow $Script:BluetoothStatusList "Device limit" "INFO" "Not reliably exposed; practical limit depends on adapter, profile mix, and driver."

    foreach ($adapter in $snapshot.Adapters) {
        Add-BluetoothDeviceRow $Script:BluetoothAdapterList $adapter "Adapter"
    }

    foreach ($device in $snapshot.PairedOrKnown) {
        $role = if ($device.Status -eq "OK") { "Active/OK" } else { "Known" }
        Add-BluetoothDeviceRow $Script:BluetoothDeviceList $device $role
    }

    if ($snapshot.Adapters.Count -gt 0) {
        $Script:BluetoothSummaryLabel.Text = "Bluetooth adapter present. $($snapshot.ActiveDevices.Count) active/OK device(s)."
    } else {
        $Script:BluetoothSummaryLabel.Text = "No Bluetooth adapter detected by Windows."
    }

    $Script:BluetoothInfoBox.Text = "Bluetooth version: Windows standard PnP APIs usually expose driver version, not the Bluetooth radio specification version.`r`n`r`nMax connected devices: not a fixed Windows value. It depends on the adapter chipset, driver, Bluetooth profile types, bandwidth, and whether devices use Classic Bluetooth or BLE.`r`n`r`nConnected count: this panel counts Bluetooth devices with PnP Status OK as active/available; some paired devices may still appear here differently depending on driver behavior."
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
}

function Refresh-ControllerTelemetry {
    if (-not $Script:TelemetryLabel) {
        return
    }

    if (-not (Test-Path $Script:ControllerStatePath)) {
        $Script:TelemetryLabel.Text = "No controller telemetry yet. Rebuild/use the updated stadia_receiver.exe, start the bridge, then press buttons."
        Reset-ControllerIndicators
        return
    }

    try {
        $state = Get-Content -Raw -Path $Script:ControllerStatePath | ConvertFrom-Json
    } catch {
        $Script:TelemetryLabel.Text = "Controller telemetry is present but could not be parsed yet."
        return
    }

    $age = (Get-Date) - (Get-Item $Script:ControllerStatePath).LastWriteTime
    $Script:TelemetryLabel.Text = "Telemetry live: updated $([math]::Round($age.TotalSeconds, 1)) seconds ago"

    $buttons = $state.buttons
    Set-ButtonIndicator "a" ([bool]$buttons.a)
    Set-ButtonIndicator "b" ([bool]$buttons.b)
    Set-ButtonIndicator "x" ([bool]$buttons.x)
    Set-ButtonIndicator "y" ([bool]$buttons.y)
    Set-ButtonIndicator "lb" ([bool]$buttons.lb)
    Set-ButtonIndicator "rb" ([bool]$buttons.rb)
    Set-ButtonIndicator "select" ([bool]$buttons.select)
    Set-ButtonIndicator "start" ([bool]$buttons.start)
    Set-ButtonIndicator "stadia" ([bool]$buttons.stadia)
    Set-ButtonIndicator "assistant" ([bool]$buttons.assistant)
    Set-ButtonIndicator "l3" ([bool]$buttons.l3)
    Set-ButtonIndicator "r3" ([bool]$buttons.r3)
    Set-ButtonIndicator "dpad_up" ([bool]$buttons.dpad_up)
    Set-ButtonIndicator "dpad_down" ([bool]$buttons.dpad_down)
    Set-ButtonIndicator "dpad_left" ([bool]$buttons.dpad_left)
    Set-ButtonIndicator "dpad_right" ([bool]$buttons.dpad_right)

    $axes = $state.axes
    $lt = [Math]::Max(0, [Math]::Min(255, [int]$axes.trigger_left))
    $rt = [Math]::Max(0, [Math]::Min(255, [int]$axes.trigger_right))
    $Script:LeftTriggerBar.Value = $lt
    $Script:RightTriggerBar.Value = $rt
    $Script:AxesLabel.Text = "LX $($axes.stick_lx)  LY $($axes.stick_ly)    RX $($axes.stick_rx)  RY $($axes.stick_ry)"
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
    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments @("bash", "-lc", "hostname -I 2>/dev/null") -TimeoutMs 8000
    if ($result.ExitCode -ne 0) {
        return ""
    }
    $parts = ($result.Output.Trim() -split "\s+") | Where-Object { $_ }
    if ($parts.Count -gt 0) {
        return $parts[0]
    }
    return ""
}

function Get-BatteryInfo {
    if (-not (Test-CommandAvailable "wsl")) {
        return "WSL is not available."
    }
    $result = Invoke-CapturedCommand -FileName "wsl" -Arguments @("bash", "-lc", "bluetoothctl info 2>/dev/null | grep -i Battery") -TimeoutMs 10000
    $text = ($result.Output + "`n" + $result.Error).Trim()
    if ($text -match "\(([0-9]{1,3})\)") {
        return "Controller battery: $($Matches[1])%"
    }
    if (-not [string]::IsNullOrWhiteSpace($text)) {
        return $text
    }
    return "Battery level not available. Start Stadia X and connect the controller first."
}

function Refresh-Status {
    $Script:ChecksList.Items.Clear()

    Add-CheckRow "PowerShell elevation" ($(if (Test-IsAdmin) { "OK" } else { "WARN" })) ($(if (Test-IsAdmin) { "Running as Administrator" } else { "Start/Stop will ask for UAC elevation" }))
    Add-CheckRow "usbipd" ($(if (Test-CommandAvailable "usbipd") { "OK" } else { "MISSING" })) "Required to attach the Bluetooth adapter to WSL"
    Add-CheckRow "wsl" ($(if (Test-CommandAvailable "wsl") { "OK" } else { "MISSING" })) "Required for the Linux bridge"

    $ubuntuOk = $false
    if (Test-CommandAvailable "wsl") {
        $ubuntuCheck = Invoke-CapturedCommand -FileName "wsl" -Arguments @("-d", "Ubuntu", "echo", "ok") -TimeoutMs 8000
        $ubuntuOk = ($ubuntuCheck.ExitCode -eq 0)
    }
    Add-CheckRow "Ubuntu WSL distro" ($(if ($ubuntuOk) { "OK" } else { "WARN" })) "The current scripts target distro name 'Ubuntu'"

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

    [System.IO.File]::WriteAllText($Script:ConfigPath, $Script:MacroBox.Text, [System.Text.Encoding]::UTF8)
    Add-Log "Saved macro config."
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

$macroPage = New-Object System.Windows.Forms.TabPage
$macroPage.Text = "Macros"
$macroPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($macroPage)

$logPage = New-Object System.Windows.Forms.TabPage
$logPage.Text = "Log"
$logPage.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
[void]$tabs.TabPages.Add($logPage)

$setupTop = New-Object System.Windows.Forms.Panel
$setupTop.Dock = "Top"
$setupTop.Height = 58
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

$setupHint = New-Object System.Windows.Forms.Label
$setupHint.Text = "Use this before and after starting Stadia X to catch missing requirements early."
$setupHint.AutoSize = $true
$setupHint.Font = New-Object System.Drawing.Font("Segoe UI", 9)
$setupHint.ForeColor = [System.Drawing.Color]::FromArgb(70, 80, 95)
$setupHint.Location = New-Object System.Drawing.Point(350, 18)
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
$bluetoothTop.Height = 58
$bluetoothTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$bluetoothPage.Controls.Add($bluetoothTop)

$Script:BluetoothSummaryLabel = New-Object System.Windows.Forms.Label
$Script:BluetoothSummaryLabel.Text = "Bluetooth status not loaded yet."
$Script:BluetoothSummaryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:BluetoothSummaryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:BluetoothSummaryLabel.AutoSize = $false
$Script:BluetoothSummaryLabel.Size = New-Object System.Drawing.Size(520, 30)
$Script:BluetoothSummaryLabel.Location = New-Object System.Drawing.Point(14, 16)
$bluetoothTop.Controls.Add($Script:BluetoothSummaryLabel)

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

$bluetoothDevicesGroup = New-Object System.Windows.Forms.GroupBox
$bluetoothDevicesGroup.Text = "Known / active Bluetooth devices"
$bluetoothDevicesGroup.Dock = "Fill"
$bluetoothDevicesGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$bluetoothLowerSplit.Panel2.Controls.Add($bluetoothDevicesGroup)

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

$leftPanel = New-Object System.Windows.Forms.Panel
$leftPanel.Dock = "Left"
$leftPanel.Width = 310
$leftPanel.Padding = New-Object System.Windows.Forms.Padding(14)
$statusPage.Controls.Add($leftPanel)

$actions = New-Object System.Windows.Forms.GroupBox
$actions.Text = "Actions"
$actions.Dock = "Top"
$actions.Height = 250
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
$batteryButton.Location = New-Object System.Drawing.Point(18, 130)
$batteryButton.Add_Click({
    $info = Get-BatteryInfo
    Add-Log $info
    [System.Windows.Forms.MessageBox]::Show($info, "Stadia X Battery", "OK", "Information") | Out-Null
})
$actions.Controls.Add($batteryButton)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh status"
$refreshButton.Size = New-Object System.Drawing.Size(125, 32)
$refreshButton.Location = New-Object System.Drawing.Point(18, 176)
$refreshButton.Add_Click({
    Refresh-BluetoothList
    Refresh-Status
})
$actions.Controls.Add($refreshButton)

$folderButton = New-Object System.Windows.Forms.Button
$folderButton.Text = "Open folder"
$folderButton.Size = New-Object System.Drawing.Size(125, 32)
$folderButton.Location = New-Object System.Drawing.Point(153, 176)
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

$liveEventsGroup = New-Object System.Windows.Forms.GroupBox
$liveEventsGroup.Text = "Structured status events"
$liveEventsGroup.Dock = "Fill"
$liveEventsGroup.Font = New-Object System.Drawing.Font("Segoe UI", 9, [System.Drawing.FontStyle]::Bold)
$liveSplit.Panel1.Controls.Add($liveEventsGroup)

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
$controllerTop.Height = 58
$controllerTop.Padding = New-Object System.Windows.Forms.Padding(14, 12, 14, 8)
$controllerPage.Controls.Add($controllerTop)

$Script:TelemetryLabel = New-Object System.Windows.Forms.Label
$Script:TelemetryLabel.Text = "No controller telemetry yet."
$Script:TelemetryLabel.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$Script:TelemetryLabel.ForeColor = [System.Drawing.Color]::FromArgb(28, 38, 54)
$Script:TelemetryLabel.AutoSize = $false
$Script:TelemetryLabel.Size = New-Object System.Drawing.Size(680, 30)
$Script:TelemetryLabel.Location = New-Object System.Drawing.Point(14, 16)
$controllerTop.Controls.Add($Script:TelemetryLabel)

$refreshControllerButton = New-Object System.Windows.Forms.Button
$refreshControllerButton.Text = "Refresh"
$refreshControllerButton.Size = New-Object System.Drawing.Size(90, 30)
$refreshControllerButton.Anchor = "Top,Right"
$refreshControllerButton.Location = New-Object System.Drawing.Point(820, 14)
$refreshControllerButton.Add_Click({ Refresh-ControllerTelemetry })
$controllerTop.Controls.Add($refreshControllerButton)

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
$Script:AxesLabel.Size = New-Object System.Drawing.Size(350, 80)
$Script:AxesLabel.Location = New-Object System.Drawing.Point(18, 164)
$axesPanel.Controls.Add($Script:AxesLabel)

$telemetryNote = New-Object System.Windows.Forms.TextBox
$telemetryNote.Multiline = $true
$telemetryNote.ReadOnly = $true
$telemetryNote.BorderStyle = "None"
$telemetryNote.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$telemetryNote.Font = New-Object System.Drawing.Font("Segoe UI", 8)
$telemetryNote.Text = "This screen reads logs\controller-state.json. The source code now writes that file, but the existing binary must be rebuilt before live button visualization can work."
$telemetryNote.Size = New-Object System.Drawing.Size(350, 86)
$telemetryNote.Location = New-Object System.Drawing.Point(18, 250)
$axesPanel.Controls.Add($telemetryNote)

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

$Script:MacroBox = New-Object System.Windows.Forms.TextBox
$Script:MacroBox.Multiline = $true
$Script:MacroBox.AcceptsTab = $true
$Script:MacroBox.AcceptsReturn = $true
$Script:MacroBox.ScrollBars = "Both"
$Script:MacroBox.WordWrap = $false
$Script:MacroBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$Script:MacroBox.Dock = "Fill"
$macroPage.Controls.Add($Script:MacroBox)

$Script:LogBox = New-Object System.Windows.Forms.TextBox
$Script:LogBox.Multiline = $true
$Script:LogBox.ReadOnly = $true
$Script:LogBox.ScrollBars = "Vertical"
$Script:LogBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$Script:LogBox.BackColor = [System.Drawing.Color]::FromArgb(20, 24, 32)
$Script:LogBox.ForeColor = [System.Drawing.Color]::FromArgb(220, 230, 240)
$Script:LogBox.Dock = "Fill"
$logPage.Controls.Add($Script:LogBox)

$refreshTimer = New-Object System.Windows.Forms.Timer
$refreshTimer.Interval = 2000
$refreshTimer.Add_Tick({
    Refresh-LiveStatus
    Refresh-ControllerTelemetry
    if ($tabs.SelectedTab -eq $bluetoothPage) {
        Refresh-BluetoothPanel
    }
})

$form.Add_Shown({
    Ensure-LogDir
    Add-Log "Stadia X GUI loaded from $Script:Root"
    Refresh-BluetoothList
    Refresh-Status
    Run-SetupAudit
    Refresh-BluetoothPanel
    Refresh-LiveStatus
    Refresh-ControllerTelemetry
    Load-MacroConfig
    $refreshTimer.Start()
})

if ($env:STADIA_X_GUI_TEST -eq "1") {
    Write-Output "Stadia X GUI initialized"
    return
}

[void][System.Windows.Forms.Application]::Run($form)
