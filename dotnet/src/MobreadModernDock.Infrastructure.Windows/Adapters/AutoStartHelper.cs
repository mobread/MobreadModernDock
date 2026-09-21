namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using Microsoft.Win32;

/// <summary>
/// Manages Windows auto-start registration via the Registry Run key.
/// Adds/removes the app from HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run.
/// </summary>
public static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "MobreadModernDock";

    /// <summary>Returns true if the app is registered for auto-start.</summary>
    public static bool IsAutoStartEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(AppName) != null;
    }

    /// <summary>Registers the app to start on Windows login.</summary>
    public static void EnableAutoStart()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key != null)
        {
            string exePath = Environment.ProcessPath ?? Application.ExecutablePath;
            key.SetValue(AppName, $"\"{exePath}\"");
        }
    }

    /// <summary>Removes the app from auto-start.</summary>
    public static void DisableAutoStart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(AppName, false);
    }
}
