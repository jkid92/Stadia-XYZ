using System.Diagnostics;
using System.Security.Principal;

namespace StadiaX.ControlCenter;

internal sealed class MainForm : Form
{
    private readonly AppPaths _paths;
    private readonly ProcessRunner _runner = new();
    private readonly ReleaseChecker _releaseChecker = new();
    private readonly RequirementChecker _requirementChecker;
    private readonly SelfTestService _selfTestService;
    private readonly NativeControlServices _native;

    private readonly Label _statusLabel = new();
    private readonly Label _batteryLabel = new();
    private readonly Label _selectionLabel = new();
    private readonly Label _capacityLabel = new();
    private readonly TextBox _selectedBusText = new();
    private readonly ComboBox _wslCombo = new();
    private readonly ListView _firstRunList = new();
    private readonly ListView _setupChecksList = new();
    private readonly ListView _usbipdList = new();
    private readonly ListView _windowsBluetoothList = new();
    private readonly ListView _linuxBluetoothList = new();
    private readonly ListView _profilesList = new();
    private readonly ListView _macroList = new();
    private readonly ListView _controllerList = new();
    private readonly ControllerVisualizer _controllerVisualizer = new();
    private readonly ComboBox _controllerPadCombo = new();
    private readonly Label _controllerVisualStatusLabel = new();
    private readonly ComboBox _macroChordCombo = new();
    private readonly TextBox _macroShortcutText = new();
    private readonly TextBox _profileNameText = new();
    private readonly TextBox _profileMacText = new();
    private readonly ComboBox _profileSlotCombo = new();
    private readonly CheckBox _profileAutoConnectCheck = new();
    private readonly TextBox _macroBox = new();
    private readonly TextBox _controlStatusLogBox = new();
    private readonly TextBox _controlLinuxLogBox = new();
    private readonly TextBox _statusLogBox = new();
    private readonly TextBox _linuxLogBox = new();
    private readonly TextBox _diagnosticsBox = new();
    private readonly TabControl _tabs = new();
    private readonly System.Windows.Forms.Timer _logTimer = new();
    private readonly System.Windows.Forms.Timer _batteryTimer = new();
    private readonly NotifyIcon _trayIcon = new();

    private Form? _batteryOverlay;
    private Label? _batteryOverlayLabel;

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _requirementChecker = new RequirementChecker(paths, _runner);
        _selfTestService = new SelfTestService(paths, _requirementChecker);
        _native = new NativeControlServices(paths, _runner);

