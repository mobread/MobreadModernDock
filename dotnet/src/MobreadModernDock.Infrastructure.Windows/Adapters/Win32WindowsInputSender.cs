namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Sends the Windows key using several Win32 strategies so Start menu works
/// from a WS_EX_NOACTIVATE dock window.
/// </summary>
public sealed class Win32WindowsInputSender : IWindowsInputSender
{
    private const ushort VirtualKeyLeftWindows = 0x5B;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventExtended = 0x0001;
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVirtualKeyVkToScanCode = 0;

    /// <summary>
    /// Keys whose scan code collides with a numpad key unless the extended
    /// flag is set: arrows, Home/End/PgUp/PgDn/Insert/Delete, the Windows
    /// keys, right Ctrl/Alt. Without it Win+Ctrl+Left arrives as
    /// Win+Ctrl+Numpad4 and the shell ignores it.
    /// </summary>
    private static bool IsExtendedKey(ushort vk) => vk is
        0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 // PgUp PgDn End Home Left Up Right Down
        or 0x2D or 0x2E // Insert Delete
        or 0x5B or 0x5C or 0x5D // LWin RWin Apps
        or 0xA3 or 0xA5 // RControl RMenu
        or 0x90 or 0x6F; // NumLock, numpad divide

    public bool SendWindowsKeyPress()
    {
        if (TrySendInputWithScanCode())
            return true;

        if (TrySendInputWithVirtualKey())
            return true;

        if (TryKeybdEvent())
            return true;

        Debug.WriteLine("Win32WindowsInputSender: all Start menu input strategies failed.");
        return false;
    }

    public bool SendKeyChord(ushort[] modifiers, ushort key)
    {
        // Scan codes, like the Start-menu path: the shell's own hotkey
        // handling (Win+Tab) is more reliably triggered by scan-code input
        // than by bare virtual keys from a WS_EX_NOACTIVATE window.
        var inputs = new List<INPUT>(modifiers.Length * 2 + 2);
        foreach (var m in modifiers) inputs.Add(KeyInput(m, keyUp: false));
        inputs.Add(KeyInput(key, keyUp: false));
        inputs.Add(KeyInput(key, keyUp: true));
        for (int i = modifiers.Length - 1; i >= 0; i--) inputs.Add(KeyInput(modifiers[i], keyUp: true));
        return SendInputBatch(inputs.ToArray());
    }

    private static INPUT KeyInput(ushort virtualKey, bool keyUp)
    {
        ushort scanCode = MapVirtualKey(virtualKey, MapVirtualKeyVkToScanCode);
        return scanCode != 0
            ? CreateScanCodeInput(scanCode, keyUp, IsExtendedKey(virtualKey))
            : CreateVirtualKeyInput(virtualKey, keyUp);
    }

    private static bool TrySendInputWithScanCode()
    {
        ushort scanCode = MapVirtualKey(VirtualKeyLeftWindows, MapVirtualKeyVkToScanCode);
        if (scanCode == 0)
            return false;

        var inputs = new INPUT[2];
        inputs[0] = CreateScanCodeInput(scanCode, keyUp: false, extended: true);
        inputs[1] = CreateScanCodeInput(scanCode, keyUp: true, extended: true);
        return SendInputBatch(inputs);
    }

    private static bool TrySendInputWithVirtualKey()
    {
        var inputs = new INPUT[2];
        inputs[0] = CreateVirtualKeyInput(VirtualKeyLeftWindows, keyUp: false);
        inputs[1] = CreateVirtualKeyInput(VirtualKeyLeftWindows, keyUp: true);
        return SendInputBatch(inputs);
    }

    private static bool TryKeybdEvent()
    {
        byte virtualKey = (byte)VirtualKeyLeftWindows;
        keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        keybd_event(virtualKey, 0, KeyEventKeyUp, UIntPtr.Zero);
        return true;
    }

    private static bool SendInputBatch(INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        return sent == inputs.Length;
    }

    private static INPUT CreateScanCodeInput(ushort scanCode, bool keyUp, bool extended)
    {
        return new INPUT
        {
            type = (int)InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = scanCode,
                    dwFlags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0) | (extended ? KeyEventExtended : 0)
                }
            }
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = (int)InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = keyUp ? KeyEventKeyUp : 0
                }
            }
        };
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern ushort MapVirtualKey(ushort uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        // The union must be as large as its largest member (MOUSEINPUT, 32
        // bytes on x64) or cbSize is wrong and SendInput rejects the whole
        // batch with ERROR_INVALID_PARAMETER - silently, as 0 events sent.
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
