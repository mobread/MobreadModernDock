namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Opens built-in Windows
/// surfaces (This PC, Recycle Bin, Control Panel, Settings, Start menu).
/// </summary>
public class WindowsModuleLauncher : IWindowsModuleLauncher
{
    private readonly IWindowsInputSender _inputSender;

    public WindowsModuleLauncher(IWindowsInputSender? inputSender = null)
    {
        _inputSender = inputSender ?? new Win32WindowsInputSender();
    }

    public void Launch(string module, string label)
    {
        try
        {
            switch (module)
            {
                case "start":
                    if (!_inputSender.SendWindowsKeyPress())
                    {
                        throw new InvalidOperationException(
                            "Failed to send the Windows key for the Start menu.");
                    }
                    break;
                case "mypc":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}",
                        UseShellExecute = true
                    });
                    break;
                case "trash":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "::{645FF040-5081-101B-9F08-00AA002F954E}",
                        UseShellExecute = true
                    });
                    break;
                case "ctrlpnl":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "control.exe",
                        UseShellExecute = true
                    });
                    break;
                case "pconfig":
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c start ms-settings:",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    break;

                // --- Power actions ---
                // shutdown.exe is used for the session-ending ones (the
                // documented, UAC-free path); sleep and lock have no
                // shutdown.exe equivalent that works reliably, so they go
                // through their own APIs.
                case "shutdown":
                    RunShutdownExe("/s /t 0");
                    break;
                case "restart":
                    RunShutdownExe("/r /t 0");
                    break;
                case "signout":
                    RunShutdownExe("/l");
                    break;
                case "sleep":
                    // bForce=false so apps can veto; bWakeupEventsDisabled=false.
                    // Note: on a machine with hibernation enabled this suspends
                    // to RAM, matching the Start-menu "Sleep" entry.
                    if (!SetSuspendState(false, false, false))
                        throw new InvalidOperationException("SetSuspendState failed.");
                    break;
                case "lock":
                    if (!LockWorkStation())
                        throw new InvalidOperationException("LockWorkStation failed.");
                    break;

                // --- Shell surfaces ---
                case "showdesktop":
                    // Shell.Application.ToggleDesktop is exactly what the
                    // taskbar's own corner button does (Win+D semantics,
                    // including the restore on a second click).
                    ToggleDesktop();
                    break;
                case "taskview":
                    // No documented API opens Task View; the shell itself
                    // binds it to Win+Tab, so send that chord.
                    if (!_inputSender.SendKeyChord(new ushort[] { 0x5B /* VK_LWIN */ }, 0x09 /* VK_TAB */))
                        throw new InvalidOperationException("Failed to send Win+Tab for Task View.");
                    break;
            }
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to launch Windows module '{module}'", e);
        }

        Debug.WriteLine($"{label} Clicked");
    }

    /// <summary>
    /// Runs shutdown.exe with the given switches, detached and windowless.
    /// UseShellExecute=false + CreateNoWindow avoids the console flash that a
    /// shell-executed console app produces.
    /// </summary>
    private static void RunShutdownExe(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "shutdown.exe",
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    /// <summary>
    /// <c>Shell.Application.ToggleDesktop()</c> via late-bound COM. The
    /// interop assembly for the shell automation objects is not referenced,
    /// and a one-line dynamic call is all that is needed.
    /// </summary>
    private static void ToggleDesktop()
    {
        var type = Type.GetTypeFromProgID("Shell.Application")
                   ?? throw new InvalidOperationException("Shell.Application is not registered.");
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(type);
            type.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
        }
        finally
        {
            if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
        }
    }

    [System.Runtime.InteropServices.DllImport("powrprof.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);
}
