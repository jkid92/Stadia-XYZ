using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace StadiaX.ControlCenter;

internal sealed class MainForm : Form
{
    private const double DefaultControllerFullBatteryHours = 8d;
    private static readonly XboxOutputButton[] GuidedMappingSequence =
    [
        XboxOutputButton.A,
        XboxOutputButton.B,
        XboxOutputButton.X,
        XboxOutputButton.Y,
        XboxOutputButton.LeftShoulder,
        XboxOutputButton.RightShoulder,
        XboxOutputButton.Back,
        XboxOutputButton.Start,
        XboxOutputButton.LeftStick,
        XboxOutputButton.RightStick,
        XboxOutputButton.DpadUp,
        XboxOutputButton.DpadDown,
        XboxOutputButton.DpadLeft,
        XboxOutputButton.DpadRight,
        XboxOutputButton.Guide
    ];

    private readonly AppPaths _paths;
    private readonly bool _auditMode;
    private readonly ProcessRunner _runner = new();
    private readonly ReleaseChecker _releaseChecker = new();
    private readonly RequirementChecker _requirementChecker;
    private readonly SelfTestService _selfTestService;
    private readonly NativeControlServices _native;
    private readonly UserActionLogger _actionLogger;
    private readonly UiLocalization _localization = UiLocalization.Current;
    private readonly UpdateService _updateService;
    private int _updateCheckInProgress;
    private int _windowsBluetoothPairingInProgress;
    private int _windowsNativeCapacityMonitorInProgress;

    private readonly Label _statusLabel = new();
    private readonly Label _batteryStatusLabel = new();
    private readonly Label _batteryLabel = new();
    private readonly Label _selectionLabel = new();
    private readonly Label _capacityLabel = new();
    private readonly TextBox _selectedBusText = new();
    private readonly ComboBox _wslCombo = new();
    private readonly ComboBox _languageCombo = new();
    private readonly ListView _firstRunList = new();
    private readonly ListView _setupChecksList = new();
    private readonly ListView _usbipdList = new();
    private readonly ListView _windowsBluetoothList = new();
    private readonly ListView _windowsNativeDeviceList = new();
    private readonly ListView _linuxBluetoothList = new();
    private readonly ListView _wizardLinuxBluetoothList = new();
    private readonly ListView _profilesList = new();
    private readonly ListView _macroList = new();
    private readonly ListView _controllerList = new();
    private readonly ListView _buttonMappingList = new();
    private readonly ControllerVisualizer _controllerVisualizer = new();
    private readonly ComboBox _controllerPadCombo = new();
    private readonly Label _controllerVisualStatusLabel = new();
    private readonly ComboBox _mappingProfileCombo = new();
    private readonly TextBox _mappingProfileNameText = new();
    private readonly ComboBox _mappingInputCombo = new();
    private readonly ComboBox _mappingOutputCombo = new();
    private readonly Label _mappingStatusLabel = new();
    private readonly Label _mappingCompletenessLabel = new();
    private readonly ModernButton _mappingDetectButton = new();
    private readonly ModernButton _mappingMapAllButton = new();
    private readonly ModernButton _mappingSaveAllButton = new();
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
    private readonly TextBox _windowsNativeLogBox = new();
    private readonly TextBox _windowsNativeLogPageBox = new();
    private readonly TextBox _dashboardActionLogBox = new();
    private readonly ListView _doctorList = new();
    private readonly TextBox _doctorDetailsBox = new();
    private readonly Label _doctorStatusLabel = new();
    private readonly ModernProgressBar _doctorProgress = new();
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
    private readonly ModernProgressBar[] _dashboardPadBatteryBars = new ModernProgressBar[4];
    private readonly ModernButton[] _dashboardPadRumbleButtons = new ModernButton[4];
    private readonly Label _wizardStatusLabel = new();
    private readonly Label _wizardSelectionLabel = new();
    private readonly ModernProgressBar _wizardProgress = new();
    private readonly Label[] _wizardStepLabels = new Label[7];
    private readonly Label _windowsNativeStatusLabel = new();
    private readonly Label _windowsNativePhaseLabel = new();
    private readonly ModernProgressBar _windowsNativeProgress = new();
    private readonly Label _operationTitleLabel = new();
    private readonly Label _operationDetailLabel = new();
    private readonly ModernProgressBar _operationProgress = new();
    private readonly Label _linuxBluetoothSummaryLabel = new();
    private readonly TabControl _tabs = new();
    private readonly FlowLayoutPanel _tabNavPanel = new();
    private readonly Dictionary<TabPage, ModernTabButton> _tabButtons = new();
    private readonly System.Windows.Forms.Timer _logTimer = new();
    private readonly System.Windows.Forms.Timer _batteryTimer = new();
    private readonly System.Windows.Forms.Timer _mappingCaptureTimer = new();
    private readonly System.Windows.Forms.Timer _nativeCapacityMonitorTimer = new();
    private readonly ToolTip _controllerToolTip = new();
    private readonly NotifyIcon _trayIcon = new();
    private readonly ImageList _linuxBluetoothRowSizer = new() { ImageSize = new Size(1, 26), ColorDepth = ColorDepth.Depth32Bit };
    private readonly Icon _baseIcon;

    private Form? _batteryOverlay;
    private Label? _batteryOverlayLabel;
    private Icon? _batteryIndicatorIcon;
    private bool _linuxRefreshInProgress;
    private bool _suppressSelectionLogging;
    private IReadOnlyList<LinuxBluetoothDevice> _lastLinuxBluetoothDevices = Array.Empty<LinuxBluetoothDevice>();
    private DateTimeOffset _operationStartedAt = DateTimeOffset.MinValue;
    private DateTime _lastLinuxBluetoothRefreshUtc = DateTime.MinValue;
    private DateTime _nextWindowsNativeBatteryUnavailableLogUtc = DateTime.MinValue;
    private DateTime _nextWindowsNativeCapacityRestartUtc = DateTime.MinValue;
    private IReadOnlyList<WindowsNativeHidDevice> _lastWindowsNativeDevices = Array.Empty<WindowsNativeHidDevice>();
    private IReadOnlyList<ControllerProfile> _lastProfiles = Array.Empty<ControllerProfile>();
    private ControllerTelemetrySnapshot? _lastTelemetrySnapshot;
    private DateTime _lastTelemetryFailureLogUtc = DateTime.MinValue;
    private ControllerMappingConfiguration _mappingConfiguration = ControllerMappingConfiguration.CreateDefault();
    private ControllerButtonMapping _buttonMapping = ControllerButtonMapping.CreateDefault();
    private HashSet<string> _mappingCaptureBaseline = new(StringComparer.OrdinalIgnoreCase);
    private XboxOutputButton _selectedMappingOutput = XboxOutputButton.A;
    private XboxOutputButton? _mappingCaptureTarget;
    private int _mappingGuideIndex = -1;
    private bool _mappingUiUpdating;
    private bool _mappingDirty;
    private bool _mappingCaptureArmed;

    public MainForm(AppPaths paths) : this(paths, auditMode: false)
    {
    }