        Text = "Stadia X";
        Icon = LoadApplicationIcon();
        MinimumSize = new Size(1180, 760);
        Size = new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);

        BuildUi();
        ConfigureTimers();
        ConfigureTray();

        Shown += async (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(_paths.LogDirectory);
                await RefreshEverythingAsync();
                _logTimer.Start();
                _batteryTimer.Start();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Initial refresh failed";
                _diagnosticsBox.Text = ex.ToString();
                _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
            }
        };
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
                _trayIcon.Visible = true;
            }
        };
        FormClosing += (_, _) =>
        {
            _logTimer.Stop();
            _batteryTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            HideBatteryOverlay();
        };
    }

    private void BuildUi()
    {
        Controls.Add(BuildTabs());
        Controls.Add(BuildSidebar());
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Color.FromArgb(28, 38, 54)
        };

        var title = new Label
        {
            Text = "Stadia X",
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(18, 10)
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            Text = "Native WinForms control center",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(202, 213, 225),
            AutoSize = true,
            Location = new Point(22, 49)
        };
        header.Controls.Add(subtitle);

        _statusLabel.Text = $"Version {_paths.Version}";
        _statusLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _statusLabel.ForeColor = Color.White;
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _statusLabel.Size = new Size(520, 30);
        _statusLabel.Location = new Point(Width - 560, 24);
        header.Controls.Add(_statusLabel);
        return header;
    }

    private Control BuildSidebar()
    {
        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 318,
            Padding = new Padding(14)
        };

        var actions = new GroupBox
        {
            Text = "Control",
            Dock = DockStyle.Top,
            Height = 250,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        left.Controls.Add(actions);

        AddButton(actions, "Start bridge", 18, 30, 280, StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddButton(actions, "Stop and restore Bluetooth", 18, 78, 280, StopBridge, Color.FromArgb(178, 62, 62), Color.White);
        AddButton(actions, "Refresh all", 18, 132, 135, async () => await RefreshEverythingAsync());
        AddButton(actions, "Self-test", 163, 132, 135, async () => await RunSelfTestAsync());
        AddButton(actions, "Support bundle", 18, 178, 135, async () => await CreateSupportBundleAsync());
        AddButton(actions, "Releases", 163, 178, 135, async () => await CheckUpdatesAsync());

        var summary = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 9),
            Text = $"Install folder:{Environment.NewLine}{_paths.Root}{Environment.NewLine}{Environment.NewLine}Everything user-facing lives in this WinForms app: setup, Bluetooth, Linux devices, controller profiles, macros, battery, testing, logs, and diagnostics."
        };
        left.Controls.Add(summary);
        actions.BringToFront();
        return left;
    }

    private Control BuildTabs()
    {
        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI", 9);
        _tabs.Padding = new Point(14, 8);
        _tabs.Multiline = true;

        _tabs.TabPages.Add(BuildFirstRunPage());
        _tabs.TabPages.Add(BuildControlPage());
        _tabs.TabPages.Add(BuildSetupPage());
        _tabs.TabPages.Add(BuildBluetoothPage());
        _tabs.TabPages.Add(BuildProfilesPage());
        _tabs.TabPages.Add(BuildControllerTestPage());
        _tabs.TabPages.Add(BuildMacrosPage());
        _tabs.TabPages.Add(BuildLogsPage());
        _tabs.TabPages.Add(BuildDiagnosticsPage());
        return _tabs;
    }

    private TabPage BuildFirstRunPage()
    {
        var page = CreatePage("First Run");
        _firstRunList.Dock = DockStyle.Fill;
        ConfigureList(_firstRunList, ("Step", 210), ("State", 90), ("Details", 650));
        page.Controls.Add(_firstRunList);
        page.Controls.Add(BuildTopPanel("Pre-flight and post-start checklist", ("Refresh", async () => await RefreshChecksAsync())));
        return page;
    }

    private TabPage BuildControlPage()
    {
        var page = CreatePage("Control");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var quick = CreateGroup("Bridge actions");
        var quickPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(12), WrapContents = true };
        quick.Controls.Add(quickPanel);
        AddFlowButton(quickPanel, "Start", StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddFlowButton(quickPanel, "Stop", StopBridge, Color.FromArgb(178, 62, 62), Color.White);
        AddFlowButton(quickPanel, "Open folder", () => Process.Start("explorer.exe", $"\"{_paths.Root}\""));
        AddFlowButton(quickPanel, "Session report", async () => await CreateSessionReportAsync());
        layout.Controls.Add(quick, 0, 0);

        var health = CreateGroup("Current selection and battery");
        var healthPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        healthPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        health.Controls.Add(healthPanel);
        _selectionLabel.AutoSize = false;
        _selectionLabel.Dock = DockStyle.Fill;
        _batteryLabel.AutoSize = false;
        _batteryLabel.Dock = DockStyle.Fill;
        _capacityLabel.AutoSize = false;
        _capacityLabel.Dock = DockStyle.Fill;
        healthPanel.Controls.Add(_selectionLabel, 0, 0);
        healthPanel.Controls.Add(_batteryLabel, 0, 1);
        healthPanel.Controls.Add(_capacityLabel, 0, 2);
        layout.Controls.Add(health, 1, 0);

        var status = CreateGroup("Status timeline");
        ConfigureLogBox(_controlStatusLogBox, "Status log not loaded yet.");
        status.Controls.Add(_controlStatusLogBox);
        layout.Controls.Add(status, 0, 1);

        var linux = CreateGroup("Linux core");
        ConfigureLogBox(_controlLinuxLogBox, "Linux log not loaded yet.");
        linux.Controls.Add(_controlLinuxLogBox);
        layout.Controls.Add(linux, 1, 1);
        return page;
    }

    private TabPage BuildSetupPage()
    {
        var page = CreatePage("Setup");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var wslGroup = CreateGroup("WSL distro");
        var wslPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), WrapContents = false };
        _wslCombo.Width = 260;
        wslPanel.Controls.Add(_wslCombo);
        AddFlowButton(wslPanel, "Use", SaveSelectedWslDistro);
        AddFlowButton(wslPanel, "Refresh", async () => await RefreshWslDistrosAsync());
        wslGroup.Controls.Add(wslPanel);
        layout.Controls.Add(wslGroup, 0, 0);

        var busGroup = CreateGroup("Bluetooth BUSID");
        var busPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), WrapContents = false };
        _selectedBusText.Width = 120;
        busPanel.Controls.Add(_selectedBusText);
        AddFlowButton(busPanel, "Save", SaveSelectedBluetoothBusId);
        AddFlowButton(busPanel, "Automatic", ClearSelectedBluetoothBusId);
        busGroup.Controls.Add(busPanel);
        layout.Controls.Add(busGroup, 1, 0);

        var usbGroup = CreateGroup("USB/IP devices");
        ConfigureList(_usbipdList, ("BUSID", 90), ("VID:PID", 110), ("Name", 420), ("State", 130), ("Bluetooth", 90));
        _usbipdList.Dock = DockStyle.Fill;
        _usbipdList.SelectedIndexChanged += (_, _) =>
        {
            if (_usbipdList.SelectedItems.Count > 0)
            {
                _selectedBusText.Text = _usbipdList.SelectedItems[0].Text;
                RefreshSelectionLabels();
            }
        };
        usbGroup.Controls.Add(_usbipdList);
        layout.Controls.Add(usbGroup, 0, 1);

        var checks = CreateGroup("Requirement checks");
        ConfigureList(_setupChecksList, ("Item", 210), ("State", 90), ("Details", 520));
        checks.Controls.Add(_setupChecksList);
        layout.Controls.Add(checks, 1, 1);
        return page;
    }

    private TabPage BuildBluetoothPage()
    {
        var page = CreatePage("Bluetooth");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 240));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var windowsGroup = CreateGroup("Windows Bluetooth");
        ConfigureList(_windowsBluetoothList, ("Name", 300), ("Status", 90), ("Instance ID", 480));
        windowsGroup.Controls.Add(_windowsBluetoothList);
        windowsGroup.Controls.Add(BuildTopPanel("",
            ("Refresh", async () => await RefreshWindowsBluetoothAsync()),
            ("Enable", async () => await SetSelectedWindowsBluetoothEnabledAsync(true)),
            ("Disable", async () => await SetSelectedWindowsBluetoothEnabledAsync(false))));
        layout.Controls.Add(windowsGroup, 0, 0);

        var linuxActions = BuildTopPanel("Linux / BlueZ devices",
            ("Refresh", async () => await RefreshLinuxBluetoothDevicesAsync(0)),
            ("Scan", async () => await RefreshLinuxBluetoothDevicesAsync(8)),
            ("Use selected", UseSelectedLinuxControllers),
            ("Automatic", ClearSelectedLinuxControllers),
            ("Pair", async () => await RunLinuxCommandForSelectedAsync("pair")),
            ("Connect", async () => await RunLinuxCommandForSelectedAsync("connect")),
            ("Disconnect", async () => await RunLinuxCommandForSelectedAsync("disconnect")),
            ("Repair", async () => await RepairLinuxBluetoothAsync()),
            ("Capacity", async () => await CreateCapacityReportAsync()));
        layout.Controls.Add(linuxActions, 1, 0);

        var linuxGroup = CreateGroup("Visible to Linux");
        ConfigureList(_linuxBluetoothList, ("MAC", 140), ("Name", 240), ("Connected", 90), ("Paired", 80), ("Trusted", 80), ("Battery", 80), ("Stadia", 70));
        _linuxBluetoothList.MultiSelect = true;
        linuxGroup.Controls.Add(_linuxBluetoothList);
        layout.Controls.Add(linuxGroup, 0, 1);
        layout.SetColumnSpan(linuxGroup, 2);
        return page;
    }

    private TabPage BuildProfilesPage()
    {
        var page = CreatePage("Profiles");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        page.Controls.Add(layout);

        var listGroup = CreateGroup("Saved controllers");
        ConfigureList(_profilesList, ("Name", 180), ("MAC", 150), ("Slot", 70), ("Auto", 70));
        _profilesList.SelectedIndexChanged += (_, _) => LoadSelectedProfileIntoEditor();
        listGroup.Controls.Add(_profilesList);
        listGroup.Controls.Add(BuildTopPanel("", ("Refresh", RefreshProfiles), ("Apply auto", ApplyAutoProfiles), ("Delete", DeleteSelectedProfile)));
        layout.Controls.Add(listGroup, 0, 0);

        var editor = CreateGroup("Profile editor");
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 6, Padding = new Padding(16), Height = 240 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.Controls.Add(panel);
        AddEditorRow(panel, 0, "Name", _profileNameText);
        AddEditorRow(panel, 1, "Bluetooth MAC", _profileMacText);
        _profileSlotCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _profileSlotCombo.Items.AddRange(new object[] { "1", "2", "3", "4" });
        _profileSlotCombo.SelectedIndex = 0;
        AddEditorRow(panel, 2, "Preferred pad", _profileSlotCombo);
        _profileAutoConnectCheck.Text = "Use at startup";
        panel.Controls.Add(_profileAutoConnectCheck, 1, 3);
        AddButton(panel, "Save profile", 1, 4, SaveProfile);
        AddButton(panel, "Use Linux selected", 1, 5, UseLinuxSelectedAsProfile);
        layout.Controls.Add(editor, 1, 0);
        return page;
    }

    private TabPage BuildControllerTestPage()
    {
        var page = CreatePage("Controller Test");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var visualGroup = CreateGroup("Visual controller test");
        _controllerVisualizer.Dock = DockStyle.Fill;
        _controllerVisualizer.LoadControllerImage(Path.Combine(_paths.Root, "assets", "StadiaControllerPhoto.png"));
        visualGroup.Controls.Add(_controllerVisualizer);
        layout.Controls.Add(visualGroup, 1, 0);

        var telemetryGroup = CreateGroup("Telemetry");
        var telemetryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        telemetryGroup.Controls.Add(telemetryLayout);

        var padPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _controllerPadCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _controllerPadCombo.Width = 150;
        _controllerPadCombo.Items.AddRange(new object[] { "Auto active", "P1", "P2", "P3", "P4" });
        _controllerPadCombo.SelectedIndex = 0;
        _controllerPadCombo.SelectedIndexChanged += (_, _) => RefreshControllerTelemetry();
        padPanel.Controls.Add(new Label { Text = "Pad", Width = 38, Height = 26, TextAlign = ContentAlignment.MiddleLeft });
        padPanel.Controls.Add(_controllerPadCombo);
        telemetryLayout.Controls.Add(padPanel, 0, 0);

        _controllerVisualStatusLabel.AutoSize = false;
        _controllerVisualStatusLabel.Dock = DockStyle.Fill;
        _controllerVisualStatusLabel.AutoEllipsis = true;
        _controllerVisualStatusLabel.TextAlign = ContentAlignment.TopLeft;
        telemetryLayout.Controls.Add(_controllerVisualStatusLabel, 0, 1);

        telemetryLayout.Controls.Add(new Label
        {
            Text = "Pads",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        }, 0, 2);
        _controllerList.Dock = DockStyle.Fill;
        ConfigureList(_controllerList, ("Pad", 42), ("On", 38), ("P/s", 54), ("Trig", 58), ("Pressed", 96));
        telemetryLayout.Controls.Add(_controllerList, 0, 3);
        layout.Controls.Add(telemetryGroup, 0, 0);

        page.Controls.Add(BuildTopPanel("Reads logs/controller-state.json from the native receiver",
            ("Refresh", RefreshControllerTelemetry),
            ("Open state", () => OpenFileIfExists(_paths.ControllerState))));
        return page;
    }

    private TabPage BuildMacrosPage()
    {
        var page = CreatePage("Macros");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 230 };
        page.Controls.Add(split);
        ConfigureList(_macroList, ("Chord", 160), ("Shortcut", 520));
        split.Panel1.Controls.Add(_macroList);
        ConfigureLogBox(_macroBox, "");
        _macroBox.BackColor = Color.White;
        _macroBox.ForeColor = Color.FromArgb(25, 30, 40);
        split.Panel2.Controls.Add(_macroBox);
        var visual = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 56, Padding = new Padding(12, 8, 12, 8), WrapContents = false };
        _macroChordCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _macroChordCombo.Width = 145;
        _macroChordCombo.Items.AddRange(BuildMacroChordCodes().Cast<object>().ToArray());
        if (_macroChordCombo.Items.Count > 0)
        {
            _macroChordCombo.SelectedIndex = 0;
        }
        _macroShortcutText.Width = 220;
        visual.Controls.Add(new Label { Text = "Chord", Width = 48, TextAlign = ContentAlignment.MiddleLeft, Height = 28 });
        visual.Controls.Add(_macroChordCombo);
        visual.Controls.Add(new Label { Text = "Shortcut", Width = 64, TextAlign = ContentAlignment.MiddleLeft, Height = 28 });
        visual.Controls.Add(_macroShortcutText);
        AddFlowButton(visual, "Apply chord", ApplyMacroChordToEditor);
        page.Controls.Add(visual);
        page.Controls.Add(BuildTopPanel("Macro editor",
            ("Reload", LoadMacroConfig),
            ("Save", SaveMacroConfig),
            ("Open in Notepad", () => Process.Start("notepad.exe", $"\"{_paths.MacroConfig}\""))));
        return page;
    }

    private TabPage BuildLogsPage()
    {
        var page = CreatePage("Logs");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 300 };
        page.Controls.Add(split);
        ConfigureLogBox(_statusLogBox, "Status log not loaded yet.");
        ConfigureLogBox(_linuxLogBox, "Linux log not loaded yet.");
        split.Panel1.Controls.Add(_statusLogBox);
        split.Panel2.Controls.Add(_linuxLogBox);
        page.Controls.Add(BuildTopPanel("Live logs", ("Refresh", RefreshLogs), ("Open logs", () => Process.Start("explorer.exe", $"\"{_paths.LogDirectory}\""))));
        return page;
    }

    private TabPage BuildDiagnosticsPage()
    {
        var page = CreatePage("Diagnostics");
        ConfigureLogBox(_diagnosticsBox, "Run self-test, update check, session report, or support bundle to see output here.");
        page.Controls.Add(_diagnosticsBox);
        page.Controls.Add(BuildTopPanel("Diagnostics",
            ("Self-test", async () => await RunSelfTestAsync()),
            ("Check updates", async () => await CheckUpdatesAsync()),
            ("Session report", async () => await CreateSessionReportAsync()),
            ("Support bundle", async () => await CreateSupportBundleAsync())));
        return page;
    }

    private void ConfigureTimers()
    {
        _logTimer.Interval = 2500;
        _logTimer.Tick += (_, _) =>
        {
            RefreshLogs();
            RefreshControllerTelemetry();
        };

        _batteryTimer.Interval = 300000;
        _batteryTimer.Tick += (_, _) => { _ = RunActionWithDialogAsync(() => UpdateBatteryAsync()); };
    }

    private void ConfigureTray()
    {
        _trayIcon.Icon = Icon is null ? (Icon)SystemIcons.Application.Clone() : (Icon)Icon.Clone();
        _trayIcon.Text = "Stadia X";
        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        _trayIcon.ContextMenuStrip.Items.Add("Start", null, (_, _) => StartBridge());
        _trayIcon.ContextMenuStrip.Items.Add("Stop", null, (_, _) => StopBridge());
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Close());
        _trayIcon.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
    }

    private async Task RefreshEverythingAsync()
    {
        _statusLabel.Text = "Refreshing Stadia X state...";
        await RefreshChecksAsync();
        await RefreshWslDistrosAsync();
        await RefreshUsbipdDevicesAsync();
        await RefreshWindowsBluetoothAsync();
        await RefreshLinuxBluetoothDevicesAsync(0);
        RefreshProfiles();
        LoadMacroConfig();
        RefreshControllerTelemetry();
        RefreshLogs();
        await UpdateBatteryAsync();
        RefreshSelectionLabels();
        _statusLabel.Text = $"Ready - {_paths.Version}";
    }

    private async Task RefreshChecksAsync()
    {
        _firstRunList.Items.Clear();
        _setupChecksList.Items.Clear();
        var checks = await _requirementChecker.RunAsync();
        foreach (var check in checks)
        {
            var state = check.State.ToString().ToUpperInvariant();
            var color = StateColor(check.State);
            AddListRow(_firstRunList, check.Name, state, check.Details, color);
            AddListRow(_setupChecksList, check.Name, state, check.Details, color);
        }

        var missing = checks.Count(c => c.State == CheckState.Missing);
        var warn = checks.Count(c => c.State == CheckState.Warn);
        _statusLabel.Text = missing > 0 ? $"{missing} missing requirement(s)" : warn > 0 ? $"{warn} warning(s)" : $"Ready - {_paths.Version}";
    }

    private async Task RefreshWslDistrosAsync()
    {
        var selected = _native.GetSelectedWslDistro();
        _wslCombo.Items.Clear();
        _wslCombo.Items.Add("Automatic");
        foreach (var distro in await _native.GetWslDistrosAsync())
        {
            _wslCombo.Items.Add($"{distro.Name}  (WSL{distro.Version}, {distro.State})");
        }
        _wslCombo.SelectedIndex = 0;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            for (var i = 0; i < _wslCombo.Items.Count; i++)
            {
                if (_wslCombo.Items[i]?.ToString()?.StartsWith(selected + " ", StringComparison.Ordinal) == true)
                {
                    _wslCombo.SelectedIndex = i;
                    break;
                }
            }
        }
    }

    private async Task RefreshUsbipdDevicesAsync()
    {
        _usbipdList.Items.Clear();
        var devices = await _native.GetUsbipdDevicesAsync();
        var selected = _native.GetSelectedBluetoothBusId();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _selectedBusText.Text = selected;
        }

        foreach (var device in devices)
        {
            var item = new ListViewItem(device.BusId);
            item.SubItems.Add(device.VidPid);
            item.SubItems.Add(device.Name);
            item.SubItems.Add(device.State);
            item.SubItems.Add(device.IsBluetooth ? "yes" : "no");
            item.Tag = device;
            item.ForeColor = device.IsBluetooth ? Color.FromArgb(34, 120, 72) : Color.FromArgb(70, 70, 70);
            _usbipdList.Items.Add(item);
        }

        if (string.IsNullOrWhiteSpace(_selectedBusText.Text) && devices.FirstOrDefault(d => d.IsBluetooth) is { } autoDevice)
        {
            _selectedBusText.Text = autoDevice.BusId;
        }
        RefreshSelectionLabels();
    }

    private async Task RefreshWindowsBluetoothAsync()
    {
        _windowsBluetoothList.Items.Clear();
        var devices = await _native.GetWindowsBluetoothDevicesAsync();
        foreach (var device in devices)
        {
            var item = new ListViewItem(device.Name)
            {
                Tag = device,
                ForeColor = device.Status.Equals("OK", StringComparison.OrdinalIgnoreCase) ? Color.FromArgb(34, 120, 72) : Color.FromArgb(180, 45, 45)
            };
            item.SubItems.Add(device.Status);
            item.SubItems.Add(device.InstanceId);
            _windowsBluetoothList.Items.Add(item);
        }

        var selectedDevice = SelectedUsbipdDevice();
        _capacityLabel.Text = NativeControlServices.EstimateCapacity(selectedDevice, devices);
    }

    private async Task RefreshLinuxBluetoothDevicesAsync(int scanSeconds)
    {
        _linuxBluetoothList.Items.Clear();
        var devices = await _native.GetLinuxBluetoothDevicesAsync(scanSeconds);
        foreach (var device in devices)
        {
            var item = new ListViewItem(device.Mac);
            item.SubItems.Add(device.Name);
            item.SubItems.Add(device.Connected);
            item.SubItems.Add(device.Paired);
            item.SubItems.Add(device.Trusted);
            item.SubItems.Add(device.BatteryPercent is null ? "-" : device.BatteryPercent.Value + "%");
            item.SubItems.Add(device.IsStadia ? "yes" : "no");
            item.Tag = device;
            item.ForeColor = device.IsStadia ? Color.FromArgb(34, 120, 72) : Color.FromArgb(70, 70, 70);
            _linuxBluetoothList.Items.Add(item);
        }
        await UpdateBatteryAsync(devices);
    }

    private async Task UpdateBatteryAsync(IReadOnlyList<LinuxBluetoothDevice>? knownDevices = null)
    {
        knownDevices ??= await _native.GetLinuxBluetoothDevicesAsync(0);
        var stadia = knownDevices.Where(d => d.IsStadia || d.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (stadia.Length == 0)
        {
            _batteryLabel.Text = "Battery: not available yet. Start the bridge and connect a controller.";
            HideBatteryOverlay();
            return;
        }

        _batteryLabel.Text = "Battery: " + string.Join("   ", stadia.Select((d, i) => $"P{i + 1} {(d.BatteryPercent is null ? "unknown" : d.BatteryPercent + "%")} ({d.Connected})"));
        var low = stadia.Where(d => d.BatteryPercent is <= 30).ToArray();
        if (low.Length > 0)
        {
            ShowBatteryOverlay(low);
        }
        else
        {
            HideBatteryOverlay();
        }
    }

    private void RefreshProfiles()
    {
        _profilesList.Items.Clear();
        foreach (var profile in _native.GetProfiles())
        {
            var item = new ListViewItem(profile.Name);
            item.SubItems.Add(profile.Mac);
            item.SubItems.Add(profile.Slot.ToString());
            item.SubItems.Add(profile.AutoConnect ? "yes" : "no");
            item.Tag = profile;
            _profilesList.Items.Add(item);
        }
        RefreshSelectionLabels();
    }

    private void LoadMacroConfig()
    {
        _macroBox.Text = _native.LoadMacroText();
        RefreshMacroMappings();
    }

    private void RefreshMacroMappings()
    {
        _macroList.Items.Clear();
        foreach (var mapping in _native.LoadMacroMappings())
        {
            AddListRow(_macroList, mapping.Code, mapping.Shortcut, "", Color.FromArgb(70, 70, 70));
        }
    }

    private void RefreshControllerTelemetry()
    {
        _controllerList.Items.Clear();
        ControllerTelemetrySnapshot snapshot;
        try
        {
            snapshot = _native.ReadControllerTelemetry();
        }
        catch (Exception ex)
        {
            AddListRow(_controllerList, "-", "ERROR", ex.Message, Color.FromArgb(180, 45, 45));
            _controllerVisualizer.SetTelemetry(null, "Controller telemetry could not be read.");
            _controllerVisualStatusLabel.Text = "Telemetry read failed";
            return;
        }

        foreach (var controller in snapshot.Controllers)
        {
            var pressed = controller.Buttons.Where(pair => pair.Value).Select(pair => pair.Key).DefaultIfEmpty("-").ToArray();
            AddListRow(_controllerList,
                new[]
                {
                    "P" + controller.Index,
                    controller.Active ? "yes" : "no",
                    controller.PacketsPerSecond.ToString("0.0"),
                    $"{controller.TriggerLeft}/{controller.TriggerRight}",
                    string.Join(", ", pressed)
                },
                controller.Active ? Color.FromArgb(34, 120, 72) : Color.FromArgb(90, 90, 90));
        }

        UpdateControllerVisualizer(snapshot);
    }

    private void UpdateControllerVisualizer(ControllerTelemetrySnapshot snapshot)
    {
        ControllerTelemetryRow? selected = null;
        if (_controllerPadCombo.SelectedIndex > 0)
        {
            selected = snapshot.Controllers.FirstOrDefault(controller => controller.Index == _controllerPadCombo.SelectedIndex);
        }
        else
        {
            selected = snapshot.Controllers.FirstOrDefault(controller => controller.Active) ??
                       snapshot.Controllers.FirstOrDefault(controller => controller.Packets > 0);
        }

        if (selected is null)
        {
            _controllerVisualizer.SetTelemetry(null, "No controller telemetry yet. Start the bridge and press a button.");
            _controllerVisualStatusLabel.Text = "No controller data yet";
            return;
        }

        var pressed = selected.Buttons.Where(pair => pair.Value).Select(pair => pair.Key.ToUpperInvariant()).ToArray();
        var status = $"P{selected.Index} active={selected.Active}{Environment.NewLine}packets/s={selected.PacketsPerSecond:0.0} packets={selected.Packets}{Environment.NewLine}triggers={selected.TriggerLeft}/{selected.TriggerRight}{Environment.NewLine}pressed={(pressed.Length == 0 ? "-" : string.Join(", ", pressed))}";
        _controllerVisualizer.SetTelemetry(selected, status);
        _controllerVisualStatusLabel.Text = status;
    }

    private void RefreshLogs()
    {
        var statusText = LogReader.Tail(_paths.StatusLog, 140);
        var linuxText = LogReader.Tail(_paths.LinuxLog, 180);
        _controlStatusLogBox.Text = statusText;
        _statusLogBox.Text = statusText;
        _controlLinuxLogBox.Text = linuxText;
        _linuxLogBox.Text = linuxText;
    }

    private void RefreshSelectionLabels()
    {
        var bus = _native.GetSelectedBluetoothBusId();
        if (string.IsNullOrWhiteSpace(bus))
        {
            bus = string.IsNullOrWhiteSpace(_selectedBusText.Text) ? "automatic" : _selectedBusText.Text.Trim() + " (not saved)";
        }
        var distro = _native.GetSelectedWslDistro();
        var macs = _native.GetSelectedControllerMacs();
        _selectionLabel.Text = $"Bluetooth BUSID: {bus}   WSL: {(string.IsNullOrWhiteSpace(distro) ? "automatic" : distro)}   Controllers: {(macs.Count == 0 ? "automatic" : string.Join(", ", macs))}";
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var release = await _releaseChecker.GetLatestAsync();
            _diagnosticsBox.Text = $"Installed: {_paths.Version}{Environment.NewLine}Latest:    {release.Tag}{Environment.NewLine}URL:       {release.Url}";
            _statusLabel.Text = release.Tag.Equals(_paths.Version, StringComparison.OrdinalIgnoreCase) ? "Up to date" : $"Update available: {release.Tag}";
            _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
        }
        catch (Exception ex)
        {
            _diagnosticsBox.Text = ex.ToString();
            _statusLabel.Text = "Update check failed";
        }
    }

    private async Task RunSelfTestAsync()
    {
        _statusLabel.Text = "Running self-test...";
        var result = await _selfTestService.RunAsync(json: true);
        _diagnosticsBox.Text = result.Text;
        _statusLabel.Text = result.ExitCode == 0 ? "Self-test passed" : $"Self-test exit code {result.ExitCode}";
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task CreateSessionReportAsync()
    {
        var path = await _native.CreateSessionReportAsync();
        _diagnosticsBox.Text = "Session report created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task CreateSupportBundleAsync()
    {
        var path = await _native.CreateSupportBundleAsync();
        _diagnosticsBox.Text = "Support bundle created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private void StartBridge()
    {
        if (!string.IsNullOrWhiteSpace(_selectedBusText.Text))
        {
            try { _native.SaveSelectedBluetoothBusId(_selectedBusText.Text); } catch { }
        }
        RefreshSelectionLabels();
        LaunchSelfCommand("--start-bridge", elevateWhenNeeded: true, "Stadia X start requested. Watch Live Logs for progress.");
        _tabs.SelectedTab = _tabs.TabPages["Control"];
    }

    private void StopBridge()
    {
        LaunchSelfCommand("--stop-bridge", elevateWhenNeeded: true, "Stadia X stop requested. Watch Live Logs for progress.");
        _tabs.SelectedTab = _tabs.TabPages["Control"];
    }

    private void SaveSelectedBluetoothBusId()
    {
        try
        {
            _native.SaveSelectedBluetoothBusId(_selectedBusText.Text);
            RefreshSelectionLabels();
            _statusLabel.Text = "Bluetooth BUSID saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Bluetooth BUSID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ClearSelectedBluetoothBusId()
    {
        if (File.Exists(_paths.SelectedBluetoothBusId))
        {
            File.Delete(_paths.SelectedBluetoothBusId);
        }
        _selectedBusText.Clear();
        RefreshSelectionLabels();
        _statusLabel.Text = "Bluetooth selection returned to automatic";
    }

    private void SaveSelectedWslDistro()
    {
        var selected = _wslCombo.SelectedItem?.ToString() ?? "Automatic";
        var name = selected == "Automatic" ? "" : selected.Split("  ", StringSplitOptions.None)[0];
        try
        {
            _native.SaveSelectedWslDistro(name);
            RefreshSelectionLabels();
            _statusLabel.Text = string.IsNullOrWhiteSpace(name) ? "WSL selection returned to automatic" : $"WSL distro saved: {name}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "WSL distro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void UseSelectedLinuxControllers()
    {
        var macs = _linuxBluetoothList.SelectedItems.Cast<ListViewItem>()
            .Select(item => item.Tag as LinuxBluetoothDevice)
            .Where(device => device is not null)
            .Select(device => device!.Mac)
            .Take(4)
            .ToArray();
        _native.SaveSelectedControllerMacs(macs);
        RefreshSelectionLabels();
        _statusLabel.Text = macs.Length == 0 ? "Controller selection returned to automatic" : "Manual controller selection saved";
    }

    private void ClearSelectedLinuxControllers()
    {
        _native.SaveSelectedControllerMacs(Array.Empty<string>());
        RefreshSelectionLabels();
        _statusLabel.Text = "Controller selection returned to automatic";
    }

    private async Task RunLinuxCommandForSelectedAsync(string command)
    {
        if (_linuxBluetoothList.SelectedItems.Count == 0)
        {
            MessageBox.Show("Select one or more Linux Bluetooth devices first.", "Linux Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = new List<string>();
        foreach (ListViewItem item in _linuxBluetoothList.SelectedItems)
        {
            if (item.Tag is not LinuxBluetoothDevice device)
            {
                continue;
            }
            var result = await _native.RunLinuxBluetoothCommandAsync(device.Mac, command);
            lines.Add($"== {command} {device.Mac} {device.Name} ==");
            lines.Add(result.Output.Trim());
            lines.Add(result.Error.Trim());
            lines.Add("");
        }

        _diagnosticsBox.Text = string.Join(Environment.NewLine, lines);
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
        await RefreshLinuxBluetoothDevicesAsync(0);
    }

    private async Task RepairLinuxBluetoothAsync()
    {
        var result = await _native.RunLinuxBluetoothRepairAsync();
        _diagnosticsBox.Text = "Linux Bluetooth repair" + Environment.NewLine + Environment.NewLine + result.Output + Environment.NewLine + result.Error;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
        await RefreshLinuxBluetoothDevicesAsync(0);
    }

    private async Task CreateCapacityReportAsync()
    {
        var path = await _native.CreateCapacityReportAsync();
        _diagnosticsBox.Text = "Capacity report created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task SetSelectedWindowsBluetoothEnabledAsync(bool enabled)
    {
        if (_windowsBluetoothList.SelectedItems.Count == 0 || _windowsBluetoothList.SelectedItems[0].Tag is not WindowsBluetoothDevice device)
        {
            MessageBox.Show("Select a Windows Bluetooth device first.", "Windows Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var commandName = enabled ? "Enable-PnpDevice" : "Disable-PnpDevice";
        var escapedId = device.InstanceId.Replace("'", "''", StringComparison.Ordinal);
        var command = $"{commandName} -InstanceId '{escapedId}' -Confirm:$false";
        if (IsAdministrator())
        {
            var result = await _runner.RunAsync("powershell.exe", new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command }, _paths.Root, 30000);
            _diagnosticsBox.Text = result.Output + Environment.NewLine + result.Error;
            _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
        }
        else
        {
            Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command }
            });
            _statusLabel.Text = enabled ? "Bluetooth enable requested" : "Bluetooth disable requested";
        }

        await Task.Delay(1200);
        await RefreshWindowsBluetoothAsync();
    }

    private void SaveProfile()
    {
        var profiles = _native.GetProfiles().ToList();
        var slot = _profileSlotCombo.SelectedIndex + 1;
        var profile = new ControllerProfile(_profileNameText.Text.Trim(), _profileMacText.Text.Trim().ToUpperInvariant(), slot, _profileAutoConnectCheck.Checked);
        profiles.RemoveAll(p => p.Slot == slot || p.Mac.Equals(profile.Mac, StringComparison.OrdinalIgnoreCase));
        profiles.Add(profile);
        try
        {
            _native.SaveProfiles(profiles);
            RefreshProfiles();
            _statusLabel.Text = "Controller profile saved";
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteSelectedProfile()
    {
        if (_profilesList.SelectedItems.Count == 0 || _profilesList.SelectedItems[0].Tag is not ControllerProfile selected)
        {
            return;
        }
        var profiles = _native.GetProfiles().Where(p => !p.Mac.Equals(selected.Mac, StringComparison.OrdinalIgnoreCase)).ToArray();
        _native.SaveProfiles(profiles);
        RefreshProfiles();
    }

    private void ApplyAutoProfiles()
    {
        _native.ApplyAutoConnectProfiles();
        RefreshSelectionLabels();
        _statusLabel.Text = "Auto-connect profiles applied to startup";
    }

    private void UseLinuxSelectedAsProfile()
    {
        if (_linuxBluetoothList.SelectedItems.Count == 0 || _linuxBluetoothList.SelectedItems[0].Tag is not LinuxBluetoothDevice device)
        {
            MessageBox.Show("Select a Linux Bluetooth device first.", "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _profileNameText.Text = device.IsStadia ? "Stadia Controller" : device.Name;
        _profileMacText.Text = device.Mac;
        _profileAutoConnectCheck.Checked = true;
    }

    private void LoadSelectedProfileIntoEditor()
    {
        if (_profilesList.SelectedItems.Count == 0 || _profilesList.SelectedItems[0].Tag is not ControllerProfile profile)
        {
            return;
        }

        _profileNameText.Text = profile.Name;
        _profileMacText.Text = profile.Mac;
        _profileSlotCombo.SelectedIndex = Math.Clamp(profile.Slot - 1, 0, 3);
        _profileAutoConnectCheck.Checked = profile.AutoConnect;
    }

    private void SaveMacroConfig()
    {
        _native.SaveMacroText(_macroBox.Text);
        RefreshMacroMappings();
        _statusLabel.Text = "Macro config saved";
    }

    private void ApplyMacroChordToEditor()
    {
        var code = _macroChordCombo.SelectedItem?.ToString();
        var shortcut = _macroShortcutText.Text.Trim();
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(shortcut))
        {
            MessageBox.Show("Choose a chord and type a shortcut first.", "Macro editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = _macroBox.Lines.ToList();
        var buttonsIndex = lines.FindIndex(line => line.Trim().Equals("[Buttons]", StringComparison.OrdinalIgnoreCase));
        if (buttonsIndex < 0)
        {
            lines.Insert(0, "[Buttons]");
            buttonsIndex = 0;
        }

        var replaced = false;
        var insertAt = buttonsIndex + 1;
        for (var i = buttonsIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                break;
            }
            insertAt = i + 1;
            var equals = trimmed.IndexOf('=');
            if (equals > 0 && trimmed[..equals].Trim().Equals(code, StringComparison.OrdinalIgnoreCase))
            {
                lines[i] = $"{code}={shortcut}";
                replaced = true;
                break;
            }
        }

        if (!replaced)
        {
            lines.Insert(insertAt, $"{code}={shortcut}");
        }
        _macroBox.Lines = lines.ToArray();
        RefreshMacroPreviewFromEditor();
        _statusLabel.Text = $"Macro {code} updated in editor";
    }

    private void RefreshMacroPreviewFromEditor()
    {
        _macroList.Items.Clear();
        var inButtons = false;
        foreach (var rawLine in _macroBox.Lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                inButtons = line.Equals("[Buttons]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inButtons || line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }
            var equals = line.IndexOf('=');
            if (equals > 0)
            {
                AddListRow(_macroList, line[..equals].Trim(), line[(equals + 1)..].Trim(), "", Color.FromArgb(70, 70, 70));
            }
        }
    }

    private void LaunchSelfCommand(string argument, bool elevateWhenNeeded, string message)
    {
        var executable = File.Exists(_paths.AppExecutable) ? _paths.AppExecutable : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            MessageBox.Show("StadiaX.exe was not found. Build or install the native launcher first.", "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var startInfo = new ProcessStartInfo(executable, argument)
        {
            WorkingDirectory = _paths.Root,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevateWhenNeeded && !IsAdministrator())
        {
            startInfo.Verb = "runas";
        }

        try
        {
            Process.Start(startInfo);
            _statusLabel.Text = message;
            RefreshLogs();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ShowBatteryOverlay(IReadOnlyList<LinuxBluetoothDevice> lowDevices)
    {
        if (_batteryOverlay is null || _batteryOverlay.IsDisposed)
        {
            _batteryOverlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(180, 45, 45),
                Opacity = 0.94,
                Size = new Size(210, 44)
            };
            _batteryOverlayLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.White
            };
            _batteryOverlay.Controls.Add(_batteryOverlayLabel);
        }

        _batteryOverlayLabel!.Text = lowDevices.Count == 1
            ? $"Stadia battery {lowDevices[0].BatteryPercent}%"
            : "Low batteries: " + string.Join(" / ", lowDevices.Select(d => d.BatteryPercent + "%"));
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        _batteryOverlay.Location = new Point(area.Right - _batteryOverlay.Width - 16, area.Top + 16);
        if (!_batteryOverlay.Visible)
        {
            _batteryOverlay.Show(this);
        }
    }

    private void HideBatteryOverlay()
    {
        if (_batteryOverlay is { IsDisposed: false, Visible: true })
        {
            _batteryOverlay.Hide();
        }
    }

    private UsbipdDevice? SelectedUsbipdDevice()
    {
        if (_usbipdList.SelectedItems.Count > 0 && _usbipdList.SelectedItems[0].Tag is UsbipdDevice selected)
        {
            return selected;
        }
        return _usbipdList.Items.Cast<ListViewItem>().Select(item => item.Tag as UsbipdDevice).FirstOrDefault(device => device?.IsBluetooth == true);
    }

    private static TabPage CreatePage(string name)
    {
        return new TabPage(name)
        {
            Name = name,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(0)
        };
    }

    private static GroupBox CreateGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Padding = new Padding(10)
        };
    }

    private static Control BuildTopPanel(string title, params (string Text, Action Action)[] buttons)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 64,
            MinimumSize = new Size(0, 64),
            Padding = new Padding(12, 10, 12, 10),
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(8, 0, 0, 0),
            MinimumSize = new Size(0, 40)
        };
        foreach (var button in buttons)
        {
            AddFlowButton(flow, button.Text, button.Action);
        }

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(flow, 1, 0);
        return panel;
    }

    private static Control BuildTopPanel(string title, params (string Text, Func<Task> Action)[] buttons)
    {
        return BuildTopPanel(title, buttons.Select(b => (b.Text, Action: new Action(() => { _ = RunActionWithDialogAsync(b.Action); }))).ToArray());
    }

    private static void ConfigureList(ListView list, params (string Text, int Width)[] columns)
    {
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = true;
        list.HideSelection = false;
        list.Dock = DockStyle.Fill;
        list.Columns.Clear();
        foreach (var column in columns)
        {
            list.Columns.Add(column.Text, column.Width);
        }
    }

    private static void ConfigureLogBox(TextBox box, string text)
    {
        box.Multiline = true;
        box.ReadOnly = false;
        box.ScrollBars = ScrollBars.Both;
        box.WordWrap = false;
        box.Font = new Font("Consolas", 9);
        box.BackColor = Color.FromArgb(20, 24, 32);
        box.ForeColor = Color.FromArgb(220, 230, 240);
        box.Dock = DockStyle.Fill;
        box.Text = text;
    }

    private static void AddButton(Control parent, string text, int x, int y, int width, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, 34),
            Location = new Point(x, y),
            BackColor = backColor ?? SystemColors.Control,
            ForeColor = foreColor ?? SystemColors.ControlText
        };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private static void AddButton(Control parent, string text, int x, int y, int width, Func<Task> action)
    {
        AddButton(parent, text, x, y, width, () => { _ = RunActionWithDialogAsync(action); });
    }

    private static void AddButton(TableLayoutPanel parent, string text, int column, int row, Action action)
    {
        var button = new Button { Text = text, Dock = DockStyle.Left, Width = 140 };
        button.Click += (_, _) => action();
        parent.Controls.Add(button, column, row);
    }

    private static void AddFlowButton(FlowLayoutPanel parent, string text, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(112, 36),
            Padding = new Padding(10, 0, 10, 0),
            BackColor = backColor ?? SystemColors.Control,
            ForeColor = foreColor ?? SystemColors.ControlText,
            Margin = new Padding(4, 2, 4, 2),
            UseVisualStyleBackColor = backColor is null
        };
        button.Click += (_, _) => action();
        parent.Controls.Add(button);
    }

    private static void AddFlowButton(FlowLayoutPanel parent, string text, Func<Task> action)
    {
        AddFlowButton(parent, text, () => { _ = RunActionWithDialogAsync(action); });
    }

    private static async Task RunActionWithDialogAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static void AddEditorRow(TableLayoutPanel panel, int row, string labelText, Control editor)
    {
        var label = new Label { Text = labelText, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        editor.Dock = DockStyle.Fill;
        panel.Controls.Add(label, 0, row);
        panel.Controls.Add(editor, 1, row);
    }

    private static void AddListRow(ListView list, params string[] values)
    {
        AddListRow(list, values.Concat(new[] { "" }).ToArray(), Color.FromArgb(70, 70, 70));
    }

    private static void AddListRow(ListView list, string a, string b, string c, Color color)
    {
        AddListRow(list, new[] { a, b, c }, color);
    }

    private static void AddListRow(ListView list, string a, string b, string c, string d, string e, string f, string g, Color color)
    {
        AddListRow(list, new[] { a, b, c, d, e, f, g }, color);
    }

    private static void AddListRow(ListView list, IReadOnlyList<string> values, Color color)
    {
        if (values.Count == 0)
        {
            return;
        }
        var item = new ListViewItem(values[0]) { ForeColor = color };
        for (var i = 1; i < values.Count; i++)
        {
            item.SubItems.Add(values[i]);
        }
        list.Items.Add(item);
    }

    private static Color StateColor(CheckState state)
    {
        return state switch
        {
            CheckState.Ok => Color.FromArgb(34, 120, 72),
            CheckState.Warn => Color.FromArgb(170, 104, 0),
            CheckState.Missing => Color.FromArgb(180, 45, 45),
            _ => Color.FromArgb(70, 70, 70)
        };
    }

    private static IReadOnlyList<string> BuildMacroChordCodes()
    {
        var buttons = new[] { "A", "B", "X", "Y", "UP", "DOWN", "LEFT", "RIGHT", "LB", "RB", "L2", "R2", "L3", "R3", "SELECT", "START", "STADIA" };
        return new[] { "A", "C" }
            .Concat(buttons.Select(button => "A_" + button))
            .Concat(buttons.Select(button => "C_" + button))
            .ToArray();
    }

    private static void OpenFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
    }

    private static Icon LoadApplicationIcon()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
