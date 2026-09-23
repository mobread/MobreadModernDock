namespace MobreadModernDock.Infrastructure.Windows.Adapters;

using Microsoft.Win32;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Domain;

/// <summary>
/// Virtual desktops read from the registry - the only stable surface.
/// <c>HKCU\...\Explorer\VirtualDesktops</c> holds <c>VirtualDesktopIDs</c>
/// (packed 16-byte GUIDs, in order), <c>CurrentVirtualDesktop</c> and the
/// per-desktop <c>Desktops\{guid}\Name</c>. Verified on 24H2 to update
/// immediately on create / switch / close.
///
/// The <c>IVirtualDesktopManagerInternal</c> COM interfaces change IID per
/// Windows build, so switching is done with the shell's own Win+Ctrl+Left /
/// Right chord, repeated as needed.
/// </summary>
public sealed class RegistryVirtualDesktopGateway : IVirtualDesktopGateway
{
    private const string Root = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";
    private const ushort VK_LWIN = 0x5B, VK_CONTROL = 0x11, VK_LEFT = 0x25, VK_RIGHT = 0x27;

    private readonly IWindowsInputSender _input;

    public RegistryVirtualDesktopGateway(IWindowsInputSender input) => _input = input;

    public VirtualDesktopSnapshot Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Root);
            if (key == null) return VirtualDesktopSnapshot.Empty;
            var ids = VirtualDesktopFormats.UnpackIds(key.GetValue("VirtualDesktopIDs") as byte[]);
            if (ids.Count == 0) return VirtualDesktopSnapshot.Empty;

            Guid? current = key.GetValue("CurrentVirtualDesktop") is byte[] { Length: 16 } cur ? new Guid(cur) : null;
            var list = new List<VirtualDesktopInfo>(ids.Count);
            foreach (var id in ids)
            {
                string name = "";
                using var d = key.OpenSubKey(@"Desktops\" + id.ToString("B").ToUpperInvariant());
                if (d?.GetValue("Name") is string n) name = n;
                list.Add(new VirtualDesktopInfo(id, name));
            }
            int index = current is { } c ? ids.IndexOf(c) : -1;
            // Before the user ever switches, CurrentVirtualDesktop is absent
            // and the first desktop is current.
            if (index < 0 && current is null) index = 0;
            return new VirtualDesktopSnapshot(list, index);
        }
        catch
        {
            return VirtualDesktopSnapshot.Empty;
        }
    }

    public void SwitchTo(int index)
    {
        var snap = Read();
        int steps = VirtualDesktopFormats.StepsBetween(snap.CurrentIndex, index, snap.Desktops.Count);
        ushort key = steps < 0 ? VK_LEFT : VK_RIGHT;
        for (int i = 0; i < Math.Abs(steps); i++)
        {
            if (!_input.SendKeyChord(new[] { VK_LWIN, VK_CONTROL }, key)) return;
            // The shell animates each switch; a burst of chords without a
            // pause drops some of them.
            Thread.Sleep(120);
        }
    }
}
