using System.Runtime.InteropServices;

namespace StadiaX.ControlCenter;

internal sealed record MacroHotkey(string Code, ushort Modifiers, ushort MainKey, bool Repeat);

internal static class MacroMappingLoader
{
    private const ushort ModAlt = 1;
    private const ushort ModControl = 2;
    private const ushort ModShift = 4;
    private const ushort ModWin = 8;

    private static readonly Dictionary<string, ushort> KeyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["F1"] = 0x70, ["F2"] = 0x71, ["F3"] = 0x72, ["F4"] = 0x73,
        ["F5"] = 0x74, ["F6"] = 0x75, ["F7"] = 0x76, ["F8"] = 0x77,
        ["F9"] = 0x78, ["F10"] = 0x79, ["F11"] = 0x7A, ["F12"] = 0x7B,
        ["0"] = 0x30, ["1"] = 0x31, ["2"] = 0x32, ["3"] = 0x33, ["4"] = 0x34,
        ["5"] = 0x35, ["6"] = 0x36, ["7"] = 0x37, ["8"] = 0x38, ["9"] = 0x39,
        ["A"] = 0x41, ["B"] = 0x42, ["C"] = 0x43, ["D"] = 0x44, ["E"] = 0x45,
        ["F"] = 0x46, ["G"] = 0x47, ["H"] = 0x48, ["I"] = 0x49, ["J"] = 0x4A,
        ["K"] = 0x4B, ["L"] = 0x4C, ["M"] = 0x4D, ["N"] = 0x4E, ["O"] = 0x4F,
        ["P"] = 0x50, ["Q"] = 0x51, ["R"] = 0x52, ["S"] = 0x53, ["T"] = 0x54,
        ["U"] = 0x55, ["V"] = 0x56, ["W"] = 0x57, ["X"] = 0x58, ["Y"] = 0x59,
        ["Z"] = 0x5A,
        ["TAB"] = 0x09, ["ESC"] = 0x1B, ["ESCAPE"] = 0x1B,
        ["SPACE"] = 0x20, ["ENTER"] = 0x0D, ["RETURN"] = 0x0D,
        ["BACKSPACE"] = 0x08, ["DELETE"] = 0x2E, ["INSERT"] = 0x2D,
        ["HOME"] = 0x24, ["END"] = 0x23, ["PAGEUP"] = 0x21, ["PAGEDOWN"] = 0x22,
        ["UP"] = 0x26, ["DOWN"] = 0x28, ["LEFT"] = 0x25, ["RIGHT"] = 0x27,
        ["PRINTSCREEN"] = 0x2C, ["SCROLLLOCK"] = 0x91, ["PAUSE"] = 0x13,
        ["CAPSLOCK"] = 0x14, ["NUMLOCK"] = 0x90, ["APPS"] = 0x5D,
        ["VOLUME_UP"] = 0xAF, ["VOLUME_DOWN"] = 0xAE, ["VOLUME_MUTE"] = 0xAD,
        ["MEDIA_NEXT"] = 0xB0, ["MEDIA_PREV"] = 0xB1, ["MEDIA_PLAY_PAUSE"] = 0xB3, ["MEDIA_STOP"] = 0xB2,
        ["NEXT_TRACK"] = 0xB0, ["PREV_TRACK"] = 0xB1,
        ["LWIN"] = 0x5B, ["RWIN"] = 0x5C,
        ["LCONTROL"] = 0xA2, ["RCONTROL"] = 0xA3,
        ["LMENU"] = 0xA4, ["RMENU"] = 0xA5,
        ["SHIFT"] = 0x10, ["CONTROL"] = 0x11, ["CTRL"] = 0x11, ["ALT"] = 0x12
    };

    public static IReadOnlyList<MacroHotkey> Load(
        string path,
        Action<string, object[]> logInfo,
        Action<string, object[]> logError)
    {
        if (!File.Exists(path))
        {
            logError("Config {0} not found, shortcuts disabled.", new object[] { Path.GetFileName(path) });
            return Array.Empty<MacroHotkey>();
        }

        var mappings = new List<MacroHotkey>();
        var inButtons = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                var end = line.IndexOf(']');
                inButtons = end > 0 &&
                            line.Substring(1, end - 1).Equals(
                                "Buttons",
                                StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inButtons)
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var code = line[..equals].Trim();
            var shortcut = line[(equals + 1)..].Trim();
            if (code.Length == 0 || code.Length >= 16)
            {
                continue;
            }

            var (modifiers, mainKey) = ParseShortcut(shortcut);
            mappings.Add(new MacroHotkey(code, modifiers, mainKey, IsRepeatable(mainKey)));
            if (mappings.Count >= 128)
            {
                break;
            }
        }

        logInfo(
            "Loaded {0} shortcut mappings from {1}",
            new object[] { mappings.Count, Path.GetFileName(path) });
        return mappings;
    }

    private static string StripComment(string line)
    {
        var semicolon = line.IndexOf(';');
        var hash = line.IndexOf('#');
        var cut = semicolon < 0 ? hash : hash < 0 ? semicolon : Math.Min(semicolon, hash);
        return cut < 0 ? line : line[..cut];
    }

    private static (ushort Modifiers, ushort MainKey) ParseShortcut(string shortcut)
    {
        ushort modifiers = 0;
        ushort mainKey = 0;
        foreach (var part in shortcut.Split(
                     '+',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModControl;
            }
            else if (part.Equals("ALT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModAlt;
            }
            else if (part.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModShift;
            }
            else if (part.Equals("LWIN", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("RWIN", StringComparison.OrdinalIgnoreCase) ||
                     part.Equals("WIN", StringComparison.OrdinalIgnoreCase))
            {
                modifiers |= ModWin;
            }
            else if (KeyNames.TryGetValue(part, out var key))
            {
                mainKey = key;
            }
        }

        return (modifiers, mainKey);
    }

    private static bool IsRepeatable(ushort key)
    {
        return key is 0xAF or 0xAE or 0xAD or 0xB0 or 0xB1 or 0xB2 or 0xB3 or
            0x26 or 0x28 or 0x25 or 0x27;
    }
}

internal static class KeyboardSender
{
    private const ushort ModAlt = 1;
    private const ushort ModControl = 2;
    private const ushort ModShift = 4;
    private const ushort ModWin = 8;
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkMenu = 0x12;
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkLWin = 0x5B;

    public static void PressCombo(MacroHotkey mapping)
    {
        if (mapping.Modifiers == 0 && mapping.MainKey == 0)
        {
            return;
        }

        var keys = new List<ushort>(5);
        if ((mapping.Modifiers & ModAlt) != 0) keys.Add(VkMenu);
        if ((mapping.Modifiers & ModControl) != 0) keys.Add(VkControl);
        if ((mapping.Modifiers & ModShift) != 0) keys.Add(VkShift);
        if ((mapping.Modifiers & ModWin) != 0) keys.Add(VkLWin);
        if (mapping.MainKey != 0) keys.Add(mapping.MainKey);

        Send(keys, keyUp: false);
        Thread.Sleep(20);
        keys.Reverse();
        Send(keys, keyUp: true);
    }

    private static void Send(IReadOnlyList<ushort> keys, bool keyUp)
    {
        var inputs = keys.Select(key => new NativeInput
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = key,
                    Flags = keyUp ? KeyUp : 0
                }
            }
        }).ToArray();

        if (inputs.Length > 0)
        {
            _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        }
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        NativeInput[] inputs,
        int sizeOfInputStructure);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
