using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
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
    private readonly UserActionLogger _actionLogger;

    private readonly Label _statusLabel = new();
    private readonly Label _batteryStatusLabel = new();
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
    private readonly ListView _wizardLinuxBluetoothList = new();
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
    private readonly CheckBox _batteryOverlayCheck = new();
    private readonly TextBox _macroBox = new();
    private readonly TextBox _controlStatusLogBox = new();
    private readonly TextBox _controlLinuxLogBox = new();
    private readonly TextBox _dashboardActionLogBox = new();
    private readonly ListView _doctorList = new();
    private readonly TextBox _doctorDetailsBox = new();
    private readonly Label _doctorStatusLabel = new();
    private readonly ProgressBar _doctorProgress = new();
    private readonly TextBox _statusLogBox = new();
    private readonly TextBox _linuxLogBox = new();
    private readonly TextBox _userActionLogBox = new();
    private readonly TextBox _appDiagnosticsLogBox = new();
    private readonly TextBox _diagnosticsBox = new();
    private readonly Label _dashboardStatusLabel = new();
    private readonly Label _dashboardDetailLabel = new();
    private readonly Label[] _dashboardPadNameLabels = new Label[4];
    private readonly Label[] _dashboardPadStatusLabels = new Label[4];
    private readonly Label[] _dashboardPadBatteryLabels = new Label[4];
    private readonly Label[] _dashboardPadPacketsLabels = new Label[4];
    private readonly Label[] _dashboardPadMacLabels = new Label[4];
    private readonly ProgressBar[] _dashboardPadBatteryBars = new ProgressBar[4];
    private readonly Label _wizardStatusLabel = new();
    private readonly Label _wizardSelectionLabel = new();
    private readonly ProgressBar _wizardProgress = new();
    private readonly Label[] _wizardStepLabels = new Label[7];
    private readonly Label _operationTitleLabel = new();
    private readonly Label _operationDetailLabel = new();
    private readonly ProgressBar _operationProgress = new();
    private readonly Label _linuxBluetoothSummaryLabel = new();
    private readonly TabControl _tabs = new();
    private readonly FlowLayoutPanel _tabNavPanel = new();
    private readonly Dictionary<TabPage, ModernTabButton> _tabButtons = new();
    private readonly System.Windows.Forms.Timer _logTimer = new();
    private readonly System.Windows.Forms.Timer _batteryTimer = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly ImageList _linuxBluetoothRowSizer = new() { ImageSize = new Size(1, 26), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Icon _baseIcon;

    private Form? _batteryOverlay;
    private Label? _batteryOverlayLabel;
    private Icon? _batteryIndicatorIcon;
    private bool _linuxRefreshInProgress;
    private bool _suppressSelectionLogging;
    private IReadOnlyList<LinuxBluetoothDevice> _lastLinuxBluetoothDevices = Array.Empty<LinuxBluetoothDevice>();
    private DateTime _lastLinuxBluetoothRefreshUtc = DateTime.MinValue;
    private IReadOnlyList<ControllerProfile> _lastProfiles = Array.Empty<ControllerProfile>();
    private ControllerTelemetrySnapshot? _lastTelemetrySnapshot;

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _requirementChecker = new RequirementChecker(paths, _runner);
        _selfTestService = new SelfTestService(paths, _requirementChecker);
        _native = new NativeControlServices(paths, _runner);
        _actionLogger = new UserActionLogger(paths);
        AppDiagnosticsLogger.Initialize(paths);

        Text = "Stadia X";
        _baseIcon = LoadApplicationIcon(paths);
        Icon = (Icon)_baseIcon.Clone();
        var compactUi = IsCompactUi();
        MinimumSize = compactUi ? new Size(1040, 660) : new Size(1180, 760);
        Size = compactUi ? new Size(1120, 720) : new Size(1280, 820);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);

        BuildUi();
        ConfigureTimers();
        ConfigureTray();

        Shown += async (_, _) =>
        {
            try
            {
                LogUserAction("App shown");
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
            LogUserAction("App closing");
            _logTimer.Stop();
            _batteryTimer.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _batteryIndicatorIcon?.Dispose();
            _baseIcon.Dispose();
            _linuxBluetoothRowSizer.Dispose();
            HideBatteryOverlay();
        };
    }

    private void BuildUi()
    {
        Controls.Add(BuildTabs());
        Controls.Add(BuildSidebar());
        Controls.Add(BuildHeader());
    }

    internal static bool IsCompactUi()
    {
        var density = Environment.GetEnvironmentVariable("STADIAX_UI_DENSITY");
        return !string.Equals(density, "comfortable", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(density, "classic", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBluetoothDemoMode()
    {
        return string.Equals(Environment.GetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH"), "1", StringComparison.OrdinalIgnoreCase);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = IsCompactUi() ? 70 : 78,
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
        _statusLabel.Location = new Point(Width - 560, 17);
        header.Controls.Add(_statusLabel);

        _batteryStatusLabel.Text = "Battery: --";
        _batteryStatusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _batteryStatusLabel.ForeColor = Color.FromArgb(202, 213, 225);
        _batteryStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _batteryStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _batteryStatusLabel.Size = new Size(520, 22);
        _batteryStatusLabel.Location = new Point(Width - 560, 47);
        header.Controls.Add(_batteryStatusLabel);
        return header;
    }

    private Control BuildSidebar()
    {
        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 318,
            Padding = IsCompactUi() ? new Padding(10) : new Padding(14)
        };

        var sidebarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 266));
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(sidebarLayout);

        var actions = new GroupBox
        {
            Text = "Control",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        sidebarLayout.Controls.Add(actions, 0, 0);

        var actionGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
            Padding = IsCompactUi() ? new Padding(10, 14, 10, 10) : new Padding(14, 18, 14, 14)
        };
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        actions.Controls.Add(actionGrid);

        AddActionGridButton(actionGrid, "Start bridge", 0, 0, 2, StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddActionGridButton(actionGrid, "Stop and restore", 0, 1, 2, StopBridge, Color.FromArgb(178, 62, 62), Color.White);
        AddActionGridButton(actionGrid, "Refresh all", 0, 2, 1, async () => await RefreshEverythingAsync());
        AddActionGridButton(actionGrid, "Self-test", 1, 2, 1, async () => await RunSelfTestAsync());
        AddActionGridButton(actionGrid, "Doctor", 0, 3, 1, async () => await RunControllerDoctorAsync());
        AddActionGridButton(actionGrid, "Support", 1, 3, 1, () => SelectTabIfExists("Support"));
        AddActionGridButton(actionGrid, "Bundle", 0, 4, 1, async () => await CreateSupportBundleAsync());
        AddActionGridButton(actionGrid, "Releases", 1, 4, 1, async () => await CheckUpdatesAsync());

        var summary = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9),
            Text = $"Install folder:{Environment.NewLine}{_paths.Root}{Environment.NewLine}{Environment.NewLine}Daily flow:{Environment.NewLine}Dashboard -> Doctor -> Pairing -> Test"
        };
        sidebarLayout.Controls.Add(BuildOperationProgressPanel(), 0, 1);
        sidebarLayout.Controls.Add(summary, 0, 2);
        return left;
    }

    private Control BuildTabs()
    {
        var shell = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.FromArgb(248, 250, 252)
        };
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 38 : 42));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = IsCompactUi() ? new Padding(7, 5, 7, 3) : new Padding(9, 6, 9, 4)
        };
        _tabNavPanel.Dock = DockStyle.Fill;
        _tabNavPanel.AutoScroll = false;
        _tabNavPanel.WrapContents = false;
        _tabNavPanel.FlowDirection = FlowDirection.LeftToRight;
        _tabNavPanel.BackColor = Color.Transparent;
        navHost.Controls.Add(_tabNavPanel);
        shell.Controls.Add(navHost, 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Appearance = TabAppearance.Buttons;
        _tabs.SizeMode = TabSizeMode.Fixed;
        _tabs.ItemSize = new Size(1, 1);
        _tabs.Padding = new Point(0, 0);
        _tabs.Multiline = false;
        _tabs.TabStop = false;

        _tabs.TabPages.Add(BuildDashboardPage());
        _tabs.TabPages.Add(BuildControllerDoctorPage());
        _tabs.TabPages.Add(BuildPairingWizardPage());
        _tabs.TabPages.Add(BuildControlPage());
        _tabs.TabPages.Add(BuildBluetoothPage());
        _tabs.TabPages.Add(BuildProfilesPage());
        _tabs.TabPages.Add(BuildControllerTestPage());
        _tabs.TabPages.Add(BuildMacrosPage());
        _tabs.TabPages.Add(BuildLogsPage());
        _tabs.TabPages.Add(BuildSetupPage());
        _tabs.TabPages.Add(BuildDiagnosticsPage());
        if (_tabs.TabPages.Count > 0)
        {
            _tabs.SelectedIndex = 0;
        }
        BuildTabNavigation();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            UpdateTabNavigation();
            LogUserSelection("Tab selected", ("name", _tabs.SelectedTab?.Text));
        };
        UpdateTabNavigation();
        shell.Controls.Add(_tabs, 0, 1);
        return shell;
    }

    private void BuildTabNavigation()
    {
        _tabButtons.Clear();
        _tabNavPanel.Controls.Clear();
        foreach (TabPage page in _tabs.TabPages)
        {
            var button = new ModernTabButton
            {
                Text = page.Text,
                AccessibleName = page.Text,
                AccessibleRole = AccessibleRole.PushButton,
                Tag = page,
                Width = ModernTabWidth(page.Text),
                Height = IsCompactUi() ? 27 : 30,
                Margin = new Padding(2, 0, 2, 0),
                Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 8.75F, FontStyle.Regular)
            };
            button.Click += (_, _) =>
            {
                if (button.Tag is TabPage target)
                {
                    _tabs.SelectedTab = target;
                }
            };
            _tabButtons[page] = button;
            _tabNavPanel.Controls.Add(button);
        }
    }

    private void UpdateTabNavigation()
    {
        foreach (var pair in _tabButtons)
        {
            pair.Value.IsSelected = ReferenceEquals(pair.Key, _tabs.SelectedTab);
        }
    }

    private static int ModernTabWidth(string text)
    {
        using var font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 8.75F, FontStyle.Regular);
        var measured = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        return Math.Clamp(measured + (IsCompactUi() ? 20 : 24), IsCompactUi() ? 52 : 60, IsCompactUi() ? 70 : 84);
    }

    private TabPage BuildDashboardPage()
    {
        var page = CreatePage("Home", "Dashboard");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 156 : 168));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 212));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var overview = CreateGroup("Control center");
        var overviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14, 16, 14, 12)
        };
        overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        overview.Controls.Add(overviewLayout);

        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _dashboardStatusLabel.Text = "Ready";
        _dashboardStatusLabel.Dock = DockStyle.Fill;
        _dashboardStatusLabel.AutoEllipsis = true;
        _dashboardStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _dashboardStatusLabel.Font = new Font("Segoe UI", IsCompactUi() ? 12.5F : 15, FontStyle.Bold);
        _dashboardStatusLabel.ForeColor = Color.FromArgb(24, 33, 48);
        _dashboardDetailLabel.Text = "Start the bridge or open the pairing wizard.";
        _dashboardDetailLabel.Dock = DockStyle.Fill;
        _dashboardDetailLabel.AutoEllipsis = true;
        _dashboardDetailLabel.TextAlign = ContentAlignment.TopLeft;
        _dashboardDetailLabel.Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9);
        _dashboardDetailLabel.ForeColor = Color.FromArgb(92, 106, 126);
        statusLayout.Controls.Add(_dashboardStatusLabel, 0, 0);
        statusLayout.Controls.Add(_dashboardDetailLabel, 0, 1);
        overviewLayout.Controls.Add(statusLayout, 0, 0);

        var actionFlow = CreateFullWidthToolbarFlow();
        actionFlow.Dock = DockStyle.Fill;
        actionFlow.Padding = new Padding(0, 0, 0, 0);
        AddFlowButton(actionFlow, "Start bridge", StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddFlowButton(actionFlow, "Stop", StopBridge, Color.FromArgb(178, 62, 62), Color.White);
        AddFlowButton(actionFlow, "Pairing wizard", () => SelectTabIfExists("Pairing"));
        AddFlowButton(actionFlow, "Doctor", async () => await RunControllerDoctorAsync());
        AddFlowButton(actionFlow, "Scan devices", async () => await RefreshLinuxBluetoothDevicesAsync(8));
        overviewLayout.Controls.Add(actionFlow, 1, 0);
        layout.Controls.Add(overview, 0, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0)
        };
        for (var slot = 1; slot <= 4; slot++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            cards.Controls.Add(BuildDashboardPadCard(slot), slot - 1, 0);
        }
        layout.Controls.Add(cards, 0, 1);

        var activity = CreateGroup("Recent user actions");
        activity.Margin = new Padding(0, 10, 0, 0);
        ConfigureLogBox(_dashboardActionLogBox, "User action log not loaded yet.");
        activity.Controls.Add(_dashboardActionLogBox);
        layout.Controls.Add(activity, 0, 2);

        return page;
    }

    private Control BuildDashboardPadCard(int slot)
    {
        var group = CreateGroup("P" + slot);
        group.Margin = new Padding(4);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = IsCompactUi() ? new Padding(10, 12, 10, 8) : new Padding(12, 16, 12, 10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 26 : 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 22 : 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 22 : 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(layout);

        var nameLabel = CreateDashboardValueLabel("No profile", IsCompactUi() ? 9.5F : 11, FontStyle.Bold);
        var statusLabel = CreateDashboardValueLabel("Waiting", IsCompactUi() ? 8.25F : 9, FontStyle.Bold, Color.FromArgb(92, 106, 126));
        var batteryLabel = CreateDashboardValueLabel("Battery --", IsCompactUi() ? 8.25F : 9);
        var batteryBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 4, 0, 4)
        };
        var packetsLabel = CreateDashboardValueLabel("Input 0.0/s", IsCompactUi() ? 8.25F : 9);
        var macLabel = CreateDashboardValueLabel("Automatic", 8, FontStyle.Regular, Color.FromArgb(92, 106, 126));

        _dashboardPadNameLabels[slot - 1] = nameLabel;
        _dashboardPadStatusLabels[slot - 1] = statusLabel;
        _dashboardPadBatteryLabels[slot - 1] = batteryLabel;
        _dashboardPadBatteryBars[slot - 1] = batteryBar;
        _dashboardPadPacketsLabels[slot - 1] = packetsLabel;
        _dashboardPadMacLabels[slot - 1] = macLabel;

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(statusLabel, 0, 1);
        layout.Controls.Add(batteryLabel, 0, 2);
        layout.Controls.Add(batteryBar, 0, 3);
        layout.Controls.Add(packetsLabel, 0, 4);
        layout.Controls.Add(macLabel, 0, 5);
        return group;
    }

    private static Label CreateDashboardValueLabel(string text, float size, FontStyle style = FontStyle.Regular, Color? foreColor = null)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", size, style),
            ForeColor = foreColor ?? Color.FromArgb(24, 33, 48)
        };
    }

    private TabPage BuildControllerDoctorPage()
    {
        var page = CreatePage("Doctor");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 330 : 370));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var summaryGroup = CreateGroup("Controller Doctor");
        var summaryLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = IsCompactUi() ? new Padding(12, 14, 12, 10) : new Padding(14, 16, 14, 12)
        };
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 44 : 52));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 32 : 38));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        summaryGroup.Controls.Add(summaryLayout);

        _doctorStatusLabel.Text = "Run Doctor to check bridge readiness";
        _doctorStatusLabel.Dock = DockStyle.Fill;
        _doctorStatusLabel.AutoEllipsis = true;
        _doctorStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _doctorStatusLabel.Font = new Font("Segoe UI", IsCompactUi() ? 10.5F : 12, FontStyle.Bold);
        _doctorStatusLabel.ForeColor = Color.FromArgb(24, 33, 48);
        summaryLayout.Controls.Add(_doctorStatusLabel, 0, 0);

        _doctorProgress.Dock = DockStyle.Fill;
        _doctorProgress.Minimum = 0;
        _doctorProgress.Maximum = 100;
        _doctorProgress.Value = 0;
        _doctorProgress.Style = ProgressBarStyle.Continuous;
        summaryLayout.Controls.Add(_doctorProgress, 0, 1);

        var doctorActions = CreateFullWidthToolbarFlow();
        doctorActions.Padding = new Padding(0, 4, 0, 4);
        AddFlowButton(doctorActions, "Run doctor", async () => await RunControllerDoctorAsync());
        AddFlowButton(doctorActions, "Scan", async () => await RunDoctorScanAsync());
        AddFlowButton(doctorActions, "Repair", async () => await RepairLinuxBluetoothAsync());
        AddFlowButton(doctorActions, "Logs", () => SelectTabIfExists("Logs"));
        AddFlowButton(doctorActions, "Bundle", async () => await CreateSupportBundleAsync());
        summaryLayout.Controls.Add(doctorActions, 0, 2);

        var hint = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9),
            Text = "Doctor checks the path a controller follows: Windows adapter, USB/IP bridge, BlueZ visibility, pairing state, saved profile, and input telemetry."
        };
        summaryLayout.Controls.Add(hint, 0, 3);

        ConfigureLogBox(_doctorDetailsBox, "Doctor details will appear here.");
        summaryLayout.Controls.Add(_doctorDetailsBox, 0, 4);
        layout.Controls.Add(summaryGroup, 0, 0);

        var checklistGroup = CreateGroup("Readiness checklist");
        ConfigureList(_doctorList, ("Step", 190), ("State", 80), ("Details", 520));
        _doctorList.ShowItemToolTips = true;
        _doctorList.Resize += (_, _) => ResizeDoctorColumns();
        checklistGroup.Controls.Add(_doctorList);
        layout.Controls.Add(checklistGroup, 1, 0);

        return page;
    }

    private TabPage BuildPairingWizardPage()
    {
        var page = CreatePage("Pairing");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 324 : 360));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var stepsGroup = CreateGroup("Guided pairing");
        var stepsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = PairingWizardStepNames().Length + 4,
            Padding = IsCompactUi() ? new Padding(12, 12, 12, 10) : new Padding(14, 16, 14, 12)
        };
        stepsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 38 : 46));
        stepsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 28 : 34));
        stepsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 40 : 48));
        for (var i = 0; i < PairingWizardStepNames().Length; i++)
        {
            stepsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 32 : 38));
        }
        stepsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stepsGroup.Controls.Add(stepsLayout);

        _wizardStatusLabel.Text = "Waiting for setup data";
        _wizardStatusLabel.Dock = DockStyle.Fill;
        _wizardStatusLabel.AutoEllipsis = true;
        _wizardStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _wizardStatusLabel.Font = new Font("Segoe UI", IsCompactUi() ? 10.5F : 12, FontStyle.Bold);
        stepsLayout.Controls.Add(_wizardStatusLabel, 0, 0);

        _wizardProgress.Dock = DockStyle.Fill;
        _wizardProgress.Minimum = 0;
        _wizardProgress.Maximum = 100;
        _wizardProgress.Value = 0;
        _wizardProgress.Style = ProgressBarStyle.Continuous;
        stepsLayout.Controls.Add(_wizardProgress, 0, 1);

        _wizardSelectionLabel.Text = "Selected: none";
        _wizardSelectionLabel.Dock = DockStyle.Fill;
        _wizardSelectionLabel.AutoEllipsis = true;
        _wizardSelectionLabel.TextAlign = ContentAlignment.MiddleLeft;
        _wizardSelectionLabel.ForeColor = Color.FromArgb(92, 106, 126);
        stepsLayout.Controls.Add(_wizardSelectionLabel, 0, 2);

        var stepNames = PairingWizardStepNames();
        for (var i = 0; i < stepNames.Length; i++)
        {
            var label = CreateWizardStepLabel(i + 1, stepNames[i]);
            _wizardStepLabels[i] = label;
            stepsLayout.Controls.Add(label, 0, i + 3);
        }
        layout.Controls.Add(stepsGroup, 0, 0);

        var devicesGroup = CreateGroup("Linux devices");
        var devicesLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 0)
        };
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        devicesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        devicesGroup.Controls.Add(devicesLayout);

        devicesLayout.Controls.Add(BuildPairingWizardActionsPanel(), 0, 0);
        ConfigureList(_wizardLinuxBluetoothList, ("State", 82), ("Name", 300), ("MAC", 170), ("Batt", 72));
        _wizardLinuxBluetoothList.MultiSelect = true;
        _wizardLinuxBluetoothList.ShowItemToolTips = true;
        _wizardLinuxBluetoothList.SmallImageList = _linuxBluetoothRowSizer;
        _wizardLinuxBluetoothList.Resize += (_, _) => ResizeWizardLinuxBluetoothColumns();
        _wizardLinuxBluetoothList.SelectedIndexChanged += (_, _) =>
        {
            if (_wizardLinuxBluetoothList.SelectedItems.Count > 0)
            {
                LogUserSelection("Pairing wizard device selected", ("device", SelectedListText(_wizardLinuxBluetoothList)));
            }
            RefreshPairingWizardStatus();
        };
        devicesLayout.Controls.Add(_wizardLinuxBluetoothList, 0, 1);
        layout.Controls.Add(devicesGroup, 1, 0);

        return page;
    }

    private Control BuildPairingWizardActionsPanel()
    {
        var flow = CreateFullWidthToolbarFlow();
        flow.Padding = IsCompactUi() ? new Padding(8, 5, 8, 4) : new Padding(12, 8, 12, 6);
        AddFlowButton(flow, "Refresh setup", async () =>
        {
            await RefreshChecksAsync();
            await RefreshWslDistrosAsync();
            await RefreshUsbipdDevicesAsync();
        });
        AddFlowButton(flow, "Start bridge", StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddFlowButton(flow, "Scan", async () => await RefreshLinuxBluetoothDevicesAsync(8));
        AddFlowButton(flow, "Pair", async () => await RunLinuxCommandForSelectedAsync("pair"));
        AddFlowButton(flow, "Connect", async () => await RunLinuxCommandForSelectedAsync("connect"));
        AddFlowButton(flow, "Use selected", UseSelectedLinuxControllers);
        AddFlowButton(flow, "Test input", () => SelectTabIfExists("Controller Test"));
        return flow;
    }

    private static Label CreateWizardStepLabel(int index, string text)
    {
        return new Label
        {
            Text = $"WAIT {index}. {text}",
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(92, 106, 126),
            Padding = new Padding(4, 0, 0, 0)
        };
    }

    private static string[] PairingWizardStepNames()
    {
        return new[]
        {
            "Requirements",
            "WSL distro",
            "BT adapter",
            "Bridge started",
            "Device scan",
            "Controller picked",
            "Input test"
        };
    }

    private TabPage BuildFirstRunPage()
    {
        var page = CreatePage("Checks", "First Run");
        _firstRunList.Dock = DockStyle.Fill;
        ConfigureList(_firstRunList, ("Step", 210), ("State", 90), ("Details", 650));
        page.Controls.Add(_firstRunList);
        page.Controls.Add(BuildTopPanel("Pre-flight and post-start checklist", ("Refresh", async () => await RefreshChecksAsync())));
        return page;
    }

    private TabPage BuildControlPage()
    {
        var page = CreatePage("Bridge", "Control");
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
        var healthPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
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
        var page = CreatePage("Settings", "Setup");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var wslGroup = CreateGroup("WSL distro");
        var wslPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), WrapContents = true, AutoScroll = true };
        _wslCombo.Width = 260;
        _wslCombo.SelectedIndexChanged += (_, _) => LogUserSelection("WSL distro selected", ("candidateDistro", _wslCombo.SelectedItem?.ToString()));
        wslPanel.Controls.Add(_wslCombo);
        AddFlowButton(wslPanel, "Use", SaveSelectedWslDistro);
        AddFlowButton(wslPanel, "Refresh", async () => await RefreshWslDistrosAsync());
        wslGroup.Controls.Add(wslPanel);
        layout.Controls.Add(wslGroup, 0, 0);

        var busGroup = CreateGroup("Bluetooth BUSID");
        var busPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), WrapContents = true, AutoScroll = true };
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
                LogUserSelection("USB/IP device selected", ("device", SelectedListText(_usbipdList, 1)));
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
        var page = CreatePage("Devices", "Bluetooth");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 220));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var windowsGroup = CreateGroup("Windows Bluetooth");
        var windowsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 8, 0, 0)
        };
        windowsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        windowsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        windowsGroup.Controls.Add(windowsLayout);
        ConfigureList(_windowsBluetoothList, ("Name", 300), ("Status", 90), ("Instance ID", 480));
        _windowsBluetoothList.Resize += (_, _) => ResizeWindowsBluetoothColumns();
        _windowsBluetoothList.SelectedIndexChanged += (_, _) =>
        {
            if (_windowsBluetoothList.SelectedItems.Count > 0)
            {
                LogUserSelection("Windows Bluetooth device selected", ("device", SelectedListText(_windowsBluetoothList, 1)));
            }
        };
        windowsLayout.Controls.Add(BuildWindowsBluetoothActionsPanel(), 0, 0);
        windowsLayout.Controls.Add(_windowsBluetoothList, 0, 1);
        layout.Controls.Add(windowsGroup, 0, 0);

        var linuxGroup = CreateGroup("Visible to Linux");
        var linuxLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 8, 0, 0)
        };
        linuxLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        linuxLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        linuxLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        linuxGroup.Controls.Add(linuxLayout);

        linuxLayout.Controls.Add(BuildLinuxBluetoothActionsPanel(), 0, 0);

        _linuxBluetoothSummaryLabel.Name = "LinuxBluetoothSummaryLabel";
        _linuxBluetoothSummaryLabel.Dock = DockStyle.Fill;
        _linuxBluetoothSummaryLabel.Margin = new Padding(0, 6, 0, 4);
        _linuxBluetoothSummaryLabel.Padding = new Padding(8, 0, 0, 0);
        _linuxBluetoothSummaryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _linuxBluetoothSummaryLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
        _linuxBluetoothSummaryLabel.ForeColor = Color.FromArgb(92, 106, 126);
        _linuxBluetoothSummaryLabel.Text = "Linux devices: not refreshed yet";
        linuxLayout.Controls.Add(_linuxBluetoothSummaryLabel, 0, 1);

        ConfigureList(_linuxBluetoothList, ("State", 86), ("Name", 320), ("MAC / Source", 176), ("Paired", 64), ("Trust", 64), ("Batt", 64), ("Source", 82));
        _linuxBluetoothList.Margin = new Padding(0, 4, 0, 0);
        _linuxBluetoothList.MultiSelect = true;
        _linuxBluetoothList.ShowItemToolTips = true;
        _linuxBluetoothList.SmallImageList = _linuxBluetoothRowSizer;
        _linuxBluetoothList.Resize += (_, _) => ResizeLinuxBluetoothColumns();
        _linuxBluetoothList.SelectedIndexChanged += (_, _) =>
        {
            if (_linuxBluetoothList.SelectedItems.Count > 0)
            {
                LogUserSelection("Linux Bluetooth device selected", ("device", SelectedListText(_linuxBluetoothList)));
            }
            RefreshPairingWizardStatus();
        };
        linuxLayout.Controls.Add(_linuxBluetoothList, 0, 2);
        layout.Controls.Add(linuxGroup, 0, 1);
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
        _profilesList.SelectedIndexChanged += (_, _) =>
        {
            if (_profilesList.SelectedItems.Count > 0)
            {
                LogUserSelection("Controller profile selected", ("profile", SelectedListText(_profilesList, 1)));
            }
            LoadSelectedProfileIntoEditor();
        };
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
        var page = CreatePage("Test", "Controller Test");
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
        var telemetryLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(10) };
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        telemetryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        telemetryGroup.Controls.Add(telemetryLayout);

        var padPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        _controllerPadCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _controllerPadCombo.Width = 150;
        _controllerPadCombo.Items.AddRange(new object[] { "Auto active", "P1", "P2", "P3", "P4" });
        _controllerPadCombo.SelectedIndex = 0;
        _controllerPadCombo.SelectedIndexChanged += (_, _) =>
        {
            LogUserSelection("Controller test pad selected", ("pad", _controllerPadCombo.SelectedItem?.ToString()));
            RefreshControllerTelemetry();
        };
        padPanel.Controls.Add(new Label { Text = "Pad", Width = 38, Height = 26, TextAlign = ContentAlignment.MiddleLeft });
        padPanel.Controls.Add(_controllerPadCombo);
        telemetryLayout.Controls.Add(padPanel, 0, 0);

        var rumbleGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Margin = new Padding(0) };
        rumbleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
        for (var slot = 0; slot < 4; slot++)
        {
            rumbleGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        rumbleGrid.Controls.Add(new Label { Text = "Rumble", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        AddActionGridButton(rumbleGrid, "1", 1, 0, 1, async () => await TestRumbleAsync(1));
        AddActionGridButton(rumbleGrid, "2", 2, 0, 1, async () => await TestRumbleAsync(2));
        AddActionGridButton(rumbleGrid, "3", 3, 0, 1, async () => await TestRumbleAsync(3));
        AddActionGridButton(rumbleGrid, "4", 4, 0, 1, async () => await TestRumbleAsync(4));
        telemetryLayout.Controls.Add(rumbleGrid, 0, 1);

        _controllerVisualStatusLabel.AutoSize = false;
        _controllerVisualStatusLabel.Dock = DockStyle.Fill;
        _controllerVisualStatusLabel.AutoEllipsis = true;
        _controllerVisualStatusLabel.TextAlign = ContentAlignment.TopLeft;
        telemetryLayout.Controls.Add(_controllerVisualStatusLabel, 0, 2);

        telemetryLayout.Controls.Add(new Label
        {
            Text = "Pads",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        }, 0, 3);
        _controllerList.Dock = DockStyle.Fill;
        ConfigureList(_controllerList, ("Pad", 42), ("On", 38), ("P/s", 54), ("Trig", 58), ("Pressed", 96));
        telemetryLayout.Controls.Add(_controllerList, 0, 4);
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
        _macroList.SelectedIndexChanged += (_, _) =>
        {
            if (_macroList.SelectedItems.Count > 0)
            {
                LogUserSelection("Macro mapping selected", ("mapping", SelectedListText(_macroList, 1)));
            }
        };
        split.Panel1.Controls.Add(_macroList);
        ConfigureLogBox(_macroBox, "", readOnly: false);
        _macroBox.BackColor = Color.White;
        _macroBox.ForeColor = Color.FromArgb(25, 30, 40);
        split.Panel2.Controls.Add(_macroBox);
        var visual = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 56),
            Padding = new Padding(12, 8, 12, 8),
            WrapContents = true
        };
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
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        page.Controls.Add(layout);

        ConfigureLogBox(_statusLogBox, "Status log not loaded yet.");
        ConfigureLogBox(_linuxLogBox, "Linux log not loaded yet.");
        ConfigureLogBox(_userActionLogBox, "User action log not loaded yet.");
        ConfigureLogBox(_appDiagnosticsLogBox, "App diagnostics log not loaded yet.");

        var statusGroup = CreateGroup("Status timeline");
        statusGroup.Controls.Add(_statusLogBox);
        layout.Controls.Add(statusGroup, 0, 0);

        var linuxGroup = CreateGroup("Linux core");
        linuxGroup.Controls.Add(_linuxLogBox);
        layout.Controls.Add(linuxGroup, 0, 1);

        var userGroup = CreateGroup("User actions");
        userGroup.Controls.Add(_userActionLogBox);
        layout.Controls.Add(userGroup, 0, 2);

        var appGroup = CreateGroup("App diagnostics");
        appGroup.Controls.Add(_appDiagnosticsLogBox);
        layout.Controls.Add(appGroup, 0, 3);

        page.Controls.Add(BuildTopPanel("Live logs", ("Refresh", RefreshLogs), ("Open logs", () => Process.Start("explorer.exe", $"\"{_paths.LogDirectory}\""))));
        return page;
    }

    private TabPage BuildDiagnosticsPage()
    {
        var page = CreatePage("Support", "Diagnostics");
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

        _batteryTimer.Interval = 30000;
        _batteryTimer.Tick += (_, _) => { _ = RunActionWithDialogAsync(() => UpdateBatteryAsync()); };
    }

    private void ConfigureTray()
    {
        _trayIcon.Icon = (Icon)_baseIcon.Clone();
        _trayIcon.Text = "Stadia X";
        _trayIcon.Visible = true;
        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (_, _) => { LogUserAction("Tray show"); Show(); WindowState = FormWindowState.Normal; Activate(); });
        _trayIcon.ContextMenuStrip.Items.Add("Start", null, (_, _) => { LogUserAction("Tray start"); StartBridge(); });
        _trayIcon.ContextMenuStrip.Items.Add("Stop", null, (_, _) => { LogUserAction("Tray stop"); StopBridge(); });
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { LogUserAction("Tray exit"); Close(); });
        _trayIcon.DoubleClick += (_, _) => { LogUserAction("Tray double-click show"); Show(); WindowState = FormWindowState.Normal; Activate(); };
    }

    private async Task RefreshEverythingAsync()
    {
        LogUserAction("Refresh all requested");
        BeginOperationProgress("Refreshing app state", "Checking requirements", 5);
        _statusLabel.Text = "Refreshing Stadia X state...";
        await RefreshChecksAsync();
        SetOperationProgress("Refreshing app state", "Reading WSL distros", 18);
        await RefreshWslDistrosAsync();
        SetOperationProgress("Refreshing app state", "Reading USB/IP devices", 32);
        await RefreshUsbipdDevicesAsync();
        SetOperationProgress("Refreshing app state", "Reading Windows Bluetooth devices", 46);
        await RefreshWindowsBluetoothAsync();
        SetOperationProgress("Refreshing app state", "Reading Linux Bluetooth devices", 62);
        var linuxDevices = await RefreshLinuxBluetoothDevicesAsync(0, updateProgress: false);
        SetOperationProgress("Refreshing app state", "Loading profiles and macros", 78);
        RefreshProfiles();
        LoadMacroConfig();
        RefreshControllerTelemetry();
        RefreshLogs();
        SetOperationProgress("Refreshing app state", "Updating battery status", 90);
        await UpdateBatteryAsync(linuxDevices);
        RefreshSelectionLabels();
        RefreshDashboardUi();
        RefreshPairingWizardStatus();
        var readyText = IsBluetoothDemoMode() ? $"Ready demo Bluetooth - {_paths.Version}" : $"Ready - {_paths.Version}";
        _statusLabel.Text = readyText;
        CompleteOperationProgress("Refreshing app state", readyText);
    }

    private async Task RefreshChecksAsync()
    {
        var checks = await _requirementChecker.RunAsync();
        PopulateRequirementLists(checks);
        var missing = checks.Count(c => c.State == CheckState.Missing);
        var warn = checks.Count(c => c.State == CheckState.Warn);
        _statusLabel.Text = missing > 0 ? $"{missing} missing requirement(s)" : warn > 0 ? $"{warn} warning(s)" : $"Ready - {_paths.Version}";
        RefreshPairingWizardStatus();
    }

    private void PopulateRequirementLists(IReadOnlyList<CheckResult> checks)
    {
        _firstRunList.Items.Clear();
        _setupChecksList.Items.Clear();
        foreach (var check in checks)
        {
            var state = check.State.ToString().ToUpperInvariant();
            var color = StateColor(check.State);
            AddListRow(_firstRunList, check.Name, state, check.Details, color);
            AddListRow(_setupChecksList, check.Name, state, check.Details, color);
        }
    }

    private async Task RefreshWslDistrosAsync()
    {
        var selected = _native.GetSelectedWslDistro();
        _suppressSelectionLogging = true;
        try
        {
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
        finally
        {
            _suppressSelectionLogging = false;
        }
        RefreshPairingWizardStatus();
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
        RefreshPairingWizardStatus();
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
        ResizeWindowsBluetoothColumns();

        var selectedDevice = SelectedUsbipdDevice();
        _capacityLabel.Text = NativeControlServices.EstimateCapacity(selectedDevice, devices);
        RefreshPairingWizardStatus();
    }

    private async Task<IReadOnlyList<LinuxBluetoothDevice>> RefreshLinuxBluetoothDevicesAsync(int scanSeconds, bool updateProgress = true)
    {
        if (_linuxRefreshInProgress)
        {
            if (updateProgress)
            {
                SetOperationProgress("Linux Bluetooth", "Another Linux refresh is already running", 100);
            }
            return Array.Empty<LinuxBluetoothDevice>();
        }

        var title = scanSeconds > 0 ? "Scanning Linux Bluetooth" : "Refreshing Linux Bluetooth";
        _linuxRefreshInProgress = true;
        if (updateProgress)
        {
            BeginOperationProgress(title, scanSeconds > 0 ? "Preparing bluetoothctl scan" : "Querying BlueZ devices", 8);
        }

        try
        {
            IReadOnlyList<LinuxBluetoothDevice> nativeDevices;
            if (scanSeconds > 0 && updateProgress)
            {
                nativeDevices = await AwaitWithTimedProgressAsync(
                    _native.GetLinuxBluetoothDevicesAsync(scanSeconds),
                    15,
                    72,
                    TimeSpan.FromSeconds(scanSeconds + 3),
                    title,
                    "Scanning for Bluetooth devices").ConfigureAwait(true);
            }
            else
            {
                if (updateProgress)
                {
                    SetOperationProgress(title, "Reading devices from Linux", 32);
                }
                nativeDevices = await _native.GetLinuxBluetoothDevicesAsync(scanSeconds).ConfigureAwait(true);
            }

            if (updateProgress)
            {
                SetOperationProgress(title, "Merging receiver telemetry", 76);
            }
            var devices = nativeDevices.ToList();
            AddReceiverFallbackDevices(devices);

            if (updateProgress)
            {
                SetOperationProgress(title, "Rendering device list", 84);
            }
            var visibleDevices = devices.ToArray();
            PopulateLinuxBluetoothList(_linuxBluetoothList, visibleDevices, compact: false);
            PopulateLinuxBluetoothList(_wizardLinuxBluetoothList, visibleDevices, compact: true);
            _lastLinuxBluetoothDevices = visibleDevices;
            _lastLinuxBluetoothRefreshUtc = DateTime.UtcNow;
            LogLinuxBluetoothRefresh(scanSeconds, visibleDevices, nativeDevices);
            ResizeLinuxBluetoothColumns();
            ResizeWizardLinuxBluetoothColumns();
            UpdateLinuxBluetoothSummary(devices);
            RefreshDashboardUi();
            RefreshPairingWizardStatus();

            if (updateProgress)
            {
                SetOperationProgress(title, "Updating battery status", 94);
            }
            await UpdateBatteryAsync(devices);

            if (updateProgress)
            {
                CompleteOperationProgress(title, devices.Count == 0 ? "No Linux Bluetooth devices visible yet" : $"{devices.Count} Linux device(s) visible");
            }
            return devices;
        }
        catch (Exception ex)
        {
            if (updateProgress)
            {
                FailOperationProgress(title, "Linux Bluetooth refresh failed");
            }
            _linuxBluetoothSummaryLabel.Text = "Linux devices: refresh failed - " + ex.Message;
            _linuxBluetoothSummaryLabel.ForeColor = Color.FromArgb(180, 45, 45);
            AppDiagnosticsLogger.Record("LINUX_BT_REFRESH_FAILED", ("scanSeconds", scanSeconds.ToString()), ("error", ex.Message));
            throw;
        }
        finally
        {
            _linuxRefreshInProgress = false;
        }
    }

    private static void LogLinuxBluetoothRefresh(int scanSeconds, IReadOnlyList<LinuxBluetoothDevice> visibleDevices, IReadOnlyList<LinuxBluetoothDevice> nativeDevices)
    {
        var connected = visibleDevices.Count(IsLiveBluetoothConnected);
        var stadia = visibleDevices.Count(device => device.IsStadia || device.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase));
        var batteries = visibleDevices
            .Where(device => device.BatteryPercent.HasValue)
            .Select(device => $"{device.Mac}:{device.BatteryPercent}%")
            .ToArray();
        var sources = visibleDevices
            .GroupBy(LinuxDeviceSourceText)
            .Select(group => $"{group.Key}:{group.Count()}")
            .ToArray();
        AppDiagnosticsLogger.Record(
            "LINUX_BT_REFRESH_RENDERED",
            ("scanSeconds", scanSeconds.ToString()),
            ("nativeCount", nativeDevices.Count.ToString()),
            ("visibleCount", visibleDevices.Count.ToString()),
            ("connected", connected.ToString()),
            ("stadia", stadia.ToString()),
            ("batteries", string.Join(",", batteries)),
            ("sources", string.Join(",", sources)),
            ("devices", string.Join(";", visibleDevices.Take(10).Select(DeviceDebugText))));
    }

    private static string DeviceDebugText(LinuxBluetoothDevice device)
    {
        var battery = device.BatteryPercent is null ? "-" : device.BatteryPercent + "%";
        return $"{device.Mac},{device.Name},connected={EmptyAsDash(device.Connected)},paired={EmptyAsDash(device.Paired)},trusted={EmptyAsDash(device.Trusted)},battery={battery},source={LinuxDeviceSourceText(device)}";
    }

    private void UpdateLinuxBluetoothSummary(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        if (devices.Count == 0)
        {
            _linuxBluetoothSummaryLabel.Text = "Linux devices: none visible. Start the bridge, then press Scan.";
            _linuxBluetoothSummaryLabel.ForeColor = Color.FromArgb(180, 45, 45);
            return;
        }

        var connected = devices.Count(IsLiveBluetoothConnected);
        var paired = devices.Count(device => NativeControlServices.IsBluetoothMac(device.Mac) && device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase));
        var stadia = devices.Count(device => device.IsStadia || device.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase));
        var historical = devices.Count(device => NativeControlServices.IsBluetoothMac(device.Mac) && IsHistoricalBluetoothSource(device));
        var receiver = devices.Count(device => !NativeControlServices.IsBluetoothMac(device.Mac) || device.Source.Equals(BluetoothDeviceSources.Receiver, StringComparison.OrdinalIgnoreCase));
        var firstBattery = devices
            .Where(device => device.BatteryPercent.HasValue)
            .Select(device => device.BatteryPercent!.Value + "%")
            .FirstOrDefault();

        var parts = new List<string>
        {
            $"Linux devices: {devices.Count} visible",
            $"{connected} connected",
            $"{paired} paired",
            $"{stadia} Stadia"
        };
        if (historical > 0)
        {
            parts.Add($"{historical} last seen");
        }
        if (receiver > 0)
        {
            parts.Add($"{receiver} receiver active");
        }

        _linuxBluetoothSummaryLabel.Text = string.Join(", ", parts) +
                                           (string.IsNullOrWhiteSpace(firstBattery) ? "" : $" - battery {firstBattery}");
        _linuxBluetoothSummaryLabel.ForeColor = connected > 0 ? Color.FromArgb(34, 120, 72) : Color.FromArgb(92, 106, 126);
    }

    private static void PopulateLinuxBluetoothList(ListView list, IReadOnlyList<LinuxBluetoothDevice> devices, bool compact)
    {
        if (list.IsDisposed)
        {
            return;
        }

        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var device in devices)
            {
                list.Items.Add(CreateLinuxBluetoothListItem(device, compact));
            }
        }
        finally
        {
            try
            {
                list.EndUpdate();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }

    private static ListViewItem CreateLinuxBluetoothListItem(LinuxBluetoothDevice device, bool compact)
    {
        var item = new ListViewItem(LinuxDeviceStateText(device))
        {
            Tag = device,
            ToolTipText = $"{device.Name} {device.Mac} source={LinuxDeviceSourceText(device)} connected={EmptyAsDash(device.Connected)} paired={EmptyAsDash(device.Paired)} trusted={EmptyAsDash(device.Trusted)}",
            ForeColor = LinuxDeviceColor(device)
        };
        item.SubItems.Add(device.Name);
        item.SubItems.Add(compact ? CompactMacText(device.Mac) : device.Mac);
        if (compact)
        {
            item.SubItems.Add(BatteryCellText(device));
            return item;
        }

        item.SubItems.Add(YesNoText(device.Paired));
        item.SubItems.Add(YesNoText(device.Trusted));
        item.SubItems.Add(BatteryCellText(device));
        item.SubItems.Add(LinuxDeviceSourceText(device));
        return item;
    }

    private static string BatteryCellText(LinuxBluetoothDevice device)
    {
        return device.BatteryPercent is null ? "-" : device.BatteryPercent.Value + "%";
    }

    private static string CompactMacText(string value)
    {
        if (!NativeControlServices.IsBluetoothMac(value))
        {
            return value;
        }

        return "..." + value[^8..];
    }

    private static Color LinuxDeviceColor(LinuxBluetoothDevice device)
    {
        if (!NativeControlServices.IsBluetoothMac(device.Mac))
        {
            return Color.FromArgb(45, 91, 150);
        }

        return device.IsStadia ? Color.FromArgb(34, 120, 72) : Color.FromArgb(70, 70, 70);
    }

    private void ResizeLinuxBluetoothColumns()
    {
        if (_linuxBluetoothList.Columns.Count < 7 || _linuxBluetoothList.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(520, _linuxBluetoothList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        var sourceWidth = available >= 780 ? 96 : 0;
        var stateWidth = available >= 700 ? 86 : 76;
        var macWidth = available >= 700 ? 176 : 152;
        var yesNoWidth = available >= 700 ? 62 : 54;
        var batteryWidth = available >= 700 ? 62 : 54;
        var fixedWidth = stateWidth + macWidth + yesNoWidth + yesNoWidth + batteryWidth + sourceWidth;
        _linuxBluetoothList.Columns[0].Width = stateWidth;
        _linuxBluetoothList.Columns[2].Width = macWidth;
        _linuxBluetoothList.Columns[3].Width = yesNoWidth;
        _linuxBluetoothList.Columns[4].Width = yesNoWidth;
        _linuxBluetoothList.Columns[5].Width = batteryWidth;
        _linuxBluetoothList.Columns[6].Width = sourceWidth;
        _linuxBluetoothList.Columns[1].Width = Math.Max(190, available - fixedWidth);
    }

    private void ResizeWizardLinuxBluetoothColumns()
    {
        if (_wizardLinuxBluetoothList.Columns.Count < 4 || _wizardLinuxBluetoothList.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(320, _wizardLinuxBluetoothList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        var stateWidth = available >= 430 ? 82 : 70;
        var macWidth = available >= 430 ? 128 : 92;
        var batteryWidth = available >= 430 ? 64 : 48;
        var fixedWidth = stateWidth + macWidth + batteryWidth;
        _wizardLinuxBluetoothList.Columns[0].Width = stateWidth;
        _wizardLinuxBluetoothList.Columns[2].Width = macWidth;
        _wizardLinuxBluetoothList.Columns[3].Width = batteryWidth;
        _wizardLinuxBluetoothList.Columns[1].Width = Math.Max(110, available - fixedWidth);
    }

    private void ResizeWindowsBluetoothColumns()
    {
        if (_windowsBluetoothList.Columns.Count < 3 || _windowsBluetoothList.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(360, _windowsBluetoothList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        var statusWidth = 70;
        var nameWidth = Math.Clamp((int)(available * 0.42), 190, 310);
        _windowsBluetoothList.Columns[0].Width = nameWidth;
        _windowsBluetoothList.Columns[1].Width = statusWidth;
        _windowsBluetoothList.Columns[2].Width = Math.Max(120, available - nameWidth - statusWidth);
    }

    private void ResizeDoctorColumns()
    {
        if (_doctorList.Columns.Count < 3 || _doctorList.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(330, _doctorList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        var stateWidth = 76;
        var stepWidth = Math.Clamp((int)(available * 0.36), 150, 210);
        _doctorList.Columns[0].Width = stepWidth;
        _doctorList.Columns[1].Width = stateWidth;
        _doctorList.Columns[2].Width = Math.Max(100, available - stepWidth - stateWidth);
    }

    private static string LinuxDeviceStateText(LinuxBluetoothDevice device)
    {
        if (!NativeControlServices.IsBluetoothMac(device.Mac))
        {
            return "Receiver";
        }

        if (IsLiveBluetoothConnected(device))
        {
            return "Connected";
        }

        if (IsHistoricalBluetoothSource(device))
        {
            return "Last seen";
        }

        return device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "Paired" : "Seen";
    }

    private static string LinuxDeviceSourceText(LinuxBluetoothDevice device)
    {
        if (!NativeControlServices.IsBluetoothMac(device.Mac))
        {
            return BluetoothDeviceSources.Receiver;
        }

        if (IsHistoricalBluetoothSource(device))
        {
            return device.Source;
        }

        return device.IsStadia ? "BlueZ Stadia" : BluetoothDeviceSources.BlueZ;
    }

    private static bool IsLiveBluetoothConnected(LinuxBluetoothDevice device)
    {
        return NativeControlServices.IsBluetoothMac(device.Mac) &&
               IsLiveBluetoothSource(device) &&
               device.Connected.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHistoricalBluetoothSource(LinuxBluetoothDevice device)
    {
        return NativeControlServices.IsBluetoothMac(device.Mac) && !IsLiveBluetoothSource(device);
    }

    private static bool IsLiveBluetoothSource(LinuxBluetoothDevice device)
    {
        return string.IsNullOrWhiteSpace(device.Source) ||
               device.Source.Equals(BluetoothDeviceSources.BlueZ, StringComparison.OrdinalIgnoreCase) ||
               device.Source.Equals(BluetoothDeviceSources.Demo, StringComparison.OrdinalIgnoreCase);
    }

    private static string YesNoText(string value)
    {
        return value.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "yes" :
            value.Equals("no", StringComparison.OrdinalIgnoreCase) ? "no" :
            "-";
    }

    private static string EmptyAsDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string EmptyAsNone(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value;
    }

    private void AddReceiverFallbackDevices(List<LinuxBluetoothDevice> devices)
    {
        if (!IsControllerStateFresh())
        {
            return;
        }

        ControllerTelemetrySnapshot snapshot;
        try
        {
            snapshot = _native.ReadControllerTelemetry();
        }
        catch
        {
            return;
        }

        var activeRows = snapshot.Controllers
            .Where(controller => controller.Active || controller.PacketsPerSecond > 0)
            .OrderBy(controller => controller.Index)
            .ToArray();
        var realStadiaRows = devices.Count(device =>
            NativeControlServices.IsBluetoothMac(device.Mac) &&
            IsLiveBluetoothSource(device) &&
            (device.IsStadia || device.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase)));
        foreach (var controller in activeRows.Skip(realStadiaRows))
        {
            devices.Add(new LinuxBluetoothDevice(
                $"P{controller.Index}",
                "Stadia Controller (receiver)",
                "",
                "-",
                "-",
                null,
                true,
                BluetoothDeviceSources.Receiver));
        }
    }

    private bool IsControllerStateFresh()
    {
        return File.Exists(_paths.ControllerState) &&
               File.GetLastWriteTimeUtc(_paths.ControllerState) >= DateTime.UtcNow - TimeSpan.FromSeconds(10);
    }

    private async Task TestRumbleAsync(int controllerIndex)
    {
        LogUserAction("Rumble test requested", ("pad", $"P{controllerIndex}"));
        BeginOperationProgress("Rumble test", $"Sending pulse to P{controllerIndex}", 25);
        await _native.SendRumbleTestAsync(controllerIndex).ConfigureAwait(true);
        CompleteOperationProgress("Rumble test", $"Pulse sent to P{controllerIndex}");
    }

    private async Task UpdateBatteryAsync(IReadOnlyList<LinuxBluetoothDevice>? knownDevices = null)
    {
        var usedKnownDevices = knownDevices is not null;
        var usedCache = false;
        if (knownDevices is null && IsLinuxBluetoothCacheFresh())
        {
            knownDevices = _lastLinuxBluetoothDevices;
            usedKnownDevices = true;
            usedCache = true;
        }

        if (knownDevices is null)
        {
            var devices = (await _native.GetLinuxBluetoothDevicesAsync(0)).ToList();
            AddReceiverFallbackDevices(devices);
            knownDevices = devices;
            _lastLinuxBluetoothDevices = knownDevices;
            _lastLinuxBluetoothRefreshUtc = DateTime.UtcNow;
        }

        var stadia = knownDevices.Where(d => d.IsStadia || d.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase)).ToArray();
        UpdateBatteryIndicator(stadia);
        AppDiagnosticsLogger.Record(
            "BATTERY_REFRESH",
            ("usedKnownDevices", usedKnownDevices.ToString()),
            ("usedCache", usedCache.ToString()),
            ("visibleCount", knownDevices.Count.ToString()),
            ("stadiaCount", stadia.Length.ToString()),
            ("overlayChecked", _batteryOverlayCheck.Checked.ToString()),
            ("devices", string.Join(";", stadia.Take(4).Select(DeviceDebugText))));
        if (stadia.Length == 0)
        {
            _batteryLabel.Text = "Battery: not available yet. Start the bridge and connect a controller.";
            HideBatteryOverlay();
            AppDiagnosticsLogger.Record("BATTERY_REFRESH_EMPTY", ("visibleCount", knownDevices.Count.ToString()));
            RefreshDashboardUi();
            return;
        }

        _batteryLabel.Text = "Battery: " + string.Join("   ", stadia.Select((d, i) =>
        {
            var battery = d.BatteryPercent is null ? "unknown" : d.BatteryPercent + "%";
            var state = BatteryDeviceStateText(d);
            return string.IsNullOrWhiteSpace(state) ? $"P{i + 1} {battery}" : $"P{i + 1} {battery} ({state})";
        }));
        var low = stadia.Where(d => d.BatteryPercent is <= 30).ToArray();
        if (_batteryOverlayCheck.Checked)
        {
            ShowBatteryOverlay(stadia, warning: low.Length > 0);
        }
        else if (low.Length > 0)
        {
            ShowBatteryOverlay(low, warning: true);
        }
        else
        {
            HideBatteryOverlay();
        }
        AppDiagnosticsLogger.Record(
            "BATTERY_REFRESH_RENDERED",
            ("lowCount", low.Length.ToString()),
            ("overlayVisible", (_batteryOverlay?.Visible == true).ToString()),
            ("tray", BatteryShortText(stadia)));
        RefreshDashboardUi();
    }

    private bool IsLinuxBluetoothCacheFresh()
    {
        return _lastLinuxBluetoothDevices.Count > 0 &&
               _lastLinuxBluetoothRefreshUtc >= DateTime.UtcNow - TimeSpan.FromSeconds(60);
    }

    private void RefreshProfiles()
    {
        _profilesList.Items.Clear();
        var profiles = _native.GetProfiles().ToArray();
        _lastProfiles = profiles;
        foreach (var profile in profiles)
        {
            var item = new ListViewItem(profile.Name);
            item.SubItems.Add(profile.Mac);
            item.SubItems.Add(profile.Slot.ToString());
            item.SubItems.Add(profile.AutoConnect ? "yes" : "no");
            item.Tag = profile;
            _profilesList.Items.Add(item);
        }
        RefreshSelectionLabels();
        RefreshDashboardUi();
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
            _lastTelemetrySnapshot = null;
            AddListRow(_controllerList, "-", "ERROR", ex.Message, Color.FromArgb(180, 45, 45));
            _controllerVisualizer.SetTelemetry(null, "Controller telemetry could not be read.");
            _controllerVisualStatusLabel.Text = "Telemetry read failed";
            RefreshDashboardUi();
            RefreshPairingWizardStatus();
            return;
        }

        _lastTelemetrySnapshot = snapshot;
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
        RefreshDashboardUi();
        RefreshPairingWizardStatus();
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

    private void RefreshDashboardUi()
    {
        if (_dashboardPadNameLabels[0] is null)
        {
            return;
        }

        var profiles = _lastProfiles;
        var controllers = _lastTelemetrySnapshot?.Controllers ?? Array.Empty<ControllerTelemetryRow>();
        var stadiaDevices = _lastLinuxBluetoothDevices
            .Where(IsLikelyControllerDevice)
            .OrderByDescending(IsLiveBluetoothConnected)
            .ThenByDescending(device => device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase))
            .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var activeCount = controllers.Count(controller => controller.Active || controller.PacketsPerSecond > 0);
        var connectedCount = stadiaDevices.Count(IsLiveBluetoothConnected);
        var pairedCount = stadiaDevices.Count(device => device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase));

        _dashboardStatusLabel.Text = activeCount > 0
            ? $"{activeCount} controller(s) sending input"
            : connectedCount > 0
                ? $"{connectedCount} controller(s) connected"
                : pairedCount > 0
                    ? $"{pairedCount} controller(s) paired"
                    : "No active controller yet";
        _dashboardDetailLabel.Text = $"Linux devices {stadiaDevices.Length} - profiles {profiles.Count} - bridge data {(_lastTelemetrySnapshot is null ? "not read" : _lastTelemetrySnapshot.ReadAt.ToLocalTime().ToString("HH:mm:ss"))}";

        for (var slot = 1; slot <= 4; slot++)
        {
            var profile = profiles.FirstOrDefault(item => item.Slot == slot);
            var device = FindDashboardDevice(slot, stadiaDevices, profile);
            var controller = controllers.FirstOrDefault(item => item.Index == slot);
            var hasInput = controller is not null && (controller.Active || controller.PacketsPerSecond > 0);
            var state = hasInput
                ? "Active"
                : device is not null && IsLiveBluetoothConnected(device)
                    ? "Connected"
                    : device?.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase) == true
                        ? "Paired"
                        : profile is not null
                            ? "Profile saved"
                            : "Waiting";

            _dashboardPadNameLabels[slot - 1].Text = ShortPadName(device?.Name ?? profile?.Name ?? "Pad P" + slot);
            _dashboardPadStatusLabels[slot - 1].Text = state;
            _dashboardPadStatusLabels[slot - 1].ForeColor = DashboardStateColor(state);
            _dashboardPadBatteryLabels[slot - 1].Text = device?.BatteryPercent is null ? "Battery --" : "Battery " + device.BatteryPercent.Value + "%";
            _dashboardPadBatteryBars[slot - 1].Value = device?.BatteryPercent is null ? 0 : Math.Clamp(device.BatteryPercent.Value, 0, 100);
            _dashboardPadPacketsLabels[slot - 1].Text = "Input " + (controller?.PacketsPerSecond ?? 0).ToString("0.0") + "/s";
            _dashboardPadMacLabels[slot - 1].Text = device?.Mac ?? profile?.Mac ?? "Automatic";
        }
    }

    private void RefreshPairingWizardStatus()
    {
        if (_wizardStepLabels[0] is null)
        {
            return;
        }

        var requirementsKnown = _setupChecksList.Items.Count > 0;
        var missingRequirements = _setupChecksList.Items.Cast<ListViewItem>()
            .Count(item => SubItemText(item, 1).Equals("MISSING", StringComparison.OrdinalIgnoreCase));
        var warningRequirements = _setupChecksList.Items.Cast<ListViewItem>()
            .Count(item => SubItemText(item, 1).Equals("WARN", StringComparison.OrdinalIgnoreCase));
        var requirementsOk = requirementsKnown && missingRequirements == 0;
        var wslOk = _wslCombo.Items.Count > 1 || !string.IsNullOrWhiteSpace(_native.GetSelectedWslDistro());
        var adapterOk = !string.IsNullOrWhiteSpace(_selectedBusText.Text) ||
                        _usbipdList.Items.Cast<ListViewItem>().Any(item => item.Tag is UsbipdDevice device && device.IsBluetooth);
        var bridgeOk = IsControllerStateFresh() || _lastLinuxBluetoothDevices.Count > 0;
        var scanOk = _lastLinuxBluetoothDevices.Count > 0;
        var selectedDevices = SelectedLinuxBluetoothDevices();
        var selectionOk = selectedDevices.Count > 0;
        var inputOk = _lastTelemetrySnapshot?.Controllers.Any(controller => controller.Active || controller.PacketsPerSecond > 0) == true;

        var stepNames = PairingWizardStepNames();
        var states = new[]
        {
            (Done: requirementsOk, Warn: requirementsKnown && missingRequirements > 0, Detail: !requirementsKnown ? "" : missingRequirements > 0 ? $" - {missingRequirements} missing" : warningRequirements > 0 ? $" - {warningRequirements} warning(s)" : ""),
            (Done: wslOk, Warn: false, Detail: ""),
            (Done: adapterOk, Warn: false, Detail: string.IsNullOrWhiteSpace(_selectedBusText.Text) ? "" : " - " + _selectedBusText.Text.Trim()),
            (Done: bridgeOk, Warn: false, Detail: ""),
            (Done: scanOk, Warn: false, Detail: scanOk ? " - " + _lastLinuxBluetoothDevices.Count + " visible" : ""),
            (Done: selectionOk, Warn: false, Detail: selectionOk ? " - " + selectedDevices.Count + " selected" : ""),
            (Done: inputOk, Warn: false, Detail: "")
        };

        var completed = 0;
        for (var i = 0; i < stepNames.Length; i++)
        {
            var state = states[i];
            if (state.Done)
            {
                completed++;
            }

            var prefix = state.Done ? "OK" : state.Warn ? "WARN" : "WAIT";
            var label = _wizardStepLabels[i];
            label.Text = $"{prefix} {i + 1}. {stepNames[i]}{state.Detail}";
            label.ForeColor = state.Done
                ? Color.FromArgb(34, 120, 72)
                : state.Warn
                    ? Color.FromArgb(180, 45, 45)
                    : Color.FromArgb(92, 106, 126);
        }

        _wizardProgress.Value = Math.Clamp((int)Math.Round(completed * 100.0 / stepNames.Length), 0, 100);
        _wizardStatusLabel.Text = inputOk
            ? "Input received"
            : selectionOk
                ? "Ready for pair/connect"
                : scanOk
                    ? "Select a Linux device"
                    : bridgeOk
                        ? "Scan for devices"
                        : adapterOk
                            ? "Start the bridge"
                            : "Complete setup";
        _wizardSelectionLabel.Text = selectionOk
            ? "Selected: " + string.Join("   ", selectedDevices.Take(3).Select(device => $"{device.Name} {device.Mac}"))
            : "Selected: none";
    }

    private async Task RunDoctorScanAsync()
    {
        SelectTabIfExists("Doctor");
        await RefreshLinuxBluetoothDevicesAsync(8);
        await RunControllerDoctorAsync();
    }

    private async Task RunControllerDoctorAsync()
    {
        LogUserAction("Controller Doctor requested");
        SelectTabIfExists("Doctor");
        BeginOperationProgress("Controller Doctor", "Checking requirements", 5);
        _doctorList.Items.Clear();
        _doctorDetailsBox.Text = "Running Controller Doctor..." + Environment.NewLine;
        SetDoctorStatus("Checking requirements", 5, Color.FromArgb(45, 91, 150));

        var details = new List<string>
        {
            "Controller Doctor",
            "Created: " + DateTimeOffset.Now.ToString("o"),
            "Mode: " + (IsBluetoothDemoMode() ? "demo Bluetooth" : "real devices"),
            ""
        };

        try
        {
            var checks = await _requirementChecker.RunAsync().ConfigureAwait(true);
            PopulateRequirementLists(checks);
            var missing = checks.Count(check => check.State == CheckState.Missing);
            var warnings = checks.Count(check => check.State == CheckState.Warn);
            AddDoctorRow(
                "Requirements",
                missing > 0 ? CheckState.Missing : warnings > 0 ? CheckState.Warn : CheckState.Ok,
                missing > 0 ? $"{missing} missing, {warnings} warning(s)" : warnings > 0 ? $"{warnings} warning(s)" : "All required pieces found");
            details.Add($"Requirements: missing={missing}, warnings={warnings}");

            SetDoctorStatus("Checking WSL", 18, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading WSL distros", 18);
            await RefreshWslDistrosAsync().ConfigureAwait(true);
            var savedWsl = _native.GetSelectedWslDistro();
            var wslReady = _wslCombo.Items.Count > 1 || !string.IsNullOrWhiteSpace(savedWsl);
            AddDoctorRow(
                "WSL distro",
                wslReady ? CheckState.Ok : CheckState.Warn,
                wslReady ? (string.IsNullOrWhiteSpace(savedWsl) ? "Automatic distro available" : "Saved distro: " + savedWsl) : "No WSL distro resolved yet");
            details.Add($"WSL: saved={EmptyAsNone(savedWsl)}, items={Math.Max(0, _wslCombo.Items.Count - 1)}");

            SetDoctorStatus("Checking Bluetooth adapter", 32, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading USB/IP devices", 32);
            await RefreshUsbipdDevicesAsync().ConfigureAwait(true);
            var adapter = SelectedUsbipdDevice();
            AddDoctorRow(
                "Bluetooth adapter",
                adapter is null ? CheckState.Missing : CheckState.Ok,
                adapter is null ? "No Bluetooth adapter selected or auto-detected" : adapter.Display);
            details.Add("Adapter: " + (adapter is null ? "none" : adapter.Display));

            SetDoctorStatus("Checking Windows Bluetooth", 46, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading Windows Bluetooth devices", 46);
            await RefreshWindowsBluetoothAsync().ConfigureAwait(true);
            var windowsCount = _windowsBluetoothList.Items.Count;
            var windowsOk = _windowsBluetoothList.Items.Cast<ListViewItem>()
                .Count(item => SubItemText(item, 1).Equals("OK", StringComparison.OrdinalIgnoreCase));
            AddDoctorRow(
                "Windows Bluetooth",
                windowsCount == 0 ? CheckState.Warn : windowsOk > 0 ? CheckState.Ok : CheckState.Warn,
                windowsCount == 0 ? "No Windows Bluetooth devices listed" : $"{windowsOk}/{windowsCount} device(s) OK");
            details.Add($"Windows Bluetooth: ok={windowsOk}, total={windowsCount}");

            SetDoctorStatus("Checking Linux visibility", 62, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading Linux Bluetooth devices", 62);
            var linuxDevices = await RefreshLinuxBluetoothDevicesAsync(0, updateProgress: false).ConfigureAwait(true);
            var stadia = linuxDevices.Count(device => device.IsStadia || device.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase));
            var connected = linuxDevices.Count(IsLiveBluetoothConnected);
            var paired = linuxDevices.Count(device => NativeControlServices.IsBluetoothMac(device.Mac) && device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase));
            var lowBattery = linuxDevices.Count(device => device.BatteryPercent is < 10);
            AddDoctorRow(
                "BlueZ devices",
                linuxDevices.Count == 0 ? CheckState.Warn : CheckState.Ok,
                linuxDevices.Count == 0 ? "No Linux Bluetooth devices visible" : $"{linuxDevices.Count} visible, {connected} connected, {paired} paired, {stadia} Stadia");
            AddDoctorRow(
                "Battery",
                lowBattery > 0 ? CheckState.Warn : linuxDevices.Any(device => device.BatteryPercent.HasValue) ? CheckState.Ok : CheckState.Info,
                lowBattery > 0 ? $"{lowBattery} controller(s) below 10%" : linuxDevices.Any(device => device.BatteryPercent.HasValue) ? "Battery data available" : "No battery data yet");
            details.Add($"Linux devices: visible={linuxDevices.Count}, connected={connected}, paired={paired}, stadia={stadia}, lowBattery={lowBattery}");

            SetDoctorStatus("Checking saved profiles", 76, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Loading profiles and macros", 76);
            RefreshProfiles();
            LoadMacroConfig();
            var autoProfiles = _lastProfiles.Count(profile => profile.AutoConnect);
            AddDoctorRow(
                "Profiles",
                _lastProfiles.Count == 0 ? CheckState.Info : CheckState.Ok,
                _lastProfiles.Count == 0 ? "No saved controller profiles yet" : $"{_lastProfiles.Count} profile(s), {autoProfiles} auto-connect");
            details.Add($"Profiles: total={_lastProfiles.Count}, auto={autoProfiles}");

            SetDoctorStatus("Checking input telemetry", 88, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading controller telemetry", 88);
            RefreshControllerTelemetry();
            var activeInput = _lastTelemetrySnapshot?.Controllers.Count(controller => controller.Active || controller.PacketsPerSecond > 0) ?? 0;
            AddDoctorRow(
                "Input telemetry",
                activeInput > 0 ? CheckState.Ok : CheckState.Info,
                activeInput > 0 ? $"{activeInput} pad(s) sending input" : "No input yet; expected until a controller is connected and moved");
            details.Add($"Input telemetry: active={activeInput}");

            RefreshLogs();
            var actionLogReady = File.Exists(_paths.UserActionLog);
            AddDoctorRow(
                "Action log",
                actionLogReady ? CheckState.Ok : CheckState.Info,
                actionLogReady ? "User action trail is available" : "User action trail will appear after interaction");
            details.Add("Action log: " + (actionLogReady ? _paths.UserActionLog : "not created yet"));

            var missingRows = _doctorList.Items.Cast<ListViewItem>().Count(item => SubItemText(item, 1).Equals("MISSING", StringComparison.OrdinalIgnoreCase));
            var warnRows = _doctorList.Items.Cast<ListViewItem>().Count(item => SubItemText(item, 1).Equals("WARN", StringComparison.OrdinalIgnoreCase));
            var infoRows = _doctorList.Items.Cast<ListViewItem>().Count(item => SubItemText(item, 1).Equals("INFO", StringComparison.OrdinalIgnoreCase));
            var finalState = missingRows > 0 ? CheckState.Missing : warnRows > 0 ? CheckState.Warn : CheckState.Ok;
            var finalText = missingRows > 0
                ? $"{missingRows} issue(s) need fixing"
                : warnRows > 0
                    ? $"{warnRows} warning(s), usable but not perfect"
                    : "Ready for controller test";
            SetDoctorStatus(finalText, 100, StateColor(finalState));
            _doctorDetailsBox.Text = string.Join(Environment.NewLine, details) + Environment.NewLine + Environment.NewLine +
                                     $"Summary: missing={missingRows}, warnings={warnRows}, info={infoRows}";
            CompleteOperationProgress("Controller Doctor", finalText);
        }
        catch (Exception ex)
        {
            AddDoctorRow("Doctor run", CheckState.Missing, ex.Message);
            _doctorDetailsBox.Text = string.Join(Environment.NewLine, details) + Environment.NewLine + Environment.NewLine + ex;
            SetDoctorStatus("Doctor failed", 100, Color.FromArgb(180, 45, 45));
            FailOperationProgress("Controller Doctor", "Doctor failed");
            throw;
        }
    }

    private void SetDoctorStatus(string text, int percent, Color color)
    {
        if (_doctorStatusLabel.IsDisposed || _doctorProgress.IsDisposed)
        {
            return;
        }

        _doctorStatusLabel.Text = text;
        _doctorStatusLabel.ForeColor = color;
        _doctorProgress.Value = Math.Clamp(percent, 0, 100);
    }

    private void AddDoctorRow(string step, CheckState state, string details)
    {
        var stateText = state.ToString().ToUpperInvariant();
        var item = new ListViewItem(step)
        {
            ForeColor = StateColor(state),
            ToolTipText = details
        };
        item.SubItems.Add(stateText);
        item.SubItems.Add(details);
        _doctorList.Items.Add(item);
        ResizeDoctorColumns();
    }

    private static bool IsLikelyControllerDevice(LinuxBluetoothDevice device)
    {
        return device.IsStadia ||
               device.Name.Contains("stadia", StringComparison.OrdinalIgnoreCase) ||
               device.Mac.StartsWith("P", StringComparison.OrdinalIgnoreCase);
    }

    private static LinuxBluetoothDevice? FindDashboardDevice(int slot, IReadOnlyList<LinuxBluetoothDevice> devices, ControllerProfile? profile)
    {
        if (profile is not null)
        {
            var byProfile = devices.FirstOrDefault(device => device.Mac.Equals(profile.Mac, StringComparison.OrdinalIgnoreCase));
            if (byProfile is not null)
            {
                return byProfile;
            }
        }

        return devices.Count >= slot ? devices[slot - 1] : null;
    }

    private static string ShortPadName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "Controller" : value.Trim();
        return value.Length <= 28 ? value : value[..25] + "...";
    }

    private static Color DashboardStateColor(string state)
    {
        return state switch
        {
            "Active" or "Connected" => Color.FromArgb(34, 120, 72),
            "Paired" => Color.FromArgb(45, 91, 150),
            "Profile saved" => Color.FromArgb(170, 104, 0),
            _ => Color.FromArgb(92, 106, 126)
        };
    }

    private void RefreshLogs()
    {
        var statusText = LogReader.Tail(_paths.StatusLog, 140);
        var linuxText = LogReader.Tail(_paths.LinuxLog, 180);
        var actionText = LogReader.Tail(_paths.UserActionLog, 160);
        var appDiagnosticsText = LogReader.Tail(_paths.AppDiagnosticsLog, 180);
        _controlStatusLogBox.Text = statusText;
        _dashboardActionLogBox.Text = actionText;
        _statusLogBox.Text = statusText;
        _controlLinuxLogBox.Text = linuxText;
        _linuxLogBox.Text = linuxText;
        _userActionLogBox.Text = actionText;
        _appDiagnosticsLogBox.Text = appDiagnosticsText;
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

    private void LogUserAction(string action, params (string Key, string? Value)[] details)
    {
        try
        {
            var selectedMacs = _native.GetSelectedControllerMacs();
            var context = new List<(string Key, string? Value)>
            {
                ("tab", _tabs.SelectedTab?.Text),
                ("visibleBusId", _selectedBusText.Text),
                ("savedBusId", _native.GetSelectedBluetoothBusId()),
                ("wslSelection", _wslCombo.SelectedItem?.ToString()),
                ("savedWsl", _native.GetSelectedWslDistro()),
                ("selectedControllers", selectedMacs.Count == 0 ? "automatic" : string.Join(",", selectedMacs)),
                ("usbipdSelected", SelectedListText(_usbipdList)),
                ("linuxSelected", SelectedListText(_linuxBluetoothList)),
                ("wizardLinuxSelected", SelectedListText(_wizardLinuxBluetoothList)),
                ("windowsBtSelected", SelectedListText(_windowsBluetoothList)),
                ("profileSelected", SelectedListText(_profilesList))
            };
            context.AddRange(details);
            _actionLogger.Record(action, context.ToArray());
            var diagnosticsContext = new List<(string Key, string? Value)> { ("action", action) };
            diagnosticsContext.AddRange(context);
            AppDiagnosticsLogger.Record("UI_ACTION", diagnosticsContext.ToArray());
        }
        catch
        {
            // User action logging must never block the control flow it is observing.
        }
    }

    private void LogUserSelection(string action, params (string Key, string? Value)[] details)
    {
        if (_suppressSelectionLogging || !Visible)
        {
            return;
        }

        LogUserAction(action, details);
    }

    private static string SelectedListText(ListView list, int maxItems = 3)
    {
        if (list.SelectedItems.Count == 0)
        {
            return "";
        }

        var values = list.SelectedItems
            .Cast<ListViewItem>()
            .Take(maxItems)
            .Select(SelectedItemDisplayText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();
        return values.Length == 0 ? "" : string.Join(",", values);
    }

    private static string SelectedItemDisplayText(ListViewItem item)
    {
        return item.Tag switch
        {
            LinuxBluetoothDevice device => $"{device.Name} {device.Mac}",
            UsbipdDevice device => device.Display,
            WindowsBluetoothDevice device => $"{device.Name} {device.Status}",
            ControllerProfile profile => $"{profile.Name} {profile.Mac}",
            MacroMapping mapping => $"{mapping.Code}={mapping.Shortcut}",
            _ => FirstMeaningfulSubItem(item)
        };
    }

    private static string FirstMeaningfulSubItem(ListViewItem item)
    {
        foreach (ListViewItem.ListViewSubItem subItem in item.SubItems)
        {
            var text = subItem.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text) &&
                !text.Equals("Connected", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("Disconnected", StringComparison.OrdinalIgnoreCase) &&
                !text.Equals("Seen", StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return item.Text.Trim();
    }

    private static string SubItemText(ListViewItem item, int index)
    {
        return item.SubItems.Count > index ? item.SubItems[index].Text.Trim() : "";
    }

    private IReadOnlyList<LinuxBluetoothDevice> SelectedLinuxBluetoothDevices()
    {
        var primary = _tabs.SelectedTab?.Name == "Pairing" ? _wizardLinuxBluetoothList : _linuxBluetoothList;
        var selected = SelectedLinuxBluetoothDevices(primary);
        if (selected.Count > 0)
        {
            return selected;
        }

        var fallback = ReferenceEquals(primary, _wizardLinuxBluetoothList) ? _linuxBluetoothList : _wizardLinuxBluetoothList;
        return SelectedLinuxBluetoothDevices(fallback);
    }

    private static IReadOnlyList<LinuxBluetoothDevice> SelectedLinuxBluetoothDevices(ListView list)
    {
        return list.SelectedItems
            .Cast<ListViewItem>()
            .Select(item => item.Tag as LinuxBluetoothDevice)
            .Where(device => device is not null)
            .Select(device => device!)
            .ToArray();
    }

    private void SelectTabIfExists(string name)
    {
        var page = _tabs.TabPages[name] ??
                   _tabs.TabPages.Cast<TabPage>().FirstOrDefault(tab =>
                       tab.Text.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                       tab.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (page is not null)
        {
            _tabs.SelectedTab = page;
        }
    }

    private void BeginOperationProgress(string title, string detail, int percent = 0)
    {
        SetOperationProgress(title, detail, percent);
    }

    private void SetOperationProgress(string title, string detail, int percent)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => SetOperationProgress(title, detail, percent)));
            return;
        }

        _operationProgress.Style = ProgressBarStyle.Continuous;
        _operationProgress.MarqueeAnimationSpeed = 0;
        _operationProgress.Value = ClampProgress(percent);
        _operationTitleLabel.Text = title;
        _operationDetailLabel.Text = detail;
        ResetOperationDetailColor();
        if (!string.IsNullOrWhiteSpace(detail))
        {
            _statusLabel.Text = detail;
        }
    }

    private void CompleteOperationProgress(string title, string detail)
    {
        SetOperationProgress(title, detail, 100);
    }

    private void FailOperationProgress(string title, string detail)
    {
        SetOperationProgress(title, detail, 100);
        _operationDetailLabel.ForeColor = Color.FromArgb(180, 45, 45);
    }

    private void ResetOperationDetailColor()
    {
        _operationDetailLabel.ForeColor = Color.FromArgb(92, 106, 126);
    }

    private async Task<T> AwaitWithTimedProgressAsync<T>(Task<T> operation, int startPercent, int maxPercent, TimeSpan expectedDuration, string title, string detail)
    {
        using var cancellation = new CancellationTokenSource();
        var animation = AnimateOperationProgressAsync(startPercent, maxPercent, expectedDuration, title, detail, cancellation.Token);
        try
        {
            return await operation.ConfigureAwait(true);
        }
        finally
        {
            cancellation.Cancel();
            try
            {
                await animation.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task AnimateOperationProgressAsync(int startPercent, int maxPercent, TimeSpan expectedDuration, string title, string detail, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.Now;
        var durationMs = Math.Max(1, expectedDuration.TotalMilliseconds);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = DateTimeOffset.Now - started;
            var fraction = Math.Clamp(elapsed.TotalMilliseconds / durationMs, 0, 1);
            var percent = startPercent + (int)Math.Round((maxPercent - startPercent) * fraction);
            var remaining = Math.Max(0, expectedDuration.TotalSeconds - elapsed.TotalSeconds);
            SetOperationProgress(title, $"{detail} - about {remaining:0}s left", Math.Min(maxPercent, percent));
            await Task.Delay(500, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task WaitWithProgressAsync(TimeSpan waitTime, int startPercent, int endPercent, string title, string detail)
    {
        var started = DateTimeOffset.Now;
        var durationMs = Math.Max(1, waitTime.TotalMilliseconds);
        while (DateTimeOffset.Now - started < waitTime)
        {
            var elapsed = DateTimeOffset.Now - started;
            var fraction = Math.Clamp(elapsed.TotalMilliseconds / durationMs, 0, 1);
            var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
            var remaining = Math.Max(0, waitTime.TotalSeconds - elapsed.TotalSeconds);
            SetOperationProgress(title, $"{detail} - {remaining:0}s", Math.Min(endPercent, percent));
            await Task.Delay(500).ConfigureAwait(true);
        }

        SetOperationProgress(title, detail, endPercent);
    }

    private static int ClampProgress(int percent)
    {
        return Math.Clamp(percent, 0, 100);
    }

    private static int StagePercent(int stageIndex, int stageCount)
    {
        return 5 + (int)Math.Round(Math.Clamp(stageIndex, 0, stageCount) * 88.0 / Math.Max(1, stageCount));
    }

    private static string LinuxCommandTitle(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "pair" => "Pairing Linux Bluetooth",
            "connect" => "Connecting Linux Bluetooth",
            "disconnect" => "Disconnecting Linux Bluetooth",
            _ => "Linux Bluetooth command"
        };
    }

    private static string LinuxCommandStageName(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "pair" => "Pairing",
            "connect" => "Connecting",
            "disconnect" => "Disconnecting",
            _ => "Running command"
        };
    }

    private static TimeSpan LinuxCommandExpectedDuration(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "connect" => TimeSpan.FromSeconds(22),
            "disconnect" => TimeSpan.FromSeconds(8),
            _ => TimeSpan.FromSeconds(12)
        };
    }

    private async Task CheckUpdatesAsync()
    {
        LogUserAction("Check updates requested");
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
        LogUserAction("Self-test requested");
        _statusLabel.Text = "Running self-test...";
        var result = await _selfTestService.RunAsync(json: true);
        _diagnosticsBox.Text = result.Text;
        _statusLabel.Text = result.ExitCode == 0 ? "Self-test passed" : $"Self-test exit code {result.ExitCode}";
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task CreateSessionReportAsync()
    {
        LogUserAction("Session report requested");
        var path = await _native.CreateSessionReportAsync();
        _diagnosticsBox.Text = "Session report created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task CreateSupportBundleAsync()
    {
        LogUserAction("Support bundle requested");
        var path = await _native.CreateSupportBundleAsync();
        _diagnosticsBox.Text = "Support bundle created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private void StartBridge()
    {
        LogUserAction("Start bridge requested");
        BeginOperationProgress("Starting bridge", "Saving selected Bluetooth adapter", 6);
        if (!string.IsNullOrWhiteSpace(_selectedBusText.Text))
        {
            try { _native.SaveSelectedBluetoothBusId(_selectedBusText.Text); } catch { }
        }
        RefreshSelectionLabels();
        SetOperationProgress("Starting bridge", "Launching bridge process", 24);
        LaunchSelfCommand("--start-bridge", elevateWhenNeeded: true, "Stadia X start requested. Watch Live Logs for progress.");
        SetOperationProgress("Starting bridge", "Bridge launch requested; waiting for Linux devices", 38);
        _ = RefreshLinuxListAfterBridgeStartAsync();
        _tabs.SelectedTab = _tabs.TabPages["Control"];
    }

    private async Task RefreshLinuxListAfterBridgeStartAsync()
    {
        var waits = new[]
        {
            (Wait: TimeSpan.FromSeconds(4), Before: 38, After: 52),
            (Wait: TimeSpan.FromSeconds(8), Before: 58, After: 72),
            (Wait: TimeSpan.FromSeconds(15), Before: 78, After: 92)
        };

        for (var attempt = 0; attempt < waits.Length; attempt++)
        {
            var wait = waits[attempt];
            await WaitWithProgressAsync(wait.Wait, wait.Before, wait.After, "Starting bridge", $"Waiting for bridge device pass {attempt + 1}");
            if (IsDisposed)
            {
                return;
            }

            try
            {
                SetOperationProgress("Starting bridge", $"Checking Linux Bluetooth devices ({attempt + 1}/{waits.Length})", Math.Min(96, wait.After + 3));
                var devices = await RefreshLinuxBluetoothDevicesAsync(0, updateProgress: false).ConfigureAwait(true);
                if (devices.Count > 0)
                {
                    CompleteOperationProgress("Starting bridge", $"{devices.Count} Linux device(s) visible after bridge start");
                    return;
                }
            }
            catch (Exception ex)
            {
                FailOperationProgress("Starting bridge", "Could not refresh Linux Bluetooth devices");
                MessageBox.Show(ex.Message, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        CompleteOperationProgress("Starting bridge", "Bridge launch requested; no Linux devices visible yet. Press Scan after pairing mode starts.");
    }

    private void UpdateBatteryIndicator(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        var previousIcon = _batteryIndicatorIcon;
        _batteryIndicatorIcon = devices.Count == 0 ? (Icon)_baseIcon.Clone() : CreateBatteryIndicatorIcon(devices);
        Icon = _batteryIndicatorIcon;
        _trayIcon.Icon = _batteryIndicatorIcon;
        previousIcon?.Dispose();

        _trayIcon.Text = BatteryTooltip(devices);
        _batteryStatusLabel.Text = BatteryHeaderText(devices);
        Text = devices.Count == 0 ? "Stadia X" : "Stadia X - " + BatteryShortText(devices);
        AppDiagnosticsLogger.Record(
            "BATTERY_INDICATOR_UPDATED",
            ("count", devices.Count.ToString()),
            ("title", Text),
            ("tooltip", _trayIcon.Text));
    }

    private static string BatteryHeaderText(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        return devices.Count == 0 ? "Battery: --" : "Battery: " + BatteryShortText(devices);
    }

    private static string BatteryShortText(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        return string.Join("  ", devices.Take(4).Select((device, index) =>
        {
            var battery = device.BatteryPercent is null ? "unknown" : device.BatteryPercent + "%";
            var state = BatteryDeviceStateText(device);
            return string.IsNullOrWhiteSpace(state)
                ? $"P{index + 1} {battery}"
                : $"P{index + 1} {battery} {state}";
        }));
    }

    private static string BatteryDeviceStateText(LinuxBluetoothDevice device)
    {
        if (IsLiveBluetoothConnected(device))
        {
            return "on";
        }

        if (IsHistoricalBluetoothSource(device))
        {
            return "seen";
        }

        return device.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase) ? "paired" : "";
    }

    private static string BatteryTooltip(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        if (devices.Count == 0)
        {
            return "Stadia X - no controller battery";
        }

        var text = "Stadia X - " + BatteryShortText(devices);
        return text.Length <= 63 ? text : text[..63];
    }

    private static Icon CreateBatteryIndicatorIcon(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        var known = devices
            .Where(device => device.BatteryPercent.HasValue)
            .Select(device => device.BatteryPercent!.Value)
            .ToArray();
        int? percent = known.Length > 0 ? known.Min() : null;
        var active = devices.Count > 0;
        var backColor = !active
            ? Color.FromArgb(96, 106, 116)
            : percent is null
                ? Color.FromArgb(45, 91, 150)
                : percent <= 30
                    ? Color.FromArgb(190, 55, 55)
                    : percent <= 55
                        ? Color.FromArgb(196, 132, 35)
                        : Color.FromArgb(42, 140, 89);
        var text = !active ? "-" : percent is null ? "?" : percent.Value.ToString();

        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var back = new SolidBrush(backColor);
            using var white = new SolidBrush(Color.White);
            using var border = new Pen(Color.FromArgb(240, 255, 255, 255), 2);
            g.FillEllipse(back, 1, 1, 30, 30);
            g.DrawEllipse(border, 1, 1, 30, 30);

            using var font = new Font("Segoe UI", text.Length > 2 ? 9 : 11, FontStyle.Bold, GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            g.DrawString(text, font, white, (32 - size.Width) / 2f, (32 - size.Height) / 2f - 1);

            if (devices.Count > 1)
            {
                using var countFont = new Font("Segoe UI", 7, FontStyle.Bold, GraphicsUnit.Pixel);
                var count = devices.Count.ToString();
                var countSize = g.MeasureString(count, countFont);
                g.FillEllipse(white, 21, 21, 10, 10);
                using var countBrush = new SolidBrush(backColor);
                g.DrawString(count, countFont, countBrush, 26 - countSize.Width / 2f, 26 - countSize.Height / 2f);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void StopBridge()
    {
        LogUserAction("Stop bridge requested");
        BeginOperationProgress("Stopping bridge", "Requesting bridge stop and Bluetooth restore", 20);
        LaunchSelfCommand("--stop-bridge", elevateWhenNeeded: true, "Stadia X stop requested. Watch Live Logs for progress.");
        UpdateBatteryIndicator(Array.Empty<LinuxBluetoothDevice>());
        HideBatteryOverlay();
        CompleteOperationProgress("Stopping bridge", "Stop requested; watch Live Logs for restore details");
        _tabs.SelectedTab = _tabs.TabPages["Control"];
    }

    private void SaveSelectedBluetoothBusId()
    {
        LogUserAction("Save Bluetooth BUSID requested", ("candidateBusId", _selectedBusText.Text));
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
        LogUserAction("Clear Bluetooth BUSID requested");
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
        LogUserAction("Save WSL distro requested", ("candidateDistro", string.IsNullOrWhiteSpace(name) ? "automatic" : name));
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
        LogUserAction("Use selected Linux controllers requested");
        var macs = SelectedLinuxBluetoothDevices()
            .Where(device => device is not null && NativeControlServices.IsBluetoothMac(device.Mac))
            .Select(device => device.Mac)
            .Take(4)
            .ToArray();
        _native.SaveSelectedControllerMacs(macs);
        RefreshSelectionLabels();
        RefreshPairingWizardStatus();
        _statusLabel.Text = macs.Length == 0 ? "Controller selection returned to automatic" : "Manual controller selection saved";
    }

    private void ClearSelectedLinuxControllers()
    {
        LogUserAction("Clear selected Linux controllers requested");
        _native.SaveSelectedControllerMacs(Array.Empty<string>());
        RefreshSelectionLabels();
        RefreshPairingWizardStatus();
        _statusLabel.Text = "Controller selection returned to automatic";
    }

    private async Task RunLinuxCommandForSelectedAsync(string command)
    {
        LogUserAction("Linux Bluetooth command requested", ("command", command));
        var devices = SelectedLinuxBluetoothDevices();
        if (devices.Count == 0)
        {
            MessageBox.Show("Select one or more Linux Bluetooth devices first.", "Linux Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = new List<string>();
        var validDevices = devices.Where(device => NativeControlServices.IsBluetoothMac(device.Mac)).ToArray();
        var isPairCommand = command.Equals("pair", StringComparison.OrdinalIgnoreCase);
        var stageCount = Math.Max(1, validDevices.Length * (isPairCommand ? 3 : 1) + 1);
        var stageIndex = 0;
        var title = LinuxCommandTitle(command);
        BeginOperationProgress(title, "Preparing selected Linux devices", 4);
        SelectTabIfExists("Diagnostics");

        void ShowDiagnostics(string current)
        {
            var header = new List<string>
            {
                title,
                "",
                "Selected devices:"
            };
            header.AddRange(devices.Select(device => $"- {device.Name} {device.Mac} [{LinuxDeviceStateText(device)} / {LinuxDeviceSourceText(device)}]"));
            if (isPairCommand)
            {
                header.Add("");
                header.Add("Pairing tip: keep the controller in Bluetooth pairing mode until the final refresh shows connected=yes.");
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                header.Add("");
                header.Add("Current step: " + current);
            }

            header.Add("");
            header.AddRange(lines.Where(line => !string.IsNullOrWhiteSpace(line)));
            _diagnosticsBox.Text = string.Join(Environment.NewLine, header);
            _diagnosticsBox.SelectionStart = _diagnosticsBox.TextLength;
            _diagnosticsBox.ScrollToCaret();
        }

        ShowDiagnostics("Preparing command sequence");

        async Task RunStageAsync(LinuxBluetoothDevice device, string serviceCommand, string stageName, TimeSpan expectedDuration)
        {
            stageIndex++;
            var start = StagePercent(stageIndex - 1, stageCount);
            var end = Math.Max(start + 1, StagePercent(stageIndex, stageCount) - 2);
            SetOperationProgress(title, $"{stageName}: {device.Name}", start);
            ShowDiagnostics($"{stageName}: {device.Name} {device.Mac}");
            var result = await AwaitWithTimedProgressAsync(
                _native.RunLinuxBluetoothCommandAsync(device.Mac, serviceCommand),
                start,
                end,
                expectedDuration,
                title,
                $"{stageName}: {device.Mac}").ConfigureAwait(true);
            lines.Add($"== {stageName} {device.Mac} {device.Name} ==");
            if (result.ExitCode != 0)
            {
                lines.Add($"Process exit code: {result.ExitCode}");
            }
            var output = result.Output.Trim();
            var error = result.Error.Trim();
            lines.Add(string.IsNullOrWhiteSpace(output) ? "(no command output)" : output);
            if (!string.IsNullOrWhiteSpace(error))
            {
                lines.Add("stderr:");
                lines.Add(error);
            }
            lines.Add("");
            ShowDiagnostics($"{stageName} finished for {device.Mac}");
        }

        try
        {
            foreach (var device in devices)
            {
                if (!NativeControlServices.IsBluetoothMac(device.Mac))
                {
                    lines.Add($"== {device.Mac} {device.Name} ==");
                    lines.Add("This row comes from the Windows receiver telemetry. Refresh or Scan until BlueZ reports the Bluetooth MAC before using pair/connect commands.");
                    lines.Add("");
                    continue;
                }

                if (isPairCommand)
                {
                    await RunStageAsync(device, "trust", "Trusting", TimeSpan.FromSeconds(5));
                    await RunStageAsync(device, "pair-only", "Pairing", TimeSpan.FromSeconds(22));
                    await RunStageAsync(device, "connect", "Connecting", TimeSpan.FromSeconds(22));
                }
                else
                {
                    await RunStageAsync(device, command, LinuxCommandStageName(command), LinuxCommandExpectedDuration(command));
                }
            }

            SetOperationProgress(title, "Refreshing Linux device list", 94);
            ShowDiagnostics("Refreshing BlueZ state after command");
            var refreshedDevices = await RefreshLinuxBluetoothDevicesAsync(0, updateProgress: false);
            lines.Add("== Final state after refresh ==");
            foreach (var device in validDevices)
            {
                var refreshed = refreshedDevices.FirstOrDefault(current => current.Mac.Equals(device.Mac, StringComparison.OrdinalIgnoreCase));
                if (refreshed is null)
                {
                    lines.Add($"{device.Mac} {device.Name}: not visible after refresh");
                    continue;
                }

                lines.Add($"{refreshed.Mac} {refreshed.Name}: state={LinuxDeviceStateText(refreshed)}, source={LinuxDeviceSourceText(refreshed)}, connected={YesNoText(refreshed.Connected)}, paired={YesNoText(refreshed.Paired)}, trusted={YesNoText(refreshed.Trusted)}");
            }

            var connectedAfter = validDevices.Count(device =>
                refreshedDevices.Any(current => current.Mac.Equals(device.Mac, StringComparison.OrdinalIgnoreCase) && IsLiveBluetoothConnected(current)));
            var pairedAfter = validDevices.Count(device =>
                refreshedDevices.Any(current => current.Mac.Equals(device.Mac, StringComparison.OrdinalIgnoreCase) && current.Paired.Equals("yes", StringComparison.OrdinalIgnoreCase)));

            if (isPairCommand && validDevices.Length > 0 && connectedAfter < validDevices.Length)
            {
                lines.Add("");
                lines.Add("If the device is still not connected, press Scan while the controller is flashing, then try Pair again.");
            }
            ShowDiagnostics("Finished");

            var completionText = validDevices.Length == 0
                ? "No valid BlueZ MAC selected"
                : isPairCommand
                    ? $"Pair finished: {connectedAfter}/{validDevices.Length} connected, {pairedAfter}/{validDevices.Length} paired"
                    : $"{LinuxCommandStageName(command)} finished: {connectedAfter}/{validDevices.Length} connected";
            CompleteOperationProgress(title, completionText);
        }
        catch
        {
            FailOperationProgress(title, $"{LinuxCommandStageName(command)} failed");
            throw;
        }
    }

    private async Task RepairLinuxBluetoothAsync()
    {
        LogUserAction("Linux Bluetooth repair requested");
        BeginOperationProgress("Repairing Linux Bluetooth", "Resetting BlueZ adapter state", 10);
        try
        {
            var result = await AwaitWithTimedProgressAsync(
                _native.RunLinuxBluetoothRepairAsync(),
                15,
                78,
                TimeSpan.FromSeconds(30),
                "Repairing Linux Bluetooth",
                "Running repair commands").ConfigureAwait(true);
            _diagnosticsBox.Text = "Linux Bluetooth repair" + Environment.NewLine + Environment.NewLine + result.Output + Environment.NewLine + result.Error;
            _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
            SetOperationProgress("Repairing Linux Bluetooth", "Refreshing Linux device list", 90);
            await RefreshLinuxBluetoothDevicesAsync(0, updateProgress: false);
            CompleteOperationProgress("Repairing Linux Bluetooth", "Repair completed");
        }
        catch
        {
            FailOperationProgress("Repairing Linux Bluetooth", "Repair failed");
            throw;
        }
    }

    private async Task CreateCapacityReportAsync()
    {
        LogUserAction("Capacity report requested");
        var path = await _native.CreateCapacityReportAsync();
        _diagnosticsBox.Text = "Capacity report created:" + Environment.NewLine + path;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
    }

    private async Task SetSelectedWindowsBluetoothEnabledAsync(bool enabled)
    {
        LogUserAction(enabled ? "Enable Windows Bluetooth requested" : "Disable Windows Bluetooth requested");
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
        LogUserAction("Save controller profile requested", ("name", _profileNameText.Text), ("mac", _profileMacText.Text), ("slot", (_profileSlotCombo.SelectedIndex + 1).ToString()));
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
        LogUserAction("Delete controller profile requested");
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
        LogUserAction("Apply auto profiles requested");
        _native.ApplyAutoConnectProfiles();
        RefreshSelectionLabels();
        _statusLabel.Text = "Auto-connect profiles applied to startup";
    }

    private void UseLinuxSelectedAsProfile()
    {
        LogUserAction("Use Linux selected as profile requested");
        var device = SelectedLinuxBluetoothDevices().FirstOrDefault();
        if (device is null)
        {
            MessageBox.Show("Select a Linux Bluetooth device first.", "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!NativeControlServices.IsBluetoothMac(device.Mac))
        {
            MessageBox.Show("This receiver row does not expose a Bluetooth MAC yet. Use Refresh or Scan until the BlueZ row appears, then save the profile.", "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        LogUserAction("Save macro config requested", ("macroTextLength", _macroBox.TextLength.ToString()));
        _native.SaveMacroText(_macroBox.Text);
        RefreshMacroMappings();
        _statusLabel.Text = "Macro config saved";
    }

    private void ApplyMacroChordToEditor()
    {
        var code = _macroChordCombo.SelectedItem?.ToString();
        var shortcut = _macroShortcutText.Text.Trim();
        LogUserAction("Apply macro chord requested", ("chord", code), ("shortcut", shortcut));
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
        LogUserAction("Launch self command requested", ("argument", argument), ("elevateWhenNeeded", elevateWhenNeeded.ToString()));
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

    private void ShowBatteryOverlay(IReadOnlyList<LinuxBluetoothDevice> devices, bool warning)
    {
        if (_batteryOverlay is null || _batteryOverlay.IsDisposed)
        {
            _batteryOverlay = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                Opacity = 0.44,
                Size = new Size(64, 24)
            };
            _batteryOverlayLabel = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(5, 1, 5, 1),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 7, FontStyle.Bold),
                ForeColor = Color.White
            };
            _batteryOverlay.Controls.Add(_batteryOverlayLabel);
        }

        var critical = devices.Any(device => device.BatteryPercent is < 10);
        _batteryOverlay.Opacity = critical ? 0.58 : 0.44;
        _batteryOverlay.BackColor = Color.FromArgb(8, 18, 30);
        var rows = devices.Take(4).Select((device, index) =>
        {
            var battery = device.BatteryPercent is null ? "?" : device.BatteryPercent + "%";
            return $"P{index + 1} {battery}";
        }).ToArray();
        var firstLine = string.Join("  ", rows.Take(2));
        var secondLine = rows.Length > 2 ? string.Join("  ", rows.Skip(2)) : "";
        _batteryOverlayLabel!.Text = string.IsNullOrWhiteSpace(secondLine)
            ? firstLine
            : firstLine + Environment.NewLine + secondLine;
        _batteryOverlayLabel.ForeColor = critical ? Color.FromArgb(255, 78, 78) : Color.White;
        _batteryOverlay.Size = MeasureBatteryOverlaySize(_batteryOverlayLabel);
        ApplyPillRegion(_batteryOverlay);
        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.FromControl(this).WorkingArea;
        _batteryOverlay.Location = new Point(area.Right - _batteryOverlay.Width - 10, area.Top + 10);
        if (!_batteryOverlay.Visible)
        {
            _batteryOverlay.Show();
        }
        _batteryOverlay.TopMost = false;
        _batteryOverlay.TopMost = true;
        _batteryOverlay.BringToFront();
        AppDiagnosticsLogger.Record(
            "BATTERY_OVERLAY_SHOWN",
            ("warning", warning.ToString()),
            ("critical", critical.ToString()),
            ("size", $"{_batteryOverlay.Width}x{_batteryOverlay.Height}"),
            ("text", _batteryOverlayLabel.Text));
    }

    private void HideBatteryOverlay()
    {
        if (_batteryOverlay is { IsDisposed: false, Visible: true })
        {
            _batteryOverlay.Hide();
        }
    }

    private static void ApplyPillRegion(Form overlay)
    {
        if (overlay.Width <= 0 || overlay.Height <= 0)
        {
            return;
        }

        var radius = Math.Min(overlay.Height, overlay.Width) - 1;
        var bounds = new Rectangle(0, 0, overlay.Width, overlay.Height);
        using var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, radius, radius, 180, 90);
        path.AddArc(bounds.Right - radius, bounds.Top, radius, radius, 270, 90);
        path.AddArc(bounds.Right - radius, bounds.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - radius, radius, radius, 90, 90);
        path.CloseFigure();
        var previous = overlay.Region;
        overlay.Region = new Region(path);
        previous?.Dispose();
    }

    private static GraphicsPath RoundedRectPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        var rect = new Rectangle(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Size MeasureBatteryOverlaySize(Label label)
    {
        var lines = label.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var maxWidth = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => TextRenderer.MeasureText(line, label.Font, Size.Empty, flags).Width)
            .DefaultIfEmpty(48)
            .Max();
        var lineHeight = TextRenderer.MeasureText("P1 100%", label.Font, Size.Empty, flags).Height;
        var width = Math.Clamp(maxWidth + label.Padding.Horizontal + 6, 54, 118);
        var height = lines.Length > 1
            ? Math.Clamp((lineHeight * lines.Length) + label.Padding.Vertical + 4, 32, 40)
            : Math.Clamp(lineHeight + label.Padding.Vertical + 4, 22, 26);
        return new Size(width, height);
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
        return CreatePage(name, name);
    }

    private static TabPage CreatePage(string text, string name)
    {
        return new TabPage(name)
        {
            Text = text,
            Name = name,
            BackColor = Color.FromArgb(248, 250, 252),
            Padding = new Padding(0),
            AutoScroll = true
        };
    }

    private static GroupBox CreateGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold),
            Padding = IsCompactUi() ? new Padding(8) : new Padding(10)
        };
    }

    private Control BuildOperationProgressPanel()
    {
        var group = CreateGroup("Current operation");
        group.Dock = DockStyle.Fill;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = IsCompactUi() ? new Padding(8, 9, 8, 8) : new Padding(10, 12, 10, 10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 24 : 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        group.Controls.Add(layout);

        _operationTitleLabel.Text = "Ready";
        _operationTitleLabel.Dock = DockStyle.Fill;
        _operationTitleLabel.AutoEllipsis = true;
        _operationTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _operationTitleLabel.Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold);
        layout.Controls.Add(_operationTitleLabel, 0, 0);

        _operationProgress.Dock = DockStyle.Fill;
        _operationProgress.Minimum = 0;
        _operationProgress.Maximum = 100;
        _operationProgress.Value = 0;
        _operationProgress.Style = ProgressBarStyle.Continuous;
        layout.Controls.Add(_operationProgress, 0, 1);

        _operationDetailLabel.Text = "No active request";
        _operationDetailLabel.Dock = DockStyle.Fill;
        _operationDetailLabel.AutoEllipsis = true;
        _operationDetailLabel.TextAlign = ContentAlignment.MiddleLeft;
        _operationDetailLabel.Font = new Font("Segoe UI", IsCompactUi() ? 7.75F : 8);
        _operationDetailLabel.ForeColor = Color.FromArgb(92, 106, 126);
        layout.Controls.Add(_operationDetailLabel, 0, 2);

        return group;
    }

    private Control BuildWindowsBluetoothActionsPanel()
    {
        var flow = CreateFullWidthToolbarFlow();
        AddFlowButton(flow, "Refresh", async () => await RefreshWindowsBluetoothAsync());
        AddFlowButton(flow, "Enable", async () => await SetSelectedWindowsBluetoothEnabledAsync(true));
        AddFlowButton(flow, "Disable", async () => await SetSelectedWindowsBluetoothEnabledAsync(false), Color.FromArgb(178, 62, 62), Color.White);
        return flow;
    }

    private Control BuildLinuxBluetoothActionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Padding = IsCompactUi() ? new Padding(8, 5, 8, 4) : new Padding(12, 8, 12, 6)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        panel.Controls.Add(new Label
        {
            Text = "Linux / BlueZ devices",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold),
            AutoEllipsis = true
        }, 0, 0);

        var flow = CreateFullWidthToolbarFlow();
        flow.Padding = new Padding(0);
        ConfigureBatteryOverlayToggle();
        flow.Controls.Add(_batteryOverlayCheck);
        AddFlowButton(flow, "Refresh", async () => await RefreshLinuxBluetoothDevicesAsync(0));
        AddFlowButton(flow, "Scan", async () => await RefreshLinuxBluetoothDevicesAsync(8));
        AddFlowButton(flow, "Use selected", UseSelectedLinuxControllers);
        AddFlowButton(flow, "Automatic", ClearSelectedLinuxControllers);
        AddFlowButton(flow, "Pair", async () => await RunLinuxCommandForSelectedAsync("pair"));
        AddFlowButton(flow, "Connect", async () => await RunLinuxCommandForSelectedAsync("connect"));
        AddFlowButton(flow, "Disconnect", async () => await RunLinuxCommandForSelectedAsync("disconnect"));
        AddFlowButton(flow, "Repair", async () => await RepairLinuxBluetoothAsync());
        AddFlowButton(flow, "Capacity", async () => await CreateCapacityReportAsync());
        panel.Controls.Add(flow, 0, 1);
        return panel;
    }

    private static FlowLayoutPanel CreateFullWidthToolbarFlow()
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = IsCompactUi() ? new Padding(8, 5, 8, 5) : new Padding(12, 8, 12, 8),
            MinimumSize = new Size(0, IsCompactUi() ? 36 : 46)
        };
    }

    private void ConfigureBatteryOverlayToggle()
    {
        _batteryOverlayCheck.Text = "Battery overlay";
        _batteryOverlayCheck.AutoSize = true;
        _batteryOverlayCheck.Height = 36;
        _batteryOverlayCheck.TextAlign = ContentAlignment.MiddleLeft;
        _batteryOverlayCheck.Margin = new Padding(4, 8, 10, 2);
        _batteryOverlayCheck.CheckedChanged += (_, _) =>
        {
            LogUserSelection("Battery overlay toggled", ("enabled", _batteryOverlayCheck.Checked.ToString()));
            if (_batteryOverlayCheck.Checked)
            {
                _ = RunActionWithDialogAsync(() => UpdateBatteryAsync());
            }
            else
            {
                HideBatteryOverlay();
            }
        };
    }

    private static void AddTopPanelControl(Control topPanel, Control control)
    {
        if (topPanel is TableLayoutPanel table && table.GetControlFromPosition(1, 0) is FlowLayoutPanel flow)
        {
            flow.Controls.Add(control);
            flow.Controls.SetChildIndex(control, 0);
        }
    }

    private Control BuildTopPanel(string title, params (string Text, Action Action)[] buttons)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = IsCompactUi() ? 58 : 72,
            MinimumSize = new Size(0, IsCompactUi() ? 52 : 64),
            Padding = IsCompactUi() ? new Padding(10, 7, 10, 7) : new Padding(12, 10, 12, 10),
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
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold)
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(8, 0, 0, 0),
            MinimumSize = new Size(0, IsCompactUi() ? 32 : 40)
        };
        foreach (var button in buttons)
        {
            AddFlowButton(flow, button.Text, button.Action);
        }

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(flow, 1, 0);
        return panel;
    }

    private Control BuildTopPanel(string title, params (string Text, Func<Task> Action)[] buttons)
    {
        return BuildTopPanel(title, buttons.Select(b => (b.Text, Action: new Action(() => { _ = RunLoggedActionWithDialogAsync(b.Text, b.Action); }))).ToArray());
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

    private static void ConfigureLogBox(TextBox box, string text, bool readOnly = true)
    {
        box.Multiline = true;
        box.ReadOnly = readOnly;
        box.ScrollBars = ScrollBars.Both;
        box.WordWrap = false;
        box.Font = new Font("Consolas", IsCompactUi() ? 8.25F : 9);
        box.BackColor = Color.FromArgb(20, 24, 32);
        box.ForeColor = Color.FromArgb(220, 230, 240);
        box.Dock = DockStyle.Fill;
        box.Text = text;
    }

    private void AddActionGridButton(TableLayoutPanel parent, string text, int column, int row, int columnSpan, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, IsCompactUi() ? 30 : 36),
            Margin = new Padding(4),
            BackColor = backColor ?? SystemColors.Control,
            ForeColor = foreColor ?? SystemColors.ControlText,
            UseVisualStyleBackColor = backColor is null
        };
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            action();
        };
        parent.Controls.Add(button, column, row);
        if (columnSpan > 1)
        {
            parent.SetColumnSpan(button, columnSpan);
        }
    }

    private void AddActionGridButton(TableLayoutPanel parent, string text, int column, int row, int columnSpan, Func<Task> action, Color? backColor = null, Color? foreColor = null)
    {
        AddActionGridButton(parent, text, column, row, columnSpan, () => { _ = RunLoggedActionWithDialogAsync(text, action); }, backColor, foreColor);
    }

    private void AddButton(Control parent, string text, int x, int y, int width, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new Button
        {
            Text = text,
            Size = new Size(width, IsCompactUi() ? 30 : 34),
            Location = new Point(x, y),
            BackColor = backColor ?? SystemColors.Control,
            ForeColor = foreColor ?? SystemColors.ControlText
        };
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            action();
        };
        parent.Controls.Add(button);
    }

    private void AddButton(Control parent, string text, int x, int y, int width, Func<Task> action)
    {
        AddButton(parent, text, x, y, width, () => { _ = RunLoggedActionWithDialogAsync(text, action); });
    }

    private void AddButton(TableLayoutPanel parent, string text, int column, int row, Action action)
    {
        var button = new Button { Text = text, Dock = DockStyle.Fill, MinimumSize = new Size(IsCompactUi() ? 116 : 140, IsCompactUi() ? 30 : 34), Margin = new Padding(4) };
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            action();
        };
        parent.Controls.Add(button, column, row);
    }

    private void AddFlowButton(FlowLayoutPanel parent, string text, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(IsCompactUi() ? 92 : 112, IsCompactUi() ? 30 : 36),
            Padding = IsCompactUi() ? new Padding(7, 0, 7, 0) : new Padding(10, 0, 10, 0),
            BackColor = backColor ?? SystemColors.Control,
            ForeColor = foreColor ?? SystemColors.ControlText,
            Margin = new Padding(4, 2, 4, 2),
            UseVisualStyleBackColor = backColor is null
        };
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            action();
        };
        parent.Controls.Add(button);
    }

    private void AddFlowButton(FlowLayoutPanel parent, string text, Func<Task> action)
    {
        AddFlowButton(parent, text, () => { _ = RunLoggedActionWithDialogAsync(text, action); });
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

    private async Task RunLoggedActionWithDialogAsync(string actionName, Func<Task> action)
    {
        LogUserAction($"Async action started: {actionName}");
        await RunActionWithDialogAsync(action).ConfigureAwait(true);
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

    private static Icon LoadApplicationIcon(AppPaths paths)
    {
        var assetIcon = Path.Combine(paths.Root, "assets", "StadiaX.ico");
        if (File.Exists(assetIcon))
        {
            try
            {
                using var icon = new Icon(assetIcon);
                return (Icon)icon.Clone();
            }
            catch
            {
            }
        }

        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
        }
        catch
        {
            return (Icon)SystemIcons.Application.Clone();
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed class ModernTabButton : Control
{
    private bool _hover;
    private bool _isSelected;

    public ModernTabButton()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        Cursor = Cursors.Hand;
        TabStop = false;
        BackColor = Color.Transparent;
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            Invalidate();
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Inflate(-1, -2);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var fillColor = _isSelected
            ? Color.FromArgb(16, 38, 59)
            : _hover
                ? Color.FromArgb(244, 248, 252)
                : Color.FromArgb(233, 239, 246);
        var borderColor = _isSelected
            ? Color.FromArgb(16, 38, 59)
            : _hover
                ? Color.FromArgb(198, 210, 224)
                : Color.FromArgb(226, 234, 243);
        using (var path = CreateRoundedPath(bounds, 9))
        using (var fill = new SolidBrush(fillColor))
        using (var border = new Pen(borderColor, _isSelected ? 1.4F : 1F))
        {
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);
        }

        if (_isSelected)
        {
            var accent = new Rectangle(bounds.Left + 10, bounds.Bottom - 4, Math.Max(10, bounds.Width - 20), 3);
            using var accentPath = CreateRoundedPath(accent, 2);
            using var accentBrush = new SolidBrush(Color.FromArgb(88, 218, 210));
            e.Graphics.FillPath(accentBrush, accentPath);
        }

        using var selectedFont = _isSelected ? new Font(Font, FontStyle.Bold) : null;
        var textColor = _isSelected ? Color.White : Color.FromArgb(56, 67, 82);
        var textBounds = Rectangle.Inflate(bounds, -5, 0);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            selectedFont ?? Font,
            textBounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(1, radius * 2);
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
