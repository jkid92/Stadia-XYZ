using System.Text.Json;

namespace StadiaX.ControlCenter;

internal sealed class ControllerTelemetryWriter
{
    private const int MaxControllers = 4;

    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private readonly ControllerTelemetryState[] _telemetry = Enumerable.Range(0, MaxControllers)
        .Select(_ => new ControllerTelemetryState())
        .ToArray();

    private DateTimeOffset _lastWrite = DateTimeOffset.MinValue;

    public ControllerTelemetryWriter(AppPaths paths)
    {
        _paths = paths;
    }

    public void Write(int controllerIndex, ControllerState state)
    {
        WriteCore(controllerIndex, state, connected: true, force: false);
    }

    public void Deactivate(int controllerIndex)
    {
        WriteCore(controllerIndex, default, connected: false, force: true);
    }

    private void WriteCore(int controllerIndex, ControllerState state, bool connected, bool force)
    {
        if (controllerIndex < 0 || controllerIndex >= MaxControllers)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var tickMs = Environment.TickCount64;
        ControllerTelemetryState[] snapshot;

        lock (_lock)
        {
            var telemetry = _telemetry[controllerIndex];
            telemetry.State = state;
            telemetry.Connected = connected;
            if (connected)
            {
                telemetry.Packets++;
                if (telemetry.FirstSeenMs == 0)
                {
                    telemetry.FirstSeenMs = tickMs;
                }

                telemetry.LastSeenMs = tickMs;
            }

            if (!force && now - _lastWrite < TimeSpan.FromMilliseconds(33))
            {
                return;
            }

            _lastWrite = now;
            snapshot = _telemetry.Select(item => item.Clone()).ToArray();
        }

        Directory.CreateDirectory(_paths.LogDirectory);
        var tempPath = _paths.ControllerState + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("timestamp", tickMs);
            writer.WriteNumber("active_controller", controllerIndex);
            WriteButtonsJson(writer, state);
            WriteAxesJson(writer, state);
            writer.WriteStartArray("controllers");
            for (var i = 0; i < snapshot.Length; i++)
            {
                var telemetry = snapshot[i];
                var active = telemetry.Connected && telemetry.LastSeenMs > 0 && tickMs - telemetry.LastSeenMs < 5000;
                var elapsed = telemetry.FirstSeenMs > 0 && telemetry.LastSeenMs >= telemetry.FirstSeenMs
                    ? (telemetry.LastSeenMs - telemetry.FirstSeenMs) / 1000d
                    : 0d;
                var pps = active && elapsed > 0 ? telemetry.Packets / elapsed : 0d;

                writer.WriteStartObject();
                writer.WriteNumber("index", i);
                writer.WriteBoolean("active", active);
                writer.WriteNumber("last_seen_ms", telemetry.LastSeenMs);
                writer.WriteNumber("last_seen_age_ms", telemetry.LastSeenMs > 0 ? tickMs - telemetry.LastSeenMs : 0);
                writer.WriteNumber("packets", telemetry.Packets);
                writer.WriteNumber("pps", Math.Round(pps, 2));
                WriteButtonsJson(writer, telemetry.State);
                WriteAxesJson(writer, telemetry.State);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        File.Move(tempPath, _paths.ControllerState, overwrite: true);
    }

    private static void WriteButtonsJson(Utf8JsonWriter writer, ControllerState state)
    {
        writer.WriteStartObject("buttons");
        writer.WriteBoolean("a", state.Has(ButtonBits.A));
        writer.WriteBoolean("b", state.Has(ButtonBits.B));
        writer.WriteBoolean("x", state.Has(ButtonBits.X));
        writer.WriteBoolean("y", state.Has(ButtonBits.Y));
        writer.WriteBoolean("lb", state.Has(ButtonBits.Lb));
        writer.WriteBoolean("rb", state.Has(ButtonBits.Rb));
        writer.WriteBoolean("select", state.Has(ButtonBits.Select));
        writer.WriteBoolean("start", state.Has(ButtonBits.Start));
        writer.WriteBoolean("stadia", state.Has(ButtonBits.Stadia));
        writer.WriteBoolean("l3", state.Has(ButtonBits.L3));
        writer.WriteBoolean("r3", state.Has(ButtonBits.R3));
        writer.WriteBoolean("assistant", state.Has(ButtonBits.Assistant));
        writer.WriteBoolean("dpad_up", state.Has(ButtonBits.DpadUp));
        writer.WriteBoolean("dpad_down", state.Has(ButtonBits.DpadDown));
        writer.WriteBoolean("dpad_left", state.Has(ButtonBits.DpadLeft));
        writer.WriteBoolean("dpad_right", state.Has(ButtonBits.DpadRight));
        writer.WriteEndObject();
    }

    private static void WriteAxesJson(Utf8JsonWriter writer, ControllerState state)
    {
        writer.WriteStartObject("axes");
        writer.WriteNumber("trigger_left", state.TriggerLeft);
        writer.WriteNumber("trigger_right", state.TriggerRight);
        writer.WriteNumber("stick_lx", state.StickLeftX);
        writer.WriteNumber("stick_ly", state.StickLeftY);
        writer.WriteNumber("stick_rx", state.StickRightX);
        writer.WriteNumber("stick_ry", state.StickRightY);
        writer.WriteEndObject();
    }

    private sealed class ControllerTelemetryState
    {
        public bool Connected { get; set; }
        public ControllerState State { get; set; }
        public ulong Packets { get; set; }
        public long FirstSeenMs { get; set; }
        public long LastSeenMs { get; set; }

        public ControllerTelemetryState Clone()
        {
            return new ControllerTelemetryState
            {
                Connected = Connected,
                State = State,
                Packets = Packets,
                FirstSeenMs = FirstSeenMs,
                LastSeenMs = LastSeenMs
            };
        }
    }
}
