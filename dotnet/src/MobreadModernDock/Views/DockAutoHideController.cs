using System;
using Avalonia.Threading;
using MobreadModernDock.Infrastructure.Windows.Native;

namespace MobreadModernDock.Views;

/// <summary>
/// Dock auto-hide: when enabled, the dock slides toward the nearest screen
/// edge until only a thin sliver remains, and slides back when the pointer
/// touches that sliver. Hiding is delayed after the pointer leaves so quick
/// mouse excursions don't flicker the dock, and is suppressed while a hover
/// preview or context menu is open.
///
/// Movement is done with native SetWindowPos on a UI timer so the window's
/// desktop/topmost layering is untouched (Avalonia animations would target
/// the content, not the window position).
/// </summary>
internal sealed class DockAutoHideController
{
    private const int SliverPx = 3;            // visible strip while hidden
    private const int RevealZonePx = 6;        // pointer within this many px of the edge reveals
    private const int HideDelayMs = 600;
    private const int StepMs = 12;             // ~80fps
    private const double SlideDurationMs = 160;

    private readonly Func<DockWindowBehavior?> _behavior;
    private readonly Func<(int W, int H)> _size;
    private readonly Func<(int X, int Y)> _inset;  // transparent headroom per side
    private readonly Func<bool> _blockHide;    // preview / menu open
    private readonly Func<(int X, int Y)> _restPosition; // where the dock lives when shown
    private readonly Func<(int L, int T, int R, int B)> _screenBounds;

    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly DispatcherTimer _hideDelay = new() { Interval = TimeSpan.FromMilliseconds(HideDelayMs) };
    private readonly DispatcherTimer _anim = new() { Interval = TimeSpan.FromMilliseconds(StepMs) };

    private bool _enabled;
    private bool _hidden;
    private (int X, int Y) _animFrom, _animTo;
    private DateTime _animStart;

    public bool IsHidden => _hidden;
    public bool IsEnabled => _enabled;

