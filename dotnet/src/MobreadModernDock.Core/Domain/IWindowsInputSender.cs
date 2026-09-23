namespace MobreadModernDock.Core.Domain;

/// <summary>Sends synthetic keyboard input on behalf of dock items (Start menu, Task View).</summary>
public interface IWindowsInputSender
{
    /// <summary>Taps the left Windows key so the Start menu opens.</summary>
    bool SendWindowsKeyPress();

    /// <summary>
    /// Presses the given modifier virtual-keys, taps <paramref name="key"/>,
    /// then releases the modifiers in reverse order (e.g. Win+Tab).
    /// </summary>
    bool SendKeyChord(ushort[] modifiers, ushort key);
}
