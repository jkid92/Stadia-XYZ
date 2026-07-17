using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal static class UiLayoutAudit
{
    public static int Run(AppPaths paths)
    {
        var density = MainForm.IsConstrainedUi()
            ? "constrained"
            : MainForm.IsCompactUi() ? "compact" : "comfortable";
        var scalePercent = DisplayLayout.AuditScalePercent;
        var language = UiLocalization.Current.LanguageCode;
        var reportKey = $"{density}-{language}-dpi{scalePercent}";
        var issues = new HashSet<string>(StringComparer.Ordinal);
        var observations = new List<string> { $"dpi-scale={scalePercent}%" };
        ValidateDisplayFitScenarios(issues, observations);

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
        observations.Add($"host-dpi={form.DeviceDpi}");
        Environment.SetEnvironmentVariable("STADIAX_UI_RUNTIME_SCALE_PERCENT", scalePercent.ToString());
        var targetDpi = DisplayLayout.BaseDpi * scalePercent / 100F;
        var simulationScale = targetDpi / Math.Max(DisplayLayout.BaseDpi, form.DeviceDpi);
        if (Math.Abs(simulationScale - 1F) > 0.01F)
        {
            form.SuspendLayout();
            form.Scale(new SizeF(simulationScale, simulationScale));
            form.ResumeLayout(performLayout: true);
            Application.DoEvents();
        }

        var tabs = Descendants(form).OfType<TabControl>().FirstOrDefault(control => control.TabPages.Count > 0);
        if (tabs is null)
        {
            issues.Add("Main tab control was not created.");
        }
        else
        {
            try
            {
                var snapshotPath = SaveSnapshot(form, tabs, paths, density, reportKey);
                observations.Add($"snapshot={snapshotPath}");
                if (density == "comfortable" && scalePercent == 100)
                {
                    observations.AddRange(SaveFeatureSnapshots(form, tabs, paths).Select(path => $"feature-snapshot={path}"));
                }
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
            foreach (var logicalSize in AuditSizes(density))
            {
                var requestedSize = AtScale(logicalSize, scalePercent);
                form.Size = requestedSize;
                LayoutTree(form);
                observations.Add($"requested-logical={logicalSize.Width}x{logicalSize.Height} target={requestedSize.Width}x{requestedSize.Height} actual={form.Width}x{form.Height} client={form.ClientSize.Width}x{form.ClientSize.Height}");

                foreach (TabPage page in tabs.TabPages)
                {
                    tabs.SelectedTab = page;
                    LayoutTree(form);
                    Application.DoEvents();
                    ValidateTree(form, $"{logicalSize.Width}x{logicalSize.Height}@{scalePercent}%/{page.Text}", issues);
                }
            }
        }

        Directory.CreateDirectory(paths.LogDirectory);
        var reportPath = Path.Combine(paths.LogDirectory, $"ui-layout-audit-{reportKey}.txt");
        var lines = new List<string>
        {
            $"Stadia X UI layout audit: {density} at {scalePercent}%",
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
            ("language", language),
            ("dpiScale", scalePercent.ToString()),
            ("issues", issues.Count.ToString()),
            ("report", reportPath));
        return issues.Count == 0 ? 0 : 1;
    }

    private static string SaveSnapshot(Form form, TabControl tabs, AppPaths paths, string density, string reportKey)
    {
        form.Size = AtScale(SnapshotSize(density), DisplayLayout.AuditScalePercent);
        tabs.SelectedIndex = 0;
        return CaptureSnapshot(form, paths, $"ui-layout-audit-{reportKey}.png");
    }

    private static void ValidateDisplayFitScenarios(ISet<string> issues, ICollection<string> observations)
    {
        var scenarios = new[]
        {
            (Name: "primary-100", Area: new Rectangle(0, 0, 1920, 1040), Window: new Rectangle(120, 80, 1280, 820)),
            (Name: "left-125", Area: new Rectangle(-1920, 0, 1920, 1040), Window: new Rectangle(-2350, 120, 1800, 1200)),
            (Name: "upper-150", Area: new Rectangle(0, -1440, 2560, 1400), Window: new Rectangle(2200, -1700, 1700, 1100)),
            (Name: "right-200", Area: new Rectangle(3200, 0, 2560, 1960), Window: new Rectangle(5600, 1700, 2600, 1800))
        };

        foreach (var scenario in scenarios)
        {
            var fitted = DisplayLayout.FitWindow(scenario.Window, new Size(900, 560), scenario.Area);
            var inside = fitted.Bounds.Left >= scenario.Area.Left &&
                         fitted.Bounds.Top >= scenario.Area.Top &&
                         fitted.Bounds.Right <= scenario.Area.Right &&
                         fitted.Bounds.Bottom <= scenario.Area.Bottom;
            observations.Add($"display={scenario.Name} fitted={fitted.Bounds}");
            if (!inside)
            {
                issues.Add($"Display scenario {scenario.Name}: fitted bounds {fitted.Bounds} exceed {scenario.Area}.");
            }
        }
    }

    private static IReadOnlyList<string> SaveFeatureSnapshots(Form form, TabControl tabs, AppPaths paths)
    {
        var targets = new[]
        {
            (Name: "Doctor", File: "ui-layout-audit-comfortable-doctor.png"),
            (Name: "Bluetooth", File: "ui-layout-audit-comfortable-devices.png"),
            (Name: "Windows Native", File: "ui-layout-audit-comfortable-controllers.png"),
            (Name: "Controller Test", File: "ui-layout-audit-comfortable-test.png")
        };
        var pathsWritten = new List<string>();
        foreach (var target in targets)
        {
            var page = tabs.TabPages.Cast<TabPage>().FirstOrDefault(candidate => candidate.Name == target.Name);
            if (page is null)
            {
                continue;
            }

            tabs.SelectedTab = page;
            pathsWritten.Add(CaptureSnapshot(form, paths, target.File));
        }

        tabs.SelectedIndex = 0;
        return pathsWritten;
    }

    private static string CaptureSnapshot(Form form, AppPaths paths, string fileName)
    {
        LayoutTree(form);
        Application.DoEvents();

        var snapshotPath = Path.Combine(paths.LogDirectory, fileName);
        Directory.CreateDirectory(paths.LogDirectory);
        form.Refresh();
        Application.DoEvents();
        using var bitmap = new Bitmap(Math.Max(1, form.Width), Math.Max(1, form.Height));
        var captured = false;
        using (var graphics = Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try
            {
                captured = PrintWindow(form.Handle, hdc, PrintWindowRenderFullContent);
            }
            finally
            {
                graphics.ReleaseHdc(hdc);
            }
        }
        if (!captured)
        {
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
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

    private static Size AtScale(Size logical, int percent)
    {
        return new Size(
            Math.Max(1, (int)Math.Round(logical.Width * percent / 100d)),
            Math.Max(1, (int)Math.Round(logical.Height * percent / 100d)));
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
            HasAutoScrollAncestor(control) ||
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

    private static bool HasAutoScrollAncestor(Control control)
    {
        for (var ancestor = control.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is ScrollableControl { AutoScroll: true })
            {
                return true;
            }
        }

        return false;
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
