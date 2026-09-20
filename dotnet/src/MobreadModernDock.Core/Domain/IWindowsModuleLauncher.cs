namespace MobreadModernDock.Core.Domain;

/// <summary>Port for launching built-in Windows modules (This PC, Recycle Bin, etc.).</summary>
public interface IWindowsModuleLauncher
{
    void Launch(string module, string label);
}
