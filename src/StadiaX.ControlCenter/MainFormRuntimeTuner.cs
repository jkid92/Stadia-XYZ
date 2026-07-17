using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StadiaX.ControlCenter;

internal static class MainFormRuntimeTuner
{
    private static readonly HashSet<Form> PatchedForms = new();
    private static readonly Dictionary<Form, List<System.Windows.Forms.Timer>> Timers = new();
    private static readonly Dictionary<Control, Bitmap> OwnedBitmaps = new();

    private static readonly Color AppBackground = UiTheme.Canvas;
    private static readonly Color HeaderTop = UiTheme.HeaderTop;
    private static readonly Color HeaderBottom = UiTheme.HeaderBottom;
    private static readonly Color Surface = UiTheme.Surface;
    private static readonly Color TextPrimary = UiTheme.TextPrimary;
    private static readonly Color TextMuted = UiTheme.TextMuted;
    private static readonly Color Border = UiTheme.Border;
    private static readonly Color NeutralButton = UiTheme.Surface;
    private static readonly Color NeutralButtonHover = UiTheme.AccentSoft;
    private static readonly Color Accent = UiTheme.Accent;
    private static readonly Color Success = UiTheme.Success;
    private static readonly Color Danger = UiTheme.Danger;

    private static bool CompactUi => MainForm.IsCompactUi();
    private static bool ConstrainedUi => MainForm.IsConstrainedUi();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => PatchOpenForms();
    }

    private static void PatchOpenForms()
    {
        foreach (Form form in Application.OpenForms)
        {
            ApplyForAudit(form);
        }
    }

    internal static void ApplyForAudit(Form form)
    {
        if (form.GetType().Name != "MainForm" || !PatchedForms.Add(form))
        {
            return;
        }

        PatchVisualDesign(form);
        PatchBluetoothLayout(form);
        PatchLinuxSummary(form);
        PatchControllerVisualizerRefresh(form);
    }

    private static void PatchVisualDesign(Form form)
    {
        form.BackColor = AppBackground;
        form.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9F);

        PatchShellLayout(form);
        PatchHeader(form);
        foreach (Control control in form.Controls)
        {
            StyleTree(control);
        }
    }

    private static void PatchShellLayout(Form form)
    {
        var sidebar = form.Controls.Find("AppSidebar", true).OfType<Panel>().FirstOrDefault() ??
                      form.Controls.Cast<Control>().OfType<Panel>().FirstOrDefault(panel => panel.Dock == DockStyle.Left);
        if (sidebar is not null)
        {
            sidebar.Padding = Px(sidebar, ConstrainedUi ? new Padding(8) : CompactUi ? new Padding(10) : new Padding(12));
            sidebar.BackColor = AppBackground;
        }

        var shell = form.Controls.Find("ContentShell", true).OfType<TableLayoutPanel>().FirstOrDefault();
        if (shell is not null)
        {
            shell.BackColor = AppBackground;
            if (shell.RowStyles.Count > 0)
            {
                shell.RowStyles[0] = new RowStyle(SizeType.Absolute, Px(shell, ConstrainedUi ? 38 : CompactUi ? 42 : 46));
            }
        }

        var navigation = form.Controls.Find("TabNavigationHost", true).OfType<Panel>().FirstOrDefault();
        if (navigation is null)
        {
            return;
        }

        navigation.BackColor = AppBackground;
        navigation.Padding = Px(navigation, ConstrainedUi ? new Padding(6, 4, 6, 3) : CompactUi ? new Padding(8, 5, 8, 4) : new Padding(10, 7, 10, 5));
        navigation.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawLine(pen, 0, navigation.Height - 1, navigation.Width, navigation.Height - 1);
        };
    }

    private static void PatchHeader(Form form)
    {
        var header = form.Controls.Cast<Control>()
            .FirstOrDefault(control => control is Panel && control.Dock == DockStyle.Top);
        if (header is null)
        {
            return;
        }

        var compact = CompactUi;
        var constrained = ConstrainedUi;
        header.Height = Px(header, constrained ? 72 : compact ? 78 : 88);
        header.BackColor = HeaderTop;
        header.Paint += (_, e) =>
        {
            if (header.ClientRectangle.Width <= 0 || header.ClientRectangle.Height <= 0)
            {
                return;
            }

            using var brush = new LinearGradientBrush(header.ClientRectangle, HeaderTop, HeaderBottom, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, header.ClientRectangle);
            using var pen = new Pen(Accent, 2);
            e.Graphics.DrawLine(pen, 0, header.Height - 2, header.Width, header.Height - 2);
        };

        if (!header.Controls.ContainsKey("StadiaXBrandLogo"))
        {
            var logoSize = Px(header, constrained ? 38 : compact ? 44 : 50);
            var logoBitmap = LoadHeaderLogoBitmap(form, logoSize);
            var logo = new PictureBox
            {
                Name = "StadiaXBrandLogo",
                Image = logoBitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Location = Px(header, constrained ? new Point(18, 16) : compact ? new Point(20, 17) : new Point(22, 19)),
                Size = new Size(logoSize, logoSize)
            };
            OwnedBitmaps[logo] = logoBitmap;
            logo.Disposed += (_, _) =>
            {
                if (OwnedBitmaps.Remove(logo, out var bitmap))
                {
                    bitmap.Dispose();
                }
            };
            header.Controls.Add(logo);
            logo.BringToFront();
        }

        void ArrangeHeaderLabels()
        {
            foreach (var label in header.Controls.OfType<Label>())
            {
                label.BackColor = Color.Transparent;
                if (label.Name == "AppTitleLabel" || label.Text.Equals("Stadia X", StringComparison.OrdinalIgnoreCase))
                {
                    label.AutoSize = true;
                    label.Location = Px(header, constrained ? new Point(68, 9) : compact ? new Point(80, 9) : new Point(88, 11));
                    label.Font = new Font("Segoe UI", Pt(header, constrained ? 16 : compact ? 18 : 20), FontStyle.Bold);
                    label.ForeColor = Color.White;
                }
                else if (label.Name == "AppSubtitleLabel" ||
                         label.Text.Contains("WinForms", StringComparison.OrdinalIgnoreCase) ||
                         label.Text.Contains("Bluetooth controller bridge", StringComparison.OrdinalIgnoreCase))
                {
                    label.AutoSize = true;
                    label.Location = Px(header, constrained ? new Point(70, 43) : compact ? new Point(82, 47) : new Point(90, 53));
                    label.Font = new Font("Segoe UI", Pt(header, constrained ? 8F : compact ? 8.25F : 9F));
                    label.ForeColor = Color.FromArgb(202, 213, 225);
                }
                else if (label.Name == "AppBatteryLabel" || label.Text.StartsWith("Battery:", StringComparison.OrdinalIgnoreCase))
                {
                    label.AutoSize = false;
                    var maxWidth = Px(header, constrained ? 320 : compact ? 440 : 520);
                    var minWidth = Px(header, constrained ? 160 : compact ? 220 : 260);
                    var reservedWidth = Px(header, constrained ? 260 : compact ? 360 : 430);
                    label.Size = new Size(Math.Min(maxWidth, Math.Max(minWidth, header.Width - reservedWidth)), Px(header, constrained ? 18 : compact ? 20 : 22));
                    label.Location = new Point(header.Width - label.Width - Px(header, constrained ? 14 : 24), Px(header, constrained ? 46 : compact ? 47 : 52));
                    label.Font = new Font("Segoe UI", Pt(header, constrained ? 8F : compact ? 8.25F : 9F), FontStyle.Bold);
                    label.ForeColor = Color.FromArgb(202, 213, 225);
                    label.TextAlign = ContentAlignment.MiddleRight;
                }
                else
                {
                    label.AutoSize = false;
                    var maxWidth = Px(header, constrained ? 320 : compact ? 440 : 520);
                    var minWidth = Px(header, constrained ? 160 : compact ? 220 : 260);
                    var reservedWidth = Px(header, constrained ? 260 : compact ? 360 : 430);
                    label.Size = new Size(Math.Min(maxWidth, Math.Max(minWidth, header.Width - reservedWidth)), Px(header, constrained ? 22 : compact ? 24 : 28));
                    label.Location = new Point(header.Width - label.Width - Px(header, constrained ? 14 : 24), Px(header, constrained ? 13 : compact ? 15 : 18));
                    label.Font = new Font("Segoe UI", Pt(header, constrained ? 8.75F : compact ? 9.5F : 11F), FontStyle.Bold);
                    label.ForeColor = Color.White;
                    label.TextAlign = ContentAlignment.MiddleRight;
                }
            }
        }

        ArrangeHeaderLabels();
        header.Resize += (_, _) => ArrangeHeaderLabels();
    }

    private static Bitmap LoadHeaderLogoBitmap(Form form, int size)
    {
        var paths = ReadPrivate<AppPaths>(form, "_paths");
        var logoPath = paths is null ? "" : Path.Combine(paths.Root, "assets", "StadiaX-icon.png");
        if (File.Exists(logoPath))
        {
            return new Bitmap(logoPath);
        }

        return BrandLogo.CreateBitmap(size);
    }

    private static void PatchBluetoothLayout(Form form)
    {
        if (ReadPrivate<TabControl>(form, "_tabs")?.TabPages["Bluetooth"] is not { } page)
        {
            return;
        }

        var layout = page.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (layout is null || layout.ColumnCount < 2 || layout.RowCount < 2)
        {
            return;
        }

        var windowsGroup = layout.GetControlFromPosition(0, 0);
        var linuxActions = layout.GetControlFromPosition(1, 0);
        var linuxGroup = layout.Controls.Cast<Control>()
            .OfType<GroupBox>()
            .FirstOrDefault(group => group.Text.Equals("Visible to Linux", StringComparison.OrdinalIgnoreCase));

        if (windowsGroup is not null)
        {
            layout.SetColumnSpan(windowsGroup, 2);
        }

        if (linuxActions is not null && linuxGroup is not null && linuxActions.Parent == layout)
        {
            layout.Controls.Remove(linuxActions);
            linuxActions.Dock = DockStyle.Top;
            linuxActions.AutoSize = true;
            if (linuxActions is Panel linuxActionsPanel)
            {
                linuxActionsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            }
            linuxActions.MinimumSize = new Size(0, Px(linuxActions, 72));
            linuxGroup.Controls.Add(linuxActions);
            linuxActions.BringToFront();
        }

        if (layout.RowStyles.Count > 0)
        {
            layout.RowStyles[0] = new RowStyle(SizeType.Absolute, Px(layout, ConstrainedUi ? 170 : CompactUi ? 190 : 220));
        }
    }

    private static void PatchLinuxSummary(Form form)
    {
        if (ReadPrivate<ListView>(form, "_linuxBluetoothList") is not { } list || list.Parent is not Control parent)
        {
            return;
        }

        if (parent.Controls.ContainsKey("LinuxBluetoothSummaryLabel"))
        {
            return;
        }

        var summary = new Label
        {
            Name = "LinuxBluetoothSummaryLabel",
            Dock = DockStyle.Top,
            Height = Px(parent, CompactUi ? 26 : 30),
            Padding = Px(parent, new Padding(8, 6, 0, 0)),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", CompactUi ? 8.25F : 9, FontStyle.Regular),
            ForeColor = TextMuted,
            Text = "Linux devices: not refreshed yet"
        };

        parent.Controls.Add(summary);
        summary.BringToFront();
        list.Dock = DockStyle.Fill;

        var timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => UpdateLinuxSummary(list, summary);
        timer.Start();
        TrackTimer(form, timer);
    }

    private static void UpdateLinuxSummary(ListView list, Label summary)
    {
        if (list.IsDisposed || summary.IsDisposed)
        {
            return;
        }

        var items = list.Items.Cast<ListViewItem>().ToArray();
        var connected = items.Count(item => SubItemText(item, 2).Equals("yes", StringComparison.OrdinalIgnoreCase));
        var stadia = items.Count(item =>
            SubItemText(item, 1).Contains("stadia", StringComparison.OrdinalIgnoreCase) ||
            SubItemText(item, 6).Equals("yes", StringComparison.OrdinalIgnoreCase));
        var battery = items.Select(item => SubItemText(item, 5))
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) && text != "-");

        summary.ForeColor = items.Length == 0 ? Danger : Success;
        summary.Text = items.Length == 0
            ? "Linux devices: none returned. Start the bridge, then press Refresh or Scan."
            : $"Linux devices: {items.Length} visible, {connected} connected, {stadia} Stadia" +
              (string.IsNullOrWhiteSpace(battery) ? "" : $" - battery {battery}");
    }

    private static void PatchControllerVisualizerRefresh(Form form)
    {
        var tabs = ReadPrivate<TabControl>(form, "_tabs");
        var native = ReadPrivate<NativeControlServices>(form, "_native");
        var update = form.GetType().GetMethod("UpdateControllerVisualizer", BindingFlags.Instance | BindingFlags.NonPublic);
        if (tabs is null || native is null || update is null || update.GetParameters().Length != 1)
        {
            return;
        }

        var timer = new System.Windows.Forms.Timer { Interval = 33 };
        timer.Tick += (_, _) =>
        {
            if (form.IsDisposed || tabs.SelectedTab?.Name != "Controller Test")
            {
                return;
            }

            try
            {
                update.Invoke(form, new object[] { native.ReadControllerTelemetry() });
            }
            catch
            {
                // The regular telemetry refresh path already reports read errors in the UI.
            }
        };
        timer.Start();
        TrackTimer(form, timer);
    }

    private static void StyleTree(Control control)
    {
        switch (control)
        {
            case Button button:
                StyleButton(button);
                break;
            case TabPage page:
                page.BackColor = AppBackground;
                break;
            case GroupBox group:
                group.BackColor = Surface;
                group.ForeColor = TextPrimary;
                group.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9, FontStyle.Bold);
                break;
            case ListView list:
                list.GridLines = false;
                list.BackColor = Surface;
                list.ForeColor = TextPrimary;
                list.BorderStyle = BorderStyle.None;
                list.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9);
                break;
            case TabControl tabs:
                tabs.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9);
                tabs.Padding = Px(tabs, CompactUi ? new Point(10, 5) : new Point(14, 8));
                tabs.Multiline = false;
                break;
            case TextBox textBox when textBox.Name == "SidebarSummary":
                textBox.BorderStyle = BorderStyle.None;
                textBox.BackColor = AppBackground;
                textBox.ForeColor = TextMuted;
                break;
            case TextBox textBox when textBox.Multiline && textBox.BackColor.ToArgb() == UiTheme.LogSurface.ToArgb():
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case TextBox textBox:
                textBox.BorderStyle = BorderStyle.FixedSingle;
                textBox.BackColor = Surface;
                textBox.ForeColor = TextPrimary;
                break;
            case ComboBox comboBox:
                comboBox.FlatStyle = FlatStyle.Flat;
                comboBox.BackColor = Surface;
                comboBox.ForeColor = TextPrimary;
                break;
            case CheckBox checkBox:
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.ForeColor = TextPrimary;
                break;
            case ProgressBar progressBar:
                progressBar.BackColor = Color.FromArgb(231, 237, 243);
                progressBar.ForeColor = Accent;
                break;
            case TableLayoutPanel table:
                table.BackColor = HasSurfaceAncestor(table) ? Surface : AppBackground;
                break;
            case FlowLayoutPanel flow:
                flow.BackColor = HasSurfaceAncestor(flow) ? Surface : AppBackground;
                break;
            case Panel panel when panel.Dock == DockStyle.Left:
                panel.BackColor = AppBackground;
                break;
        }

        foreach (Control child in control.Controls)
        {
            StyleTree(child);
        }
    }

    private static bool HasSurfaceAncestor(Control control)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is SurfaceGroupBox)
            {
                return true;
            }
            if (parent is TabPage)
            {
                return false;
            }
        }

        return false;
    }

    private static void StyleButton(Button button)
    {
        var lower = button.Text.ToLowerInvariant();
        var back = lower.Contains("start") ? Success :
            lower.Contains("stop") || lower.Contains("disable") || lower.Contains("delete") ? Danger :
            NeutralButton;
        var fore = back == NeutralButton ? TextPrimary : Color.White;

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.BackColor = back;
        button.ForeColor = fore;
        button.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9, back == NeutralButton ? FontStyle.Regular : FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = back == NeutralButton ? 1 : 0;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = back == NeutralButton ? NeutralButtonHover : ControlPaint.Light(back);
        button.FlatAppearance.MouseDownBackColor = back == NeutralButton ? Border : ControlPaint.Dark(back);
        var minimumHeight = Px(button, ConstrainedUi ? 30 : CompactUi ? 32 : 34);
        if (CompactUi)
        {
            var width = button.MinimumSize.Width == 0 ? 0 : Math.Min(button.MinimumSize.Width, Px(button, ConstrainedUi ? 86 : 96));
            button.MinimumSize = new Size(width, Math.Max(button.MinimumSize.Height, minimumHeight));
            button.Padding = Px(button, new Padding(7, 0, 7, 0));
        }
        if (button.Height < minimumHeight)
        {
            button.Height = minimumHeight;
        }

    }

    private static T? ReadPrivate<T>(object instance, string fieldName) where T : class
    {
        return instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance) as T;
    }

    private static int Px(Control control, int logical)
    {
        return Math.Max(0, (int)Math.Round(logical * EffectiveScale(control)));
    }

    private static float Pt(Control control, float logical)
    {
        if (!int.TryParse(Environment.GetEnvironmentVariable("STADIAX_UI_RUNTIME_SCALE_PERCENT"), out var percent))
        {
            return logical;
        }

        return logical * Math.Clamp(percent, 100, 200) / 100F * DisplayLayout.BaseDpi / Math.Max(DisplayLayout.BaseDpi, control.DeviceDpi);
    }

    private static double EffectiveScale(Control control)
    {
        return int.TryParse(Environment.GetEnvironmentVariable("STADIAX_UI_RUNTIME_SCALE_PERCENT"), out var percent)
            ? Math.Clamp(percent, 100, 200) / 100D
            : control.DeviceDpi / (double)DisplayLayout.BaseDpi;
    }

    private static Point Px(Control control, Point logical)
    {
        return new Point(Px(control, logical.X), Px(control, logical.Y));
    }

    private static Padding Px(Control control, Padding logical)
    {
        return new Padding(
            Px(control, logical.Left),
            Px(control, logical.Top),
            Px(control, logical.Right),
            Px(control, logical.Bottom));
    }

    private static string SubItemText(ListViewItem item, int index)
    {
        return item.SubItems.Count > index ? item.SubItems[index].Text.Trim() : "";
    }

    private static void TrackTimer(Form form, System.Windows.Forms.Timer timer)
    {
        if (!Timers.TryGetValue(form, out var timers))
        {
            timers = new List<System.Windows.Forms.Timer>();
            Timers[form] = timers;
            form.Disposed += (_, _) =>
            {
                if (!Timers.Remove(form, out var ownedTimers))
                {
                    return;
                }

                foreach (var ownedTimer in ownedTimers)
                {
                    ownedTimer.Stop();
                    ownedTimer.Dispose();
                }
            };
        }

        timers.Add(timer);
    }
}
