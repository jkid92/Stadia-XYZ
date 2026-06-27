using System.Reflection;
using System.Runtime.CompilerServices;

namespace StadiaX.ControlCenter;

internal static class MainFormRuntimeTuner
{
    private static readonly HashSet<Form> PatchedForms = new();
    private static readonly Dictionary<Form, List<System.Windows.Forms.Timer>> Timers = new();

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

            PatchLinuxSummary(form);
            PatchControllerVisualizerRefresh(form);
        }
    }

    private static void PatchLinuxSummary(Form form)
    {
        var formType = form.GetType();
        if (formType.GetField("_linuxBluetoothSummaryLabel", BindingFlags.Instance | BindingFlags.NonPublic) is not null)
        {
            return;
        }

        if (ReadPrivate<ListView>(form, "_linuxBluetoothList") is not { } list || list.Parent is not Control parent)
        {
            return;
        }

        var summary = new Label
        {
            Name = "LinuxBluetoothSummaryLabel",
            Dock = DockStyle.Top,
            Height = 28,
            Padding = new Padding(8, 5, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 70, 70),
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

        summary.ForeColor = items.Length == 0 ? Color.FromArgb(180, 45, 45) : Color.FromArgb(34, 120, 72);
        summary.Text = items.Length == 0
            ? "Linux devices: none returned. Start the bridge, then press Refresh or Scan."
            : $"Linux devices: {items.Length} visible, {connected} connected, {stadia} Stadia" +
              (string.IsNullOrWhiteSpace(battery) ? "" : $" - battery {battery}");
    }

    private static void PatchControllerVisualizerRefresh(Form form)
    {
        var formType = form.GetType();
        if (formType.GetField("_controllerTimer", BindingFlags.Instance | BindingFlags.NonPublic) is not null)
        {
            return;
        }

        var tabs = ReadPrivate<TabControl>(form, "_tabs");
        var native = ReadPrivate<NativeControlServices>(form, "_native");
        var update = formType.GetMethod("UpdateControllerVisualizer", BindingFlags.Instance | BindingFlags.NonPublic);
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
