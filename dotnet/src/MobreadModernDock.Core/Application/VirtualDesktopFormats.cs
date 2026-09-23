namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;

/// <summary>
/// Pure helpers for the virtual-desktop widget. The registry stores the
/// desktop ids as one packed byte array of 16-byte GUIDs; the label falls
/// back to the 1-based number when a desktop has no name.
/// </summary>
public static class VirtualDesktopFormats
{
    public static List<Guid> UnpackIds(byte[]? packed)
    {
        var ids = new List<Guid>();
        if (packed == null) return ids;
        for (int i = 0; i + 16 <= packed.Length; i += 16)
            ids.Add(new Guid(packed.AsSpan(i, 16)));
        return ids;
    }

    public static string Label(VirtualDesktopInfo desktop, int index, bool showNames) =>
        showNames && !string.IsNullOrWhiteSpace(desktop.Name) ? desktop.Name : (index + 1).ToString();

    /// <summary>
    /// Number of Win+Ctrl+Left (negative) or Right (positive) presses to go
    /// from <paramref name="current"/> to <paramref name="target"/>. 0 when
    /// either index is out of range or they are equal.
    /// </summary>
    public static int StepsBetween(int current, int target, int count)
    {
        if (current < 0 || target < 0 || current >= count || target >= count) return 0;
        return target - current;
    }
}
