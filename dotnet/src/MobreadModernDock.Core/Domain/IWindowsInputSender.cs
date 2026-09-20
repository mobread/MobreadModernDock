namespace MobreadModernDock.Core.Domain;

/// <summary>Sends a left-Windows key press so the Start menu can open.</summary>
public interface IWindowsInputSender
{
    bool SendWindowsKeyPress();
}
