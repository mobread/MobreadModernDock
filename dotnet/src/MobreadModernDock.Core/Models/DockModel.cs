namespace MobreadModernDock.Core.Models;

using System.Text.Json.Serialization;
using MobreadModernDock.Core.Application;

/// <summary>
/// Direct port of DockModel. Holds the full dock configuration: items,
/// appearance settings, positioning, and language.
///
/// JSON property names use camelCase to match the original Java/Jackson
/// config.json format exactly, ensuring existing user configs load as-is.
/// </summary>
public class DockModel
{
    [JsonPropertyName("items")]
    public List<DockItem> Items { get; set; } = new();

    private int _iconsSize = 24;
    [JsonPropertyName("iconsSize")]
    public int IconsSize
    {
        get => _iconsSize;
        set => _iconsSize = value;
    }

    private int _spacingBetweenIcons = 0;
    [JsonPropertyName("spacingBetweenIcons")]
    public int SpacingBetweenIcons
    {
        get => _spacingBetweenIcons;
        set => _spacingBetweenIcons = value;
    }

    private double _dockTransparency = 0.3;
    [JsonPropertyName("dockTransparency")]
    public double DockTransparency
    {
        get => _dockTransparency;
        set => _dockTransparency = value;
    }

    [JsonPropertyName("dockBorderRounding")]
    public int DockBorderRounding { get; set; } = 10;

    [JsonPropertyName("dockColorRGB")]
    public string DockColorRGB { get; set; } = "0, 0, 0, ";

    [JsonPropertyName("dockPositionX")]
    [JsonInclude]
    public double DockPositionX { get; private set; }

    [JsonPropertyName("dockPositionY")]
    [JsonInclude]
    public double DockPositionY { get; private set; }

    [JsonPropertyName("positioningMode")]
    public DockPositioningMode PositioningMode { get; set; } = DockPositioningMode.STATIC;

    [JsonPropertyName("verticalAnchor")]
    public DockVerticalAnchor VerticalAnchor { get; set; } = DockVerticalAnchor.TOP;

    [JsonPropertyName("horizontalAnchor")]
    public DockHorizontalAnchor HorizontalAnchor { get; set; } = DockHorizontalAnchor.MIDDLE;

    [JsonPropertyName("topSpacing")]
    public int TopSpacing { get; set; } = 20;

    [JsonPropertyName("leftSpacing")]
    public int LeftSpacing { get; set; } = 20;

    [JsonPropertyName("rightSpacing")]
    public int RightSpacing { get; set; } = 20;

    [JsonPropertyName("bottomSpacing")]
    public int BottomSpacing { get; set; } = 20;

    [JsonPropertyName("language")]
    public SupportedLanguage Language { get; set; } = SupportedLanguage.EN_US;

    [JsonPropertyName("showUnpinnedRunningApps")]
    public bool ShowUnpinnedRunningApps { get; set; } = true;

    [JsonPropertyName("verticalDock")]
    public bool VerticalDock { get; set; }

    [JsonPropertyName("tintIcons")]
    public bool TintIcons { get; set; }

    [JsonPropertyName("tintColorRGB")]
    public string TintColorRGB { get; set; } = "0, 80, 140";

    /// <summary>
    /// When true the dock and all widgets float above every other window
    /// (topmost). When false (default) they live on the desktop layer behind
    /// normal windows, like the desktop icons.
    /// </summary>
    [JsonPropertyName("alwaysOnTop")]
    public bool AlwaysOnTop { get; set; }

    /// <summary>Number of rows (horizontal dock) or columns (vertical dock) the pinned icons wrap into. 1 = classic single line.</summary>
    [JsonPropertyName("dockRows")]
    public int DockRows { get; set; } = 1;

    /// <summary>Hides the Windows taskbar on every monitor while the app runs; restored on exit.</summary>
    [JsonPropertyName("hideTaskbar")]
    public bool HideTaskbar { get; set; }

    /// <summary>
    /// Reserve the dock's screen edge as an appbar, so maximized windows stop
    /// at the dock instead of sitting underneath it. Only applies while the
    /// dock is snapped to an edge and auto-hide is off.
    /// </summary>
    [JsonPropertyName("reserveScreenEdge")]
    public bool ReserveScreenEdge { get; set; }

    /// <summary>Hide the dock and widgets while a fullscreen app (game, video) is in the foreground.</summary>
    [JsonPropertyName("hideInFullscreen")]
    public bool HideInFullscreen { get; set; } = true;

    /// <summary>Bounce a dock icon when its app flashes the taskbar for attention.</summary>
    [JsonPropertyName("attentionBounce")]
    public bool AttentionBounce { get; set; } = true;

