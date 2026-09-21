using System;
using Avalonia.Threading;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// #13 Drives a hop animation for one icon: while running, BounceOffset
/// traces 0 → -10 → 0 every 900 ms. Owned by the item VM; cheap enough that
/// a few concurrent bouncers are fine.
/// </summary>
public sealed class BounceAnimator
{
    private const double Period = 0.9, Height = 10;
    private readonly Action<double> _set;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _started;

    public BounceAnimator(Action<double> setOffset)
    {
        _set = setOffset;
        _timer.Tick += (_, _) =>
        {
            double t = ((DateTime.UtcNow - _started).TotalSeconds % Period) / Period;
            // Up for 30 % of the period (ease-out), down for 25 %, rest 45 %.
            double y = t < 0.30 ? -Height * EaseOut(t / 0.30)
                     : t < 0.55 ? -Height * (1 - EaseIn((t - 0.30) / 0.25))
                     : 0;
            _set(y);
        };
    }

    private static double EaseOut(double x) => 1 - (1 - x) * (1 - x);
    private static double EaseIn(double x) => x * x;

    public void Start() { if (_timer.IsEnabled) return; _started = DateTime.UtcNow; _timer.Start(); }
    public void Stop() { _timer.Stop(); _set(0); }
}
