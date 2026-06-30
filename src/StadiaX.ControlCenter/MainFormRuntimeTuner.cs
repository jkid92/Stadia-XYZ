using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StadiaX.ControlCenter;

internal static class MainFormRuntimeTuner
{
    private static readonly HashSet<Form> PatchedForms = new();
    private static readonly Dictionary<Form, List<System.Windows.Forms.Timer>> Timers = new();
    private static readonly Dictionary<Control, Bitmap> OwnedBitmaps = new();

    private static readonly Color AppBackground = Color.FromArgb(241, 245, 249);
    private static readonly Color HeaderTop = Color.FromArgb(16, 29, 45);
    private static readonly Color HeaderBottom = Color.FromArgb(24, 43, 64);
    private static readonly Color Surface = Color.White;
    private static readonly Color TextPrimary = Color.FromArgb(24, 33, 48);
    private static readonly Color TextMuted = Color.FromArgb(92, 106, 126);
    private static readonly Color Border = Color.FromArgb(203, 213, 225);
    private static readonly Color NeutralButton = Color.FromArgb(248, 250, 252);
    private static readonly Color NeutralButtonHover = Color.FromArgb(226, 232, 240);
    private static readonly Color Accent = Color.FromArgb(23, 184, 178);
    private static readonly Color Success = Color.FromArgb(36, 132, 85);
    private static readonly Color Danger = Color.FromArgb(184, 64, 64);

    private static bool CompactUi => MainForm.IsCompactUi();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += (_, _) => PatchOpenForms();
    }

    private static void PatchOpenForms()
    {
        foreach (Form form in Application.OpenForms)
        {
            if (form.GetType().Name != "MainForm" || !PatchedForms.Add(form))
            {
                continue;
            }

            PatchVisualDesign(form);
            PatchBluetoothLayout(form);
            PatchLinuxSummary(form);
            PatchControllerVisualizerRefresh(form);
        }
    }

    private static void PatchVisualDesign(Form form)
    {
        form.BackColor = AppBackground;
        form.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9F);

        PatchHeader(form);
        foreach (Control control in form.Controls)
        {
            StyleTree(control);
        }
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
        header.Height = compact ? 80 : 96;
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
            var logoSize = compact ? 44 : 54;
            var logoBitmap = LoadHeaderLogoBitmap(form, logoSize);
            var logo = new PictureBox
            {
                Name = "StadiaXBrandLogo",
                Image = logoBitmap,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Location = compact ? new Point(22, 12) : new Point(20, 14),
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
                if (label.Text.Equals("Stadia X", StringComparison.OrdinalIgnoreCase))
                {
                    label.AutoSize = true;
                    label.Location = compact ? new Point(86, 8) : new Point(100, 8);
                    label.Font = new Font("Segoe UI", compact ? 18 : 21, FontStyle.Bold);
                    label.ForeColor = Color.White;
                }
                else if (label.Text.Contains("WinForms", StringComparison.OrdinalIgnoreCase) ||
                         label.Text.Contains("Bluetooth controller bridge", StringComparison.OrdinalIgnoreCase))
                {
                    label.Text = "Bluetooth controller bridge";
                    label.AutoSize = true;
                    label.Location = compact ? new Point(88, 50) : new Point(102, 60);
                    label.Font = new Font("Segoe UI", compact ? 8.25F : 9F);
                    label.ForeColor = Color.FromArgb(202, 213, 225);
                }
                else if (label.Text.StartsWith("Battery:", StringComparison.OrdinalIgnoreCase))
                {
                    label.AutoSize = false;
                    label.Size = new Size(Math.Min(compact ? 440 : 520, Math.Max(compact ? 220 : 260, header.Width - (compact ? 360 : 430))), compact ? 20 : 22);
                    label.Location = new Point(header.Width - label.Width - 28, compact ? 50 : 56);
                    label.Font = new Font("Segoe UI", compact ? 8.25F : 9F, FontStyle.Bold);
                    label.ForeColor = Color.FromArgb(202, 213, 225);
                    label.TextAlign = ContentAlignment.MiddleRight;
                }
                else
                {
                    label.AutoSize = false;
                    label.Size = new Size(Math.Min(compact ? 440 : 520, Math.Max(compact ? 220 : 260, header.Width - (compact ? 360 : 430))), compact ? 24 : 28);
                    label.Location = new Point(header.Width - label.Width - 28, compact ? 16 : 18);
                    label.Font = new Font("Segoe UI", compact ? 9.5F : 11F, FontStyle.Bold);
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
            linuxActions.MinimumSize = new Size(0, 72);
            linuxGroup.Controls.Add(linuxActions);
            linuxActions.BringToFront();
        }

        if (layout.RowStyles.Count > 0)
        {
            layout.RowStyles[0] = new RowStyle(SizeType.Absolute, CompactUi ? 190 : 220);
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
            Height = CompactUi ? 26 : 30,
            Padding = new Padding(8, 6, 0, 0),
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
            case GroupBox group:
                group.BackColor = Surface;
                group.ForeColor = TextPrimary;
                group.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9, FontStyle.Bold);
                break;
            case ListView list:
                list.GridLines = false;
                list.BackColor = Surface;
                list.ForeColor = TextPrimary;
                list.BorderStyle = BorderStyle.FixedSingle;
                list.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9);
                break;
            case TabControl tabs:
                tabs.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9);
                tabs.Padding = CompactUi ? new Point(10, 5) : new Point(14, 8);
                tabs.Multiline = false;
                break;
            case TextBox textBox when textBox.Multiline && textBox.BackColor.ToArgb() == Color.FromArgb(20, 24, 32).ToArgb():
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
            case Panel panel when panel.Dock == DockStyle.Left:
                panel.BackColor = AppBackground;
                break;
        }

        foreach (Control child in control.Controls)
        {
            StyleTree(child);
        }
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
        button.Font = new Font("Segoe UI", CompactUi ? 8.25F : 9, FontStyle.Bold);
        button.Cursor = Cursors.Hand;
        button.FlatAppearance.BorderSize = back == NeutralButton ? 1 : 0;
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = back == NeutralButton ? NeutralButtonHover : ControlPaint.Light(back);
        button.FlatAppearance.MouseDownBackColor = back == NeutralButton ? Border : ControlPaint.Dark(back);
        if (CompactUi)
        {
            var width = button.MinimumSize.Width == 0 ? 0 : Math.Min(button.MinimumSize.Width, 96);
            button.MinimumSize = new Size(width, Math.Max(button.MinimumSize.Height, 34));
            button.Padding = new Padding(7, 0, 7, 0);
        }
        if (button.Height < 34)
        {
            button.Height = 34;
        }
    }

    private static T? ReadPrivate<T>(object instance, string fieldName) where T : class
    {
        return instance.GetType()
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(instance) as T;
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