    public DockAutoHideController(
        Func<DockWindowBehavior?> behavior,
        Func<(int W, int H)> size,
        Func<(int X, int Y)> restPosition,
        Func<(int L, int T, int R, int B)> screenBounds,
        Func<bool> blockHide,
        Func<(int X, int Y)>? inset = null)
    {
        _behavior = behavior;
        _size = size;
        _restPosition = restPosition;
        _screenBounds = screenBounds;
        _blockHide = blockHide;
        _inset = inset ?? (() => (0, 0));
        _poll.Tick += (_, _) => Poll();
        _hideDelay.Tick += (_, _) => { _hideDelay.Stop(); if (_enabled && !PointerOverDock() && !_blockHide()) Hide(); };
        _anim.Tick += (_, _) => AnimStep();
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == _enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            _poll.Start();
            // Hide right away unless the pointer really is on the dock; if it
            // is, arm the normal leave delay so the dock still hides on its
            // own once the pointer moves off (previously it waited for a
            // pointer-exit event that never comes if the pointer was over
            // another window covering the dock rect - e.g. Settings).
            if (!PointerOverDock()) Hide();
            else _hideDelay.Start();
        }
        else
        {
            _poll.Stop();
            _hideDelay.Stop();
            _anim.Stop();
            if (_hidden) { _hidden = false; SlideTo(_restPosition()); }
        }
    }

    /// <summary>Call when the dock's rest position or size changed (settings, anchor, items).</summary>
    public void OnLayoutChanged()
    {
        if (!_enabled) return;
        if (_hidden) SlideTo(HiddenPosition(), animate: false);
    }

    /// <summary>Call when the pointer enters the dock (any child) so a pending hide is cancelled.</summary>
    public void OnPointerEntered()
    {
        if (!_enabled) return;
        _hideDelay.Stop();
        if (_hidden) Reveal();
    }

    /// <summary>Call when the pointer leaves the dock; hides after the delay.</summary>
    public void OnPointerExited()
    {
        if (!_enabled) return;
        _hideDelay.Stop();
        _hideDelay.Start();
    }

    private void Poll()
    {
        if (!_enabled) return;
        if (!User32.GetCursorPos(out POINT p)) return;
        var (x, y, w, h) = CurrentRect();
        bool nearRect = p.X >= x - RevealZonePx && p.X < x + w + RevealZonePx &&
                        p.Y >= y - RevealZonePx && p.Y < y + h + RevealZonePx;
        // Hidden: anything near the sliver reveals. Shown: the pointer only
        // counts as "over" if the dock is really the window under it - a
        // Settings window (or anything else) covering the dock rect must not
        // keep the dock awake.
        bool over = nearRect && (_hidden || IsDockUnderPointer(p));
        if (_hidden && over) Reveal();
        else if (!_hidden && !over && !_hideDelay.IsEnabled && !_blockHide()) _hideDelay.Start();
        else if (!_hidden && over) _hideDelay.Stop();
    }

    private bool PointerOverDock()
    {
        if (!User32.GetCursorPos(out POINT p)) return false;
        var (x, y, w, h) = CurrentRect();
        return p.X >= x && p.X < x + w && p.Y >= y && p.Y < y + h && IsDockUnderPointer(p);
    }

    /// <summary>
    /// True when the top-level window under the pointer is the dock itself or
    /// one of its untitled popups (preview, menu, folder stack). Owned windows
    /// with a title (Settings) do not count: they cover the dock's rect
    /// without being the dock, and must not keep it awake. Falls back to true
    /// when there is no native handle to compare against.
    /// </summary>
    private bool IsDockUnderPointer(POINT p)
    {
        var b = _behavior();
        if (b == null || b.Hwnd == IntPtr.Zero) return true;
        IntPtr hit = User32.WindowFromPoint(p);
        if (hit == IntPtr.Zero) return false;
        IntPtr root = User32.GetAncestor(hit, 2 /* GA_ROOT */);
        if (root == b.Hwnd) return true;
        return IsOwnedBy(root, b.Hwnd) && User32.GetWindowTextLength(root) == 0;
    }

    private static bool IsOwnedBy(IntPtr window, IntPtr owner)
    {
        for (IntPtr w = User32.GetWindow(window, 4 /* GW_OWNER */); w != IntPtr.Zero; w = User32.GetWindow(w, 4))
            if (w == owner) return true;
        return false;
    }

    /// <summary>
    /// Rect of the <i>visible bar</i> in screen pixels: the window rect minus
    /// the transparent headroom it carries for the attention bounce and for
    /// magnified end icons. Hover detection and the hide-edge choice both have
    /// to use this - the headroom is invisible, so treating it as part of the
    /// dock would keep the dock awake while the pointer is over empty space,
    /// and could pick the wrong edge to hide against.
    /// </summary>
    private (int X, int Y, int W, int H) CurrentRect()
    {
        var b = _behavior();
        var (w, h) = _size();
        var (x, y) = b?.GetScreenPosition() ?? _restPosition();
        var (ix, iy) = _inset();
        return (x + ix, y + iy, Math.Max(1, w - 2 * ix), Math.Max(1, h - 2 * iy));
    }

    private void Hide() { _hidden = true; SlideTo(HiddenPosition()); }
    private void Reveal() { _hidden = false; SlideTo(_restPosition()); }

    /// <summary>Nearest screen edge to the dock's rest position, leaving a sliver visible.</summary>
    private (int X, int Y) HiddenPosition()
    {
        var (rx, ry) = _restPosition();
        var (w, h) = _size();
        var (l, t, r, btm) = _screenBounds();
        var (ix, iy) = _inset();
        // Measure the bar, not the padded window, so the nearest edge is the
        // one the user sees the dock against.
        int bx = rx + ix, by = ry + iy;
        int bw = Math.Max(1, w - 2 * ix), bh = Math.Max(1, h - 2 * iy);
        int dLeft = bx - l, dRight = r - (bx + bw), dTop = by - t, dBottom = btm - (by + bh);
        int min = Math.Min(Math.Min(dLeft, dRight), Math.Min(dTop, dBottom));
        // Targets are window positions: push the bar off-screen, then convert.
        if (min == dBottom) return (rx, btm - SliverPx - iy);
        if (min == dTop) return (rx, t - bh + SliverPx - iy);
        if (min == dLeft) return (l - bw + SliverPx - ix, ry);
        return (r - SliverPx - ix, ry);
    }

    private void SlideTo((int X, int Y) target, bool animate = true)
    {
        var b = _behavior();
        if (b == null) return;
        _anim.Stop();
        if (!animate) { b.MoveToScreen(target.X, target.Y); return; }
        _animFrom = b.GetScreenPosition();
        _animTo = target;
        _animStart = DateTime.UtcNow;
        _anim.Start();
    }

    private void AnimStep()
    {
        var b = _behavior();
        if (b == null) { _anim.Stop(); return; }
        double t = Math.Min(1.0, (DateTime.UtcNow - _animStart).TotalMilliseconds / SlideDurationMs);
        double e = 1 - Math.Pow(1 - t, 3); // ease-out cubic
        int x = (int)Math.Round(_animFrom.X + (_animTo.X - _animFrom.X) * e);
        int y = (int)Math.Round(_animFrom.Y + (_animTo.Y - _animFrom.Y) * e);
        b.MoveToScreen(x, y);
        if (t >= 1.0) _anim.Stop();
    }

    public void Dispose()
    {
        _poll.Stop(); _hideDelay.Stop(); _anim.Stop();
    }
}
