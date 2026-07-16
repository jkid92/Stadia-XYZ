using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal static class UiLayoutAudit
{
    public static int Run(AppPaths paths)
    {
        var density = MainForm.IsConstrainedUi()
            ? "constrained"
            : MainForm.IsCompactUi() ? "compact" : "comfortable";
        var issues = new HashSet<string>(StringComparer.Ordinal);
        var observations = new List<string>();

        using var form = new MainForm(paths, auditMode: true)
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty,
            Opacity = 1
        };
        MainFormRuntimeTuner.ApplyForAudit(form);
        form.Show();
        Application.DoEvents();

        var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault(control => control.TabPages.Count > 0);
        if (tabs is null)
        {
            issues.Add("Main tab control was not created.");
        }
        else
        {
            try
            {
                var snapshotPath = SaveSnapshot(form, tabs, paths, density);
                observations.Add($"snapshot={snapshotPath}");
            }
            catch (Exception ex)
            {
                observations.Add($"snapshot=unavailable ({ex.GetType().Name}: {Shorten(ex.Message)})");
                AppDiagnosticsLogger.Record(
                    "UI_LAYOUT_SNAPSHOT_WARN",
                    ("density", density),
                    ("exceptionType", ex.GetType().FullName),
                    ("error", ex.ToString()));
            }
            foreach (var requestedSize in AuditSizes(density))
            {
                form.Size = requestedSize;
                LayoutTree(form);
                observations.Add($"requested={requestedSize.Width}x{requestedSize.Height} actual={form.Width}x{form.Height} client={form.ClientSize.Width}x{form.ClientSize.Height}");

                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    LayoutTree(form);
                    Application.DoEvents();
                    ValidateTree(form, $"{requestedSize.Width}x{requestedSize.Height}/{page.Text}", issues);
                }
            }
        }

        Directory.CreateDirectory(paths.LogDirectory);
        var reportPath = Path.Combine(paths.LogDirectory, $"ui-layout-audit-{density}.txt");
        var lines = new List<string>
        {
            $"Stadia X UI layout audit: {density}",
            $"Result: {(issues.Count == 0 ? "PASS" : "FAIL")}",
            $"Issues: {issues.Count}",
            "",
            "Scenarios:"
        };
        lines.AddRange(observations.Select(value => "- " + value));
        lines.Add("");
        lines.Add("Findings:");
        lines.AddRange(issues.Count == 0 ? new[] { "- none" } : issues.Select(value => "- " + value));
        File.WriteAllLines(reportPath, lines);

        AppDiagnosticsLogger.Record(
            issues.Count == 0 ? "UI_LAYOUT_AUDIT_PASS" : "UI_LAYOUT_AUDIT_FAIL",
            ("density", density),
            ("issues", issues.Count.ToString()),
            ("report", reportPath));
        return issues.Count == 0 ? 0 : 1;
    }

    private static string SaveSnapshot(Form form, TabControl tabs, AppPaths paths, string density)
    {
        form.Size = SnapshotSize(density);
        tabs.SelectedIndex = 0;
        LayoutTree(form);
        Application.DoEvents();

        var snapshotPath = Path.Combine(paths.LogDirectory, $"ui-layout-audit-{density}.png");
        Directory.CreateDirectory(paths.LogDirectory);
        form.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        try
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        }
        catch (ArgumentException)
        {
            using var graphics = Graphics.FromImage(bitmap);
            var hdc = graphics.GetHdc();
            try
            {
                if (!PrintWindow(form.Handle, hdc, PrintWindowRenderFullContent))
                {
                    throw new InvalidOperationException("Neither DrawToBitmap nor PrintWindow could capture the UI.");
                }
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        bitmap.Save(snapshotPath, System.Drawing.Imaging.ImageFormat.Png);
        return snapshotPath;
    }

    private static Size SnapshotSize(string density)
    {
        return density switch
        {
            "constrained" => new Size(1024, 720),
            "comfortable" => new Size(1280, 820),
            _ => new Size(1120, 720)
        };
    }

    private static IReadOnlyList<Size> AuditSizes(string density)
    {
        return density switch
        {
            "constrained" => new[] { new Size(820, 520), new Size(900, 600), new Size(1024, 720) },
            "comfortable" => new[] { new Size(1100, 620), new Size(1280, 820), new Size(1600, 900) },
            _ => new[] { new Size(1080, 560), new Size(1120, 720), new Size(1280, 720) }
        };
    }

    private static void ValidateTree(Control root, string scenario, ISet<string> issues)
    {
        foreach (var control in Descendants(root).Where(control => control.Visible && control.Width > 0 && control.Height > 0))
        {
            ValidateBounds(control, scenario, issues);
            switch (control)
            {
                case Button button:
                    ValidateSingleLineText(button, button.Text, button.Font, button.Padding.Horizontal + 8, scenario, issues);
                    break;
                case ModernTabButton tab:
                    using (var selectedFont = new Font(tab.Font, FontStyle.Bold))
                    {
                        ValidateSingleLineText(tab, tab.Text, selectedFont, 12, scenario, issues);
                    }
                    break;
                case Label label when !label.AutoSize:
                    ValidateLabelText(label, scenario, issues);
                    break;
                case GroupBox group:
                    ValidateSingleLineText(group, group.Text, group.Font, 22, scenario, issues);
                    break;
            }
        }
    }

    private static void ValidateBounds(Control control, string scenario, ISet<string> issues)
    {
        var parent = control.Parent;
        if (parent is null ||
            parent is ScrollableControl { AutoScroll: true } ||
            parent.ClientSize.Width <= 0 ||
            parent.ClientSize.Height <= 0)
        {
            return;
        }

        const int tolerance = 2;
        if (control.Left < -tolerance || control.Top < -tolerance ||
            control.Right > parent.ClientSize.Width + tolerance ||
            control.Bottom > parent.ClientSize.Height + tolerance)
        {
            issues.Add($"{scenario}: {Describe(control)} exceeds {Describe(parent)} bounds ({control.Bounds} in {parent.ClientSize}).");
        }
    }

    private static void ValidateSingleLineText(
        Control control,
        string text,
        Font font,
        int reservedWidth,
        string scenario,
        ISet<string> issues)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var availableWidth = Math.Max(0, control.ClientSize.Width - reservedWidth);
        var measured = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        if (measured.Width > availableWidth || measured.Height > control.ClientSize.Height + 2)
        {
            issues.Add($"{scenario}: text '{Shorten(text)}' does not fit {Describe(control)} ({measured.Width}x{measured.Height} in {availableWidth}x{control.ClientSize.Height}).");
        }
    }

    private static void ValidateLabelText(Label label, string scenario, ISet<string> issues)
    {
        if (string.IsNullOrWhiteSpace(label.Text))
        {
            return;
        }

        var available = new Size(
            Math.Max(1, label.ClientSize.Width - label.Padding.Horizontal),
            Math.Max(1, label.ClientSize.Height - label.Padding.Vertical));
        var flags = (label.AutoEllipsis ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak) |
                    TextFormatFlags.NoPadding;
        var measured = TextRenderer.MeasureText(
            label.Text,
            label.Font,
            new Size(available.Width, int.MaxValue),
            flags);
        if (measured.Height > available.Height + 2)
        {
            issues.Add($"{scenario}: label '{Shorten(label.Text)}' needs {measured.Height}px but {available.Height}px is available.");
        }
    }

    private static void LayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls)
        {
            LayoutTree(child);
        }
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static string Describe(Control control)
    {
        return string.IsNullOrWhiteSpace(control.Name) ? control.GetType().Name : $"{control.GetType().Name} '{control.Name}'";
    }

    private static string Shorten(string value)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= 48 ? value : value[..48] + "...";
    }

    private const uint PrintWindowRenderFullContent = 0x00000002;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr windowHandle, IntPtr deviceContext, uint flags);
}
