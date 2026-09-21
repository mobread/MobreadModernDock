namespace MobreadModernDock.Infrastructure.Windows.Native;

using System;
using Microsoft.Win32;

/// <summary>
/// Watches the Windows app light/dark preference and raises a callback when it
/// flips.
///
/// The value lives at
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme</c>
/// (1 = light, 0 = dark). <see cref="SystemEvents.UserPreferenceChanged"/>
/// fires for the General category on a theme switch, which is cheaper and more
/// reliable than a registry change notification thread — but it does not carry
/// the new value, so the registry is re-read on every notification and the
/// callback only runs when the value actually changed.
/// </summary>
public sealed class SystemThemeWatcher : IDisposable
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Action<bool> _onChanged;
    private readonly UserPreferenceChangedEventHandler _handler;
    private bool _lastIsLight;
    private bool _disposed;

    /// <summary>True when Windows is currently using the light app theme.</summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch
        {
            return false; // dark is the safer assumption for a dock
        }
    }

    /// <param name="onChanged">Called with the new value whenever it flips.</param>
    public SystemThemeWatcher(Action<bool> onChanged)
    {
        _onChanged = onChanged;
        _lastIsLight = IsLightTheme();
        _handler = (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            bool isLight = IsLightTheme();
            if (isLight == _lastIsLight) return;
            _lastIsLight = isLight;
            try { _onChanged(isLight); } catch { /* never break the shell callback */ }
        };
        SystemEvents.UserPreferenceChanged += _handler;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= _handler;
    }
}