    internal MainForm(AppPaths paths, bool auditMode)
    {
        _paths = paths;
        _auditMode = auditMode;
        _requirementChecker = new RequirementChecker(paths, _runner);
        _selfTestService = new SelfTestService(paths, _requirementChecker);
        _native = new NativeControlServices(paths, _runner);
        _actionLogger = new UserActionLogger(paths);
        _updateService = new UpdateService(paths, windowsNative: true);
        AppDiagnosticsLogger.Initialize(paths);
        _mappingConfiguration = ControllerButtonMappingStore.LoadConfiguration(
            paths.ControllerMapping,
            warning => AppDiagnosticsLogger.Record("BUTTON_MAPPING_LOAD_WARN", ("error", warning)));
        _buttonMapping = _mappingConfiguration.ActiveMapping;

        Text = "Stadia X";
        _baseIcon = LoadApplicationIcon(paths);
        Icon = (Icon)_baseIcon.Clone();
        var compactUi = IsCompactUi();
        var sizing = CalculateStartupSizing(compactUi);
        MinimumSize = sizing.Minimum;
        Size = sizing.Initial;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Canvas;

        BuildUi();
        AutoScaleDimensions = new SizeF(DisplayLayout.BaseDpi, DisplayLayout.BaseDpi);
        AutoScaleMode = AutoScaleMode.Dpi;
        ApplyHighDpiLayoutGuards();
        ConfigureTimers();
        ConfigureTray();
        ApplyLocalization();

        Shown += async (_, _) =>
        {
            if (_auditMode)
            {
                return;
            }

            try
            {
                LogUserAction("App shown");
                Directory.CreateDirectory(_paths.LogDirectory);
                await RefreshEverythingAsync();
                _logTimer.Start();
                _nativeCapacityMonitorTimer.Start();
                if (_updateService.CanInstallAutomatically)
                {
                    _ = CheckForUpdatesAsync(interactive: false);
                }
            }
            catch (Exception ex)
            {
                var reason = RecordUiFailure("Initial refresh", ex);
                FailOperationProgress("Initial refresh", $"Failed - {reason}");
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
        Shown += (_, _) => EnsureWindowFitsDisplay();
        DpiChanged += (_, _) => BeginInvoke(EnsureWindowFitsDisplay);
        FormClosing += (_, args) =>
        {
            if (!_auditMode && _mappingDirty && args.CloseReason == CloseReason.UserClosing)
            {
                var choice = ShowLocalizedMessage(
                    "Save mapping changes before closing?",
                    "Stadia X",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel)
                {
                    args.Cancel = true;
                    return;
                }
                if (choice == DialogResult.Yes)
                {
                    SaveAllButtonMappings();
                    if (_mappingDirty)
                    {
                        args.Cancel = true;
                        return;
                    }
                }
            }

            LogUserAction("App closing");
            _logTimer.Stop();
            _batteryTimer.Stop();
            _mappingCaptureTimer.Stop();
            _nativeCapacityMonitorTimer.Stop();
            _controllerToolTip.Dispose();
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

    internal static bool IsConstrainedUi()
    {
        var constrained = Environment.GetEnvironmentVariable("STADIAX_UI_CONSTRAINED");
        if (string.Equals(constrained, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(constrained, "0", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return DisplayLayout.IsConstrained(area, DisplayLayout.SystemDpi);
    }

    private static (Size Minimum, Size Initial) CalculateStartupSizing(bool compactUi)
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        return DisplayLayout.CalculateStartupSizing(compactUi, area, DisplayLayout.SystemDpi);
    }

    private void ApplyHighDpiLayoutGuards()
    {
        var constrained = IsConstrainedUi();
        var pageMinimum = constrained
            ? new Size(500, 680)
            : IsCompactUi() ? new Size(680, 500) : new Size(760, 500);
        foreach (TabPage page in _tabs.TabPages)
        {
            page.AutoScroll = true;
            page.AutoScrollMargin = new Size(16, 16);
            foreach (Control child in page.Controls)
            {
                if (child.Dock == DockStyle.Fill && child.MinimumSize.Width == 0 && child.MinimumSize.Height == 0)
                {
                    child.MinimumSize = pageMinimum;
                }
            }
        }
    }

    private void EnsureWindowFitsDisplay()
    {
        if (_auditMode || WindowState != FormWindowState.Normal)
        {
            return;
        }

        var fitted = DisplayLayout.FitWindow(Bounds, MinimumSize, Screen.FromControl(this).WorkingArea);
        MinimumSize = fitted.Minimum;
        if (Bounds != fitted.Bounds)
        {
            Bounds = fitted.Bounds;
        }
    }

    private static bool IsBluetoothDemoMode()
    {
        return string.Equals(Environment.GetEnvironmentVariable("STADIAX_DEMO_BLUETOOTH"), "1", StringComparison.OrdinalIgnoreCase);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Name = "AppHeader",
            Dock = DockStyle.Top,
            Height = IsCompactUi() ? 70 : 78,
            BackColor = UiTheme.HeaderTop
        };

        var title = new Label
        {
            Name = "AppTitleLabel",
            Text = "Stadia X",
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(18, 10)
        };
        header.Controls.Add(title);

        var subtitle = new Label
        {
            Name = "AppSubtitleLabel",
            Text = "Automatic virtual controller",
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.FromArgb(202, 213, 225),
            AutoSize = true,
            Location = new Point(22, 49)
        };
        header.Controls.Add(subtitle);

        _statusLabel.Name = "AppStatusLabel";
        _statusLabel.Text = $"Version {_paths.Version}";
        _statusLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        _statusLabel.ForeColor = Color.White;
        _statusLabel.TextAlign = ContentAlignment.MiddleRight;
        _statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _statusLabel.Size = new Size(520, 30);
        _statusLabel.Location = new Point(Width - 560, 17);
        header.Controls.Add(_statusLabel);

        _batteryStatusLabel.Name = "AppBatteryLabel";
        _batteryStatusLabel.Text = "";
        _batteryStatusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        _batteryStatusLabel.ForeColor = Color.FromArgb(202, 213, 225);
        _batteryStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _batteryStatusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _batteryStatusLabel.Size = new Size(520, 22);
        _batteryStatusLabel.Location = new Point(Width - 560, 47);
        _batteryStatusLabel.Visible = false;
        header.Controls.Add(_batteryStatusLabel);
        return header;
    }

    private Control BuildSidebar()
    {
        var constrained = IsConstrainedUi();
        var left = new Panel
        {
            Name = "AppSidebar",
            Dock = DockStyle.Left,
            Width = constrained ? 248 : 318,
            Padding = IsCompactUi() ? new Padding(10) : new Padding(14)
        };

        var sidebarLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            RowCount = 3
        };
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 214 : IsCompactUi() ? 234 : 246));
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 116 : IsCompactUi() ? 126 : 138));
        sidebarLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(sidebarLayout);

        var actions = new SurfaceGroupBox
        {
            Text = "Controller service",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        sidebarLayout.Controls.Add(actions, 0, 0);

        var actionGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = constrained ? new Padding(8, 10, 8, 8) : IsCompactUi() ? new Padding(10, 14, 10, 10) : new Padding(14, 18, 14, 14)
        };
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 42 : 48));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 42 : 48));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 38 : 44));
        actionGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 38 : 44));
        actions.Controls.Add(actionGrid);

        AddActionGridButton(actionGrid, "Start", 0, 0, 2, StartWindowsNative, Color.FromArgb(45, 125, 90), Color.White);
        AddActionGridButton(actionGrid, "Stop and restore", 0, 1, 2, StopWindowsNative, Color.FromArgb(178, 62, 62), Color.White);
        AddActionGridButton(actionGrid, "Check", 0, 2, 1, async () => await ProbeWindowsNativeAsync());
        AddActionGridButton(actionGrid, "Refresh", 1, 2, 1, async () => await RefreshEverythingAsync());
        AddActionGridButton(actionGrid, "Test input", 0, 3, 1, () => SelectTabIfExists("Controller Mapping"));
        AddActionGridButton(actionGrid, "Logs", 1, 3, 1, () => SelectTabIfExists("Logs"));

        var summary = new TextBox
        {
            Name = "SidebarSummary",
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Canvas,
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9),
            Text = $"Install folder:{Environment.NewLine}{_paths.Root}{Environment.NewLine}{Environment.NewLine}Quick flow:{Environment.NewLine}Connect controller -> Start -> Test input"
        };
        sidebarLayout.Controls.Add(BuildOperationProgressPanel(), 0, 1);
        sidebarLayout.Controls.Add(summary, 0, 2);
        return left;
    }

    private Control BuildTabs()
    {
        var shell = new TableLayoutPanel
        {
            Name = "ContentShell",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = UiTheme.Canvas
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 38 : 42));
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navHost = new Panel
        {
            Name = "TabNavigationHost",
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Canvas,
            Padding = IsCompactUi() ? new Padding(7, 5, 7, 3) : new Padding(9, 6, 9, 4),
            AutoScroll = true
        };
        _tabNavPanel.Dock = DockStyle.None;
        _tabNavPanel.AutoSize = true;
        _tabNavPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _tabNavPanel.MinimumSize = new Size(0, IsCompactUi() ? 30 : 34);
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
        _tabs.TabPages.Add(BuildWindowsNativePage());
        _tabs.TabPages.Add(BuildControllerDoctorPage());
        _tabs.TabPages.Add(BuildProfilesPage());
        _tabs.TabPages.Add(BuildButtonMappingPage());
        _tabs.TabPages.Add(BuildMacrosPage());
        _tabs.TabPages.Add(BuildLogsPage());
        _tabs.TabPages.Add(BuildDiagnosticsPage());
        if (_tabs.TabPages.Count > 0)
        {
            _tabs.SelectedIndex = 0;
        }
        BuildTabNavigation();
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_mappingCaptureArmed && _tabs.SelectedTab?.Name != "Controller Mapping")
            {
                StopButtonMappingCapture(
                    _mappingGuideIndex >= 0 ? "Guided mapping cancelled" : "Input detection cancelled");
            }
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
                Height = IsCompactUi() ? 28 : 31,
                Margin = new Padding(1, 0, 1, 0),
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
        using var font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 8.75F, FontStyle.Bold);
        var measured = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        var scale = Math.Max(1F, DisplayLayout.SystemDpi / (float)DisplayLayout.BaseDpi);
        var padding = (int)Math.Round((IsCompactUi() ? 18 : 22) * scale);
        var minimum = (int)Math.Round((IsCompactUi() ? 52 : 60) * scale);
        return Math.Max(minimum, measured + padding);
    }

    private TabPage BuildDashboardPage()
    {
        var page = CreatePage("Home", "Dashboard");
        var constrained = IsConstrainedUi();
        var minimumLayoutHeight = constrained ? 682 : IsCompactUi() ? 498 : 550;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            Height = minimumLayoutHeight,
            MinimumSize = new Size(0, minimumLayoutHeight)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 194 : IsCompactUi() ? 168 : 184));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 340 : IsCompactUi() ? 182 : 198));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);
        page.ClientSizeChanged += (_, _) => SizeDashboardLayoutToPage(layout);

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
        _dashboardDetailLabel.Text = "Connect a Stadia controller, then press Start. Virtual controller setup is automatic.";
        _dashboardDetailLabel.Dock = DockStyle.Fill;
        _dashboardDetailLabel.AutoEllipsis = true;
        _dashboardDetailLabel.TextAlign = ContentAlignment.TopLeft;
        _dashboardDetailLabel.Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9);
        _dashboardDetailLabel.ForeColor = Color.FromArgb(92, 106, 126);
        statusLayout.Controls.Add(_dashboardStatusLabel, 0, 0);
        statusLayout.Controls.Add(_dashboardDetailLabel, 0, 1);
        overviewLayout.Controls.Add(statusLayout, 0, 0);

        var actionFlow = CreateFullWidthToolbarFlow();
        actionFlow.AutoSize = false;
        actionFlow.Dock = DockStyle.Fill;
        actionFlow.Padding = new Padding(0, 0, 0, 0);
        AddFlowButton(actionFlow, "Start", StartWindowsNative, Color.FromArgb(45, 125, 90), Color.White);
        AddFlowButton(actionFlow, "Stop and restore", StopWindowsNative, Color.FromArgb(178, 62, 62), Color.White);
        AddFlowButton(actionFlow, "Check controllers", async () => await ProbeWindowsNativeAsync());
        AddFlowButton(actionFlow, "Test input", () => SelectTabIfExists("Controller Mapping"));
        AddFlowButton(actionFlow, "Logs", () => SelectTabIfExists("Logs"));
        _batteryOverlayCheck.Checked = true;
        ConfigureBatteryOverlayToggle();
        actionFlow.Controls.Add(_batteryOverlayCheck);
        if (IsCompactUi())
        {
            foreach (var button in actionFlow.Controls.OfType<ModernButton>())
            {
                button.MinimumSize = new Size(78, 28);
                button.Margin = new Padding(2, 1, 2, 1);
            }
        }
        overviewLayout.Controls.Add(actionFlow, 1, 0);
        layout.Controls.Add(overview, 0, 0);

        var cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = constrained ? 2 : 4,
            RowCount = constrained ? 2 : 1,
            Margin = new Padding(0, 10, 0, 0)
        };
        var cardControls = Enumerable.Range(1, 4)
            .Select(BuildDashboardPadCard)
            .ToArray();
        for (var row = 0; row < cards.RowCount; row++)
        {
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / cards.RowCount));
        }
        for (var index = 0; index < cardControls.Length; index++)
        {
            if (index < cards.ColumnCount)
            {
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / cards.ColumnCount));
            }

            var column = constrained ? index % 2 : index;
            var row = constrained ? index / 2 : 0;
            cards.Controls.Add(cardControls[index], column, row);
        }
        var reflowPending = false;
        cards.SizeChanged += (_, _) =>
        {
            if (!cards.IsHandleCreated || cards.IsDisposed || reflowPending)
            {
                return;
            }

            reflowPending = true;
            cards.BeginInvoke(new Action(() =>
            {
                reflowPending = false;
                if (!cards.IsDisposed)
                {
                    ReflowDashboardCards(layout, cards, cardControls);
                }
            }));
        };
        layout.Controls.Add(cards, 0, 1);

        var activity = CreateGroup("Recent user actions");
        activity.Margin = new Padding(0, 10, 0, 0);
        ConfigureLogBox(_dashboardActionLogBox, "User action log not loaded yet.");
        activity.Controls.Add(_dashboardActionLogBox);
        layout.Controls.Add(activity, 0, 2);

        return page;
    }

    private static void ReflowDashboardCards(
        TableLayoutPanel pageLayout,
        TableLayoutPanel cards,
        IReadOnlyList<Control> cardControls)
    {
        if (cards.ClientSize.Width <= 0)
        {
            return;
        }

        var compact = IsCompactUi();
        var hasAuditScale = int.TryParse(
            Environment.GetEnvironmentVariable("STADIAX_UI_RUNTIME_SCALE_PERCENT"),
            out var auditPercent);
        var responsiveScale = hasAuditScale
            ? Math.Clamp(auditPercent, 100, 200) / 100F
            : Math.Max(1F, cards.DeviceDpi / (float)DisplayLayout.BaseDpi);
        var responsiveThreshold = (int)Math.Round((compact ? 800 : 900) * responsiveScale);
        var columns = cards.ClientSize.Width < responsiveThreshold ? 2 : 4;
        var rows = columns == 2 ? 2 : 1;
        if (cards.ColumnCount == columns &&
            cards.RowCount == rows &&
            cards.Controls.Count == cardControls.Count)
        {
            return;
        }

        cards.SuspendLayout();
        cards.Controls.Clear();
        cards.ColumnStyles.Clear();
        cards.RowStyles.Clear();
        cards.ColumnCount = columns;
        cards.RowCount = rows;
        for (var column = 0; column < columns; column++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        }
        for (var row = 0; row < rows; row++)
        {
            cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));
        }
        for (var index = 0; index < cardControls.Count; index++)
        {
            cards.Controls.Add(cardControls[index], index % columns, index / columns);
        }

        var stacked = columns == 2;
        var cardsHeight = stacked
            ? compact ? 340 : 380
            : compact ? 182 : 220;
        var minimumHeight = stacked
            ? compact ? 682 : 722
            : compact ? 498 : 572;
        var auditScale = hasAuditScale
            ? Math.Clamp(auditPercent, 100, 200) / 100F
            : 1F;
        pageLayout.RowStyles[1].Height = (int)Math.Round(cardsHeight * auditScale);
        pageLayout.MinimumSize = new Size(
            0,
            (int)Math.Round(minimumHeight * auditScale));
        SizeDashboardLayoutToPage(pageLayout);
        cards.ResumeLayout(performLayout: true);
    }

    private static void SizeDashboardLayoutToPage(TableLayoutPanel layout)
    {
        var availableHeight = layout.Parent?.ClientSize.Height ?? 0;
        layout.Height = Math.Max(layout.MinimumSize.Height, availableHeight);
    }

    private Control BuildDashboardPadCard(int slot)
    {
        var group = CreateGroup("P" + slot);
        group.Margin = new Padding(4);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = IsCompactUi() ? new Padding(10, 9, 10, 7) : new Padding(12, 11, 12, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 23));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 20 : 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 24 : 28));
        group.Controls.Add(layout);

        var nameLabel = CreateDashboardValueLabel("No profile", IsCompactUi() ? 9.5F : 11, FontStyle.Bold);
        var statusLabel = CreateDashboardValueLabel("Waiting", IsCompactUi() ? 8.25F : 9, FontStyle.Bold, Color.FromArgb(92, 106, 126));
        var batteryLabel = CreateDashboardValueLabel("Virtual pad waiting", IsCompactUi() ? 8.25F : 9);
        var batteryBar = new ModernProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Margin = new Padding(0, 4, 0, 4)
        };
        var packetsLabel = CreateDashboardValueLabel("Input 0.0/s", IsCompactUi() ? 8.25F : 9);
        var macLabel = CreateDashboardValueLabel("Automatic mapping", 8, FontStyle.Regular, Color.FromArgb(92, 106, 126));
        var rumbleButton = CreateDashboardRumbleButton(slot);
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        footer.Controls.Add(macLabel, 0, 0);
        footer.Controls.Add(rumbleButton, 1, 0);

        _dashboardPadNameLabels[slot - 1] = nameLabel;
        _dashboardPadStatusLabels[slot - 1] = statusLabel;
        _dashboardPadBatteryLabels[slot - 1] = batteryLabel;
        _dashboardPadBatteryBars[slot - 1] = batteryBar;
        _dashboardPadPacketsLabels[slot - 1] = packetsLabel;
        _dashboardPadMacLabels[slot - 1] = macLabel;
        _dashboardPadRumbleButtons[slot - 1] = rumbleButton;
        UpdateDashboardRumbleButton(slot);

        layout.Controls.Add(nameLabel, 0, 0);
        layout.Controls.Add(statusLabel, 0, 1);
        layout.Controls.Add(batteryLabel, 0, 2);
        layout.Controls.Add(batteryBar, 0, 3);
        layout.Controls.Add(packetsLabel, 0, 4);
        layout.Controls.Add(footer, 0, 5);
        return group;
    }

    private ModernButton CreateDashboardRumbleButton(int slot)
    {
        var button = new ModernButton
        {
            Name = $"DashboardP{slot}RumbleButton",
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 1, 0, 1),
            Padding = new Padding(2, 0, 2, 0),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", IsCompactUi() ? 7F : 7.5F, FontStyle.Bold),
            AccessibleRole = AccessibleRole.PushButton
        };
        button.Click += (_, _) =>
        {
            LogUserAction("Dashboard rumble toggle clicked", ("pad", $"P{slot}"));
            _ = RunActionWithDialogAsync(
                $"P{slot} rumble",
                () => ToggleDashboardRumbleAsync(slot),
                showDialog: true);
        };
        return button;
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

        _doctorStatusLabel.Text = "Run Doctor to check Windows Native readiness";
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
        AddFlowButton(doctorActions, "Repair", RepairWindowsNative);
        AddFlowButton(doctorActions, "Logs", () => SelectTabIfExists("Logs"));
        AddFlowButton(doctorActions, "Bundle", async () => await CreateSupportBundleAsync());
        summaryLayout.Controls.Add(doctorActions, 0, 2);

        var hint = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = UiTheme.Canvas,
            Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9),
            Text = "Doctor checks the complete native path: Windows Bluetooth, Stadia HID, HidHide isolation, virtual Xbox pads, profiles, battery, vibration, macros, and live input."
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
        AddFlowButton(flow, "Test input", () => SelectTabIfExists("Controller Mapping"));
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

    private TabPage BuildWindowsNativePage()
    {
        var page = CreatePage("Controllers", "Windows Native");
        var constrained = IsConstrainedUi();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, constrained ? 280 : IsCompactUi() ? 230 : 260));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(layout);

        var statusGroup = CreateGroup("Controller connection");
        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(12) };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 34 : 40));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 28 : 34));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 28 : 32));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusGroup.Controls.Add(statusLayout);

        _windowsNativeStatusLabel.Text = "Waiting for controller";
        _windowsNativeStatusLabel.Dock = DockStyle.Fill;
        _windowsNativeStatusLabel.AutoEllipsis = true;
        _windowsNativeStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _windowsNativeStatusLabel.Font = new Font("Segoe UI", IsCompactUi() ? 10.5F : 12, FontStyle.Bold);
        statusLayout.Controls.Add(_windowsNativeStatusLabel, 0, 0);

        _windowsNativeProgress.Dock = DockStyle.Fill;
        _windowsNativeProgress.Minimum = 0;
        _windowsNativeProgress.Maximum = 100;
        _windowsNativeProgress.Value = 0;
        _windowsNativeProgress.Style = ProgressBarStyle.Continuous;
        statusLayout.Controls.Add(_windowsNativeProgress, 0, 1);

        _windowsNativePhaseLabel.Text = "Connect a Stadia controller to continue";
        _windowsNativePhaseLabel.Dock = DockStyle.Fill;
        _windowsNativePhaseLabel.AutoEllipsis = true;
        _windowsNativePhaseLabel.TextAlign = ContentAlignment.MiddleLeft;
        _windowsNativePhaseLabel.Font = new Font("Segoe UI", IsCompactUi() ? 8.25F : 9, FontStyle.Bold);
        _windowsNativePhaseLabel.ForeColor = Color.FromArgb(92, 106, 126);
        statusLayout.Controls.Add(_windowsNativePhaseLabel, 0, 2);

        var actionColumns = constrained ? 2 : 3;
        var actionRows = constrained ? 4 : 3;
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = actionColumns,
            RowCount = actionRows,
            Margin = new Padding(0)
        };
        for (var column = 0; column < actionColumns; column++)
        {
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / actionColumns));
        }
        for (var row = 0; row < actionRows; row++)
        {
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / actionRows));
        }
        if (constrained)
        {
            AddActionGridButton(actions, "Check", 0, 0, 1, async () => await ProbeWindowsNativeAsync());
            AddActionGridButton(actions, "Pair", 1, 0, 1, async () => await PairStadiaBluetoothAsync());
            AddActionGridButton(actions, "Start", 0, 1, 1, StartWindowsNative, Color.FromArgb(45, 125, 90), Color.White);
            AddActionGridButton(actions, "Stop", 1, 1, 1, StopWindowsNative, Color.FromArgb(178, 62, 62), Color.White);
            AddActionGridButton(actions, "Repair", 0, 2, 1, RepairWindowsNative);
            AddActionGridButton(actions, "Test input", 1, 2, 1, () => SelectTabIfExists("Controller Mapping"));
            AddActionGridButton(actions, "Profiles", 0, 3, 1, () => SelectTabIfExists("Profiles"));
            AddActionGridButton(actions, "Macros", 1, 3, 1, () => SelectTabIfExists("Macros"));
        }
        else
        {
            AddActionGridButton(actions, "Check", 0, 0, 1, async () => await ProbeWindowsNativeAsync());
            AddActionGridButton(actions, "Start", 1, 0, 1, StartWindowsNative, Color.FromArgb(45, 125, 90), Color.White);
            AddActionGridButton(actions, "Stop", 2, 0, 1, StopWindowsNative, Color.FromArgb(178, 62, 62), Color.White);
            AddActionGridButton(actions, "Pair", 0, 1, 1, async () => await PairStadiaBluetoothAsync());
            AddActionGridButton(actions, "Repair", 1, 1, 1, RepairWindowsNative);
            AddActionGridButton(actions, "Test input", 2, 1, 1, () => SelectTabIfExists("Controller Mapping"));
            AddActionGridButton(actions, "Profiles", 0, 2, 1, () => SelectTabIfExists("Profiles"));
            AddActionGridButton(actions, "Macros", 1, 2, 1, () => SelectTabIfExists("Macros"));
            AddActionGridButton(actions, "Details", 2, 2, 1, () => OpenFileIfExists(Path.Combine(_paths.LogDirectory, "windows-native-probe.txt")));
        }
        statusLayout.Controls.Add(actions, 0, 3);
        layout.Controls.Add(statusGroup, 0, 0);

        var deviceGroup = CreateGroup("Detected Stadia controllers");
        ConfigureList(_windowsNativeDeviceList, ("Pad", 50), ("Controller", 210), ("Bluetooth", 132), ("Input", 62), ("Protected", 92), ("Battery", 72));
        _windowsNativeDeviceList.ShowItemToolTips = true;
        _windowsNativeDeviceList.Resize += (_, _) => ResizeWindowsNativeColumns();
        _windowsNativeDeviceList.SelectedIndexChanged += (_, _) =>
        {
            if (_windowsNativeDeviceList.SelectedItems.Count > 0)
            {
                LogUserSelection("Windows Native HID selected", ("device", SelectedListText(_windowsNativeDeviceList)));
            }
        };
        deviceGroup.Controls.Add(_windowsNativeDeviceList);
        layout.Controls.Add(deviceGroup, 1, 0);

        var logGroup = CreateGroup("Connection activity");
        ConfigureLogBox(_windowsNativeLogBox, "Connection activity not loaded yet.");
        logGroup.Controls.Add(_windowsNativeLogBox);
        layout.Controls.Add(logGroup, 0, 1);
        layout.SetColumnSpan(logGroup, 2);
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
        var page = CreatePage("Controller profiles", "Profiles");
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
        listGroup.Controls.Add(BuildTopPanel("", ("Refresh", RefreshProfiles), ("Apply order", ApplyAutoProfiles), ("Delete", DeleteSelectedProfile)));
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
        AddButton(panel, "Save profile", 0, 4, SaveProfile, columnSpan: 2);
        AddButton(panel, "Use selected controller", 0, 5, UseWindowsSelectedAsProfile, columnSpan: 2);
        layout.Controls.Add(editor, 1, 0);
        return page;
    }

    private Control BuildControllerLivePanel()
    {
        var liveGroup = CreateGroup("Live controller");
        liveGroup.Margin = new Padding(4, 4, 0, 4);
        var liveLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = IsCompactUi() ? new Padding(6) : new Padding(8)
        };
        liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 34 : 38));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        liveLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 42 : 48));
        liveGroup.Controls.Add(liveLayout);

        var toolbar = new TableLayoutPanel
        {
            Name = "ControllerLiveToolbar",
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0)
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 158 : 180));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 82 : 96));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 48 : 56));
        toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var padSelector = new TableLayoutPanel
        {
            Name = "ControllerPadSelector",
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        padSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 66 : 76));
        padSelector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        padSelector.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        padSelector.Controls.Add(new Label
        {
            Name = "ControllerPadLabel",
            Text = "Controller",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _controllerPadCombo.AccessibleName = "Controller";
        _controllerPadCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _controllerPadCombo.Dock = DockStyle.None;
        _controllerPadCombo.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _controllerPadCombo.Margin = new Padding(4, 0, 0, 0);
        _controllerPadCombo.Items.AddRange(new object[] { "Automatic", "P1", "P2", "P3", "P4" });
        _controllerPadCombo.SelectedIndex = 0;
        _controllerPadCombo.SelectedIndexChanged += (_, _) =>
        {
            LogUserSelection("Controller test pad selected", ("pad", _controllerPadCombo.SelectedItem?.ToString()));
            RefreshControllerTelemetry();
        };
        padSelector.Controls.Add(_controllerPadCombo, 1, 0);
        toolbar.Controls.Add(padSelector, 0, 0);
        AddControllerLiveButton(toolbar, "Vibrate", 2, TestSelectedRumbleAsync);
        AddControllerLiveButton(
            toolbar,
            "Refresh",
            3,
            () =>
            {
                RefreshControllerTelemetry();
                return Task.CompletedTask;
            },
            glyph: "\u21BB");
        liveLayout.Controls.Add(toolbar, 0, 0);

        _controllerVisualizer.Dock = DockStyle.Fill;
        _controllerVisualizer.MinimumSize = new Size(IsCompactUi() ? 300 : 360, IsCompactUi() ? 170 : 200);
        _controllerVisualizer.LoadControllerImage(_paths.ResolveAssetCandidates("StadiaControllerCutout.png").ToArray());
        _controllerVisualizer.MappingTargetSelected += StartMappingCaptureFromVisualizer;
        _controllerToolTip.SetToolTip(
            _controllerVisualizer,
            _localization.Translate("Click a button on the controller image, then press the physical button to assign it"));
        liveLayout.Controls.Add(_controllerVisualizer, 0, 1);

        var statusRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 74 : 90));
        statusRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _controllerVisualStatusLabel.AutoSize = false;
        _controllerVisualStatusLabel.Dock = DockStyle.Fill;
        _controllerVisualStatusLabel.AutoEllipsis = true;
        _controllerVisualStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        statusRow.Controls.Add(_controllerVisualStatusLabel, 0, 0);
        AddControllerLiveButton(
            statusRow,
            "State",
            1,
            () =>
            {
                OpenFileIfExists(_paths.ControllerState);
                return Task.CompletedTask;
            });
        liveLayout.Controls.Add(statusRow, 0, 2);
        return liveGroup;
    }

    private Task TestSelectedRumbleAsync()
    {
        var selectedPad = _controllerPadCombo.SelectedIndex;
        if (selectedPad <= 0)
        {
            selectedPad = _lastTelemetrySnapshot?.Controllers
                .FirstOrDefault(controller => controller.Active)?.Index ?? 1;
        }

        return TestRumbleAsync(Math.Clamp(selectedPad, 1, 4));
    }

    private void AddControllerLiveButton(
        TableLayoutPanel parent,
        string text,
        int column,
        Func<Task> action,
        string? glyph = null)
    {
        var button = new ModernButton
        {
            Name = $"ControllerLive{text}Button",
            Text = glyph ?? text,
            Dock = DockStyle.None,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Height = IsCompactUi() ? 26 : 30,
            MinimumSize = new Size(0, IsCompactUi() ? 26 : 30),
            Margin = new Padding(2, 1, 2, 1),
            Padding = new Padding(3, 0, 3, 0),
            AccessibleName = text,
            AccessibleRole = AccessibleRole.PushButton
        };
        if (glyph is not null)
        {
            button.Font = new Font("Segoe UI Symbol", IsCompactUi() ? 10F : 11F, FontStyle.Bold);
        }
        _controllerToolTip.SetToolTip(button, _localization.Translate(text));
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            _ = RunActionWithDialogAsync(text, action, showDialog: true);
        };
        parent.Controls.Add(button, column, 0);
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
            RowCount = 3,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        page.Controls.Add(layout);

        ConfigureLogBox(_windowsNativeLogPageBox, "Windows Native log not loaded yet.");
        ConfigureLogBox(_userActionLogBox, "User action log not loaded yet.");
        ConfigureLogBox(_appDiagnosticsLogBox, "App diagnostics log not loaded yet.");

        var nativeGroup = CreateGroup("Windows Native timeline");
        nativeGroup.Controls.Add(_windowsNativeLogPageBox);
        layout.Controls.Add(nativeGroup, 0, 0);

        var userGroup = CreateGroup("User actions");
        userGroup.Controls.Add(_userActionLogBox);
        layout.Controls.Add(userGroup, 0, 1);

        var appGroup = CreateGroup("App diagnostics");
        appGroup.Controls.Add(_appDiagnosticsLogBox);
        layout.Controls.Add(appGroup, 0, 2);

        page.Controls.Add(BuildTopPanel("Live logs", ("Refresh", RefreshLogs), ("Open logs", () => Process.Start("explorer.exe", $"\"{_paths.LogDirectory}\""))));
        return page;
    }

    private TabPage BuildDiagnosticsPage()
    {
        var page = CreatePage("Support", "Diagnostics");
        ConfigureLogBox(_diagnosticsBox, "Run self-test, update check, session report, or support bundle to see output here.");
        page.Controls.Add(_diagnosticsBox);
        var topPanel = BuildTopPanel("Diagnostics",
            ("Self-test", async () => await RunSelfTestAsync()),
            ("Probe", async () => await ProbeWindowsNativeAsync()),
            ("Capacity", async () => await CreateWindowsNativeCapacityReportAsync()),
            ("Check updates", async () => await CheckUpdatesAsync()),
            ("Support bundle", async () => await CreateSupportBundleAsync()),
            ("Rollback", async () => await RollbackUpdateAsync()));
        ConfigureLanguageSelector();
        AddTopPanelControl(topPanel, _languageCombo);
        page.Controls.Add(topPanel);
        return page;
    }

    private TabPage BuildButtonMappingPage()
    {
        var page = CreatePage("Mapping + Test", "Controller Mapping");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = IsCompactUi() ? new Padding(10) : new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 76 : 86));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 142 : 158));
        page.Controls.Add(layout);

        var profileGroup = CreateGroup("Mapping profiles");
        var profileLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = IsCompactUi() ? new Padding(8, 5, 8, 5) : new Padding(10, 7, 10, 7)
        };
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 58 : 70));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 158 : 190));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 50 : 62));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        profileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        profileGroup.Controls.Add(profileLayout);

        profileLayout.Controls.Add(CreateMappingFieldLabel("Profile"), 0, 0);
        _mappingProfileCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureMappingFieldControl(_mappingProfileCombo);
        _mappingProfileCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_mappingUiUpdating || _mappingProfileCombo.SelectedItem is not ControllerMappingProfile profile)
            {
                return;
            }

            SelectMappingProfile(profile.Id);
        };
        profileLayout.Controls.Add(_mappingProfileCombo, 1, 0);

        profileLayout.Controls.Add(CreateMappingFieldLabel("Name"), 2, 0);
        ConfigureMappingFieldControl(_mappingProfileNameText);
        _mappingProfileNameText.MaxLength = 64;
        _mappingProfileNameText.TextChanged += (_, _) => StageMappingProfileName();
        profileLayout.Controls.Add(_mappingProfileNameText, 3, 0);

        var profileActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(8, 0, 0, 0)
        };
        AddFlowButton(profileActions, "Duplicate", DuplicateMappingProfile);
        AddFlowButton(profileActions, "Delete", DeleteMappingProfile);
        profileLayout.Controls.Add(profileActions, 4, 0);
        layout.Controls.Add(profileGroup, 0, 0);

        var workArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        workArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        workArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        layout.Controls.Add(workArea, 0, 1);

        var leftStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        workArea.Controls.Add(leftStack, 0, 0);

        var listGroup = CreateGroup("Xbox outputs");
        listGroup.Margin = new Padding(0, 4, 4, 4);
        ConfigureList(_buttonMappingList, ("Xbox output", 108), ("Stadia input", 120), ("State", 54));
        _buttonMappingList.SelectedIndexChanged += (_, _) =>
        {
            if (_buttonMappingList.SelectedItems.Count == 0 ||
                _buttonMappingList.SelectedItems[0].Tag is not XboxOutputDescriptor output)
            {
                return;
            }

            SelectMappingOutput(output.Id, selectList: false);
            LogUserSelection(
                "Button mapping selected",
                ("output", output.Id.ToString()),
                ("input", MappingInputSummary(output.Id)));
        };
        listGroup.Controls.Add(_buttonMappingList);
        leftStack.Controls.Add(listGroup, 0, 0);

        var telemetryGroup = CreateGroup("Connected pads");
        telemetryGroup.Margin = new Padding(0, 4, 4, 4);
        ConfigureList(
            _controllerList,
            ("Pad", 38),
            ("On", 34),
            ("P/s", 46),
            ("Trig", 52),
            ("Pressed", 96));
        _controllerList.SelectedIndexChanged += (_, _) =>
        {
            if (_controllerList.SelectedItems.Count == 0)
            {
                return;
            }

            var padText = _controllerList.SelectedItems[0].Text;
            if (padText.Length == 2 &&
                padText[0] == 'P' &&
                int.TryParse(padText[1..], out var pad) &&
                pad is >= 1 and <= 4)
            {
                _controllerPadCombo.SelectedIndex = pad;
            }
        };
        telemetryGroup.Controls.Add(_controllerList);
        leftStack.Controls.Add(telemetryGroup, 0, 1);

        workArea.Controls.Add(BuildControllerLivePanel(), 1, 0);

        var editorGroup = CreateGroup("Assignment");
        editorGroup.Margin = new Padding(0, 4, 0, 0);
        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = IsCompactUi() ? new Padding(10, 6, 10, 6) : new Padding(14, 8, 14, 8)
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 82 : 102));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, IsCompactUi() ? 82 : 102));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 31 : 35));
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, IsCompactUi() ? 28 : 32));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editorGroup.Controls.Add(editor);

        editor.Controls.Add(CreateMappingFieldLabel("Xbox output"), 0, 0);
        _mappingOutputCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureMappingFieldControl(_mappingOutputCombo);
        _mappingOutputCombo.Items.AddRange(
            ControllerButtonCatalog.Outputs
                .Where(output => output.Id != XboxOutputButton.None)
                .Cast<object>()
                .ToArray());
        _mappingOutputCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_mappingUiUpdating && _mappingOutputCombo.SelectedItem is XboxOutputDescriptor output)
            {
                SelectMappingOutput(output.Id, selectList: true);
            }
        };
        editor.Controls.Add(_mappingOutputCombo, 1, 0);

        editor.Controls.Add(CreateMappingFieldLabel("Stadia input"), 2, 0);
        _mappingInputCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        ConfigureMappingFieldControl(_mappingInputCombo);
        _mappingInputCombo.Items.Add(new MappingInputOption(null, "Not assigned"));
        _mappingInputCombo.Items.AddRange(
            ControllerButtonCatalog.Inputs
                .Select(input => new MappingInputOption(input.Id, input.DisplayName))
                .Cast<object>()
                .ToArray());
        _mappingInputCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!_mappingUiUpdating && _mappingInputCombo.SelectedItem is MappingInputOption input)
            {
                StageMappingAssignment(_selectedMappingOutput, input.Id, detected: false);
            }
        };
        editor.Controls.Add(_mappingInputCombo, 3, 0);

        _mappingCompletenessLabel.Dock = DockStyle.Fill;
        _mappingCompletenessLabel.AutoEllipsis = true;
        _mappingCompletenessLabel.TextAlign = ContentAlignment.MiddleLeft;
        _mappingCompletenessLabel.Font = new Font("Segoe UI", IsCompactUi() ? 7.75f : 8.25f, FontStyle.Bold);
        editor.Controls.Add(_mappingCompletenessLabel, 0, 1);
        editor.SetColumnSpan(_mappingCompletenessLabel, 2);

        _mappingStatusLabel.Text = "Click a button on the controller image, then press the physical button to assign it";
        _mappingStatusLabel.Dock = DockStyle.Fill;
        _mappingStatusLabel.AutoEllipsis = true;
        _mappingStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _mappingStatusLabel.ForeColor = Color.FromArgb(76, 91, 112);
        editor.Controls.Add(_mappingStatusLabel, 2, 1);
        editor.SetColumnSpan(_mappingStatusLabel, 2);

        var actions = CreateFullWidthToolbarFlow();
        actions.Dock = DockStyle.Fill;
        actions.Padding = new Padding(0);
        ConfigureMappingActionButton(_mappingDetectButton, "Record", ToggleButtonMappingCapture);
        actions.Controls.Add(_mappingDetectButton);
        AddFlowButton(actions, "Clear", () => StageMappingAssignment(_selectedMappingOutput, null, detected: false));
        ConfigureMappingActionButton(_mappingMapAllButton, "Map all", ToggleGuidedButtonMapping);
        actions.Controls.Add(_mappingMapAllButton);
        ConfigureMappingActionButton(
            _mappingSaveAllButton,
            "Save all",
            SaveAllButtonMappings,
            Color.FromArgb(35, 116, 85),
            Color.White);
        actions.Controls.Add(_mappingSaveAllButton);
        AddFlowButton(actions, "Revert", ReloadButtonMappings);
        AddFlowButton(actions, "Reset defaults", ResetButtonMapping);
        editor.Controls.Add(actions, 0, 2);
        editor.SetColumnSpan(actions, 4);
        layout.Controls.Add(editorGroup, 0, 2);

        RefreshMappingProfileSelector();
        return page;
    }

    private void RefreshButtonMappingList()
    {
        var selectedInput = _buttonMappingList.SelectedItems.Count > 0 &&
                            _buttonMappingList.SelectedItems[0].Tag is XboxOutputDescriptor selected
            ? selected.Id
            : (XboxOutputButton?)null;

        _buttonMappingList.BeginUpdate();
        try
        {
            _buttonMappingList.Items.Clear();
            foreach (var output in ControllerButtonCatalog.Outputs.Where(output => output.Id != XboxOutputButton.None))
            {
                var inputs = _buttonMapping.InputsFor(output.Id);
                var state = inputs.Count switch
                {
                    0 => "Missing",
                    1 => "OK",
                    _ => "Conflict"
                };
                var item = new ListViewItem(_localization.Translate(output.DisplayName))
                {
                    Tag = output,
                    ForeColor = state switch
                    {
                        "OK" => Color.FromArgb(38, 80, 62),
                        "Missing" => Color.FromArgb(155, 92, 15),
                        _ => Color.FromArgb(178, 45, 45)
                    }
                };
                item.SubItems.Add(MappingInputSummary(output.Id));
                item.SubItems.Add(_localization.Translate(state));
                _buttonMappingList.Items.Add(item);
                if (selectedInput == output.Id)
                {
                    item.Selected = true;
                }
            }
        }
        finally
        {
            _buttonMappingList.EndUpdate();
        }
    }

    private void SelectMappingOutput(XboxOutputButton outputId, bool selectList)
    {
        if (outputId == XboxOutputButton.None)
        {
            return;
        }

        _selectedMappingOutput = outputId;
        _mappingUiUpdating = true;
        try
        {
            var outputIndex = _mappingOutputCombo.Items
                .Cast<XboxOutputDescriptor>()
                .ToList()
                .FindIndex(output => output.Id == outputId);
            if (outputIndex >= 0)
            {
                _mappingOutputCombo.SelectedIndex = outputIndex;
            }

            var assignedInput = _buttonMapping.InputsFor(outputId).FirstOrDefault();
            var hasAssignedInput = _buttonMapping.InputsFor(outputId).Count > 0;
            var inputIndex = _mappingInputCombo.Items
                .Cast<MappingInputOption>()
                .ToList()
                .FindIndex(input => input.Id == (hasAssignedInput ? assignedInput : null));
            _mappingInputCombo.SelectedIndex = Math.Max(0, inputIndex);
            _controllerVisualizer.SelectedOutput = outputId;
        }
        finally
        {
            _mappingUiUpdating = false;
        }

        if (!selectList)
        {
            return;
        }

        var matchingItem = _buttonMappingList.Items.Cast<ListViewItem>()
            .FirstOrDefault(item => item.Tag is XboxOutputDescriptor output && output.Id == outputId);
        if (matchingItem is not null && !matchingItem.Selected)
        {
            _buttonMappingList.SelectedItems.Clear();
            matchingItem.Selected = true;
            matchingItem.EnsureVisible();
        }
    }

    private void StartMappingCaptureFromVisualizer(XboxOutputButton output)
    {
        if (_mappingCaptureArmed)
        {
            StopButtonMappingCapture(
                _mappingGuideIndex >= 0 ? "Guided mapping cancelled" : "Input detection cancelled");
        }

        _mappingGuideIndex = -1;
        ArmButtonMappingCapture(output);
        LogUserSelection(
            "Controller image mapping target selected",
            ("output", output.ToString()),
            ("profile", _mappingConfiguration.ActiveProfile.Name));
        AppDiagnosticsLogger.Record(
            "BUTTON_MAPPING_IMAGE_TARGET_SELECTED",
            ("output", output.ToString()),
            ("profileId", _mappingConfiguration.ActiveProfileId));
    }

    private void StageMappingAssignment(
        XboxOutputButton output,
        ControllerInputButton? input,
        bool detected)
    {
        if (_mappingUiUpdating)
        {
            return;
        }

        try
        {
            var previousOutput = input is null ? XboxOutputButton.None : _buttonMapping[input.Value];
            _buttonMapping = _buttonMapping.AssignOutput(output, input);
            _mappingConfiguration = _mappingConfiguration.ReplaceProfile(
                _mappingConfiguration.ActiveProfileId,
                _mappingConfiguration.ActiveProfile.Name,
                _buttonMapping);
            SetMappingDirty(true);
            RefreshButtonMappingList();
            SelectMappingOutput(output, selectList: true);
            UpdateMappingValidation();

            var outputName = _localization.Translate(ControllerButtonCatalog.Output(output).DisplayName);
            var inputName = input is null
                ? _localization.Translate("Not assigned")
                : _localization.Translate(ControllerButtonCatalog.Input(input.Value).DisplayName);
            _mappingStatusLabel.Text = detected
                ? $"{_localization.Translate("Recorded")}: {outputName} <- {inputName}"
                : $"{outputName} <- {inputName}";
            LogUserAction(
                "Button mapping staged",
                ("profile", _mappingConfiguration.ActiveProfile.Name),
                ("input", input?.ToString() ?? "none"),
                ("output", output.ToString()),
                ("previousOutput", previousOutput.ToString()),
                ("detected", detected.ToString()));
            AppDiagnosticsLogger.Record(
                "BUTTON_MAPPING_STAGED",
                ("profileId", _mappingConfiguration.ActiveProfileId),
                ("input", input?.ToString() ?? "none"),
                ("output", output.ToString()),
                ("detected", detected.ToString()));
        }
        catch (Exception ex)
        {
            var reason = RecordUiFailure("Stage button mapping", ex);
            _mappingStatusLabel.Text = $"Mapping failed: {reason}";
        }
    }

    private void SaveAllButtonMappings()
    {
        try
        {
            CommitMappingProfileName();
            var validation = _buttonMapping.Validate();
            if (!validation.IsComplete)
            {
                var confirm = ShowLocalizedMessage(
                    "This profile has unassigned or conflicting Xbox outputs. Save it anyway?",
                    "Stadia X",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes)
                {
                    return;
                }
            }

            ControllerButtonMappingStore.Save(_paths.ControllerMapping, _mappingConfiguration);
            SetMappingDirty(false);
            RefreshMappingProfileSelector();
            _mappingStatusLabel.Text = _localization.Translate("Mapping saved and active");
            LogUserAction(
                "Button mappings saved",
                ("profile", _mappingConfiguration.ActiveProfile.Name),
                ("profiles", _mappingConfiguration.Profiles.Count.ToString()),
                ("assigned", validation.AssignedOutputCount.ToString()),
                ("total", validation.TotalOutputCount.ToString()));
            AppDiagnosticsLogger.Record(
                "BUTTON_MAPPING_SAVED",
                ("profileId", _mappingConfiguration.ActiveProfileId),
                ("profile", _mappingConfiguration.ActiveProfile.Name),
                ("profiles", _mappingConfiguration.Profiles.Count.ToString()),
                ("path", _paths.ControllerMapping));
        }
        catch (Exception ex)
        {
            var reason = RecordUiFailure("Save button mappings", ex);
            _mappingStatusLabel.Text = $"Save failed: {reason}";
            ShowLocalizedMessage(
                $"The button mapping could not be saved.{Environment.NewLine}{reason}",
                "Stadia X",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ReloadButtonMappings()
    {
        if (_mappingDirty)
        {
            var confirm = ShowLocalizedMessage(
                "Discard all unsaved mapping changes?",
                "Stadia X",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
            {
                return;
            }
        }

        _mappingConfiguration = ControllerButtonMappingStore.LoadConfiguration(
            _paths.ControllerMapping,
            warning => AppDiagnosticsLogger.Record("BUTTON_MAPPING_RELOAD_WARN", ("error", warning)));
        _buttonMapping = _mappingConfiguration.ActiveMapping;
        SetMappingDirty(false);
        RefreshMappingProfileSelector();
        _mappingStatusLabel.Text = _localization.Translate("Saved mapping reloaded");
        LogUserAction("Button mappings reloaded");
    }

    private void ResetButtonMapping()
    {
        var confirm = ShowLocalizedMessage(
            "Reset the current profile to the Stadia/Xbox defaults?",
            "Stadia X",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _buttonMapping = ControllerButtonMapping.CreateDefault();
            _mappingConfiguration = _mappingConfiguration.ReplaceProfile(
                _mappingConfiguration.ActiveProfileId,
                _mappingConfiguration.ActiveProfile.Name,
                _buttonMapping);
            SetMappingDirty(true);
            RefreshButtonMappingList();
            SelectMappingOutput(XboxOutputButton.A, selectList: true);
            UpdateMappingValidation();
            _mappingStatusLabel.Text = _localization.Translate("Default mapping restored");
            LogUserAction(
                "Button mapping reset to defaults",
                ("profile", _mappingConfiguration.ActiveProfile.Name));
            AppDiagnosticsLogger.Record(
                "BUTTON_MAPPING_DEFAULTS_RESTORED",
                ("profileId", _mappingConfiguration.ActiveProfileId));
        }
        catch (Exception ex)
        {
            var reason = RecordUiFailure("Reset button mapping", ex);
            _mappingStatusLabel.Text = $"Reset failed: {reason}";
        }
    }

    private void RefreshMappingProfileSelector()
    {
        _mappingUiUpdating = true;
        try
        {
            _mappingProfileCombo.Items.Clear();
            _mappingProfileCombo.Items.AddRange(_mappingConfiguration.Profiles.Cast<object>().ToArray());
            var activeIndex = _mappingConfiguration.Profiles
                .Select((profile, index) => (profile, index))
                .First(pair => pair.profile.Id.Equals(
                    _mappingConfiguration.ActiveProfileId,
                    StringComparison.OrdinalIgnoreCase))
                .index;
            _mappingProfileCombo.SelectedIndex = activeIndex;
            _mappingProfileNameText.Text = _mappingConfiguration.ActiveProfile.Name;
            _buttonMapping = _mappingConfiguration.ActiveMapping;
        }
        finally
        {
            _mappingUiUpdating = false;
        }

        RefreshButtonMappingList();
        SelectMappingOutput(_selectedMappingOutput, selectList: true);
        UpdateMappingValidation();
    }

    private void SelectMappingProfile(string profileId)
    {
        try
        {
            CommitMappingProfileName();
            _mappingConfiguration = _mappingConfiguration.WithActiveProfile(profileId);
            _buttonMapping = _mappingConfiguration.ActiveMapping;
            SetMappingDirty(true);
            RefreshMappingProfileSelector();
            _mappingStatusLabel.Text = $"{_localization.Translate("Active profile")}: {_mappingConfiguration.ActiveProfile.Name}";
            LogUserSelection(
                "Mapping profile selected",
                ("profileId", profileId),
                ("profile", _mappingConfiguration.ActiveProfile.Name));
        }
        catch (Exception ex)
        {
            _mappingStatusLabel.Text = $"Profile selection failed: {RecordUiFailure("Select mapping profile", ex)}";
        }
    }

    private void StageMappingProfileName()
    {
        if (_mappingUiUpdating || string.IsNullOrWhiteSpace(_mappingProfileNameText.Text))
        {
            return;
        }

        try
        {
            _mappingConfiguration = _mappingConfiguration.ReplaceProfile(
                _mappingConfiguration.ActiveProfileId,
                _mappingProfileNameText.Text,
                _buttonMapping);
            SetMappingDirty(true);
        }
        catch (ArgumentException)
        {
            // The validation message is shown when the user saves.
        }
    }

    private void CommitMappingProfileName()
    {
        if (string.IsNullOrWhiteSpace(_mappingProfileNameText.Text))
        {
            throw new InvalidOperationException(_localization.Translate("Profile name cannot be empty"));
        }

        _mappingConfiguration = _mappingConfiguration.ReplaceProfile(
            _mappingConfiguration.ActiveProfileId,
            _mappingProfileNameText.Text,
            _buttonMapping);
    }

    private void DuplicateMappingProfile()
    {
        try
        {
            CommitMappingProfileName();
            var baseName = _localization.IsItalian ? "Profilo" : "Profile";
            var existingNames = _mappingConfiguration.Profiles
                .Select(profile => profile.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var index = 2;
            var name = $"{baseName} {index}";
            while (existingNames.Contains(name))
            {
                name = $"{baseName} {++index}";
            }

            _mappingConfiguration = _mappingConfiguration.AddProfile(name, _buttonMapping);
            _buttonMapping = _mappingConfiguration.ActiveMapping;
            SetMappingDirty(true);
            RefreshMappingProfileSelector();
            _mappingProfileNameText.SelectAll();
            _mappingProfileNameText.Focus();
            _mappingStatusLabel.Text = _localization.Translate("Profile duplicated");
            LogUserAction(
                "Mapping profile duplicated",
                ("profileId", _mappingConfiguration.ActiveProfileId),
                ("profile", name));
        }
        catch (Exception ex)
        {
            _mappingStatusLabel.Text = $"Profile duplication failed: {RecordUiFailure("Duplicate mapping profile", ex)}";
        }
    }

    private void DeleteMappingProfile()
    {
        if (_mappingConfiguration.Profiles.Count == 1)
        {
            _mappingStatusLabel.Text = _localization.Translate("At least one mapping profile is required");
            return;
        }

        var profile = _mappingConfiguration.ActiveProfile;
        var confirm = ShowLocalizedMessage(
            _localization.IsItalian
                ? $"Eliminare il profilo di mappatura '{profile.Name}'?"
                : $"Delete mapping profile '{profile.Name}'?",
            "Stadia X",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _mappingConfiguration = _mappingConfiguration.RemoveProfile(profile.Id);
            _buttonMapping = _mappingConfiguration.ActiveMapping;
            SetMappingDirty(true);
            RefreshMappingProfileSelector();
            _mappingStatusLabel.Text = _localization.Translate("Profile deleted");
            LogUserAction("Mapping profile deleted", ("profileId", profile.Id), ("profile", profile.Name));
        }
        catch (Exception ex)
        {
            _mappingStatusLabel.Text = $"Profile deletion failed: {RecordUiFailure("Delete mapping profile", ex)}";
        }
    }

    private void SetMappingDirty(bool dirty)
    {
        _mappingDirty = dirty;
        _mappingSaveAllButton.Text = _localization.Translate(dirty ? "Save all changes" : "Save all");
        _mappingSaveAllButton.BackColor = dirty ? Color.FromArgb(35, 116, 85) : Color.FromArgb(72, 88, 104);
        _mappingSaveAllButton.Invalidate();
    }

    private void UpdateMappingValidation()
    {
        var validation = _buttonMapping.Validate();
        var unusedInputs = ControllerButtonCatalog.Inputs.Count(input =>
            _buttonMapping[input.Id] == XboxOutputButton.None);
        _mappingCompletenessLabel.Text =
            $"{validation.AssignedOutputCount}/{validation.TotalOutputCount} {_localization.Translate("Xbox outputs assigned")}  ·  " +
            $"{unusedInputs} {_localization.Translate("unused Stadia inputs")}";
        _mappingCompletenessLabel.ForeColor = validation.IsComplete
            ? Color.FromArgb(26, 118, 78)
            : Color.FromArgb(166, 91, 10);
    }

    private string MappingInputSummary(XboxOutputButton output)
    {
        var inputs = _buttonMapping.InputsFor(output);
        return inputs.Count == 0
            ? _localization.Translate("Not assigned")
            : string.Join(
                ", ",
                inputs.Select(input => _localization.Translate(ControllerButtonCatalog.Input(input).DisplayName)));
    }

    private void ToggleButtonMappingCapture()
    {
        if (_mappingCaptureArmed)
        {
            StopButtonMappingCapture("Input detection cancelled");
            return;
        }

        _mappingGuideIndex = -1;
        ArmButtonMappingCapture(_selectedMappingOutput);
    }

    private void ToggleGuidedButtonMapping()
    {
        if (_mappingCaptureArmed && _mappingGuideIndex >= 0)
        {
            StopButtonMappingCapture("Guided mapping cancelled");
            return;
        }
        if (_mappingCaptureArmed)
        {
            StopButtonMappingCapture("Input detection cancelled");
        }

        _mappingGuideIndex = 0;
        ArmButtonMappingCapture(GuidedMappingSequence[_mappingGuideIndex]);
        _mappingMapAllButton.Text = _localization.Translate("Cancel guided mapping");
        _mappingStatusLabel.Text = GuidedMappingPrompt();
        AppDiagnosticsLogger.Record(
            "BUTTON_MAPPING_GUIDE_STARTED",
            ("profileId", _mappingConfiguration.ActiveProfileId),
            ("steps", GuidedMappingSequence.Length.ToString()));
    }

    private void ArmButtonMappingCapture(XboxOutputButton output)
    {
        try
        {
            _mappingCaptureBaseline = PressedTelemetryKeys(_native.ReadControllerTelemetry());
        }
        catch
        {
            _mappingCaptureBaseline.Clear();
        }

        _mappingCaptureTarget = output;
        _mappingCaptureArmed = true;
        _controllerVisualizer.AwaitingMappingInput = true;
        _mappingDetectButton.Text = _localization.Translate(
            _mappingGuideIndex >= 0 ? "Recording guided mapping" : "Cancel detection");
        _mappingDetectButton.Enabled = _mappingGuideIndex < 0;
        SelectMappingOutput(output, selectList: true);
        if (_mappingGuideIndex < 0)
        {
            var outputName = _localization.Translate(ControllerButtonCatalog.Output(output).DisplayName);
            _mappingStatusLabel.Text =
                $"{_localization.Translate("Press the physical controller button for")} {outputName}";
        }
        _mappingCaptureTimer.Start();
        AppDiagnosticsLogger.Record(
            "BUTTON_MAPPING_CAPTURE_ARMED",
            ("output", output.ToString()),
            ("guided", (_mappingGuideIndex >= 0).ToString()));
    }

    private void RefreshButtonMappingCapture()
    {
        if (!_mappingCaptureArmed)
        {
            return;
        }

        try
        {
            UpdateButtonMappingCapture(_native.ReadControllerTelemetry());
        }
        catch (Exception ex)
        {
            _mappingStatusLabel.Text = $"Waiting for controller: {ex.Message}";
        }
    }

    private void UpdateButtonMappingCapture(ControllerTelemetrySnapshot snapshot)
    {
        var pressed = PressedTelemetryKeys(snapshot);
        if (!_mappingCaptureArmed)
        {
            return;
        }

        var detectedKey = pressed.FirstOrDefault(key => !_mappingCaptureBaseline.Contains(key));
        _mappingCaptureBaseline.IntersectWith(pressed);
        if (detectedKey is null)
        {
            return;
        }

        var input = ControllerButtonCatalog.FindInput(detectedKey);
        if (input is null)
        {
            return;
        }

        var target = _mappingCaptureTarget ?? _selectedMappingOutput;
        StageMappingAssignment(target, input.Id, detected: true);
        LogUserSelection(
            "Button mapping input detected",
            ("input", input.Id.ToString()),
            ("output", target.ToString()),
            ("guidedStep", _mappingGuideIndex.ToString()));
        AppDiagnosticsLogger.Record(
            "BUTTON_MAPPING_INPUT_DETECTED",
            ("input", input.Id.ToString()),
            ("output", target.ToString()),
            ("telemetryKey", input.TelemetryKey),
            ("guidedStep", _mappingGuideIndex.ToString()));

        if (_mappingGuideIndex < 0)
        {
            StopButtonMappingCapture(
                $"{_localization.Translate("Recorded")}: " +
                $"{_localization.Translate(ControllerButtonCatalog.Output(target).DisplayName)} <- " +
                $"{_localization.Translate(input.DisplayName)}  ·  " +
                _localization.Translate("Save all changes"));
            return;
        }

        _mappingCaptureBaseline = pressed;
        _mappingGuideIndex++;
        if (_mappingGuideIndex >= GuidedMappingSequence.Length)
        {
            StopButtonMappingCapture("Guided mapping complete. Save all changes.");
            AppDiagnosticsLogger.Record(
                "BUTTON_MAPPING_GUIDE_COMPLETED",
                ("profileId", _mappingConfiguration.ActiveProfileId));
            return;
        }

        _mappingCaptureTarget = GuidedMappingSequence[_mappingGuideIndex];
        SelectMappingOutput(_mappingCaptureTarget.Value, selectList: true);
        _mappingStatusLabel.Text = GuidedMappingPrompt();
    }

    private void StopButtonMappingCapture(string status)
    {
        _mappingCaptureArmed = false;
        _mappingCaptureTimer.Stop();
        _mappingCaptureBaseline.Clear();
        _mappingCaptureTarget = null;
        _mappingGuideIndex = -1;
        _controllerVisualizer.AwaitingMappingInput = false;
        _mappingDetectButton.Enabled = true;
        _mappingDetectButton.Text = _localization.Translate("Record");
        _mappingMapAllButton.Text = _localization.Translate("Map all");
        _mappingStatusLabel.Text = _localization.Translate(status);
    }

    private string GuidedMappingPrompt()
    {
        var output = GuidedMappingSequence[Math.Clamp(_mappingGuideIndex, 0, GuidedMappingSequence.Length - 1)];
        var outputName = _localization.Translate(ControllerButtonCatalog.Output(output).DisplayName);
        return $"{_localization.Translate("Step")} {_mappingGuideIndex + 1}/{GuidedMappingSequence.Length}: " +
               $"{_localization.Translate("press the Stadia input for")} {outputName}";
    }

    private static HashSet<string> PressedTelemetryKeys(ControllerTelemetrySnapshot snapshot)
    {
        return snapshot.Controllers
            .SelectMany(controller => controller.Buttons)
            .Where(pair => pair.Value)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static Label CreateMappingFieldLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", IsCompactUi() ? 7.75f : 8.25f, FontStyle.Bold)
        };
    }

    private static void ConfigureMappingFieldControl(Control control)
    {
        control.Dock = DockStyle.None;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        control.Margin = new Padding(4, 0, 4, 0);
    }

    private void ConfigureMappingActionButton(
        ModernButton button,
        string text,
        Action action,
        Color? backColor = null,
        Color? foreColor = null)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(IsCompactUi() ? 92 : 108, IsCompactUi() ? 30 : 36);
        button.Padding = IsCompactUi() ? new Padding(7, 0, 7, 0) : new Padding(10, 0, 10, 0);
        button.Margin = new Padding(4, 2, 4, 2);
        button.BackColor = backColor ?? SystemColors.Control;
        button.ForeColor = foreColor ?? SystemColors.ControlText;
        button.UseVisualStyleBackColor = backColor is null;
        button.Click += (_, _) =>
        {
            LogUserAction($"Button clicked: {text}");
            action();
        };
    }

    private sealed record MappingInputOption(ControllerInputButton? Id, string DisplayName)
    {
        public override string ToString() => UiLocalization.Current.Translate(DisplayName);
    }

    private void ConfigureLanguageSelector()
    {
        _languageCombo.Name = "LanguageSelector";
        _languageCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageCombo.Width = IsCompactUi() ? 94 : 110;
        _languageCombo.AccessibleName = "Language";
        _languageCombo.Items.Clear();
        _languageCombo.Items.AddRange(new object[] { "Italiano", "English" });
        _languageCombo.SelectedIndex = _localization.IsItalian ? 0 : 1;
        _languageCombo.SelectedIndexChanged += (_, _) =>
        {
            if (_languageCombo.SelectedIndex < 0)
            {
                return;
            }

            var languageCode = _languageCombo.SelectedIndex == 0 ? "it" : "en";
            if (_localization.LanguageCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _localization.SetLanguage(languageCode);
            ApplyLocalization();
            LogUserSelection("Language changed", ("language", languageCode));
        };
    }

    private void ApplyLocalization()
    {
        _localization.Apply(this);
        _localization.Apply(_trayIcon.ContextMenuStrip);
        if (_controllerPadCombo.Items.Count > 0)
        {
            _controllerPadCombo.Items[0] = _localization.Translate("Automatic");
        }
        _controllerToolTip.SetToolTip(
            _controllerVisualizer,
            _localization.Translate("Click a button on the controller image, then press the physical button to assign it"));
        if (_buttonMappingList.Columns.Count > 0)
        {
            RefreshButtonMappingList();
            UpdateMappingValidation();
            SetMappingDirty(_mappingDirty);
            _mappingInputCombo.Refresh();
            _mappingOutputCombo.Refresh();
        }
        foreach (var pair in _tabButtons)
        {
            pair.Value.AccessibleName = pair.Value.Text;
            pair.Value.Width = ModernTabWidth(pair.Value.Text);
        }

        var expectedLanguageIndex = _localization.IsItalian ? 0 : 1;
        if (_languageCombo.Items.Count > 0 && _languageCombo.SelectedIndex != expectedLanguageIndex)
        {
            _languageCombo.SelectedIndex = expectedLanguageIndex;
        }
    }

    private DialogResult ShowLocalizedMessage(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        return MessageBox.Show(this, _localization.Translate(text), _localization.Translate(caption), buttons, icon);
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
        _batteryTimer.Tick += (_, _) => { _ = RunActionWithDialogAsync("Battery refresh", () => UpdateBatteryAsync(), showDialog: false); };

        _mappingCaptureTimer.Interval = 75;
        _mappingCaptureTimer.Tick += (_, _) => RefreshButtonMappingCapture();

        _nativeCapacityMonitorTimer.Interval = 8000;
        _nativeCapacityMonitorTimer.Tick += (_, _) =>
        {
            _ = RunActionWithDialogAsync(
                "Windows Native capacity monitor",
                MonitorWindowsNativeCapacityAsync,
                showDialog: false);
        };
    }

    private void ConfigureTray()
    {
        _trayIcon.Icon = (Icon)_baseIcon.Clone();
        _trayIcon.Text = "Stadia X";
        _trayIcon.Visible = true;
        _trayIcon.ContextMenuStrip = new ContextMenuStrip();
        _trayIcon.ContextMenuStrip.Items.Add("Show", null, (_, _) => { LogUserAction("Tray show"); Show(); WindowState = FormWindowState.Normal; Activate(); });
        _trayIcon.ContextMenuStrip.Items.Add("Start native", null, (_, _) => { LogUserAction("Tray start Windows Native"); StartWindowsNative(); });
        _trayIcon.ContextMenuStrip.Items.Add("Stop native", null, (_, _) => { LogUserAction("Tray stop Windows Native"); StopWindowsNative(); });
        _trayIcon.ContextMenuStrip.Items.Add("Exit", null, (_, _) => { LogUserAction("Tray exit"); Close(); });
        _trayIcon.DoubleClick += (_, _) => { LogUserAction("Tray double-click show"); Show(); WindowState = FormWindowState.Normal; Activate(); };
    }

    private async Task RefreshEverythingAsync()
    {
        LogUserAction("Refresh all requested");
        BeginOperationProgress("Refreshing app state", "Checking requirements", 5);
        _statusLabel.Text = "Refreshing Stadia X state...";
        await RefreshChecksAsync();
        SetOperationProgress("Refreshing app state", "Reading Windows Native HID devices", 35);
        await RefreshWindowsNativeDevicesAsync(updateOperationProgress: false);
        SetOperationProgress("Refreshing app state", "Reading controller telemetry", 62);
        RefreshControllerTelemetry();
        SetOperationProgress("Refreshing app state", "Loading logs", 78);
        RefreshLogs();
        RefreshSelectionLabels();
        RefreshDashboardUi();
        var readyText = IsBluetoothDemoMode() ? $"Ready demo Windows Native - {_paths.Version}" : $"Windows Native ready - {_paths.Version}";
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

    private async Task ProbeWindowsNativeAsync()
    {
        LogUserAction("Windows Native probe requested");
        BeginOperationProgress("Windows Native probe", "Checking HidHide", 6);
        SetWindowsNativeStatus("Checking connected controllers", 12, warn: false);

        var runner = new ProcessRunner();
        var hidHide = new HidHideManager(_paths, runner);
        var scanner = new WindowsNativeHidScanner(hidHide);
        var devices = await RefreshWindowsNativeDevicesAsync(scanner, updateOperationProgress: false).ConfigureAwait(true);

        SetOperationProgress("Windows Native probe", "Capturing HID reports", 34);
        SetWindowsNativeStatus(devices.Count == 0 ? "No Stadia controller detected" : $"Checking {devices.Count} controller(s)", 48, devices.Count == 0);
        var report = await AwaitWithTimedProgressAsync(
            scanner.CreateProbeReportAsync(TimeSpan.FromSeconds(8)),
            42,
            92,
            TimeSpan.FromSeconds(9),
            "Windows Native probe",
            "Reading raw HID input").ConfigureAwait(true);

        var reportPath = Path.Combine(_paths.LogDirectory, "windows-native-probe.txt");
        Directory.CreateDirectory(_paths.LogDirectory);
        await File.WriteAllTextAsync(reportPath, report).ConfigureAwait(true);
        _diagnosticsBox.Text = report;
        _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
        RefreshLogs();

        if (devices.Count == 0)
        {
            FailOperationProgress("Windows Native probe", "Not ready - no Stadia HID controller visible");
            SetWindowsNativeStatus("No controller found - pair it in Windows, then check again", 100, warn: true);
            return;
        }

        CompleteOperationProgress("Windows Native probe", $"{devices.Count} Stadia HID device(s) visible");
        SetWindowsNativeStatus($"{devices.Count} Stadia controller(s) detected", 100, warn: false);
    }

    private async Task PairStadiaBluetoothAsync()
    {
        if (Interlocked.CompareExchange(ref _windowsBluetoothPairingInProgress, 1, 0) != 0)
        {
            WarnOperationProgress(
                "Stadia Bluetooth pairing",
                "A Stadia Bluetooth search is already running",
                _operationProgress.Value);
            return;
        }

        LogUserAction("Automatic Stadia Bluetooth pairing requested");
        BeginOperationProgress("Stadia Bluetooth pairing", "Checking the Windows Bluetooth radio", 8);
        SetWindowsNativeStatus("Searching for Stadia Bluetooth controllers", 10, warn: false);
        var status = new StatusWriter(_paths, "windows-native.log");
        status.Write("WINDOWS_NATIVE_BLUETOOTH_MANUAL_START", "Automatic Stadia Bluetooth discovery requested from the app");

        try
        {
            var pairing = await new WindowsStadiaBluetoothPairingService().DiscoverAndPairAsync(
                4,
                progress =>
                {
                    status.Write(
                        "WINDOWS_NATIVE_BLUETOOTH_MANUAL_PROGRESS",
                        $"stage={progress.Stage} percent={progress.Percent} detail={progress.Detail}");
                    if (!IsDisposed && IsHandleCreated)
                    {
                        try
                        {
                            BeginInvoke(new Action(() =>
                            {
                                SetOperationProgress("Stadia Bluetooth pairing", progress.Detail, progress.Percent);
                                SetWindowsNativeStatus(progress.Detail, progress.Percent, warn: false);
                            }));
                        }
                        catch (InvalidOperationException)
                        {
                            // The form closed while the native inquiry was finishing.
                        }
                    }
                },
                parentWindow: Handle).ConfigureAwait(true);

            foreach (var attempt in pairing.Attempts)
            {
                status.Write(
                    attempt.Outcome is StadiaBluetoothPairingOutcome.Paired or StadiaBluetoothPairingOutcome.AlreadyPaired
                        ? "WINDOWS_NATIVE_BLUETOOTH_MANUAL_OK"
                        : "WINDOWS_NATIVE_BLUETOOTH_MANUAL_FAILED",
                    $"{attempt.Device.Name} {attempt.Device.Address}: {attempt.Outcome} - {attempt.Detail}");
            }

            if (!pairing.BluetoothAvailable)
            {
                FailOperationProgress("Stadia Bluetooth pairing", "Windows Bluetooth is unavailable or disabled");
                SetWindowsNativeStatus("Bluetooth unavailable - check the Windows radio", 100, warn: true);
                RefreshLogs();
                return;
            }

            if (pairing.Devices.Count == 0)
            {
                FailOperationProgress(
                    "Stadia Bluetooth pairing",
                    "No Stadia controller found - put it in Bluetooth pairing mode and try again");
                SetWindowsNativeStatus("No Stadia controller found in pairing mode", 100, warn: true);
                RefreshLogs();
                return;
            }

            SetOperationProgress("Stadia Bluetooth pairing", "Waiting for Windows to expose the controller input", 94);
            IReadOnlyList<WindowsNativeHidDevice> hidDevices = Array.Empty<WindowsNativeHidDevice>();
            for (var attempt = 0; attempt < 6 && hidDevices.Count == 0; attempt++)
            {
                if (attempt > 0)
                {
                    await Task.Delay(1500).ConfigureAwait(true);
                }
                hidDevices = await RefreshWindowsNativeDevicesAsync(updateOperationProgress: false).ConfigureAwait(true);
            }

            if (hidDevices.Count == 0)
            {
                WarnOperationProgress(
                    "Stadia Bluetooth pairing",
                    "Pairing completed; starting Windows Native while waiting for controller input",
                    100);
                SetWindowsNativeStatus("Paired - starting controller input", 100, warn: false);
                StartWindowsNative();
            }
            else
            {
                CompleteOperationProgress(
                    "Stadia Bluetooth pairing",
                    $"{hidDevices.Count} Stadia controller(s) ready - starting automatically");
                SetWindowsNativeStatus($"{hidDevices.Count} Stadia controller(s) ready - starting", 100, warn: false);
                StartWindowsNative();
            }
            RefreshLogs();
        }
        catch (OperationCanceledException)
        {
            FailOperationProgress("Stadia Bluetooth pairing", "Bluetooth pairing cancelled");
            SetWindowsNativeStatus("Bluetooth pairing cancelled", 100, warn: true);
        }
        catch (Exception ex)
        {
            var reason = RecordUiFailure("Automatic Stadia Bluetooth pairing", ex);
            status.Write("WINDOWS_NATIVE_BLUETOOTH_MANUAL_FAILED", reason);
            FailOperationProgress("Stadia Bluetooth pairing", "Bluetooth pairing failed - check the log");
            SetWindowsNativeStatus("Bluetooth pairing failed", 100, warn: true);
        }
        finally
        {
            Volatile.Write(ref _windowsBluetoothPairingInProgress, 0);
        }
    }

    private async Task<IReadOnlyList<WindowsNativeHidDevice>> RefreshWindowsNativeDevicesAsync(
        WindowsNativeHidScanner? scanner = null,
        bool updateOperationProgress = true)
    {
        if (updateOperationProgress)
        {
            BeginOperationProgress("Windows Native HID", "Scanning Windows HID devices", 10);
        }

        scanner ??= new WindowsNativeHidScanner(new HidHideManager(_paths, new ProcessRunner()));
        var devices = _native.OrderWindowsNativeDevices(
            await scanner.FindStadiaControllerInventoryAsync().ConfigureAwait(true));
        _lastWindowsNativeDevices = devices;
        PopulateWindowsNativeDevices(devices);
        ResizeWindowsNativeColumns();

        if (devices.Count == 0)
        {
            SetWindowsNativeStatus("No Stadia controller detected", 100, warn: true);
            if (updateOperationProgress)
            {
                FailOperationProgress("Windows Native HID", "No Stadia HID controller visible");
            }
        }
        else
        {
            var hidden = devices.Count(device => !string.IsNullOrWhiteSpace(device.DeviceInstancePath));
            var capacityMismatch = WindowsNativeRuntime.TryGetActiveReceiver(_paths, out var receiverPid, out var activeSlots) &&
                                   devices.Count > activeSlots;
            if (capacityMismatch)
            {
                SetWindowsNativeStatus(
                    $"{devices.Count} controller(s) detected - additional slots will activate automatically",
                    100,
                    warn: true);
                AppDiagnosticsLogger.Record(
                    "WINDOWS_NATIVE_CAPACITY_MISMATCH",
                    ("receiverPid", receiverPid.ToString()),
                    ("activeSlots", activeSlots.ToString()),
                    ("visibleControllers", devices.Count.ToString()),
                    ("missingSlots", string.Join(",", Enumerable.Range(activeSlots + 1, devices.Count - activeSlots).Select(slot => "P" + slot))));
            }
            else
            {
                SetWindowsNativeStatus(
                    hidden < devices.Count
                        ? $"{devices.Count} controller(s) detected - input protection incomplete"
                        : $"{devices.Count} controller(s) ready",
                    100,
                    warn: hidden < devices.Count);
            }
            if (updateOperationProgress)
            {
                if (capacityMismatch)
                {
                    FailOperationProgress("Windows Native HID", $"Restart required to activate {devices.Count - activeSlots} additional controller(s)");
                }
                else
                {
                    CompleteOperationProgress("Windows Native HID", $"{devices.Count} Stadia HID device(s) visible");
                }
            }
        }

        return devices;
    }

    private async Task MonitorWindowsNativeCapacityAsync()
    {
        if (Interlocked.CompareExchange(ref _windowsNativeCapacityMonitorInProgress, 1, 0) != 0)
        {
            return;
        }

        try
        {
            if (!WindowsNativeRuntime.TryGetActiveReceiver(
                    _paths,
                    out var receiverPid,
                    out var activeSlots))
            {
                return;
            }

            var scanner = new WindowsNativeHidScanner(new HidHideManager(_paths, new ProcessRunner()));
            var devices = _native.OrderWindowsNativeDevices(
                await scanner.FindStadiaControllerInventoryAsync().ConfigureAwait(true));
            _lastWindowsNativeDevices = devices;
            PopulateWindowsNativeDevices(devices);
            ResizeWindowsNativeColumns();
            RefreshDashboardUi();

            if (devices.Count <= activeSlots ||
                DateTime.UtcNow < _nextWindowsNativeCapacityRestartUtc)
            {
                return;
            }

            _nextWindowsNativeCapacityRestartUtc = DateTime.UtcNow.AddSeconds(45);
            LogUserAction(
                "Automatic multi-controller expansion requested",
                ("receiverPid", receiverPid.ToString()),
                ("activeSlots", activeSlots.ToString()),
                ("detectedControllers", devices.Count.ToString()));
            new StatusWriter(_paths, "windows-native.log").Write(
                "WINDOWS_NATIVE_CAPACITY_AUTO_EXPAND",
                $"Additional Stadia controller detected; requesting automatic slot expansion {activeSlots}->{devices.Count}");
            BeginOperationProgress(
                "Adding Stadia controller",
                $"Expanding virtual pads from {activeSlots} to {devices.Count}",
                22);
            LaunchSelfCommand(
                "--start-windows-native",
                elevateWhenNeeded: true,
                "Additional Stadia controller detected. Expanding Windows Native automatically.");
            _ = RefreshWindowsNativeAfterStartAsync();
        }
        finally
        {
            Volatile.Write(ref _windowsNativeCapacityMonitorInProgress, 0);
        }
    }

    private void PopulateWindowsNativeDevices(IReadOnlyList<WindowsNativeHidDevice> devices)
    {
        _windowsNativeDeviceList.Items.Clear();
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            var hidHideState = string.IsNullOrWhiteSpace(device.DeviceInstancePath) ? "missing" : "matched";
            var item = new ListViewItem("P" + (i + 1))
            {
                Tag = device,
                ForeColor = hidHideState == "matched" ? Color.FromArgb(34, 120, 72) : Color.FromArgb(180, 45, 45),
                ToolTipText = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        string.IsNullOrWhiteSpace(device.BluetoothAddress) ? null : "Bluetooth: " + device.BluetoothAddress,
                        $"VID/PID: {device.VendorId:X4}:{device.ProductId:X4}",
                        device.FileSystemName
                    }.Where(value => !string.IsNullOrWhiteSpace(value)))
            };
            item.SubItems.Add(string.IsNullOrWhiteSpace(device.FriendlyName) ? device.ProductName : device.FriendlyName);
            item.SubItems.Add(string.IsNullOrWhiteSpace(device.BluetoothAddress)
                ? $"{device.VendorId:X4}:{device.ProductId:X4}"
                : device.BluetoothAddress);
            item.SubItems.Add(device.MaxInputReportLength > 0 ? device.MaxInputReportLength.ToString() : "hidden");
            item.SubItems.Add(hidHideState);
            item.SubItems.Add(device.BatteryPercent.HasValue ? device.BatteryPercent + "%" : "-");
            _windowsNativeDeviceList.Items.Add(item);
        }
    }

    private void SetWindowsNativeStatus(string text, int percent, bool warn)
    {
        _windowsNativeStatusLabel.Text = text;
        _windowsNativeStatusLabel.ForeColor = warn ? Color.FromArgb(180, 45, 45) : Color.FromArgb(24, 33, 48);
        _windowsNativeProgress.Value = ClampProgress(percent);
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
        return device.BatteryPercent is null ? "-" : BatteryPercentWithRuntime(device.BatteryPercent);
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

    private void ResizeWindowsNativeColumns()
    {
        if (_windowsNativeDeviceList.Columns.Count < 6 || _windowsNativeDeviceList.ClientSize.Width <= 0)
        {
            return;
        }

        var available = Math.Max(420, _windowsNativeDeviceList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 10);
        var padWidth = 42;
        var addressWidth = Math.Clamp((int)Math.Round(available * 0.22), 104, 140);
        var inputWidth = 52;
        var hideWidth = 74;
        var batteryWidth = 64;
        var nameWidth = Math.Max(130, available - padWidth - addressWidth - inputWidth - hideWidth - batteryWidth);
        _windowsNativeDeviceList.Columns[0].Width = padWidth;
        _windowsNativeDeviceList.Columns[1].Width = nameWidth;
        _windowsNativeDeviceList.Columns[2].Width = addressWidth;
        _windowsNativeDeviceList.Columns[3].Width = inputWidth;
        _windowsNativeDeviceList.Columns[4].Width = hideWidth;
        _windowsNativeDeviceList.Columns[5].Width = batteryWidth;
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
        return NativeControlServices.IsControllerTelemetryFileFresh(_paths.ControllerState);
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
        var nativeReceiverActive = WindowsNativeRuntime.TryGetActiveReceiver(
            _paths,
            out var nativeReceiverPid,
            out var nativeControllerCount);

        if (knownDevices is null)
        {
            try
            {
                var scanner = new WindowsNativeHidScanner(new HidHideManager(_paths, new ProcessRunner()));
                _lastWindowsNativeDevices = _native.OrderWindowsNativeDevices(
                    await scanner.FindStadiaControllerInventoryAsync().ConfigureAwait(true));
                PopulateWindowsNativeDevices(_lastWindowsNativeDevices);
                ResizeWindowsNativeColumns();
            }
            catch (Exception ex)
            {
                _lastWindowsNativeDevices = Array.Empty<WindowsNativeHidDevice>();
                AppDiagnosticsLogger.Record(
                    "WINDOWS_NATIVE_BATTERY_SCAN_WARN",
                    ("error", ex.ToString()));
            }

            knownDevices = _lastWindowsNativeDevices.Select(device => new LinuxBluetoothDevice(
                device.DeviceInstancePath,
                string.IsNullOrWhiteSpace(device.FriendlyName) ? device.ProductName : device.FriendlyName,
                "yes",
                "yes",
                "yes",
                device.BatteryPercent,
                true,
                string.IsNullOrWhiteSpace(device.BatterySource) ? "Windows PnP" : device.BatterySource)).ToArray();
            usedKnownDevices = true;
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
            if (nativeReceiverActive)
            {
                _batteryLabel.Text = $"Battery: Windows has not exposed a level for {nativeControllerCount} controller(s) yet.";
                HideBatteryOverlay();
                if (DateTime.UtcNow >= _nextWindowsNativeBatteryUnavailableLogUtc)
                {
                    _nextWindowsNativeBatteryUnavailableLogUtc = DateTime.UtcNow.AddMinutes(1);
                    AppDiagnosticsLogger.Record(
                        _batteryOverlayCheck.Checked
                            ? "WINDOWS_NATIVE_BATTERY_OVERLAY_UNAVAILABLE"
                            : "WINDOWS_NATIVE_BATTERY_UNAVAILABLE",
                        ("receiverPid", nativeReceiverPid.ToString()),
                        ("controllers", nativeControllerCount.ToString()),
                        ("battery", "unavailable"),
                        ("overlay", "unavailable"),
                        ("reason", "Windows PnP battery property is not available for the connected controller"));
                }
                RefreshDashboardUi();
                return;
            }

            _batteryLabel.Text = "Battery: not available yet. Connect a controller and start Windows Native.";
            HideBatteryOverlay();
            AppDiagnosticsLogger.Record("BATTERY_REFRESH_EMPTY", ("visibleCount", knownDevices.Count.ToString()));
            RefreshDashboardUi();
            return;
        }

        _batteryLabel.Text = "Battery: " + string.Join("   ", stadia.Select((d, i) =>
        {
            var battery = BatteryPercentWithRuntime(d.BatteryPercent);
            var state = BatteryDeviceStateText(d);
            return string.IsNullOrWhiteSpace(state) ? $"P{i + 1} {battery}" : $"P{i + 1} {battery} ({state})";
        }));
        var batteryKnown = stadia.Where(d => d.BatteryPercent.HasValue).ToArray();
        var low = batteryKnown.Where(d => d.BatteryPercent is <= 30).ToArray();
        if (_batteryOverlayCheck.Checked && batteryKnown.Length > 0)
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
            if (_lastTelemetryFailureLogUtc <= DateTime.UtcNow - TimeSpan.FromSeconds(30))
            {
                _lastTelemetryFailureLogUtc = DateTime.UtcNow;
                AppDiagnosticsLogger.Record(
                    "CONTROLLER_TELEMETRY_READ_FAILED",
                    ("exceptionType", ex.GetType().FullName),
                    ("error", ex.ToString()));
            }
            RefreshDashboardUi();
            RefreshPairingWizardStatus();
            return;
        }

        _lastTelemetrySnapshot = snapshot;
        UpdateButtonMappingCapture(snapshot);
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
            selected = snapshot.Controllers.FirstOrDefault(controller =>
                controller.Index == _controllerPadCombo.SelectedIndex &&
                controller.Active);
        }
        else
        {
            selected = snapshot.Controllers.FirstOrDefault(controller => controller.Active);
        }

        if (selected is null)
        {
            var stale = File.Exists(_paths.ControllerState) &&
                        !NativeControlServices.IsControllerTelemetryFileFresh(_paths.ControllerState);
            var detailText = stale
                ? $"{_localization.Translate("Controller telemetry is stale. Last update")} {snapshot.ReadAt.ToLocalTime():HH:mm:ss}."
                : _localization.Translate("No controller telemetry yet. Start Windows Native and press a button.");
            var statusText = stale
                ? $"{_localization.Translate("Data not updated")} - {snapshot.ReadAt.ToLocalTime():HH:mm:ss}"
                : _localization.Translate("No active controller. Press a button.");
            _controllerVisualizer.SetTelemetry(null, detailText);
            _controllerVisualStatusLabel.Text = statusText;
            _controllerToolTip.SetToolTip(_controllerVisualStatusLabel, detailText);
            return;
        }

        var pressed = selected.Buttons.Where(pair => pair.Value).Select(pair => pair.Key.ToUpperInvariant()).ToArray();
        var status = $"P{selected.Index}  |  {selected.PacketsPerSecond:0.0} p/s  |  LT/RT {selected.TriggerLeft}/{selected.TriggerRight}  |  " +
                     $"{_localization.Translate("Pressed")}: {(pressed.Length == 0 ? "-" : string.Join(", ", pressed))}";
        _controllerVisualizer.SetTelemetry(selected, status);
        _controllerVisualStatusLabel.Text = status;
        _controllerToolTip.SetToolTip(_controllerVisualStatusLabel, status);
    }

    private void RefreshDashboardUi()
    {
        if (_dashboardPadNameLabels[0] is null)
        {
            return;
        }

        var controllers = _lastTelemetrySnapshot?.Controllers ?? Array.Empty<ControllerTelemetryRow>();
        var stadiaDevices = _lastWindowsNativeDevices.ToArray();

        var activeCount = controllers.Count(controller => controller.Active || controller.PacketsPerSecond > 0);
        var hiddenCount = stadiaDevices.Count(device => !string.IsNullOrWhiteSpace(device.DeviceInstancePath));

        _dashboardStatusLabel.Text = activeCount > 0
            ? $"{activeCount} controller(s) sending input"
            : stadiaDevices.Length > 0
                ? $"{stadiaDevices.Length} Stadia controller(s) detected"
                : "No Stadia controller detected";
        _dashboardDetailLabel.Text =
            $"Detected {stadiaDevices.Length} - protected {hiddenCount} - last input {(_lastTelemetrySnapshot is null ? "not received" : _lastTelemetrySnapshot.ReadAt.ToLocalTime().ToString("HH:mm:ss"))}";

        for (var slot = 1; slot <= 4; slot++)
        {
            var device = stadiaDevices.ElementAtOrDefault(slot - 1);
            var controller = controllers.FirstOrDefault(item => item.Index == slot);
            var hasInput = controller is not null && (controller.Active || controller.PacketsPerSecond > 0);
            var state = hasInput
                ? "Active"
                : device is not null && !string.IsNullOrWhiteSpace(device.DeviceInstancePath)
                    ? "Ready"
                    : device is not null
                        ? "Detected"
                        : "Waiting";

            var profile = device is null || string.IsNullOrWhiteSpace(device.BluetoothAddress)
                ? null
                : _lastProfiles.FirstOrDefault(item =>
                    item.Mac.Equals(device.BluetoothAddress, StringComparison.OrdinalIgnoreCase));
            _dashboardPadNameLabels[slot - 1].Text = ShortPadName(
                profile?.Name ?? WindowsNativeDisplayName(device) ?? "Pad P" + slot);
            _dashboardPadStatusLabels[slot - 1].Text = state;
            _dashboardPadStatusLabels[slot - 1].ForeColor = DashboardStateColor(state);
            _dashboardPadBatteryLabels[slot - 1].Text = device?.BatteryPercent is int batteryPercent
                ? $"Battery {batteryPercent}%"
                : state switch
                {
                    "Active" => "Virtual pad active",
                    "Ready" => "Input protected",
                    "Detected" => "Ready to start",
                    _ => "Virtual pad waiting"
                };
            _dashboardPadBatteryBars[slot - 1].Value = device?.BatteryPercent is int percent
                ? percent
                : hasInput
                    ? Math.Clamp((int)Math.Round((controller?.PacketsPerSecond ?? 0) * 8), 8, 100)
                    : state == "Ready"
                        ? 20
                        : 0;
            _dashboardPadBatteryBars[slot - 1].ForeColor = device?.BatteryPercent switch
            {
                < 10 => Color.FromArgb(211, 52, 52),
                < 25 => Color.FromArgb(221, 140, 36),
                _ => UiTheme.Accent
            };
            _dashboardPadPacketsLabels[slot - 1].Text = "Input " + (controller?.PacketsPerSecond ?? 0).ToString("0.0") + "/s";
            _dashboardPadMacLabels[slot - 1].Text = device is null
                ? "Automatic mapping"
                : string.IsNullOrWhiteSpace(device.BluetoothAddress)
                    ? "Bluetooth identity pending"
                    : profile is null
                        ? device.BluetoothAddress
                        : $"P{profile.Slot} - {device.BluetoothAddress}";
            UpdateDashboardRumbleButton(slot);
        }
    }

    private async Task ToggleDashboardRumbleAsync(int slot)
    {
        var button = _dashboardPadRumbleButtons[slot - 1];
        button.Enabled = false;
        try
        {
            var enabled = !_native.IsControllerRumbleEnabled(slot);
            await _native.SetControllerRumbleEnabledAsync(slot, enabled).ConfigureAwait(true);
            LogUserAction(
                "Controller vibration changed",
                ("pad", $"P{slot}"),
                ("enabled", enabled.ToString()));
            AppDiagnosticsLogger.Record(
                "CONTROLLER_RUMBLE_SETTING_CHANGED",
                ("pad", $"P{slot}"),
                ("enabled", enabled.ToString()),
                ("receiverActive", WindowsNativeRuntime.TryGetActiveReceiver(_paths, out _, out _).ToString()));
            _statusLabel.Text = enabled
                ? $"P{slot} vibration enabled"
                : $"P{slot} vibration disabled";
        }
        finally
        {
            button.Enabled = true;
            UpdateDashboardRumbleButton(slot);
        }
    }

    private void UpdateDashboardRumbleButton(int slot)
    {
        var button = _dashboardPadRumbleButtons[slot - 1];
        if (button is null)
        {
            return;
        }

        var enabled = _native.IsControllerRumbleEnabled(slot);
        button.Text = enabled ? "Rumble ON" : "Rumble OFF";
        button.AccessibleName = _localization.Translate(
            enabled ? $"Disable vibration for P{slot}" : $"Enable vibration for P{slot}");
        button.BackColor = enabled ? UiTheme.AccentSoft : UiTheme.SurfaceMuted;
        button.ForeColor = enabled ? UiTheme.AccentDark : UiTheme.TextMuted;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = enabled ? UiTheme.Accent : UiTheme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = enabled
            ? Color.FromArgb(208, 239, 238)
            : Color.FromArgb(238, 242, 246);
        button.FlatAppearance.MouseDownBackColor = enabled
            ? Color.FromArgb(190, 229, 228)
            : Color.FromArgb(226, 232, 238);
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
        await RefreshWindowsNativeDevicesAsync();
        await RunControllerDoctorAsync();
    }

    private async Task RunControllerDoctorAsync()
    {
        LogUserAction("Controller Doctor requested");
        SelectTabIfExists("Doctor");
        BeginOperationProgress("Controller Doctor", "Checking Windows Native requirements", 5);
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

            SetDoctorStatus("Checking Windows Bluetooth", 22, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading the Windows Bluetooth stack", 22);
            var windowsBluetooth = await _native.GetWindowsBluetoothDevicesAsync().ConfigureAwait(true);
            var windowsCount = windowsBluetooth.Count;
            var windowsOk = windowsBluetooth.Count(device =>
                device.Status.Equals("OK", StringComparison.OrdinalIgnoreCase));
            var capacity = NativeControlServices.EstimateWindowsNativeCapacity(
                windowsBluetooth,
                _lastWindowsNativeDevices.Count);
            AddDoctorRow(
                "Windows Bluetooth",
                windowsCount == 0 ? CheckState.Warn : windowsOk > 0 ? CheckState.Ok : CheckState.Warn,
                windowsCount == 0
                    ? "No Windows Bluetooth devices listed"
                    : $"{windowsOk}/{windowsCount} device(s) OK; estimated Stadia capacity {capacity.Controllers}/4");
            details.Add($"Windows Bluetooth: adapter={capacity.AdapterName}, ok={windowsOk}, total={windowsCount}, otherActive={capacity.OtherActiveBluetoothDevices}, capacity={capacity.Controllers}/4");

            SetDoctorStatus("Checking Stadia HID", 38, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Scanning native Stadia controller input", 38);
            var nativeDevices = await RefreshWindowsNativeDevicesAsync(updateOperationProgress: false).ConfigureAwait(true);
            AddDoctorRow(
                "Stadia HID",
                nativeDevices.Count == 0 ? CheckState.Info : CheckState.Ok,
                nativeDevices.Count == 0
                    ? "No powered Stadia controller is visible right now"
                    : $"{nativeDevices.Count} Stadia HID controller(s) visible");
            details.Add($"Stadia HID: visible={nativeDevices.Count}");

            SetDoctorStatus("Checking input isolation", 52, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Matching Stadia devices in HidHide", 52);
            var protectedDevices = nativeDevices.Count(device =>
                !string.IsNullOrWhiteSpace(device.DeviceInstancePath));
            AddDoctorRow(
                "HidHide isolation",
                nativeDevices.Count == 0
                    ? CheckState.Info
                    : protectedDevices == nativeDevices.Count
                        ? CheckState.Ok
                        : CheckState.Warn,
                nativeDevices.Count == 0
                    ? "Isolation will be checked when a controller is connected"
                    : $"{protectedDevices}/{nativeDevices.Count} controller(s) matched for duplicate-input protection");
            details.Add($"HidHide: matched={protectedDevices}/{nativeDevices.Count}");

            var receiverActive = WindowsNativeRuntime.TryGetActiveReceiver(
                _paths,
                out var receiverPid,
                out var activeSlots);
            AddDoctorRow(
                "Virtual Xbox pads",
                receiverActive ? CheckState.Ok : CheckState.Info,
                receiverActive
                    ? $"{activeSlots} virtual pad(s) active in receiver PID {receiverPid}"
                    : "Receiver is stopped; press Start when ready");
            var rumbleCapable = nativeDevices.Count(device =>
                device.MaxOutputReportLength >= WindowsNativeRumbleReport.MinimumLength);
            AddDoctorRow(
                "Vibration",
                nativeDevices.Count == 0
                    ? CheckState.Info
                    : rumbleCapable == nativeDevices.Count
                        ? CheckState.Ok
                        : CheckState.Warn,
                nativeDevices.Count == 0
                    ? "Vibration capability will be checked after connection"
                    : $"{rumbleCapable}/{nativeDevices.Count} controller(s) expose a compatible HID output report");
            details.Add($"Receiver: active={receiverActive}, pid={(receiverActive ? receiverPid : 0)}, slots={(receiverActive ? activeSlots : 0)}, rumbleCapable={rumbleCapable}");

            SetDoctorStatus("Checking battery and profiles", 70, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Loading battery, profiles, and macros", 70);
            var batteryAvailable = nativeDevices.Count(device => device.BatteryPercent.HasValue);
            var lowBattery = nativeDevices.Count(device => device.BatteryPercent is < 10);
            AddDoctorRow(
                "Battery",
                lowBattery > 0
                    ? CheckState.Warn
                    : batteryAvailable > 0
                        ? CheckState.Ok
                        : CheckState.Info,
                lowBattery > 0
                    ? $"{lowBattery} controller(s) below 10%"
                    : batteryAvailable > 0
                        ? $"Windows exposes battery for {batteryAvailable}/{nativeDevices.Count} controller(s)"
                        : "Windows has not exposed controller battery data");
            RefreshProfiles();
            LoadMacroConfig();
            var autoProfiles = _lastProfiles.Count(profile => profile.AutoConnect);
            var matchedProfiles = nativeDevices.Count(device =>
                !string.IsNullOrWhiteSpace(device.BluetoothAddress) &&
                _lastProfiles.Any(profile =>
                    profile.Mac.Equals(device.BluetoothAddress, StringComparison.OrdinalIgnoreCase)));
            AddDoctorRow(
                "Profiles",
                _lastProfiles.Count == 0 ? CheckState.Info : CheckState.Ok,
                _lastProfiles.Count == 0
                    ? "No preferred controller order configured"
                    : $"{_lastProfiles.Count} profile(s), {autoProfiles} active at startup, {matchedProfiles} currently matched");
            var configuredMacros = _native.LoadMacroMappings()
                .Count(mapping => !string.IsNullOrWhiteSpace(mapping.Shortcut));
            AddDoctorRow(
                "Macros",
                configuredMacros > 0 ? CheckState.Ok : CheckState.Info,
                configuredMacros > 0
                    ? $"{configuredMacros} native shortcut(s) configured"
                    : "No Assistant/Capture shortcuts configured");
            details.Add($"Battery: available={batteryAvailable}, low={lowBattery}; profiles: total={_lastProfiles.Count}, auto={autoProfiles}, matched={matchedProfiles}; macros={configuredMacros}");

            SetDoctorStatus("Checking input telemetry", 88, Color.FromArgb(45, 91, 150));
            SetOperationProgress("Controller Doctor", "Reading live controller telemetry", 88);
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

    private static string? WindowsNativeDisplayName(WindowsNativeHidDevice? device)
    {
        if (device is null)
        {
            return null;
        }

        return !string.IsNullOrWhiteSpace(device.FriendlyName)
            ? device.FriendlyName
            : !string.IsNullOrWhiteSpace(device.ProductName)
                ? device.ProductName
                : "Stadia Controller";
    }

    private static Color DashboardStateColor(string state)
    {
        return state switch
        {
            "Active" or "Ready" => Color.FromArgb(34, 120, 72),
            "Detected" => Color.FromArgb(45, 91, 150),
            _ => Color.FromArgb(92, 106, 126)
        };
    }

    private void RefreshLogs()
    {
        var statusText = LogReader.Tail(_paths.StatusLog, 140);
        var windowsNativeText = LogReader.Tail(Path.Combine(_paths.LogDirectory, "windows-native.log"), 220);
        var actionText = LogReader.Tail(_paths.UserActionLog, 160);
        var appDiagnosticsText = LogReader.Tail(_paths.AppDiagnosticsLog, 180);
        _controlStatusLogBox.Text = statusText;
        _dashboardActionLogBox.Text = actionText;
        _statusLogBox.Text = statusText;
        _windowsNativeLogBox.Text = string.IsNullOrWhiteSpace(windowsNativeText) ? statusText : windowsNativeText;
        _windowsNativeLogPageBox.Text = string.IsNullOrWhiteSpace(windowsNativeText) ? statusText : windowsNativeText;
        UpdateWindowsNativePhaseLabel(statusText + Environment.NewLine + windowsNativeText);
        _userActionLogBox.Text = actionText;
        _appDiagnosticsLogBox.Text = appDiagnosticsText;
    }

    private void UpdateWindowsNativePhaseLabel(string text)
    {
        if (!ConnectionPhaseParser.TryParseLatest(text, "Windows Native", out var phase) || phase is null)
        {
            _windowsNativePhaseLabel.Text = "Connect a Stadia controller to continue";
            _windowsNativePhaseLabel.ForeColor = Color.FromArgb(92, 106, 126);
            return;
        }

        var detail = phase.IsTimeout
            ? $"Timeout - {phase.Phase}: {phase.Detail}"
            : $"{phase.Phase}: {phase.Detail}";
        var stateColor = phase.State switch
        {
            "OK" => Color.FromArgb(34, 120, 72),
            "FAIL" or "TIMEOUT" => Color.FromArgb(180, 45, 45),
            "WAIT" or "WARN" or "INSTALL" => Color.FromArgb(170, 104, 0),
            _ => Color.FromArgb(45, 91, 150)
        };
        _windowsNativePhaseLabel.Text = $"Phase {phase.Step}/{phase.Total} - {phase.Phase} ({phase.State}): {phase.Detail}";
        _windowsNativePhaseLabel.ForeColor = stateColor;

        var title = _operationTitleLabel.Text;
        if ((!title.StartsWith("Starting Windows Native", StringComparison.OrdinalIgnoreCase) &&
             !title.StartsWith("Stopping Windows Native", StringComparison.OrdinalIgnoreCase)) ||
            phase.Timestamp < _operationStartedAt)
        {
            return;
        }

        var percent = phase.IsTerminal ? 100 : phase.ProgressPercent;
        _windowsNativeStatusLabel.Text = detail;
        _windowsNativeStatusLabel.ForeColor = stateColor;
        _windowsNativeProgress.Value = ClampProgress(percent);
        switch (phase.State)
        {
            case "FAIL":
            case "TIMEOUT":
                FailOperationProgress(title, detail);
                break;
            case "WARN":
            case "WAIT":
            case "INSTALL":
                WarnOperationProgress(title, detail, percent);
                break;
            case "OK" when phase.IsTerminal:
                CompleteOperationProgress(title, detail);
                break;
            default:
                SetOperationProgress(title, detail, percent);
                break;
        }
    }

    private void RefreshSelectionLabels()
    {
        var hidden = _lastWindowsNativeDevices.Count(device => !string.IsNullOrWhiteSpace(device.DeviceInstancePath));
        _selectionLabel.Text = $"Windows Native HID: {_lastWindowsNativeDevices.Count} visible   HidHide: {hidden} matched   Virtual pads: automatic";
    }

    private void LogUserAction(string action, params (string Key, string? Value)[] details)
    {
        try
        {
            var context = new List<(string Key, string? Value)>
            {
                ("tab", _tabs.SelectedTab?.Text),
                ("operation", _operationTitleLabel.Text),
                ("operationDetail", _operationDetailLabel.Text),
                ("operationPercent", _operationProgress.Value.ToString()),
                ("windowsNativeStatus", _windowsNativeStatusLabel.Text),
                ("windowsNativeVisible", _lastWindowsNativeDevices.Count.ToString()),
                ("windowsNativeSelected", SelectedListText(_windowsNativeDeviceList)),
                ("telemetryPads", (_lastTelemetrySnapshot?.Controllers.Count ?? 0).ToString())
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
            WindowsNativeHidDevice device => $"{WindowsNativeDisplayName(device)} {device.BluetoothAddress}".Trim(),
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
        _operationStartedAt = DateTimeOffset.Now;
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
        _operationDetailLabel.ForeColor = Color.FromArgb(34, 120, 72);
    }

    private void FailOperationProgress(string title, string detail)
    {
        SetOperationProgress(title, detail, 100);
        _operationDetailLabel.ForeColor = Color.FromArgb(180, 45, 45);
    }

    private void WarnOperationProgress(string title, string detail, int percent)
    {
        SetOperationProgress(title, detail, percent);
        _operationDetailLabel.ForeColor = Color.FromArgb(170, 104, 0);
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
        await CheckForUpdatesAsync(interactive: true);
    }

    private async Task CheckForUpdatesAsync(bool interactive)
    {
        LogUserAction("Check updates requested");
        if (Interlocked.CompareExchange(ref _updateCheckInProgress, 1, 0) != 0)
        {
            AppDiagnosticsLogger.Record(
                "UPDATE_CHECK_SKIPPED",
                ("reason", "already_running"),
                ("interactive", interactive.ToString()));
            if (interactive)
            {
                _statusLabel.Text = _localization.IsItalian
                    ? "Controllo aggiornamenti già in corso"
                    : "Update check already running";
            }
            return;
        }

        AppDiagnosticsLogger.Record(
            "UPDATE_CHECK_STARTED",
            ("installedVersion", _paths.Version),
            ("interactive", interactive.ToString()));
        try
        {
            var release = await _releaseChecker.GetLatestAsync();
            _diagnosticsBox.Text = $"Installed: {_paths.Version}{Environment.NewLine}Latest:    {release.Tag}{Environment.NewLine}URL:       {release.Url}";
            if (!_updateService.IsUpdateAvailable(_paths.Version, release.Tag))
            {
                _statusLabel.Text = "Up to date";
                AppDiagnosticsLogger.Record(
                    "UPDATE_CHECK_COMPLETED",
                    ("result", "up_to_date"),
                    ("installedVersion", _paths.Version),
                    ("targetVersion", release.Tag));
                if (interactive) _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
                return;
            }

            _statusLabel.Text = $"Update available: {release.Tag}";
            AppDiagnosticsLogger.Record(
                "UPDATE_AVAILABLE",
                ("installedVersion", _paths.Version),
                ("targetVersion", release.Tag),
                ("automaticInstall", _updateService.CanInstallAutomatically.ToString()));
            _tabs.SelectedTab = _tabs.TabPages["Diagnostics"];
            if (!_updateService.CanInstallAutomatically)
            {
                if (interactive && !string.IsNullOrWhiteSpace(release.Url)) Process.Start(new ProcessStartInfo(release.Url) { UseShellExecute = true });
                return;
            }

            BeginOperationProgress("Updating Stadia X", "Downloading verified setup", 5);
            var progress = new Progress<int>(percent => SetOperationProgress("Updating Stadia X", $"Downloading setup - {percent}%", Math.Clamp(percent, 5, 95)));
            var prepared = await _updateService.PrepareAsync(release, _paths.Version, progress);
            if (prepared is null)
            {
                CompleteOperationProgress("Updating Stadia X", "Already up to date");
                AppDiagnosticsLogger.Record(
                    "UPDATE_CHECK_COMPLETED",
                    ("result", "up_to_date_after_wait"),
                    ("installedVersion", _paths.Version),
                    ("targetVersion", release.Tag));
                return;
            }

            CompleteOperationProgress("Updating Stadia X", "Setup downloaded and SHA-256 verified");
            var updatePrompt = _localization.IsItalian
                ? $"La versione {release.Tag} è pronta. Stadia X si chiuderà, si aggiornerà e verrà riaperto. Se il nuovo avvio non riesce, la versione precedente verrà ripristinata automaticamente."
                : $"Version {release.Tag} is ready. Stadia X will close, update itself, and reopen. If startup fails, the previous version will be restored automatically.";
            var install = ShowLocalizedMessage(
                updatePrompt,
                "Stadia X update",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (install == DialogResult.Yes)
            {
                LogUserAction("Verified update install accepted", ("version", release.Tag));
                AppDiagnosticsLogger.Record(
                    "UPDATE_INSTALL_ACCEPTED",
                    ("installedVersion", _paths.Version),
                    ("targetVersion", release.Tag),
                    ("sha256", prepared.ExpectedSha256));
                _updateService.LaunchInstall(prepared, _paths.Version);
                Close();
            }
            else
            {
                AppDiagnosticsLogger.Record(
                    "UPDATE_INSTALL_DEFERRED",
                    ("installedVersion", _paths.Version),
                    ("targetVersion", release.Tag));
            }
        }
        catch (Exception ex)
        {
            _diagnosticsBox.Text = ex.ToString();
            var serviceUnavailable = ex is HttpRequestException or TaskCanceledException;
            _statusLabel.Text = serviceUnavailable
                ? (_localization.IsItalian ? "Servizio aggiornamenti non raggiungibile" : "Update service unavailable")
                : (_localization.IsItalian ? "Controllo aggiornamenti non riuscito" : "Update check failed");
            AppDiagnosticsLogger.Record(
                serviceUnavailable ? "UPDATE_CHECK_UNAVAILABLE" : "UPDATE_CHECK_FAILED",
                ("interactive", interactive.ToString()),
                ("exceptionType", ex.GetType().FullName),
                ("message", ex.Message),
                ("details", interactive ? "See matching UI_ASYNC_ACTION_FAILED event." : ex.ToString()));
            if (interactive) throw;
        }
        finally
        {
            Volatile.Write(ref _updateCheckInProgress, 0);
        }
    }

    private Task RollbackUpdateAsync()
    {
        if (!_updateService.HasRollback)
        {
            ShowLocalizedMessage("No previous version is available.", "Stadia X update", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        }

        var rollbackPrompt = _localization.IsItalian
            ? "Ripristinare la versione precedente di Stadia X? L'app si chiuderà e verrà riaperta automaticamente."
            : "Restore the previous Stadia X version? The app will close and reopen automatically.";
        var rollback = ShowLocalizedMessage(
            rollbackPrompt,
            "Stadia X update",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (rollback == DialogResult.Yes)
        {
            LogUserAction("Update rollback accepted");
            _updateService.LaunchRollback();
            Close();
        }
        return Task.CompletedTask;
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

    private async Task CreateWindowsNativeCapacityReportAsync()
    {
        LogUserAction("Windows Native capacity report requested");
        BeginOperationProgress("Controller capacity", "Inspecting the Windows Bluetooth adapter", 15);
        var path = await _native.CreateWindowsNativeCapacityReportAsync();
        CompleteOperationProgress("Controller capacity", "Capacity report created");
        _diagnosticsBox.Text = await File.ReadAllTextAsync(path);
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

    private void StartWindowsNative()
    {
        LogUserAction("Start Windows Native requested");
        BeginOperationProgress("Starting Windows Native", "Launching native receiver", 18);
        SetWindowsNativeStatus("Starting Windows Native", 20, warn: false);
        LaunchSelfCommand("--start-windows-native", elevateWhenNeeded: true, "Windows Native start requested. Watch logs for readiness.");
        _ = RefreshWindowsNativeAfterStartAsync();
        _tabs.SelectedTab = _tabs.TabPages["Windows Native"];
    }

    private async Task RefreshWindowsNativeAfterStartAsync()
    {
        var waits = new[]
        {
            (Wait: TimeSpan.FromSeconds(2), Before: 24, After: 45),
            (Wait: TimeSpan.FromSeconds(5), Before: 48, After: 62),
            (Wait: TimeSpan.FromSeconds(8), Before: 64, After: 78),
            (Wait: TimeSpan.FromSeconds(12), Before: 80, After: 94)
        };

        for (var attempt = 0; attempt < waits.Length; attempt++)
        {
            var wait = waits[attempt];
            await WaitWithProgressAsync(wait.Wait, wait.Before, wait.After, "Starting Windows Native", $"Waiting for native receiver pass {attempt + 1}");
            if (IsDisposed)
            {
                return;
            }

            RefreshLogs();
            var latest = LogReader.Tail(Path.Combine(_paths.LogDirectory, "windows-native.log"), 30);
            if (latest.Contains("WINDOWS_NATIVE_INPUT_READY", StringComparison.OrdinalIgnoreCase))
            {
                await RefreshWindowsNativeDevicesAsync(updateOperationProgress: false);
                CompleteOperationProgress("Starting Windows Native", "Windows Native receiver is running");
                SetWindowsNativeStatus("Running - physical input hidden", 100, warn: false);
                RefreshControllerTelemetry();
                return;
            }

            if (latest.Contains("WINDOWS_NATIVE_READY", StringComparison.OrdinalIgnoreCase))
            {
                SetOperationProgress("Starting Windows Native", "Virtual pads ready; opening Stadia controller input", 90);
                SetWindowsNativeStatus("Virtual pads ready - opening controller input", 90, warn: false);
            }

            if (latest.Contains("WINDOWS_NATIVE_BLUETOOTH_HID_WAIT", StringComparison.OrdinalIgnoreCase))
            {
                SetOperationProgress("Starting Windows Native", "Bluetooth paired; waiting for controller input", 74);
                SetWindowsNativeStatus("Bluetooth paired - activating controller input", 74, warn: false);
            }
            else if (latest.Contains("WINDOWS_NATIVE_BLUETOOTH_PAIRING_OK", StringComparison.OrdinalIgnoreCase) ||
                     latest.Contains("WINDOWS_NATIVE_BLUETOOTH_ALREADY_PAIRED", StringComparison.OrdinalIgnoreCase))
            {
                SetOperationProgress("Starting Windows Native", "Stadia controller paired through Windows Bluetooth", 68);
                SetWindowsNativeStatus("Controller paired - preparing input", 68, warn: false);
            }
            else if (latest.Contains("WINDOWS_NATIVE_BLUETOOTH_FOUND", StringComparison.OrdinalIgnoreCase))
            {
                SetOperationProgress("Starting Windows Native", "Stadia controller found; pairing", 58);
                SetWindowsNativeStatus("Stadia controller found - pairing", 58, warn: false);
            }
            else if (latest.Contains("WINDOWS_NATIVE_BLUETOOTH_SEARCH_START", StringComparison.OrdinalIgnoreCase))
            {
                SetOperationProgress("Starting Windows Native", "Searching automatically for Stadia Bluetooth controllers", 44);
                SetWindowsNativeStatus("Searching for Stadia Bluetooth controllers", 44, warn: false);
            }

            if (latest.Contains("WINDOWS_NATIVE_NOT_READY", StringComparison.OrdinalIgnoreCase))
            {
                var pairingFailed = latest.Contains("WINDOWS_NATIVE_BLUETOOTH_PAIRING_FAILED", StringComparison.OrdinalIgnoreCase);
                FailOperationProgress(
                    "Starting Windows Native",
                    pairingFailed
                        ? "Automatic Bluetooth pairing failed - check the Windows Native log"
                        : "No Stadia controller found - put it in pairing mode and press Start again");
                SetWindowsNativeStatus(
                    pairingFailed ? "Automatic pairing failed - check log" : "No Stadia controller found in pairing mode",
                    100,
                    warn: true);
                await RefreshWindowsNativeDevicesAsync(updateOperationProgress: false);
                return;
            }
        }

        CompleteOperationProgress("Starting Windows Native", "Start requested; waiting for receiver status in logs");
        SetWindowsNativeStatus("Start requested - watching logs", 96, warn: false);
    }

    private void StopWindowsNative()
    {
        LogUserAction("Stop Windows Native requested");
        BeginOperationProgress("Stopping Windows Native", "Sending receiver stop signal", 35);
        LaunchSelfCommand("--stop-windows-native", elevateWhenNeeded: true, "Windows Native stop requested. Physical input will be restored.");
        SetWindowsNativeStatus("Stop requested - restoring physical input", 100, warn: false);
        CompleteOperationProgress("Stopping Windows Native", "Stop requested; physical input restore requested");
        RefreshLogs();
        _tabs.SelectedTab = _tabs.TabPages["Windows Native"];
    }

    private void RepairWindowsNative()
    {
        LogUserAction("Repair Windows Native requested");
        BeginOperationProgress("Repairing Windows Native", "Stopping receiver and restoring physical input", 10);
        SetWindowsNativeStatus("Repairing controller connection", 12, warn: false);
        LaunchSelfCommand(
            "--repair-windows-native",
            elevateWhenNeeded: true,
            "Windows Native repair requested. Stadia devices will reconnect automatically.");
        SetOperationProgress("Repairing Windows Native", "Restarting Stadia PnP devices and Bluetooth discovery", 28);
        _ = RefreshWindowsNativeAfterStartAsync();
        _tabs.SelectedTab = _tabs.TabPages["Windows Native"];
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
                var reason = RecordUiFailure("Post-start Linux Bluetooth refresh", ex);
                FailOperationProgress("Starting bridge", $"Bluetooth refresh failed - {reason}");
                ShowLocalizedMessage(ex.Message, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            var battery = BatteryPercentWithRuntime(device.BatteryPercent);
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
            RecordUiFailure("Save Bluetooth BUSID", ex);
            ShowLocalizedMessage(ex.Message, "Bluetooth BUSID", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            RecordUiFailure("Save WSL distro", ex);
            ShowLocalizedMessage(ex.Message, "WSL distro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            ShowLocalizedMessage("Select one or more Linux Bluetooth devices first.", "Linux Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            ShowLocalizedMessage("Select a Windows Bluetooth device first.", "Windows Bluetooth", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            RecordUiFailure("Save controller profile", ex);
            ShowLocalizedMessage(ex.Message, "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        LogUserAction("Apply preferred controller order requested");
        _native.ApplyAutoConnectProfiles();
        RefreshSelectionLabels();
        if (WindowsNativeRuntime.TryGetActiveReceiver(_paths, out _, out _))
        {
            BeginOperationProgress("Applying controller order", "Restarting the native receiver safely", 18);
            LaunchSelfCommand(
                "--restart-windows-native",
                elevateWhenNeeded: true,
                "Controller order saved. Restarting Windows Native automatically.");
            _ = RefreshWindowsNativeAfterStartAsync();
            return;
        }

        _statusLabel.Text = "Controller order saved for the next start";
    }

    private void UseWindowsSelectedAsProfile()
    {
        LogUserAction("Use Windows Native selected controller as profile requested");
        var device = _windowsNativeDeviceList.SelectedItems.Count > 0
            ? _windowsNativeDeviceList.SelectedItems[0].Tag as WindowsNativeHidDevice
            : _lastWindowsNativeDevices.FirstOrDefault();
        if (device is null)
        {
            ShowLocalizedMessage("Select a Stadia controller first.", "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!NativeControlServices.IsBluetoothMac(device.BluetoothAddress))
        {
            ShowLocalizedMessage("Windows has not exposed this controller Bluetooth address yet. Keep it connected and press Check, then try again.", "Controller profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _profileNameText.Text = WindowsNativeDisplayName(device) ?? "Stadia Controller";
        _profileMacText.Text = device.BluetoothAddress;
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
            ShowLocalizedMessage("Choose a chord and type a shortcut first.", "Macro editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var executable = ResolveSelfExecutable();
        if (string.IsNullOrWhiteSpace(executable))
        {
            LogUserAction("Launch self command failed", ("argument", argument), ("reason", "StadiaX.exe not found"));
            AppDiagnosticsLogger.Record("SELF_COMMAND_START_FAILED", ("argument", argument), ("reason", "executable_missing"), ("root", _paths.Root));
            ShowLocalizedMessage("StadiaX.exe was not found. Build or install the native launcher first.", "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var willElevate = elevateWhenNeeded && !IsAdministrator();
        var startInfo = new ProcessStartInfo(executable, argument)
        {
            WorkingDirectory = _paths.Root,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (willElevate)
        {
            startInfo.Verb = "runas";
        }

        try
        {
            using var process = Process.Start(startInfo);
            var pid = process?.Id.ToString() ?? "unknown";
            LogUserAction(
                "Launch self command started",
                ("argument", argument),
                ("pid", pid),
                ("elevated", willElevate.ToString()),
                ("executable", executable));
            AppDiagnosticsLogger.Record(
                "SELF_COMMAND_STARTED",
                ("argument", argument),
                ("pid", pid),
                ("elevated", willElevate.ToString()),
                ("executable", executable),
                ("cwd", _paths.Root));
            _statusLabel.Text = message;
            RefreshLogs();
        }
        catch (Exception ex)
        {
            LogUserAction("Launch self command failed", ("argument", argument), ("error", ex.Message));
            AppDiagnosticsLogger.Record(
                "SELF_COMMAND_START_FAILED",
                ("argument", argument),
                ("elevated", willElevate.ToString()),
                ("executable", executable),
                ("cwd", _paths.Root),
                ("error", ex.Message));
            ShowLocalizedMessage(ex.Message, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private string? ResolveSelfExecutable()
    {
        if (File.Exists(_paths.AppExecutable))
        {
            return _paths.AppExecutable;
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            File.Exists(processPath) &&
            Path.GetFileNameWithoutExtension(processPath).Equals("StadiaX", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        return null;
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
        _batteryOverlayLabel!.Text = BatteryOverlayText(devices);
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

    private static string BatteryOverlayText(IReadOnlyList<LinuxBluetoothDevice> devices)
    {
        var rows = devices.Take(4).Select((device, index) =>
        {
            var battery = device.BatteryPercent is null ? "?" : device.BatteryPercent + "%";
            return $"P{index + 1} {battery}";
        }).ToArray();
        var firstLine = string.Join("  ", rows.Take(2));
        var secondLine = rows.Length > 2 ? string.Join("  ", rows.Skip(2)) : "";
        return string.IsNullOrWhiteSpace(secondLine)
            ? firstLine
            : firstLine + Environment.NewLine + secondLine;
    }

    internal static void RunBatteryOverlaySelfTest()
    {
        var devices = new[]
        {
            new LinuxBluetoothDevice("P1", "Stadia P1", "yes", "yes", "yes", 84, true),
            new LinuxBluetoothDevice("P2", "Stadia P2", "yes", "yes", "yes", null, true),
            new LinuxBluetoothDevice("P3", "Stadia P3", "yes", "yes", "yes", 9, true),
            new LinuxBluetoothDevice("P4", "Stadia P4", "yes", "yes", "yes", 100, true)
        };
        var expected = "P1 84%  P2 ?" + Environment.NewLine + "P3 9%  P4 100%";
        if (!BatteryOverlayText(devices).Equals(expected, StringComparison.Ordinal) ||
            !devices.Any(device => device.BatteryPercent is < 10) ||
            new[] { devices[0], devices[1], devices[3] }.Any(device => device.BatteryPercent is < 10))
        {
            throw new InvalidOperationException("Linux-style battery overlay self-test failed.");
        }
    }

    private static string BatteryPercentWithRuntime(int? percent)
    {
        if (percent is null)
        {
            return "?";
        }

        var clamped = Math.Clamp(percent.Value, 0, 100);
        return $"{clamped}% {EstimatedBatteryRuntimeText(clamped)}";
    }

    private static string EstimatedBatteryRuntimeText(int percent)
    {
        var minutes = Math.Max(0, (int)Math.Round(ControllerFullBatteryHours() * 60 * Math.Clamp(percent, 0, 100) / 100.0));
        if (minutes < 60)
        {
            return minutes + "m";
        }

        var hours = minutes / 60;
        var remainder = minutes % 60;
        return remainder < 15 ? hours + "h" : $"{hours}h{remainder / 15 * 15:00}";
    }

    private static double ControllerFullBatteryHours()
    {
        var configured = Environment.GetEnvironmentVariable("STADIAX_CONTROLLER_FULL_BATTERY_HOURS");
        return double.TryParse(
                   configured,
                   System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var hours)
            ? Math.Clamp(hours, 1d, 24d)
            : DefaultControllerFullBatteryHours;
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
            BackColor = UiTheme.Canvas,
            Padding = new Padding(0),
            AutoScroll = true
        };
    }

    private static GroupBox CreateGroup(string text)
    {
        return new SurfaceGroupBox
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
                _ = RunActionWithDialogAsync("Battery overlay refresh", () => UpdateBatteryAsync(), showDialog: true);
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
        box.BackColor = UiTheme.LogSurface;
        box.ForeColor = UiTheme.LogText;
        box.Dock = DockStyle.Fill;
        box.Text = text;
    }

    private void AddActionGridButton(TableLayoutPanel parent, string text, int column, int row, int columnSpan, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new ModernButton
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
        var button = new ModernButton
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

    private Button AddButton(TableLayoutPanel parent, string text, int column, int row, Action action, int columnSpan = 1)
    {
        var button = new ModernButton { Text = text, Dock = DockStyle.Fill, MinimumSize = new Size(IsCompactUi() ? 116 : 140, IsCompactUi() ? 30 : 34), Margin = new Padding(4) };
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
        return button;
    }

    private void AddFlowButton(FlowLayoutPanel parent, string text, Action action, Color? backColor = null, Color? foreColor = null)
    {
        var button = new ModernButton
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

    private async Task RunActionWithDialogAsync(string actionName, Func<Task> action, bool showDialog)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var reason = RecordUiFailure(actionName, ex, includeUserAction: showDialog);
            if (showDialog)
            {
                ShowLocalizedMessage(reason, "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private async Task RunLoggedActionWithDialogAsync(string actionName, Func<Task> action)
    {
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        LogUserAction("Async action started", ("action", actionName), ("operationId", operationId));
        AppDiagnosticsLogger.Record(
            "UI_ASYNC_ACTION_STARTED",
            ("action", actionName),
            ("operationId", operationId));

        try
        {
            await action().ConfigureAwait(true);
            stopwatch.Stop();
            LogUserAction(
                "Async action completed",
                ("action", actionName),
                ("operationId", operationId),
                ("durationMs", stopwatch.ElapsedMilliseconds.ToString()));
            AppDiagnosticsLogger.Record(
                "UI_ASYNC_ACTION_COMPLETED",
                ("action", actionName),
                ("operationId", operationId),
                ("durationMs", stopwatch.ElapsedMilliseconds.ToString()));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var reason = FormatOperationErrorForUser(ex);
            LogUserAction(
                "Async action failed",
                ("action", actionName),
                ("operationId", operationId),
                ("durationMs", stopwatch.ElapsedMilliseconds.ToString()),
                ("reason", reason));
            AppDiagnosticsLogger.Record(
                "UI_ASYNC_ACTION_FAILED",
                ("action", actionName),
                ("operationId", operationId),
                ("durationMs", stopwatch.ElapsedMilliseconds.ToString()),
                ("exceptionType", ex.GetType().FullName),
                ("error", ex.ToString()));
            FailOperationProgress(actionName, $"Failed - {reason}");
            ShowLocalizedMessage(
                $"{reason}{Environment.NewLine}{Environment.NewLine}Operation ID: {operationId}{Environment.NewLine}Full details were recorded in Logs.",
                "Stadia X",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static string FormatOperationErrorForUser(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
        return message.Length <= 240 ? message : message[..237] + "...";
    }

    private string RecordUiFailure(string operation, Exception exception, bool includeUserAction = true)
    {
        var reason = FormatOperationErrorForUser(exception);
        if (includeUserAction)
        {
            LogUserAction("UI operation failed", ("operation", operation), ("reason", reason));
        }
        AppDiagnosticsLogger.Record(
            "UI_OPERATION_FAILED",
            ("operation", operation),
            ("exceptionType", exception.GetType().FullName),
            ("error", exception.ToString()));
        return reason;
    }

    private static void AddEditorRow(TableLayoutPanel panel, int row, string labelText, Control editor)
    {
        var label = new Label { Text = labelText, Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
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
        foreach (var assetIcon in paths.ResolveAssetCandidates("StadiaX-WindowsNative.ico")
                     .Concat(paths.ResolveAssetCandidates("StadiaX.ico"))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
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
        TabStop = true;
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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Right && Parent is { } parent)
        {
            var tabs = parent.Controls.OfType<ModernTabButton>().ToArray();
            var current = Array.IndexOf(tabs, this);
            if (current >= 0 && tabs.Length > 1)
            {
                var next = e.KeyCode == Keys.Right
                    ? (current + 1) % tabs.Length
                    : (current - 1 + tabs.Length) % tabs.Length;
                tabs[next].Focus();
                tabs[next].OnClick(EventArgs.Empty);
                e.Handled = true;
            }
        }

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = ClientRectangle;
        bounds.Inflate(-1, 0);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var fillColor = _hover ? UiTheme.AccentSoft : UiTheme.Canvas;
        using (var fill = new SolidBrush(fillColor))
        {
            e.Graphics.FillRectangle(fill, bounds);
        }

        if (_isSelected)
        {
            var accent = new Rectangle(bounds.Left + 7, bounds.Bottom - 2, Math.Max(10, bounds.Width - 14), 2);
            using var accentBrush = new SolidBrush(UiTheme.Accent);
            e.Graphics.FillRectangle(accentBrush, accent);
        }

        using var selectedFont = _isSelected ? new Font(Font, FontStyle.Bold) : null;
        var textColor = _isSelected ? UiTheme.TextPrimary : UiTheme.TextMuted;
        var textBounds = Rectangle.Inflate(bounds, -5, 0);
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            selectedFont ?? Font,
            textBounds,
            textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

    }

}
