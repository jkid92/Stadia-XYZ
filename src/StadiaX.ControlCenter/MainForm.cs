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
    private readonly Label _statusLabel = new();
    private readonly ListView _checksList = new();
    private readonly TextBox _statusLogBox = new();
    private readonly TextBox _linuxLogBox = new();
    private readonly TextBox _diagnosticsBox = new();
    private readonly System.Windows.Forms.Timer _logTimer = new();

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        _requirementChecker = new RequirementChecker(paths, _runner);
        _selfTestService = new SelfTestService(paths, _requirementChecker);
        Text = "Stadia X";
        MinimumSize = new Size(960, 640);
        Size = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(248, 250, 252);

        BuildUi();
        _logTimer.Interval = 2500;
        _logTimer.Tick += (_, _) => RefreshLogs();
        Shown += async (_, _) =>
        {
            Directory.CreateDirectory(_paths.LogDirectory);
            RefreshLogs();
            await RefreshChecksAsync();
            _logTimer.Start();
        };
        FormClosed += (_, _) => _logTimer.Stop();
    }

    private void BuildUi()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Color.FromArgb(28, 38, 54)
        };
        Controls.Add(header);

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
            Text = "Native Windows control center",
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
        _statusLabel.Size = new Size(360, 30);
        _statusLabel.Location = new Point(Width - 400, 24);
        header.Controls.Add(_statusLabel);

        var left = new Panel
        {
            Dock = DockStyle.Left,
            Width = 300,
            Padding = new Padding(14)
        };
        Controls.Add(left);

        var actions = new GroupBox
        {
            Text = "Actions",
            Dock = DockStyle.Top,
            Height = 302,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        left.Controls.Add(actions);

        AddButton(actions, "Start bridge", 18, 30, 260, StartBridge, Color.FromArgb(45, 125, 90), Color.White);
        AddButton(actions, "Stop and restore Bluetooth", 18, 78, 260, StopBridge, Color.FromArgb(178, 62, 62), Color.White);
        AddButton(actions, "Refresh checks", 18, 132, 125, async () => await RefreshChecksAsync());
        AddButton(actions, "Check updates", 153, 132, 125, async () => await CheckUpdatesAsync());
        AddButton(actions, "Run self-test", 18, 178, 125, async () => await RunSelfTestAsync());
        AddButton(actions, "PowerShell GUI", 153, 178, 125, OpenPowerShellGui);
        AddButton(actions, "Open folder", 18, 224, 125, () => Process.Start("explorer.exe", $"\"{_paths.Root}\""));
        AddButton(actions, "Open releases", 153, 224, 125, () => Process.Start(new ProcessStartInfo("https://github.com/jkid92/Stadia-XYZ/releases") { UseShellExecute = true }));

        var info = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(248, 250, 252),
            Text = $"Install folder:{Environment.NewLine}{_paths.Root}{Environment.NewLine}{Environment.NewLine}This is the native .exe launcher. The PowerShell GUI remains available as an advanced fallback while remaining tools are migrated."
        };
        left.Controls.Add(info);
        actions.BringToFront();

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9)
        };
        Controls.Add(tabs);

        var checksPage = new TabPage("Checks") { BackColor = Color.FromArgb(248, 250, 252) };
        _checksList.View = View.Details;
        _checksList.FullRowSelect = true;
        _checksList.GridLines = true;
        _checksList.Dock = DockStyle.Fill;
        _checksList.Columns.Add("Item", 220);
        _checksList.Columns.Add("State", 90);
        _checksList.Columns.Add("Details", 620);
        checksPage.Controls.Add(_checksList);
        tabs.TabPages.Add(checksPage);

        var logsPage = new TabPage("Live Logs") { BackColor = Color.FromArgb(248, 250, 252) };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
        logsPage.Controls.Add(split);
        ConfigureLogBox(_statusLogBox, "Status log not loaded yet.");
        ConfigureLogBox(_linuxLogBox, "Linux log not loaded yet.");
        split.Panel1.Controls.Add(_statusLogBox);
        split.Panel2.Controls.Add(_linuxLogBox);
        tabs.TabPages.Add(logsPage);

        var diagnosticsPage = new TabPage("Diagnostics") { BackColor = Color.FromArgb(248, 250, 252) };
        ConfigureLogBox(_diagnosticsBox, "Run self-test or update check to see output here.");
        diagnosticsPage.Controls.Add(_diagnosticsBox);
        tabs.TabPages.Add(diagnosticsPage);
    }

    private static void ConfigureLogBox(TextBox box, string text)
    {
        box.Multiline = true;
        box.ReadOnly = true;
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
        AddButton(parent, text, x, y, width, () => _ = action());
    }

    private async Task RefreshChecksAsync()
    {
        _statusLabel.Text = "Checking requirements...";
        _checksList.Items.Clear();
        var checks = await _requirementChecker.RunAsync();
        foreach (var check in checks)
        {
            var item = new ListViewItem(check.Name);
            item.SubItems.Add(check.State.ToString().ToUpperInvariant());
            item.SubItems.Add(check.Details);
            item.ForeColor = check.State switch
            {
                CheckState.Ok => Color.FromArgb(34, 120, 72),
                CheckState.Warn => Color.FromArgb(170, 104, 0),
                CheckState.Missing => Color.FromArgb(180, 45, 45),
                _ => Color.FromArgb(70, 70, 70)
            };
            _checksList.Items.Add(item);
        }

        var missing = checks.Count(c => c.State == CheckState.Missing);
        var warn = checks.Count(c => c.State == CheckState.Warn);
        _statusLabel.Text = missing > 0 ? $"{missing} missing requirement(s)" : warn > 0 ? $"{warn} warning(s)" : $"Ready - {_paths.Version}";
    }

    private async Task CheckUpdatesAsync()
    {
        try
        {
            var release = await _releaseChecker.GetLatestAsync();
            _diagnosticsBox.Text = $"Installed: {_paths.Version}{Environment.NewLine}Latest:    {release.Tag}{Environment.NewLine}URL:       {release.Url}";
            _statusLabel.Text = release.Tag.Equals(_paths.Version, StringComparison.OrdinalIgnoreCase) ? "Up to date" : $"Update available: {release.Tag}";
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
    }

    private void RefreshLogs()
    {
        _statusLogBox.Text = LogReader.Tail(_paths.StatusLog, 120);
        _linuxLogBox.Text = LogReader.Tail(_paths.LinuxLog, 160);
    }

    private void StartBridge()
    {
        LaunchSelfCommand("--start-bridge", elevateWhenNeeded: true, "Stadia X start requested. Watch Live Logs for progress.");
    }

    private void StopBridge()
    {
        LaunchSelfCommand("--stop-bridge", elevateWhenNeeded: true, "Stadia X stop requested. Watch Live Logs for progress.");
    }

    private void OpenPowerShellGui()
    {
        if (!File.Exists(_paths.PowerShellGuiScript))
        {
            MessageBox.Show("StadiaX-GUI.ps1 was not found.", "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{_paths.PowerShellGuiScript}\"")
        {
            WorkingDirectory = _paths.Root,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        });
    }

    private void LaunchBatch(string path, bool elevateWhenNeeded)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show($"{Path.GetFileName(path)} was not found.", "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe", $"/k \"{path}\"")
        {
            WorkingDirectory = _paths.Root,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal
        };
        if (elevateWhenNeeded && !IsAdministrator())
        {
            startInfo.Verb = "runas";
        }

        Process.Start(startInfo);
    }

    private void LaunchSelfCommand(string argument, bool elevateWhenNeeded, string message)
    {
        var executable = File.Exists(_paths.AppExecutable) ? _paths.AppExecutable : Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
        {
            MessageBox.Show("StadiaX.exe was not found. Falling back to legacy scripts.", "Stadia X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            LaunchBatch(argument.Contains("stop", StringComparison.OrdinalIgnoreCase) ? _paths.StopScript : _paths.StartScript, elevateWhenNeeded);
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

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
