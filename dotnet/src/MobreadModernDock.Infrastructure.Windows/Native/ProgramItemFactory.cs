namespace MobreadModernDock.Infrastructure.Windows.Native;

using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

/// <summary>
/// Turns a path the user (or Windows) points at into a dock program item.
/// A <c>.lnk</c> shortcut contributes its target, arguments and display
/// name; a bare <c>.exe</c> goes through the Squirrel-aware resolver.
/// Returns null when the path cannot be reduced to an existing executable.
/// </summary>
public static class ProgramItemFactory
{
    public static DockProgramItemModel? Create(string path)
    {
        if (ShellLinkResolver.IsShortcut(path))
        {
            var link = ShellLinkResolver.Resolve(path);
            if (link == null || string.IsNullOrWhiteSpace(link.TargetPath)) return null;
            if (!link.TargetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(link.TargetPath))
                return null;
            var sel = ProgramSelectionResolver.Resolve(link.TargetPath);
            // The shortcut's file name is the name the user already knows
            // ("Microsoft Edge"), which beats the exe stem ("msedge").
            string label = Path.GetFileNameWithoutExtension(path);
            return new DockProgramItemModel(label, sel.ExecutablePath, link.Arguments);
        }

        var resolved = ProgramSelectionResolver.Resolve(path);
        return new DockProgramItemModel(resolved.Label, resolved.ExecutablePath);
    }
}