    /// <summary>#10 Show a copy of the dock on every monitor (secondaries use the static anchors).</summary>
    [JsonPropertyName("mirrorOnAllMonitors")]
    public bool MirrorOnAllMonitors { get; set; }

    /// <summary>Slide the dock off the nearest screen edge when the pointer leaves it; reveal on edge hover.</summary>
    [JsonPropertyName("autoHide")]
    public bool AutoHide { get; set; }

    /// <summary>Clicking a folder item shows its contents in a popup ("stack") instead of opening Explorer.</summary>
    [JsonPropertyName("folderStacks")]
    public bool FolderStacks { get; set; } = true;

    /// <summary>Snap the dock and widgets to screen edges/centre when a drag ends near them.</summary>
    [JsonPropertyName("edgeSnapping")]
    public bool EdgeSnapping { get; set; } = true;

    /// <summary>
    /// Backdrop material drawn behind the dock and widgets: "none" (flat
    /// alpha, the classic look), "blur" (gaussian) or "acrylic" (Fluent
    /// acrylic with tint + noise).
    /// </summary>
    [JsonPropertyName("blurMode")]
    public string BlurMode { get; set; } = "none";

    /// <summary>
    /// macOS-style hover magnification: icons near the pointer grow and push
    /// their neighbours aside. Only applies to a single-line dock (see
    /// <see cref="DockRows"/>); wrapped rows would collide.
    /// </summary>
    [JsonPropertyName("magnifyIcons")]
    public bool MagnifyIcons { get; set; }

    /// <summary>Peak magnification, 1.0 = off. Clamped to 2.5.</summary>
    [JsonPropertyName("magnifyScale")]
    public double MagnifyScale { get; set; } = 1.6;

    /// <summary>
    /// Check GitHub for a newer release shortly after startup (at most once a
    /// day). Only sets a flag in Settings — nothing is downloaded or installed
    /// without the user asking.
    /// </summary>
    [JsonPropertyName("checkUpdatesOnStartup")]
    public bool CheckUpdatesOnStartup { get; set; } = true;

