using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal sealed class VigemVirtualGamepadBusFactory : IVirtualGamepadBusFactory
{
    public IVirtualGamepadBus Create(int controllerCount)
    {
        return new VigemVirtualGamepadBus(controllerCount);
    }
}

internal sealed class VigemVirtualGamepadBus : IVirtualGamepadBus
{
    private const int MaxControllers = 4;

    private readonly IntPtr[] _targets;
    private readonly bool[] _targetsAdded;
    private readonly bool[] _notificationsRegistered;
    private readonly VigemNative.X360Notification _rumbleCallback;
    private readonly List<string> _initializationWarnings = new();

    private IntPtr _client;
    private bool _connected;
    private bool _disposed;

    public VigemVirtualGamepadBus(int controllerCount)
    {
        ControllerCount = Math.Clamp(controllerCount, 1, MaxControllers);
        _targets = new IntPtr[ControllerCount];
        _targetsAdded = new bool[ControllerCount];
        _notificationsRegistered = new bool[ControllerCount];
        _rumbleCallback = OnRumble;

        try
        {
            Initialize();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public int ControllerCount { get; }

    public IReadOnlyList<string> InitializationWarnings => _initializationWarnings;

    public event Action<int, byte, byte>? RumbleRequested;

    public bool TryUpdate(int controllerIndex, VirtualGamepadReport report, out string error)
    {
        if (!TryGetTarget(controllerIndex, out var target, out error))
        {
            return false;
        }

        try
        {
            var result = VigemNative.vigem_target_x360_update(_client, target, report);
            if (VigemNative.Success(result))
            {
                error = "";
                return true;
            }

            error = $"0x{result:X8}";
            return false;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryNeutralize(int controllerIndex, out string error)
    {
        return TryUpdate(controllerIndex, default, out error);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        RumbleRequested = null;
        for (var i = 0; i < _targets.Length; i++)
        {
            var target = _targets[i];
            if (target == IntPtr.Zero)
            {
                continue;
            }

            if (_notificationsRegistered[i])
            {
                try { VigemNative.vigem_target_x360_unregister_notification(target); } catch { }
            }
            if (_targetsAdded[i] && _client != IntPtr.Zero)
            {
                try { VigemNative.vigem_target_remove(_client, target); } catch { }
            }
            try { VigemNative.vigem_target_free(target); } catch { }
            _targets[i] = IntPtr.Zero;
            _targetsAdded[i] = false;
            _notificationsRegistered[i] = false;
        }

        if (_client != IntPtr.Zero)
        {
            if (_connected)
            {
                try { VigemNative.vigem_disconnect(_client); } catch { }
            }
            try { VigemNative.vigem_free(_client); } catch { }
            _client = IntPtr.Zero;
            _connected = false;
        }
    }

    private void Initialize()
    {
        _client = VigemNative.vigem_alloc();
        if (_client == IntPtr.Zero)
        {
            throw new InvalidOperationException("ViGEm client allocation failed.");
        }

        var connect = VigemNative.vigem_connect(_client);
        if (!VigemNative.Success(connect))
        {
            throw new InvalidOperationException($"ViGEmBus init failed: 0x{connect:X8}. Is the driver installed?");
        }
        _connected = true;

        for (var i = 0; i < ControllerCount; i++)
        {
            var target = VigemNative.vigem_target_x360_alloc();
            if (target == IntPtr.Zero)
            {
                throw new InvalidOperationException($"ViGEm virtual pad {i + 1} allocation failed.");
            }

            _targets[i] = target;
            var add = VigemNative.vigem_target_add(_client, target);
            if (!VigemNative.Success(add))
            {
                throw new InvalidOperationException($"ViGEm virtual pad {i + 1} init failed: 0x{add:X8}.");
            }

            _targetsAdded[i] = true;
            var notify = VigemNative.vigem_target_x360_register_notification(
                _client,
                target,
                _rumbleCallback,
                new IntPtr(i));
            if (!VigemNative.Success(notify))
            {
                _initializationWarnings.Add($"P{i + 1} rumble notification registration failed: 0x{notify:X8}");
            }
            else
            {
                _notificationsRegistered[i] = true;
            }
        }
    }

    private bool TryGetTarget(int controllerIndex, out IntPtr target, out string error)
    {
        target = IntPtr.Zero;
        if (_disposed)
        {
            error = "Virtual controller bus is disposed.";
            return false;
        }

        if (controllerIndex < 0 || controllerIndex >= _targets.Length)
        {
            error = $"Virtual controller index {controllerIndex} is outside the active range.";
            return false;
        }

        target = _targets[controllerIndex];
        if (_client == IntPtr.Zero || target == IntPtr.Zero)
        {
            error = $"Virtual controller P{controllerIndex + 1} is not initialized.";
            return false;
        }

        error = "";
        return true;
    }

    private void OnRumble(
        IntPtr client,
        IntPtr target,
        byte largeMotor,
        byte smallMotor,
        byte ledNumber,
        IntPtr userData)
    {
        if (_disposed)
        {
            return;
        }

        var controllerIndex = userData.ToInt32();
        if (controllerIndex < 0 || controllerIndex >= ControllerCount)
        {
            return;
        }

        RumbleRequested?.Invoke(controllerIndex, largeMotor, smallMotor);
    }
}

internal static class VigemNative
{
    private const uint VigEmErrorNone = 0x20000000;

    public static bool Success(uint error) => error == VigEmErrorNone;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate void X360Notification(
        IntPtr client,
        IntPtr target,
        byte largeMotor,
        byte smallMotor,
        byte ledNumber,
        IntPtr userData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr vigem_alloc();

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_free(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_connect(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_disconnect(IntPtr vigem);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr vigem_target_x360_alloc();

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_target_free(IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_add(IntPtr vigem, IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_remove(IntPtr vigem, IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_x360_register_notification(
        IntPtr vigem,
        IntPtr target,
        X360Notification notification,
        IntPtr userData);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern void vigem_target_x360_unregister_notification(IntPtr target);

    [DefaultDllImportSearchPaths(DllImportSearchPath.ApplicationDirectory | DllImportSearchPath.AssemblyDirectory)]
    [DllImport("ViGEmClient.dll", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint vigem_target_x360_update(
        IntPtr vigem,
        IntPtr target,
        VirtualGamepadReport report);

    internal static void RunSelfTest()
    {
        if (Marshal.SizeOf<VirtualGamepadReport>() != 12 ||
            Marshal.OffsetOf<VirtualGamepadReport>(nameof(VirtualGamepadReport.Buttons)).ToInt32() != 0 ||
            Marshal.OffsetOf<VirtualGamepadReport>(nameof(VirtualGamepadReport.LeftTrigger)).ToInt32() != 2 ||
            Marshal.OffsetOf<VirtualGamepadReport>(nameof(VirtualGamepadReport.ThumbLX)).ToInt32() != 4 ||
            Marshal.OffsetOf<VirtualGamepadReport>(nameof(VirtualGamepadReport.ThumbRY)).ToInt32() != 10)
        {
            throw new InvalidOperationException("Virtual Xbox 360 report layout self-test failed.");
        }
    }
}
