namespace CedroModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices;
using CedroModernDock.Core.Domain;
using CedroModernDock.Infrastructure.Windows.Native;
using Microsoft.Win32;

/// <summary>
/// Reads the Windows 11 notification area through UI Automation. The
/// taskbar's tray is a XAML island: Win32 enumeration sees only an empty
/// <c>TrayNotifyWnd</c>, but UIA exposes each icon as a button with a name
/// (the tooltip), a screen rect and an Invoke pattern.
///
/// Icon bitmaps are not capturable from the screen (DWM excludes the
/// taskbar from GDI capture), so they come from the shell's own cache:
/// <c>HKCU\Control Panel\NotifyIconSettings\*\IconSnapshot</c> holds a PNG
/// per registered tray icon, keyed by tooltip and executable path.
/// </summary>
public sealed class UiaTrayIconGateway : ITrayIconGateway
{
    private const string NotifyItemAutomationId = "NotifyItemIcon";
    private const string SystemIconAutomationId = "SystemTrayIcon";
    private const string NotifyIconSettingsKey = @"Control Panel\NotifyIconSettings";

    private readonly object _sync = new();
    private IUIAutomation? _uia;
    private Dictionary<string, IUIAutomationElement> _elementsByKey = new();

    public List<TrayIconInfo> GetIcons()
    {
        lock (_sync)
        {
            var result = new List<TrayIconInfo>();
            var elements = new Dictionary<string, IUIAutomationElement>();
            try
            {
                var root = GetTrayRoot();
                if (root == null) return result;

                var snapshots = LoadRegistrySnapshots();
                var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var (automationId, isSystem) in new[] { (NotifyItemAutomationId, false), (SystemIconAutomationId, true) })
                {
                    var cond = _uia!.CreatePropertyCondition(UiaInterop.UIA_AutomationIdPropertyId, automationId);
                    var found = root.FindAll(TreeScope.Descendants, cond);
                    int n = found.get_Length();
                    for (int i = 0; i < n; i++)
                    {
                        var el = found.GetElement(i);
                        string fullName = SafeName(el);
                        string name = FirstLine(fullName);
                        if (isSystem && IsHiddenSystemIcon(name)) continue;

                        // Key: name plus an ordinal so duplicates stay distinct.
                        string baseKey = (isSystem ? "sys:" : "app:") + name;
                        seen[baseKey] = seen.TryGetValue(baseKey, out var c) ? c + 1 : 0;
                        string key = seen[baseKey] == 0 ? baseKey : $"{baseKey}#{seen[baseKey]}";

                        RegistrySnapshot? snap = isSystem ? null : MatchSnapshot(snapshots, name, fullName);
                        result.Add(new TrayIconInfo(key, name, snap?.ExecutablePath, snap?.Png, isSystem));
                        elements[key] = el;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[UiaTrayIconGateway] enumeration failed: {e.Message}");
                _uia = null; // force re-create on next call
            }
            _elementsByKey = elements;
            return result;
        }
    }

    public bool Activate(string key)
    {
        lock (_sync)
        {
            if (!_elementsByKey.TryGetValue(key, out var el)) return false;
            try
            {
                if (el.GetCurrentPattern(UiaInterop.UIA_InvokePatternId) is IUIAutomationInvokePattern invoke)
                {
                    invoke.Invoke();
                    return true;
                }
                // No invoke pattern: fall back to a synthesized left click.
                return ClickAt(el, rightButton: false);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"[UiaTrayIconGateway] activate failed: {e.Message}");
                return false;
            }
        }
    }

    public bool ShowContextMenu(string key)
    {
        lock (_sync)
        {
            if (!_elementsByKey.TryGetValue(key, out var el)) return false;
            try { return ClickAt(el, rightButton: true); }
            catch (Exception e)
            {
                Debug.WriteLine($"[UiaTrayIconGateway] context menu failed: {e.Message}");
                return false;
            }
        }
    }

    // --- UIA ---

    private IUIAutomationElement? GetTrayRoot()
    {
        _uia ??= UiaInterop.Create();
        IntPtr tray = User32.FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero) return null;
        // The XAML island bridge hosts the whole taskbar content on Win11.
        // On Win10 there is no bridge; the classic TrayNotifyWnd is used.
        IntPtr host = User32.FindWindowEx(tray, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
        if (host == IntPtr.Zero)
            host = User32.FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
        if (host == IntPtr.Zero) host = tray;
        return _uia.ElementFromHandle(host);
    }

    private static string SafeName(IUIAutomationElement el)
    {
        try { return el.get_CurrentName() ?? ""; } catch { return ""; }
    }

    private static string FirstLine(string s)
    {
        int nl = s.IndexOfAny(new[] { '\r', '\n' });
        return (nl >= 0 ? s[..nl] : s).Trim();
    }