    /// <summary>
    /// When the last automatic check ran, so a restart doesn't re-check.
    /// Round-trips as an ISO-8601 string; null means never.
    /// </summary>
    [JsonPropertyName("lastUpdateCheckUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>
    /// How long the pointer must rest on an icon before its window preview
    /// (and tooltip) appears, in milliseconds. 0 = immediate. A delay keeps a
    /// sweep across the dock from firing a preview per icon, which is most
    /// noticeable with magnification on.
    /// </summary>
    [JsonPropertyName("previewDelayMs")]
    public int PreviewDelayMs { get; set; } = 400;

    /// <summary>
    /// Follow the Windows app light/dark setting: switches the dock colour
    /// between a dark and a light preset whenever the system theme changes.
    /// </summary>
    [JsonPropertyName("followSystemTheme")]
    public bool FollowSystemTheme { get; set; }

    /// <summary>Gap in px kept between a snapped dock/widget and the screen edge. 0 = flush.</summary>
    [JsonPropertyName("edgeSnapMargin")]
    public int EdgeSnapMargin { get; set; } = 8;

    /// <summary>
    /// Whole-window opacity (icons, text and background together) for the dock
    /// and for widgets that follow the global value. 0.2..1.0. Distinct from
    /// DockTransparency, which only affects the background fill.
    /// </summary>
    [JsonPropertyName("globalOpacity")]
    public double GlobalOpacity { get; set; } = 1.0;

    /// <summary>#11 User-saved appearance presets (built-ins are not persisted).</summary>
    [JsonPropertyName("presets")]
    public List<AppearancePreset> Presets { get; set; } = new();

    // --- Floating widgets ---

    [JsonPropertyName("widgets")]
    public List<WidgetDefinition> Widgets { get; set; } = new();

    // Legacy single text-widget fields (pre-widget-framework configs). Kept
    // so existing configs still deserialize; WidgetService migrates them
    // into a "text" WidgetDefinition on first load and they are not written
    // back once null.
    [JsonPropertyName("widgetEnabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WidgetEnabled { get; set; }

    [JsonPropertyName("widgetText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? WidgetText { get; set; }

    [JsonPropertyName("widgetFontSize")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? WidgetFontSize { get; set; }

    [JsonPropertyName("widgetPositionX")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? WidgetPositionX { get; set; }

    [JsonPropertyName("widgetPositionY")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? WidgetPositionY { get; set; }

    public void AddItem(DockItem item)
    {
        Items.Add(item);
        KeepSettingsLast();
    }

    public void RemoveItem(int index) => Items.RemoveAt(index);

    /// <summary>
    /// Fills a brand-new config. <paramref name="seed"/> (typically
    /// <see cref="Application.FirstRunDefaults.Compose"/>) goes first; the
    /// Settings gear is always appended last so the dock is never empty and
    /// always reachable.
    /// </summary>
    public void LoadDefaultItems(IEnumerable<DockItem>? seed = null)
    {
        if (seed != null) Items.AddRange(seed);
        Items.Add(new DockSettingsItemModel());
    }

    public void SwapItems(int firstItemIdx, int secondItemIdx)
    {
        (Items[firstItemIdx], Items[secondItemIdx]) =
            (Items[secondItemIdx], Items[firstItemIdx]);
        KeepSettingsLast();
    }

    /// <summary>
    /// Moves the item at <paramref name="fromIndex"/> into the gap at
    /// <paramref name="toIndex"/> (0..Count): the item ends up at that final
    /// position, with the others shifting accordingly. Unlike SwapItems this
    /// supports arbitrary jumps, which drag-reorder needs.
    /// </summary>
    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Items.Count) return;
        toIndex = Math.Clamp(toIndex, 0, Items.Count);
        if (fromIndex == toIndex) return;

        var item = Items[fromIndex];
        Items.RemoveAt(fromIndex);
        // After removal the gap index shifts left when moving downwards.
        if (toIndex > fromIndex) toIndex--;
        Items.Insert(toIndex, item);
        KeepSettingsLast();
    }

    /// <summary>
    /// The Settings gear is always the last dock item. Called after every
    /// mutation and once on load, so a config edited by hand or written by an
    /// older version is normalized too. Returns true when the order changed.
    /// </summary>
    public bool KeepSettingsLast()
    {
        var settings = Items.Where(i => i is DockSettingsItemModel).ToList();
        if (settings.Count == 0) return false;
        int firstSettingsIndex = Items.IndexOf(settings[0]);
        if (settings.Count == 1 && firstSettingsIndex == Items.Count - 1) return false;

        Items.RemoveAll(i => i is DockSettingsItemModel);
        Items.AddRange(settings);
        return true;
    }

    public void SetDockPosition(double positionX, double positionY)
    {
        DockPositionX = positionX;
        DockPositionY = positionY;
    }

    /// <summary>
    /// Copies every persisted field from <paramref name="other"/> into this
    /// instance. Used by config import: the whole app holds one DockModel
    /// reference (services, view models), so the imported config has to be
    /// merged in place rather than swapped for a new object.
    /// </summary>
    public void CopyFrom(DockModel other)
    {
        Items = other.Items;
        IconsSize = other.IconsSize;
        SpacingBetweenIcons = other.SpacingBetweenIcons;
        DockTransparency = other.DockTransparency;
        DockBorderRounding = other.DockBorderRounding;
        DockColorRGB = other.DockColorRGB;
        DockPositionX = other.DockPositionX;
        DockPositionY = other.DockPositionY;
        PositioningMode = other.PositioningMode;
        VerticalAnchor = other.VerticalAnchor;
        HorizontalAnchor = other.HorizontalAnchor;
        TopSpacing = other.TopSpacing;
        LeftSpacing = other.LeftSpacing;
        RightSpacing = other.RightSpacing;
        BottomSpacing = other.BottomSpacing;
        Language = other.Language;
        ShowUnpinnedRunningApps = other.ShowUnpinnedRunningApps;
        VerticalDock = other.VerticalDock;
        TintIcons = other.TintIcons;
        TintColorRGB = other.TintColorRGB;
        AlwaysOnTop = other.AlwaysOnTop;
        DockRows = other.DockRows;
        HideTaskbar = other.HideTaskbar;
        ReserveScreenEdge = other.ReserveScreenEdge;
        HideInFullscreen = other.HideInFullscreen;
        AttentionBounce = other.AttentionBounce;
        MirrorOnAllMonitors = other.MirrorOnAllMonitors;
        AutoHide = other.AutoHide;
        FolderStacks = other.FolderStacks;
        EdgeSnapping = other.EdgeSnapping;
        EdgeSnapMargin = other.EdgeSnapMargin;
        BlurMode = other.BlurMode;
        MagnifyIcons = other.MagnifyIcons;
        MagnifyScale = other.MagnifyScale;
        CheckUpdatesOnStartup = other.CheckUpdatesOnStartup;
        LastUpdateCheckUtc = other.LastUpdateCheckUtc;
        PreviewDelayMs = other.PreviewDelayMs;
        FollowSystemTheme = other.FollowSystemTheme;
        GlobalOpacity = other.GlobalOpacity;
        Presets = other.Presets;
        Widgets = other.Widgets;
        KeepSettingsLast();
    }
}
