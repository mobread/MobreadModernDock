namespace MobreadModernDock.Core.Domain;

/// <summary>One virtual desktop as the shell lists it: id, name ("" when unnamed).</summary>
public sealed record VirtualDesktopInfo(Guid Id, string Name);

/// <summary>Desktops in order plus the index of the current one (-1 if unknown).</summary>
public sealed record VirtualDesktopSnapshot(IReadOnlyList<VirtualDesktopInfo> Desktops, int CurrentIndex)
{
    public static readonly VirtualDesktopSnapshot Empty = new(Array.Empty<VirtualDesktopInfo>(), -1);
}

/// <summary>Port for the shell's virtual desktops.</summary>
public interface IVirtualDesktopGateway
{
    VirtualDesktopSnapshot Read();

    /// <summary>Makes desktop <paramref name="index"/> current. No-op when already there or out of range.</summary>
    void SwitchTo(int index);
}
