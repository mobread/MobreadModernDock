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

    // --- Backdrop blur / acrylic ---

    public const string BlurNone = "none";
    public const string BlurBlur = "blur";
    public const string BlurAcrylic = "acrylic";

    /// <summary>Backdrop material behind the dock: none / blur / acrylic.</summary>
    public string GetBlurMode()
    {
        string mode = GetDock().BlurMode;
        return mode is BlurBlur or BlurAcrylic ? mode : BlurNone;
    }

    public void SetBlurMode(string value)
    {
        GetDock().BlurMode = value is BlurBlur or BlurAcrylic ? value : BlurNone;
        _dockService.SaveChanges();
    }

    // --- macOS-style hover magnification ---

    /// <summary>
    /// Whether magnification is switched on AND actually usable. It is
    /// restricted to a single-line dock: with wrapped rows the pushed-apart
    /// icons would collide across lines.
    /// </summary>
    public bool GetMagnifyIcons() => GetDock().MagnifyIcons && GetDockRows() == 1;

    /// <summary>The raw setting, ignoring whether the current layout supports it.</summary>
    public bool GetMagnifyIconsSetting() => GetDock().MagnifyIcons;

    public void SetMagnifyIcons(bool value)
    {
        GetDock().MagnifyIcons = value;
        _dockService.SaveChanges();
    }

    /// <summary>Peak magnification as a percentage (100..250) for the slider.</summary>
    public int GetMagnifyScalePercentage() =>
        (int)Math.Round(Math.Clamp(GetDock().MagnifyScale, 1.0, DockMagnification.MaxScaleLimit) * 100);

    public void SetMagnifyScalePercentage(int value)
    {
        GetDock().MagnifyScale = Math.Clamp(value, 100, (int)(DockMagnification.MaxScaleLimit * 100)) / 100.0;
        _dockService.SaveChanges();
    }

    /// <summary>Peak magnification as a raw factor, clamped to the supported range.</summary>
    public double GetMagnifyScale() =>
        Math.Clamp(GetDock().MagnifyScale, 1.0, DockMagnification.MaxScaleLimit);

    // --- Update checking ---

    public bool GetCheckUpdatesOnStartup() => GetDock().CheckUpdatesOnStartup;

    public void SetCheckUpdatesOnStartup(bool value)
    {
        GetDock().CheckUpdatesOnStartup = value;
        _dockService.SaveChanges();
    }

    /// <summary>
    /// True when the startup check is enabled and a day has passed since the
    /// last one. Keeps a restart loop from hammering the GitHub API, which is
    /// rate-limited to 60 requests/hour for unauthenticated callers.
    /// </summary>
    public bool ShouldCheckForUpdates(DateTime utcNow)
    {
        if (!GetDock().CheckUpdatesOnStartup) return false;
        var last = GetDock().LastUpdateCheckUtc;
        return last is null || (utcNow - last.Value) >= TimeSpan.FromDays(1);
    }

    public void MarkUpdateChecked(DateTime utcNow)
    {
        GetDock().LastUpdateCheckUtc = utcNow;
        _dockService.SaveChanges();
    }

    // --- Window preview / tooltip delay ---

    /// <summary>Longest delay offered by the slider, in ms.</summary>
    public const int MaxPreviewDelayMs = 2000;

    /// <summary>
    /// How long the pointer must rest on an icon before its preview appears.
    /// 0 means immediate, which is how the dock behaved before this setting.
    /// </summary>
    public int GetPreviewDelayMs() => Math.Clamp(GetDock().PreviewDelayMs, 0, MaxPreviewDelayMs);

    public void SetPreviewDelayMs(int value)
    {
        GetDock().PreviewDelayMs = Math.Clamp(value, 0, MaxPreviewDelayMs);
        _dockService.SaveChanges();
    }

    // --- Follow system light/dark theme ---

    /// <summary>Dock background used when Windows is in dark mode.</summary>
    public const string DarkThemeColorRGB = "0, 0, 0, ";

    /// <summary>Dock background used when Windows is in light mode.</summary>
    public const string LightThemeColorRGB = "245, 245, 245, ";

    public bool GetFollowSystemTheme() => GetDock().FollowSystemTheme;

    public void SetFollowSystemTheme(bool value)
    {
        GetDock().FollowSystemTheme = value;
        _dockService.SaveChanges();
    }

    /// <summary>
    /// Switches the dock colour to the light or dark preset. No-op (returns
    /// false) when the setting is off or the colour is already correct, so the
    /// caller can skip a dock rebuild.
    /// </summary>
    public bool ApplySystemTheme(bool isLightTheme)
    {
        if (!GetFollowSystemTheme()) return false;
        string wanted = isLightTheme ? LightThemeColorRGB : DarkThemeColorRGB;
        if (string.Equals(GetDock().DockColorRGB, wanted, StringComparison.Ordinal)) return false;
        GetDock().DockColorRGB = wanted;
        _dockService.SaveChanges();
        return true;
    }
}