    /// <summary>The "Show Desktop" sliver and empty-named elements are not icons.</summary>
    private static bool IsHiddenSystemIcon(string name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("Show Desktop", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Synthesizes a mouse click at the element's centre. The cursor is moved
    /// there and restored afterwards; UIA offers no right-click pattern, so
    /// this is the only way to open a tray icon's context menu.
    /// </summary>
    private static bool ClickAt(IUIAutomationElement el, bool rightButton)
    {
        // Avalonia runs DPI-unaware on this app (no dpiAware manifest), so UIA
        // rects and GetSystemMetrics are virtualized (scaled down), while
        // SendInput's absolute coordinates are always physical. Temporarily
        // switch this thread to per-monitor awareness to read everything in
        // physical pixels; UIA re-evaluates rects under the calling thread's
        // awareness, so the element rect is re-read inside the window.
        IntPtr previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var r = el.get_CurrentBoundingRectangle();
            if (r.right <= r.left || r.bottom <= r.top) return false;
            int cx = (r.left + r.right) / 2, cy = (r.top + r.bottom) / 2;

            User32.GetCursorPos(out POINT original);
            int vx = User32.GetSystemMetrics(SM_XVIRTUALSCREEN), vy = User32.GetSystemMetrics(SM_YVIRTUALSCREEN);
            int vw = User32.GetSystemMetrics(SM_CXVIRTUALSCREEN), vh = User32.GetSystemMetrics(SM_CYVIRTUALSCREEN);
            int ax = (int)Math.Round((cx - vx) * 65535.0 / Math.Max(1, vw - 1));
            int ay = (int)Math.Round((cy - vy) * 65535.0 / Math.Max(1, vh - 1));
            uint down = rightButton ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
            uint up = rightButton ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

            // Move first and let the shell see the hover before pressing: the
            // XAML tray needs a pointer-enter to target the right item.
            var move = new[] { MouseInput(ax, ay, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK) };
            SendInput(1, move, Marshal.SizeOf<INPUT>());
            Thread.Sleep(40);
            var click = new[]
            {
                MouseInput(ax, ay, down | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK),
                MouseInput(ax, ay, up | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK),
            };
            uint sent = SendInput((uint)click.Length, click, Marshal.SizeOf<INPUT>());
            // Leave the cursor on the menu for right-click (moving it away would
            // dismiss the menu on some shells); restore for a plain activate.
            if (!rightButton)
                User32.SetCursorPos(original.X, original.Y);
            return sent == click.Length;
        }
        finally
        {
            if (previous != IntPtr.Zero) SetThreadDpiAwarenessContext(previous);
        }
    }

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

    // --- Registry snapshots ---

    private sealed record RegistrySnapshot(string? ExecutablePath, string? Tooltip, byte[]? Png);

    private static List<RegistrySnapshot> LoadRegistrySnapshots()
    {
        var list = new List<RegistrySnapshot>();
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(NotifyIconSettingsKey);
            if (key == null) return list;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var k = key.OpenSubKey(sub);
                if (k == null) continue;
                string? exe = k.GetValue("ExecutablePath") as string;
                string? tip = k.GetValue("InitialTooltip") as string;
                byte[]? png = k.GetValue("IconSnapshot") as byte[];
                if (png != null && png.Length < 8) png = null;
                list.Add(new RegistrySnapshot(ExpandKnownFolder(exe), tip, png));
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine($"[UiaTrayIconGateway] registry read failed: {e.Message}");
        }
        return list;
    }

    /// <summary>
    /// Matches a UIA icon to a registry entry. The tray name is the live
    /// tooltip; the registry stores the tooltip at registration time, so try
    /// exact, then prefix, then the executable's file name as a last resort.
    /// </summary>
    private static RegistrySnapshot? MatchSnapshot(List<RegistrySnapshot> snaps, string name, string fullName)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var exact = snaps.FirstOrDefault(s => Eq(s.Tooltip, name) || Eq(s.Tooltip, fullName));
        if (exact != null) return exact;
        var prefix = snaps.FirstOrDefault(s => !string.IsNullOrEmpty(s.Tooltip) &&
            (name.StartsWith(s.Tooltip!, StringComparison.OrdinalIgnoreCase) ||
             s.Tooltip!.StartsWith(name, StringComparison.OrdinalIgnoreCase)));
        if (prefix != null) return prefix;
        // Fall back to executable stem: "Spotify" ↔ Spotify.exe
        string stem = name.Split(' ')[0];
        return snaps.FirstOrDefault(s => s.ExecutablePath != null &&
            Eq(Path.GetFileNameWithoutExtension(s.ExecutablePath), stem));
    }

    private static bool Eq(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Registry paths use KNOWNFOLDERID prefixes, e.g. {6D809377-…}\Steam\steam.exe.</summary>
    private static string? ExpandKnownFolder(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('{')) return path;
        int end = path.IndexOf('}');
        if (end < 0) return path;
        string guid = path[..(end + 1)];
        string rest = path[(end + 1)..].TrimStart('\\');
        string? root = guid.ToUpperInvariant() switch
        {
            "{6D809377-6AF0-444B-8957-A3773F02200E}" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "{7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E}" => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}" => Environment.GetFolderPath(Environment.SpecialFolder.System),
            "{F38BF404-1D43-42F2-9305-67DE0B28FC23}" => Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "{F1B32785-6FBA-4FCF-9D55-7B8E7F157091}" => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "{3EB685DB-65F9-4CF6-A03A-E3EF65729F3D}" => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            _ => null
        };
        return root == null ? path : Path.Combine(root, rest);
    }

    // --- SendInput ---

    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_ABSOLUTE = 0x8000,
        MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private static INPUT MouseInput(int x, int y, uint flags) => new()
    {
        type = 0,
        U = new InputUnion { mi = new MOUSEINPUT { dx = x, dy = y, dwFlags = flags } }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public int type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
}
