namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Models;

/// <summary>Direct port of DockAppearanceService.</summary>
public class DockAppearanceService
{
    private readonly DockService _dockService;

    public DockAppearanceService(DockService dockService)
    {
        _dockService = dockService;
    }

    public DockModel GetDock() => _dockService.GetDock();

    public int GetIconsSize() => GetDock().IconsSize;

    public void SetIconsSize(int iconsSize)
    {
        GetDock().IconsSize = iconsSize;
        _dockService.SaveChanges();
    }

    public int GetSpacingBetweenIcons() => GetDock().SpacingBetweenIcons;

    public void SetSpacingBetweenIcons(int spacingValue)
    {
        GetDock().SpacingBetweenIcons = spacingValue;
        _dockService.SaveChanges();
    }

    public int GetDockTransparencyPercentage() => (int)(GetDock().DockTransparency * 100);

    public void SetDockTransparencyPercentage(int value)
    {
        GetDock().DockTransparency = (double)value / 100;
        _dockService.SaveChanges();
    }

    public int GetDockBorderRounding() => GetDock().DockBorderRounding;

    public void SetDockBorderRounding(int value)
    {
        GetDock().DockBorderRounding = value;
        _dockService.SaveChanges();
    }

    public string GetDockColorRGB() => GetDock().DockColorRGB;

    public void SetDockColorRGB(string value)
    {
        GetDock().DockColorRGB = value;
        _dockService.SaveChanges();
    }

    public DockTheme GetDockTheme()
    {
        DockModel dock = GetDock();
        return new DockTheme(dock.DockColorRGB, dock.DockTransparency, dock.DockBorderRounding);
    }

    public bool GetShowUnpinnedRunningApps() => GetDock().ShowUnpinnedRunningApps;

    public void SetShowUnpinnedRunningApps(bool value)
    {
        GetDock().ShowUnpinnedRunningApps = value;
        _dockService.SaveChanges();
    }

    public bool GetVerticalDock() => GetDock().VerticalDock;

    public void SetVerticalDock(bool value)
    {
        GetDock().VerticalDock = value;
        _dockService.SaveChanges();
    }

    public bool GetAlwaysOnTop() => GetDock().AlwaysOnTop;

    public void SetAlwaysOnTop(bool value)
    {
        GetDock().AlwaysOnTop = value;
        _dockService.SaveChanges();
    }

    public int GetDockRows() => Math.Clamp(GetDock().DockRows, 1, 4);

    public void SetDockRows(int value)
    {
        GetDock().DockRows = Math.Clamp(value, 1, 4);
        _dockService.SaveChanges();
    }

    /// <summary>Global whole-window opacity as a percentage (20..100).</summary>
    public int GetGlobalOpacityPercentage() => (int)Math.Round(Math.Clamp(GetDock().GlobalOpacity, 0.2, 1.0) * 100);

    public void SetGlobalOpacityPercentage(int value)
    {
        GetDock().GlobalOpacity = Math.Clamp(value, 20, 100) / 100.0;
        _dockService.SaveChanges();
    }

    public bool GetEdgeSnapping() => GetDock().EdgeSnapping;

    public int GetEdgeSnapMargin() => Math.Clamp(GetDock().EdgeSnapMargin, 0, 64);

    public void SetEdgeSnapMargin(int px)
    {
        GetDock().EdgeSnapMargin = Math.Clamp(px, 0, 64);
        _dockService.SaveChanges();
    }

    public void SetEdgeSnapping(bool value)
    {
        GetDock().EdgeSnapping = value;
        _dockService.SaveChanges();
    }

    public bool GetFolderStacks() => GetDock().FolderStacks;

    public void SetFolderStacks(bool value)
    {
        GetDock().FolderStacks = value;
        _dockService.SaveChanges();
    }

    public bool GetAutoHide() => GetDock().AutoHide;

    public void SetAutoHide(bool value)
    {
        GetDock().AutoHide = value;
        _dockService.SaveChanges();
    }

    public bool GetHideInFullscreen() => GetDock().HideInFullscreen;

    public bool GetAttentionBounce() => GetDock().AttentionBounce;

    // --- #11 presets ---

    public IReadOnlyList<AppearancePreset> GetUserPresets() => GetDock().Presets;

    public void SavePreset(string name)
    {
        var dock = GetDock();
        dock.Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        dock.Presets.Add(AppearancePreset.Capture(name, dock));
        _dockService.SaveChanges();
    }

    public void DeletePreset(string name)
    {
        GetDock().Presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        _dockService.SaveChanges();
    }

    public void ApplyPreset(AppearancePreset preset)
    {
        preset.ApplyTo(GetDock());
        _dockService.SaveChanges();
    }

    public void SetAttentionBounce(bool value)
    {
        GetDock().AttentionBounce = value;
        _dockService.SaveChanges();
    }

    public void SetHideInFullscreen(bool value)
    {
        GetDock().HideInFullscreen = value;
        _dockService.SaveChanges();
    }

    public bool GetHideTaskbar() => GetDock().HideTaskbar;

    public void SetHideTaskbar(bool value)
    {
        GetDock().HideTaskbar = value;
        _dockService.SaveChanges();
    }

    public bool GetTintIcons() => GetDock().TintIcons;

    public void SetTintIcons(bool value)
    {
        GetDock().TintIcons = value;
        _dockService.SaveChanges();
    }

    public string GetTintColorRGB() => GetDock().TintColorRGB;

    public void SetTintColorRGB(string value)
    {
        GetDock().TintColorRGB = value;
        _dockService.SaveChanges();
    }
}
