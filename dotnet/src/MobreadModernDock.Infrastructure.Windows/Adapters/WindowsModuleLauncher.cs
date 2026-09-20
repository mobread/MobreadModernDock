namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Direct port of DefaultWindowsModuleLauncher.java. Opens built-in Windows
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
            }
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to launch Windows module '{module}'", e);
        }

        Debug.WriteLine($"{label} Clicked");
    }
}
