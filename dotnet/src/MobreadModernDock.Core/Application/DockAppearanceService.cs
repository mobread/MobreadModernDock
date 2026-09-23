namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Models;

/// <summary>
/// Reads and writes every appearance setting on the dock model — icon size,
/// spacing, colour, rounding, magnification and the rest — saving on each change.
/// </summary>
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
        var dock = GetDock();
        dock.DockColorRGB = value;
        // A colour picked while the system theme is driving the dock is the
        // user's latest deliberate choice: make it the one that comes back
        // when follow-theme is switched off.
        if (dock.FollowSystemTheme) dock.CustomDockColorRGB = value;
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

    /// <summary>Padding inside the dock bar (0..40 px).</summary>
    public int GetDockPadding() => Math.Clamp(GetDock().DockPadding, 0, 40);

    public void SetDockPadding(int value)
    {
        GetDock().DockPadding = Math.Clamp(value, 0, 40);
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

    /// <summary>
    /// Stores a preset that came from a theme file rather than the live dock.
    /// A name collision is resolved by suffixing rather than overwriting:
    /// importing someone else's "Glass" must not silently destroy yours.
    /// </summary>
    public void SaveImportedPreset(AppearancePreset preset)
    {
        var dock = GetDock();
        var taken = new HashSet<string>(
            AppearancePreset.BuiltIns().Select(p => p.Name).Concat(dock.Presets.Select(p => p.Name)),
            StringComparer.OrdinalIgnoreCase);

        string name = preset.Name;
        for (int i = 2; taken.Contains(name); i++)
            name = $"{preset.Name} ({i})";
        preset.Name = name;

        dock.Presets.Add(preset);
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

    // --- Lock dock ---

    public bool GetLockDock() => GetDock().LockDock;

    public void SetLockDock(bool value)
    {
        GetDock().LockDock = value;
        _dockService.SaveChanges();
    }

    // --- Per-app hide rules ---

    /// <summary>Executable file names that hide the dock while focused, lower-cased, de-duplicated.</summary>
    public IReadOnlyList<string> GetHideForApps() => GetDock().HideForApps;

    /// <summary>Adds a rule by executable file name (a full path is reduced to its file name). Returns false if already present.</summary>
    public bool AddHideForApp(string executable)
    {
        string name = NormalizeExeName(executable);
        if (name.Length == 0) return false;
        var list = GetDock().HideForApps;
        if (list.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase))) return false;
        list.Add(name);
        _dockService.SaveChanges();
        return true;
    }

    public void RemoveHideForApp(string executable)
    {
        string name = NormalizeExeName(executable);
        int removed = GetDock().HideForApps.RemoveAll(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) _dockService.SaveChanges();
    }

    /// <summary>True when the given foreground executable (path or name) matches a hide rule.</summary>
    public bool IsHideForApp(string? executablePath)
    {
        if (string.IsNullOrEmpty(executablePath)) return false;
        var rules = GetDock().HideForApps;
        if (rules.Count == 0) return false;
        string name = NormalizeExeName(executablePath);
        return rules.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeExeName(string executable)
    {
        string trimmed = executable.Trim().Trim('"');
        if (trimmed.Length == 0) return "";
        try { trimmed = Path.GetFileName(trimmed); } catch { }
        return trimmed.ToLowerInvariant();
    }

    // --- Keyboard shortcuts ---

    public bool GetHotkeysEnabled() => GetDock().HotkeysEnabled;

    public void SetHotkeysEnabled(bool value)
    {
        GetDock().HotkeysEnabled = value;
        _dockService.SaveChanges();
    }

    public string GetHotkeyToggleDock() => GetDock().HotkeyToggleDock ?? "";

    /// <summary>Stores the chord in canonical form; an unparsable one is stored as empty (= off).</summary>
    public void SetHotkeyToggleDock(string chord)
    {
        var parsed = HotkeyChord.TryParse(chord);
        GetDock().HotkeyToggleDock = parsed is null ? "" : HotkeyChord.Format(parsed);
        _dockService.SaveChanges();
    }

    public string GetHotkeyLaunchModifiers() => GetDock().HotkeyLaunchModifiers ?? "";

    public void SetHotkeyLaunchModifiers(string modifiers)
    {
        var parsed = HotkeyChord.TryParse(modifiers, allowModifiersOnly: true);
        GetDock().HotkeyLaunchModifiers = parsed is null || parsed.HasKey ? "" : HotkeyChord.Format(parsed);
        _dockService.SaveChanges();
    }

    public void SetHideTaskbar(bool value)
    {
        GetDock().HideTaskbar = value;
        _dockService.SaveChanges();
    }

    public bool GetReserveScreenEdge() => GetDock().ReserveScreenEdge;

    public void SetReserveScreenEdge(bool value)
    {
        GetDock().ReserveScreenEdge = value;
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

    /// <summary>
    /// Turning follow-theme on stashes the user's current colour; turning it
    /// off restores it. Without the stash the last light/dark preset the
    /// watcher wrote would simply stay behind, and the colour the user had
    /// picked would be gone. Returns true when the dock colour changed.
    /// </summary>
    public bool SetFollowSystemTheme(bool value)
    {
        var dock = GetDock();
        bool changedColor = false;
        if (value && !dock.FollowSystemTheme)
        {
            dock.CustomDockColorRGB = dock.DockColorRGB;
        }
        else if (!value && dock.FollowSystemTheme && dock.CustomDockColorRGB is { } custom)
        {
            changedColor = !string.Equals(dock.DockColorRGB, custom, StringComparison.Ordinal);
            dock.DockColorRGB = custom;
            dock.CustomDockColorRGB = null;
        }
        dock.FollowSystemTheme = value;
        _dockService.SaveChanges();
        return changedColor;
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
