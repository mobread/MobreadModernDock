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
    private const uint KeyEventScanCode = 0x0008;
    private const uint MapVirtualKeyVkToScanCode = 0;

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

    private static bool TrySendInputWithScanCode()
    {
        ushort scanCode = MapVirtualKey(VirtualKeyLeftWindows, MapVirtualKeyVkToScanCode);
        if (scanCode == 0)
            return false;

        var inputs = new INPUT[2];
        inputs[0] = CreateScanCodeInput(scanCode, keyUp: false);
        inputs[1] = CreateScanCodeInput(scanCode, keyUp: true);
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

    private static INPUT CreateScanCodeInput(ushort scanCode, bool keyUp)
    {
        return new INPUT
        {
            type = (int)InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wScan = scanCode,
                    dwFlags = KeyEventScanCode | (keyUp ? KeyEventKeyUp : 0)
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
        [FieldOffset(0)] public KEYBDINPUT ki;
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
