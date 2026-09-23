namespace MobreadModernDock.Infrastructure.Windows.Native;

using System.Runtime.InteropServices;
using MobreadModernDock.Core.Application;

/// <summary>
/// System-wide keyboard shortcuts via <c>RegisterHotKey</c>. Owns a hidden
/// top-level window that receives <c>WM_HOTKEY</c>; each registration maps
/// an id to a callback. Re-registering replaces the whole set, so a settings
/// change just calls <see cref="Apply"/> again.
///
/// The window must live on a thread that pumps messages; create it on the
/// UI thread. Callbacks run on that thread too.
/// </summary>
public sealed class GlobalHotkeys : IDisposable
{
    private const uint WM_HOTKEY = 0x0312;
    private const string ClassName = "MobreadDockHotkeyWindow";
    private static readonly IntPtr HInstance = Kernel32.GetModuleHandle(null);
    private static readonly WndProc WndProcDelegate = WndProcImpl;
    private static GlobalHotkeys? _instance;

    static GlobalHotkeys()
    {
        User32.RegisterClass(new WNDCLASS
        {
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcDelegate),
            hInstance = HInstance,
            lpszClassName = ClassName,
        });
    }

    private IntPtr _hwnd;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    /// <summary>One requested binding: a parsed chord plus what it does.</summary>
    public sealed record Binding(HotkeyChord.Parsed Chord, string Label, Action Action);

    public GlobalHotkeys()
    {
        _instance = this;
        _hwnd = User32.CreateWindowEx(0, ClassName, "", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, HInstance, IntPtr.Zero);
    }

    /// <summary>
    /// Replaces every registration. Returns the labels of chords that could
    /// not be taken (already owned by another program - Win+1..9 is Explorer's,
    /// for example) so the UI can say so.
    /// </summary>
    public IReadOnlyList<string> Apply(IEnumerable<Binding> bindings)
    {
        Clear();
        var failed = new List<string>();
        if (_hwnd == IntPtr.Zero) return failed;
        foreach (var b in bindings)
        {
            int id = _nextId++;
            if (RegisterHotKey(_hwnd, id, b.Chord.Modifiers | HotkeyChord.ModNoRepeat, b.Chord.Key))
                _actions[id] = b.Action;
            else
                failed.Add(b.Label);
        }
        return failed;
    }

    public void Clear()
    {
        if (_hwnd != IntPtr.Zero)
            foreach (var id in _actions.Keys) UnregisterHotKey(_hwnd, id);
        _actions.Clear();
    }

    private static IntPtr WndProcImpl(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_HOTKEY && _instance != null
            && _instance._actions.TryGetValue((int)wParam, out var action))
        {
            try { action(); } catch { }
            return IntPtr.Zero;
        }
        return User32.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        Clear();
        if (_hwnd != IntPtr.Zero)
        {
            User32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
        }
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
