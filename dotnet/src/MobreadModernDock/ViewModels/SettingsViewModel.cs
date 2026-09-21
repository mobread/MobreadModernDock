using System.Collections.ObjectModel;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MobreadModernDock.Core.Application;
using MobreadModernDock.Core.Models;

namespace MobreadModernDock.ViewModels;

/// <summary>One row in the Settings dock-items list: display label + icon.</summary>
public sealed record DockItemListEntry(string Label, Bitmap? Icon);

/// <summary>Alignment combo-box entry: localized label + enum value.</summary>
public sealed record AnchorOption(string Label, DockVerticalAnchor Value);

/// <summary>Alignment combo-box entry: localized label + enum value.</summary>
public sealed record HorizontalAnchorOption(string Label, DockHorizontalAnchor Value);

/// <summary>ViewModel for the Settings window. Port of SettingsController.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly AppServices _appServices;
    private readonly Action _dockRefreshAction;
    private readonly Action<DockPositioningMode> _positioningModeChangeAction;
    private readonly Action _applyLocalizedTexts;
    private bool _localizationRegistered;
    private bool _isInitialized;

    private SupportedLanguage _selectedLanguage;

    // Icon management
    private int _selectedItemIndex = -1;

    // Appearance
    private int _iconSize;
    private int _iconSpacing;
    private int _transparency;
    private int _borderRounding;
    private Color _dockColor = Colors.Black;
    private bool _tintIcons;
    private Color _tintColor = Color.FromRgb(0, 80, 140); // 7th preset by default

    // Positioning
    private bool _isStaticMode = true;
    private DockVerticalAnchor _verticalAnchor = DockVerticalAnchor.TOP;
    private DockHorizontalAnchor _horizontalAnchor = DockHorizontalAnchor.MIDDLE;
    private int _topSpacing, _leftSpacing, _rightSpacing, _bottomSpacing;

    public ObservableCollection<DockItemListEntry> ItemEntries { get; } = new();
    public SupportedLanguage[] Languages => Enum.GetValues<SupportedLanguage>();
    public string[] LanguageNames => Languages.Select(l => l.NativeDisplayName()).ToArray();

    public IReadOnlyList<AnchorOption> VerticalAnchorOptions => new[]
    {
        new AnchorOption(T("settings.positioning.choice.top"), DockVerticalAnchor.TOP),
        new AnchorOption(T("settings.positioning.choice.middle"), DockVerticalAnchor.MIDDLE),
        new AnchorOption(T("settings.positioning.choice.down"), DockVerticalAnchor.DOWN)
    };

    public IReadOnlyList<HorizontalAnchorOption> HorizontalAnchorOptions => new[]
    {
        new HorizontalAnchorOption(T("settings.positioning.choice.left"), DockHorizontalAnchor.LEFT),
        new HorizontalAnchorOption(T("settings.positioning.choice.middle"), DockHorizontalAnchor.MIDDLE),
        new HorizontalAnchorOption(T("settings.positioning.choice.right"), DockHorizontalAnchor.RIGHT)
    };

    public int SelectedLanguageIndex
    {
        get => Array.IndexOf(Languages, _selectedLanguage);
        set
        {
            if (value < 0 || value >= Languages.Length) return;
            SelectedLanguage = Languages[value];
        }
    }

    public int SelectedItemIndex
    {
        get => _selectedItemIndex;
        set { SetProperty(ref _selectedItemIndex, value); UpdateButtonStates(); }
    }

    public int IconSize { get => _iconSize; set => SetProperty(ref _iconSize, value); }
    public int IconSpacing { get => _iconSpacing; set => SetProperty(ref _iconSpacing, value); }
    private int _dockRows = 1;
    public int DockRows
    {
        get => _dockRows;
        // Feeds the magnification warning: the effect is single-row only.
        set { if (SetProperty(ref _dockRows, value)) OnPropertyChanged(nameof(ShowMagnifyRowsWarning)); }
    }
    public int Transparency { get => _transparency; set => SetProperty(ref _transparency, value); }

    private int _dockPadding = 10;
    /// <summary>Padding inside the dock bar; with icon size this sets the bar's thickness.</summary>
    public int DockPadding
    {
        get => _dockPadding;
        set { if (SetProperty(ref _dockPadding, value)) OnPropertyChanged(nameof(DockPaddingLabel)); }
    }
    public string DockPaddingLabel => $"{DockPadding} px";
    private int _globalOpacity = 100;
    public int GlobalOpacity { get => _globalOpacity; set => SetProperty(ref _globalOpacity, value); }
    public int BorderRounding { get => _borderRounding; set => SetProperty(ref _borderRounding, value); }

    /// <summary>
    /// Quick-pick colours offered inside the colour picker's palette tab, so
    /// the common choices stay one click away now that the swatch grid has
    /// been replaced by a wheel.
    /// </summary>
    public IReadOnlyList<Color> PresetPaletteColors { get; } = new[]
    {
        Color.FromRgb(0, 0, 0),       // black
        Color.FromRgb(30, 30, 30),    // dark gray
        Color.FromRgb(60, 60, 60),    // gray
        Color.FromRgb(120, 120, 120), // light gray
        Color.FromRgb(255, 255, 255), // white
        Color.FromRgb(20, 50, 90),    // navy
        Color.FromRgb(0, 80, 140),    // blue
        Color.FromRgb(0, 100, 80),    // teal
        Color.FromRgb(60, 90, 20),    // olive
        Color.FromRgb(120, 70, 0),    // brown
        Color.FromRgb(140, 30, 40),   // dark red
        Color.FromRgb(90, 40, 90)     // purple
    };

    // The brush properties are computed from these, and must re-notify even
    // during Initialize() (when _isInitialized is false and the change router
    // below is skipped) - otherwise the preview swatch keeps the constructor's
    // default instead of the loaded colour.
    public Color DockColor
    {
        get => _dockColor;
        set { if (SetProperty(ref _dockColor, value)) OnPropertyChanged(nameof(DockColorBrush)); }
    }
    public IBrush DockColorBrush => new SolidColorBrush(DockColor);

    /// <summary>Enables the iOS-style color tint over all dock icons.</summary>
    public bool TintIcons { get => _tintIcons; set => SetProperty(ref _tintIcons, value); }
    public Color TintColor
    {
        get => _tintColor;
        set { if (SetProperty(ref _tintColor, value)) OnPropertyChanged(nameof(TintColorBrush)); }
    }
    public IBrush TintColorBrush => new SolidColorBrush(TintColor);

    public bool IsStaticMode { get => _isStaticMode; set => SetProperty(ref _isStaticMode, value); }

    private bool _followSystemTheme;
    /// <summary>Swap the dock colour with the Windows light/dark app theme.</summary>
    public bool FollowSystemTheme { get => _followSystemTheme; set => SetProperty(ref _followSystemTheme, value); }
    public string FollowSystemThemeText => T("settings.general.followSystemTheme");
    public string FollowSystemThemeHelper => T("settings.general.followSystemTheme.helper");

    // --- macOS-style magnification ---

    private bool _magnifyIcons;
    /// <summary>Grow icons near the pointer, macOS-dock style.</summary>
    public bool MagnifyIcons
    {
        get => _magnifyIcons;
        set { if (SetProperty(ref _magnifyIcons, value)) OnPropertyChanged(nameof(ShowMagnifyRowsWarning)); }
    }

    private int _magnifyScale = 160;
    /// <summary>Peak magnification as a percentage (100..250).</summary>
    public int MagnifyScale
    {
        get => _magnifyScale;
        set { if (SetProperty(ref _magnifyScale, value)) OnPropertyChanged(nameof(MagnifyScaleLabel)); }
    }

    public string MagnifyScaleLabel => $"{MagnifyScale}%";
    public string MagnifyIconsText => T("settings.iconsCustomization.magnify.title");
    public string MagnifyIconsHelper => T("settings.iconsCustomization.magnify.helper");
    public string MagnifyScaleTitle => T("settings.iconsCustomization.magnify.scale");

    // --- Preview / tooltip hover delay ---

    private int _previewDelay = 400;
    /// <summary>Hover delay before a window preview or tooltip appears, in ms.</summary>
    public int PreviewDelay
    {
        get => _previewDelay;
        set { if (SetProperty(ref _previewDelay, value)) OnPropertyChanged(nameof(PreviewDelayLabel)); }
    }

    public string PreviewDelayLabel => PreviewDelay == 0
        ? T("settings.general.previewDelay.instant")
        : $"{PreviewDelay} ms";
    public string PreviewDelayTitle => T("settings.general.previewDelay");
    public string PreviewDelayHelper => T("settings.general.previewDelay.helper");

    /// <summary>
    /// Magnification only works on a single-line dock, so the warning shows
    /// whenever it is enabled while more than one row is configured.
    /// </summary>
    public bool ShowMagnifyRowsWarning => MagnifyIcons && DockRows > 1;
    public string MagnifyRowsWarning => T("settings.iconsCustomization.magnify.rowsWarning");

    // --- Config export / import ---

    public string ConfigTitle => T("settings.general.config.title");
    public string ConfigHelper => T("settings.general.config.helper");
    public string ConfigExportText => T("settings.general.config.export");
    public string ConfigImportText => T("settings.general.config.import");
    private string _configStatusText = "";
    public string ConfigStatusText { get => _configStatusText; set => SetProperty(ref _configStatusText, value); }

    public DockVerticalAnchor VerticalAnchor { get => _verticalAnchor; set => SetProperty(ref _verticalAnchor, value); }
    public DockHorizontalAnchor HorizontalAnchor { get => _horizontalAnchor; set => SetProperty(ref _horizontalAnchor, value); }

    /// <summary>Selected alignment combo entry (localized label + value).</summary>
    public AnchorOption? SelectedVerticalOption
    {
        get => VerticalAnchorOptions.FirstOrDefault(o => o.Value == VerticalAnchor);
        set
        {
            if (value != null && value.Value != VerticalAnchor)
                VerticalAnchor = value.Value;
        }
    }

    /// <summary>Selected alignment combo entry (localized label + value).</summary>
    public HorizontalAnchorOption? SelectedHorizontalOption
    {
        get => HorizontalAnchorOptions.FirstOrDefault(o => o.Value == HorizontalAnchor);
        set
        {
            if (value != null && value.Value != HorizontalAnchor)
                HorizontalAnchor = value.Value;
        }
    }
    public int TopSpacing { get => _topSpacing; set => SetProperty(ref _topSpacing, value); }
    public int LeftSpacing { get => _leftSpacing; set => SetProperty(ref _leftSpacing, value); }
    public int RightSpacing { get => _rightSpacing; set => SetProperty(ref _rightSpacing, value); }
    public int BottomSpacing { get => _bottomSpacing; set => SetProperty(ref _bottomSpacing, value); }

    // Button enabled states
    public bool CanRemove { get; private set; }
    public bool CanMoveUp { get; private set; }
    public bool CanMoveDown { get; private set; }

    public SupportedLanguage SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (SetProperty(ref _selectedLanguage, value))
                OnPropertyChanged(nameof(SelectedLanguageIndex));
        }
    }

    // Localized text properties
    public string WindowTitle => T("settings.window.title");
    public string PageTitle => T("settings.page.title");
    public string PageSubtitle => T("settings.page.subtitle");
    public string LanguageLabel => T("settings.language.label");
    public string TabIcons => T("settings.tab.icons");
    public string TabAppearance => T("settings.tab.appearance");
    public string TabLayout => T("settings.tab.layout");
    public string TabBehavior => T("settings.tab.behavior");
    public string TabGeneral => T("settings.tab.general");
    public string TabWidget => T("settings.tab.widget");
    public string SectionDock => T("settings.section.dock");
    public string SectionIcons => T("settings.section.icons");
    public string SectionMonitors => T("settings.section.monitors");
    public string SectionVisibility => T("settings.section.visibility");
    public string SectionApps => T("settings.section.apps");
    public string SectionStartup => T("settings.section.startup");
    public string SectionAbout => T("settings.section.about");
    public string WidgetsTitle => T("settings.widgets.title");
    public string WidgetsHelper => T("settings.widgets.helper");
    public string WidgetAddText => T("settings.widgets.add");
    public string WidgetRemoveText => T("settings.widgets.remove");
    public string WidgetEnabledText => T("settings.widgets.enabled");
    public string WidgetNoSelectionText => T("settings.widgets.noSelection");

    /// <summary>Localized display name for a widget type key.</summary>
    public string WidgetTypeName(string typeKey)
    {
        var provider = App.WidgetRegistry.Get(typeKey);
        return provider != null ? T(provider.DisplayNameKey) : typeKey;
    }
    // --- continued below ---
    public string ItemsTitle => T("settings.icons.items.title");
    public string ItemsHelper => T("settings.icons.items.helper");
    public string ActionsTitle => T("settings.icons.actions.title");
    public string ActionsHelper => T("settings.icons.actions.helper");
    public string MoveUpText => T("settings.icons.moveUp");
    public string MoveDownText => T("settings.icons.moveDown");
    public string AddProgramText => T("settings.icons.addProgram");
    public string AddFolderText => T("settings.icons.addFolder");
    public string ImportTaskbarText => T("settings.icons.importTaskbar");
    public string AddSeparatorText => T("settings.icons.addSeparator");

    /// <summary>
    /// Appends a divider to the dock. Separators are the one item type that
    /// is meaningfully repeatable, so there is no duplicate check.
    /// </summary>
    public void AddSeparator()
    {
        _appServices.DockService.AddItem(new DockSeparatorItemModel());
        RefreshItemLabels();
        _dockRefreshAction();
    }

    // --- #11 presets ---
    public string PresetsTitle => T("settings.dockCustomization.presets.title");
    public string PresetsHelper => T("settings.dockCustomization.presets.helper");
    public string PresetApplyText => T("settings.dockCustomization.presets.apply");
    public string PresetDeleteText => T("settings.dockCustomization.presets.delete");
    public string PresetSaveText => T("settings.dockCustomization.presets.save");
    public string PresetNameHint => T("settings.dockCustomization.presets.nameHint");

    public System.Collections.ObjectModel.ObservableCollection<string> PresetNames { get; } = new();
    private List<AppearancePreset> _presetList = new();
    private int _builtInCount;

    private int _selectedPresetIndex = -1;
    public int SelectedPresetIndex
    {
        get => _selectedPresetIndex;
        set { if (SetProperty(ref _selectedPresetIndex, value)) { OnPropertyChanged(nameof(CanApplyPreset)); OnPropertyChanged(nameof(CanDeletePreset)); } }
    }
    public bool CanApplyPreset => SelectedPresetIndex >= 0 && SelectedPresetIndex < _presetList.Count;
    public bool CanDeletePreset => SelectedPresetIndex >= _builtInCount && SelectedPresetIndex < _presetList.Count;

    private string _newPresetName = "";
    public string NewPresetName
    {
        get => _newPresetName;
        set { if (SetProperty(ref _newPresetName, value)) OnPropertyChanged(nameof(CanSavePreset)); }
    }
    public bool CanSavePreset => !string.IsNullOrWhiteSpace(NewPresetName);

    private void ReloadPresets()
    {
        var builtIns = AppearancePreset.BuiltIns();
        _builtInCount = builtIns.Count;
        _presetList = builtIns.Concat(_appServices.AppearanceService.GetUserPresets()).ToList();
        int keep = SelectedPresetIndex;
        PresetNames.Clear();
        foreach (var p in _presetList) PresetNames.Add(p.Name);
        SelectedPresetIndex = keep >= 0 && keep < _presetList.Count ? keep : -1;
        OnPropertyChanged(nameof(CanApplyPreset)); OnPropertyChanged(nameof(CanDeletePreset));
    }

    public void ApplySelectedPreset()
    {
        if (!CanApplyPreset) return;
        _appServices.AppearanceService.ApplyPreset(_presetList[SelectedPresetIndex]);
        // Re-read every appearance field so the sliders/pickers reflect the preset,
        // without the change handlers writing back one by one.
        _isInitialized = false;
        var app = _appServices.AppearanceService;
        IconSize = app.GetIconsSize();
        IconSpacing = app.GetSpacingBetweenIcons();
        DockRows = app.GetDockRows();
        DockPadding = app.GetDockPadding();
        Transparency = app.GetDockTransparencyPercentage();
        GlobalOpacity = app.GetGlobalOpacityPercentage();
        BorderRounding = app.GetDockBorderRounding();
        DockColor = ParseRgbColor(app.GetDockColorRGB());
        TintIcons = app.GetTintIcons();
        TintColor = ParseRgbColor(app.GetTintColorRGB());
        IsVerticalDock = app.GetVerticalDock();
        // Added with the theme format — a preset now carries magnification
        // too, so those controls must follow it as well.
        MagnifyIcons = app.GetMagnifyIconsSetting();
        MagnifyScale = app.GetMagnifyScalePercentage();
        _isInitialized = true;
        _dockRefreshAction();
    }

    public void SaveCurrentAsPreset()
    {
        if (!CanSavePreset) return;
        string name = NewPresetName.Trim();
        _appServices.AppearanceService.SavePreset(name);
        NewPresetName = "";
        ReloadPresets();
        SelectedPresetIndex = _presetList.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public void DeleteSelectedPreset()
    {
        if (!CanDeletePreset) return;
        _appServices.AppearanceService.DeletePreset(_presetList[SelectedPresetIndex].Name);
        SelectedPresetIndex = -1;
        ReloadPresets();
    }

    // --- Theme sharing (.mbtheme) ---

    private string _themeStatusText = "";
    /// <summary>One-line result of the last theme export/import, shown under the buttons.</summary>
    public string ThemeStatusText { get => _themeStatusText; set => SetProperty(ref _themeStatusText, value); }

    public string ThemeExportText => T("settings.theme.export");
    public string ThemeImportText => T("settings.theme.import");
    public string ThemeHelper => T("settings.theme.helper");

    /// <summary>
    /// Writes the *current look* to a .mbtheme file. Deliberately the live
    /// appearance rather than the selected preset: what you are looking at is
    /// what you want to share, and it saves a "save preset first" step.
    /// </summary>
    public async Task ExportThemeAsync(Window window)
    {
        string name = string.IsNullOrWhiteSpace(NewPresetName)
            ? (CanApplyPreset ? _presetList[SelectedPresetIndex].Name : "My theme")
            : NewPresetName.Trim();

        var file = await window.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = ThemeExportText,
                SuggestedFileName = ThemeFile.SuggestedFileName(name),
                DefaultExtension = ThemeFile.Extension.TrimStart('.'),
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Mobread theme")
                    {
                        Patterns = new[] { "*" + ThemeFile.Extension },
                    },
                }
            });
        if (file == null) return;

        try
        {
            var preset = AppearancePreset.Capture(name, _appServices.DockService.GetDock());
            await System.IO.File.WriteAllTextAsync(file.Path.LocalPath, ThemeFile.Serialize(preset));
            ThemeStatusText = T("settings.theme.exported");
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[theme] export failed: {e.Message}");
            ThemeStatusText = T("settings.theme.failed");
        }
    }

    /// <summary>
    /// Loads a .mbtheme, saves it as a user preset and applies it, so an
    /// imported theme is both visible immediately and kept for later.
    /// </summary>
    public async Task ImportThemeAsync(Window window)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = ThemeImportText,
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("Mobread theme")
                    {
                        Patterns = new[] { "*" + ThemeFile.Extension, "*.json" },
                    },
                }
            });
        if (files.Count == 0) return;

        ApplyImportedTheme(files[0].Path.LocalPath);
    }

    /// <summary>
    /// Shared by the Import button and the drag-drop handler: read, validate,
    /// store as a preset, apply. Returns false when the file is not a theme.
    /// </summary>
    public bool ApplyImportedTheme(string path)
    {
        AppearancePreset? preset = null;
        try
        {
            if (System.IO.File.Exists(path))
                preset = ThemeFile.TryParse(System.IO.File.ReadAllText(path));
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine($"[theme] import failed: {e.Message}");
        }

        if (preset == null)
        {
            ThemeStatusText = T("settings.theme.invalid");
            return false;
        }

        // Keeping it as a preset means an imported theme survives switching
        // to another one and back.
        _appServices.AppearanceService.SaveImportedPreset(preset);
        ReloadPresets();
        SelectedPresetIndex = _presetList.FindIndex(
            p => string.Equals(p.Name, preset.Name, StringComparison.OrdinalIgnoreCase));
        ApplySelectedPreset();
        ThemeStatusText = string.Format(T("settings.theme.imported"), preset.Name);
        return true;
    }
    public string AddModuleText => T("settings.icons.addWindowsModule");
    public string RemoveText => T("settings.icons.removeSelected");
    public string IconSizeTitle => T("settings.iconsCustomization.size.title");
    public string DockRowsTitle => T("settings.iconsCustomization.rows.title");
    public string DockRowsHelper => T("settings.iconsCustomization.rows.helper");
    public string IconSizeHelper => T("settings.iconsCustomization.size.helper");
    public string SpacingTitle => T("settings.iconsCustomization.spacing.title");
    public string SpacingHelper => T("settings.iconsCustomization.spacing.helper");
    public string TintIconsTitle => T("settings.iconsCustomization.tint.title");
    public string TintColorHelper => T("settings.iconsCustomization.tint.helper");
    public string TransparencyTitle => T("settings.dockCustomization.transparency.title");
    public string TransparencyHelper => T("settings.dockCustomization.transparency.helper");
    public string GlobalOpacityTitle => T("settings.dockCustomization.opacity.title");
    public string GlobalOpacityHelper => T("settings.dockCustomization.opacity.helper");
    public string WidgetOpacityTitle => T("settings.widgets.opacity.title");
    public string WidgetLayerTitle => T("settings.widgets.layer.title");
    public string WidgetLayerFollowGlobal => T("settings.widgets.layer.followGlobal");
    public string WidgetLayerTop => T("settings.widgets.layer.top");
    public string WidgetLayerDesktop => T("settings.widgets.layer.desktop");
    public string WidgetAutoHideText => T("settings.widgets.autoHide");
    public string WidgetAutoHideHelper => T("settings.widgets.autoHide.helper");
    public string EdgeSnapMarginTitle => T("settings.general.edgeSnapMargin");
    private int _edgeSnapMargin = 8;
    public int EdgeSnapMargin { get => _edgeSnapMargin; set => SetProperty(ref _edgeSnapMargin, value); }
    public string EdgeSnapMarginLabel => string.Format(T("settings.general.edgeSnapMargin.value"), EdgeSnapMargin);
    public string WidgetOpacityFollowGlobal => T("settings.widgets.opacity.followGlobal");
    public string WidgetOpacityCustom => T("settings.widgets.opacity.custom");
    public string RoundingTitle => T("settings.dockCustomization.rounding.title");
    public string RoundingHelper => T("settings.dockCustomization.rounding.helper");
    public string DockPaddingTitle => T("settings.dockCustomization.padding.title");
    public string DockPaddingHelper => T("settings.dockCustomization.padding.helper");
    public string BgColorTitle => T("settings.dockCustomization.background.title");
    public string BgColorHelper => T("settings.dockCustomization.background.helper");
    public string PosModeTitle => T("settings.positioning.mode.title");
    public string PosModeHelper => T("settings.positioning.mode.helper");
    public string StaticText => T("settings.positioning.mode.static");
    public string DynamicText => T("settings.positioning.mode.dynamic");
    public string AlignmentTitle => T("settings.positioning.alignment.title");
    public string VerticalText => T("settings.positioning.alignment.vertical");
    public string HorizontalText => T("settings.positioning.alignment.horizontal");
    public string ScreenSpacingTitle => T("settings.positioning.spacing.title");
    public string TopSpacingLabel => T("settings.positioning.spacing.top");
    public string LeftSpacingLabel => T("settings.positioning.spacing.left");
    public string RightSpacingLabel => T("settings.positioning.spacing.right");
    public string BottomSpacingLabel => T("settings.positioning.spacing.down");
    public string DynamicPosTitle => T("settings.positioning.dynamic.title");
    public string DynamicPosHelper => T("settings.positioning.dynamic.helper");
    public string VersionText => string.Format(T("settings.general.version"), CurrentVersion.ToString(3));

    // --- #16 update check ---
    public static Version CurrentVersion
    {
        get
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly();
            var info = asm?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return (info != null ? UpdateChecker.ParseVersion(info) : null) ?? asm?.GetName().Version ?? new Version(0, 0, 0);
        }
    }
    public string CheckUpdatesText => T("settings.general.checkUpdates");
    public string DownloadUpdateText => T("settings.general.downloadUpdate");
    public string InstallUpdateText => T("settings.general.installUpdate");
    public string CheckUpdatesOnStartupText => T("settings.general.checkUpdatesOnStartup");

    private bool _checkUpdatesOnStartup = true;
    /// <summary>Look for a newer release shortly after launch, at most once a day.</summary>
    public bool CheckUpdatesOnStartup { get => _checkUpdatesOnStartup; set => SetProperty(ref _checkUpdatesOnStartup, value); }

    private bool _isCheckingUpdates;
    public bool IsCheckingUpdates { get => _isCheckingUpdates; set => SetProperty(ref _isCheckingUpdates, value); }
    private bool _updateAvailable;
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set { if (SetProperty(ref _updateAvailable, value)) OnPropertyChanged(nameof(CanInstallUpdate)); }
    }
    private string _updateStatusText = "";
    public string UpdateStatusText { get => _updateStatusText; set => SetProperty(ref _updateStatusText, value); }
    private string? _updateUrl;

    // --- In-app update install ---

    private UpdateChecker.Result? _updateResult;

    private bool _isInstallingUpdate;
    /// <summary>True while the MSI is downloading; hides the buttons and shows progress.</summary>
    public bool IsInstallingUpdate
    {
        get => _isInstallingUpdate;
        set { if (SetProperty(ref _isInstallingUpdate, value)) OnPropertyChanged(nameof(CanInstallUpdate)); }
    }

    private double _updateProgress;
    /// <summary>Download progress, 0..100, for the progress bar.</summary>
    public double UpdateProgress
    {
        get => _updateProgress;
        set { if (SetProperty(ref _updateProgress, value)) OnPropertyChanged(nameof(UpdateProgressText)); }
    }

    public string UpdateProgressText => $"{(int)UpdateProgress}%";

    /// <summary>
    /// The one-click install is offered only for installed copies: a portable
    /// build has no MSI to upgrade and cannot overwrite its own running exe,
    /// so those users get the download link instead.
    /// </summary>
    public bool CanInstallUpdate =>
        UpdateAvailable && !IsInstallingUpdate
        && !Infrastructure.Windows.Adapters.AppDataLocator.IsPortable
        && _updateResult?.DownloadUrl != null;

    public async Task CheckForUpdatesAsync()
    {
        IsCheckingUpdates = true;
        UpdateAvailable = false;
        UpdateStatusText = T("settings.general.updateChecking");
        try
        {
            var r = await UpdateChecker.CheckAsync(CurrentVersion);
            _updateResult = r;
            if (r == null) UpdateStatusText = T("settings.general.updateUnavailable");
            else if (r.UpdateAvailable)
            {
                _updateUrl = r.DownloadUrl ?? r.ReleaseUrl ?? UpdateChecker.ReleasesPage;
                UpdateAvailable = true;
                UpdateStatusText = string.Format(T("settings.general.updateFound"), r.Latest!.ToString(3));
            }
            else UpdateStatusText = T("settings.general.updateNone");
        }
        finally { IsCheckingUpdates = false; }
    }

    /// <summary>
    /// Downloads the release MSI, verifies it against the SHA-256 published in
    /// the release notes, and hands it to Windows Installer — then asks the app
    /// to exit, because msiexec cannot replace files this process holds open.
    ///
    /// Confirmation is deliberate: the app is unsigned, so silently fetching
    /// and running an executable is precisely the behaviour that earns an
    /// antivirus report.
    /// </summary>
    public async Task InstallUpdateAsync()
    {
        if (_updateResult?.DownloadUrl is not { } url) return;
        if (!ConfirmInstall()) return;

        IsInstallingUpdate = true;
        UpdateProgress = 0;
        UpdateStatusText = T("settings.general.updateDownloading");

        try
        {
            string assetName = System.IO.Path.GetFileName(new Uri(url).LocalPath);
            string? sha = _updateResult.FindSha256(assetName);

            var progress = new Progress<double>(p => UpdateProgress = p * 100);
            var result = await Infrastructure.Windows.Adapters.UpdateInstaller.DownloadAsync(
                url, sha, _updateResult.DownloadSize, progress);

            if (!result.Ok || result.FilePath is null)
            {
                UpdateStatusText = T(result.Error == "checksum"
                    ? "settings.general.updateChecksumFailed"
                    : "settings.general.updateDownloadFailed");
                return;
            }

            UpdateStatusText = T("settings.general.updateLaunching");
            if (!Infrastructure.Windows.Adapters.UpdateInstaller.LaunchInstaller(result.FilePath))
            {
                UpdateStatusText = T("settings.general.updateDownloadFailed");
                return;
            }

            // Give msiexec a moment to take hold, then quit so it can replace
            // the files. RequestShutdown also restores the taskbar.
            await Task.Delay(1200);
            App.RequestShutdown();
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }

    /// <summary>
    /// Asks before downloading and running the installer. Uses the WinForms
    /// MessageBox the dock already uses elsewhere — the Settings window is
    /// owned by a WS_EX_NOACTIVATE dock, so an Avalonia dialog cannot take
    /// focus reliably.
    /// </summary>
    private bool ConfirmInstall()
    {
        var version = _updateResult?.Latest?.ToString(3) ?? "";
        return System.Windows.Forms.MessageBox.Show(
            _appServices.LocalizationService.Text("dialog.updateInstall.message", version),
            T("dialog.updateInstall.title"),
            System.Windows.Forms.MessageBoxButtons.YesNo,
            System.Windows.Forms.MessageBoxIcon.Question,
            System.Windows.Forms.MessageBoxDefaultButton.Button1)
            == System.Windows.Forms.DialogResult.Yes;
    }

    public void OpenUpdateDownload()
    {
        string url = _updateUrl ?? UpdateChecker.ReleasesPage;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }
    public string RepoText => T("settings.general.repository");
    public string ContactText => T("settings.general.contact");
    public string OpenSourceText => T("settings.general.openSource");
    public string AcknowledgementsText => T("settings.general.acknowledgements");
    public string KofiText => T("settings.general.kofi");
    public const string KofiUrl = "https://ko-fi.com/mobreadmeo";

    public void OpenKofi()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(KofiUrl) { UseShellExecute = true }); }
        catch { }
    }
    public string StartWithWindowsText => T("settings.general.startWithWindows");
    public string ShowUnpinnedRunningAppsText => T("settings.general.showUnpinnedRunningApps");
    public string ArrangeVerticalText => T("settings.general.arrangeVertical");
    public string AlwaysOnTopText => T("settings.general.alwaysOnTop");
    public string HideTaskbarText => T("settings.general.hideTaskbar");
    public string ReserveScreenEdgeText => T("settings.general.reserveScreenEdge");
    public string HideInFullscreenText => T("settings.general.hideInFullscreen");
    public string AttentionBounceText => T("settings.general.attentionBounce");
    public string MirrorMonitorsText => T("settings.general.mirrorMonitors");
    public string AutoHideText => T("settings.general.autoHide");
    public string FolderStacksText => T("settings.general.folderStacks");
    public string EdgeSnappingText => T("settings.general.edgeSnapping");

    private bool _isAutoStartEnabled;
    public bool IsAutoStartEnabled
    {
        get => _isAutoStartEnabled;
        set => SetProperty(ref _isAutoStartEnabled, value);
    }

    private bool _showUnpinnedRunningApps = true;
    public bool ShowUnpinnedRunningApps
    {
        get => _showUnpinnedRunningApps;
        set => SetProperty(ref _showUnpinnedRunningApps, value);
    }

    private bool _edgeSnapping = true;
    public bool EdgeSnapping { get => _edgeSnapping; set => SetProperty(ref _edgeSnapping, value); }

    private bool _folderStacks = true;
    public bool FolderStacks { get => _folderStacks; set => SetProperty(ref _folderStacks, value); }

    private bool _autoHide;
    public bool AutoHide { get => _autoHide; set => SetProperty(ref _autoHide, value); }

    private bool _hideInFullscreen = true;
    public bool HideInFullscreen { get => _hideInFullscreen; set => SetProperty(ref _hideInFullscreen, value); }
    private bool _attentionBounce;
    public bool AttentionBounce { get => _attentionBounce; set => SetProperty(ref _attentionBounce, value); }
    private bool _mirrorMonitors;
    public bool MirrorMonitors { get => _mirrorMonitors; set => SetProperty(ref _mirrorMonitors, value); }

    private bool _hideTaskbar;
    public bool HideTaskbar { get => _hideTaskbar; set => SetProperty(ref _hideTaskbar, value); }

    private bool _reserveScreenEdge;
    public bool ReserveScreenEdge { get => _reserveScreenEdge; set => SetProperty(ref _reserveScreenEdge, value); }

    private bool _alwaysOnTop;
    public bool AlwaysOnTop { get => _alwaysOnTop; set => SetProperty(ref _alwaysOnTop, value); }

    private bool _isVerticalDock;
    public bool IsVerticalDock
    {
        get => _isVerticalDock;
        set => SetProperty(ref _isVerticalDock, value);
    }

    public SettingsViewModel(AppServices appServices, Action dockRefreshAction,
        Action<DockPositioningMode> positioningModeChangeAction)
    {
        _appServices = appServices;
        _dockRefreshAction = dockRefreshAction;
        _positioningModeChangeAction = positioningModeChangeAction;
        _applyLocalizedTexts = () => { RefreshAllProperties(); RefreshItemLabels(); };
    }

    public void Initialize()
    {
        _appServices.LocalizationService.AddListener(_applyLocalizedTexts);
        _localizationRegistered = true;

        var app = _appServices.AppearanceService;
        IconSize = app.GetIconsSize();
        IconSpacing = app.GetSpacingBetweenIcons();
        DockRows = app.GetDockRows();
        DockPadding = app.GetDockPadding();
        Transparency = app.GetDockTransparencyPercentage();
        GlobalOpacity = app.GetGlobalOpacityPercentage();
        BorderRounding = app.GetDockBorderRounding();
        DockColor = ParseRgbColor(app.GetDockColorRGB());
        TintIcons = app.GetTintIcons();
        TintColor = ParseRgbColor(app.GetTintColorRGB());

        var pos = _appServices.PositioningService;
        IsStaticMode = pos.GetPositioningMode() == DockPositioningMode.STATIC;
        VerticalAnchor = pos.GetVerticalAnchor();
        HorizontalAnchor = pos.GetHorizontalAnchor();
        TopSpacing = pos.GetTopSpacing();
        LeftSpacing = pos.GetLeftSpacing();
        RightSpacing = pos.GetRightSpacing();
        BottomSpacing = pos.GetBottomSpacing();

        RefreshItemLabels();
        SelectedLanguage = _appServices.LocalizationService.GetCurrentLanguage();
        IsAutoStartEnabled = Infrastructure.Windows.Adapters.AutoStartHelper.IsAutoStartEnabled();
        ShowUnpinnedRunningApps = app.GetShowUnpinnedRunningApps();
        IsVerticalDock = app.GetVerticalDock();
        AlwaysOnTop = app.GetAlwaysOnTop();
        HideTaskbar = app.GetHideTaskbar();
        ReserveScreenEdge = app.GetReserveScreenEdge();
        HideInFullscreen = app.GetHideInFullscreen();
        AttentionBounce = app.GetAttentionBounce();
        MirrorMonitors = _appServices.PositioningService.GetMirrorOnAllMonitors();
        AutoHide = app.GetAutoHide();
        FolderStacks = app.GetFolderStacks();
        EdgeSnapping = app.GetEdgeSnapping();
        EdgeSnapMargin = app.GetEdgeSnapMargin();
        MagnifyIcons = app.GetMagnifyIconsSetting();
        MagnifyScale = app.GetMagnifyScalePercentage();
        PreviewDelay = app.GetPreviewDelayMs();
        CheckUpdatesOnStartup = app.GetCheckUpdatesOnStartup();
        FollowSystemTheme = app.GetFollowSystemTheme();
        ReloadPresets();
        _isInitialized = true;
    }

    /// <summary>Routes property changes to the appropriate service method (after Initialize).</summary>
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_isInitialized) return;

        switch (e.PropertyName)
        {
            case nameof(IconSize): OnIconSizeChanged(); break;
            case nameof(IconSpacing): OnIconSpacingChanged(); break;
            case nameof(DockRows): _appServices.AppearanceService.SetDockRows(DockRows); _dockRefreshAction(); break;
            case nameof(DockPadding): _appServices.AppearanceService.SetDockPadding(DockPadding); _dockRefreshAction(); break;
            case nameof(Transparency): OnTransparencyChanged(); break;
            case nameof(GlobalOpacity): _appServices.AppearanceService.SetGlobalOpacityPercentage(GlobalOpacity); _dockRefreshAction(); break;
            case nameof(BorderRounding): OnBorderRoundingChanged(); break;
            // The brush notifications live in the property setters, so they
            // fire during Initialize() too.
            case nameof(DockColor): OnDockColorChanged(); break;
            case nameof(TintIcons): OnTintIconsChanged(); break;
            case nameof(TintColor): OnTintColorChanged(); break;
            case nameof(SelectedLanguage): OnLanguageChanged(SelectedLanguage); break;
            case nameof(IsAutoStartEnabled): OnAutoStartChanged(); break;
            case nameof(ShowUnpinnedRunningApps): OnShowUnpinnedRunningAppsChanged(); break;
            case nameof(IsVerticalDock): OnVerticalDockChanged(); break;
            case nameof(AlwaysOnTop): OnAlwaysOnTopChanged(); break;
            case nameof(HideTaskbar): OnHideTaskbarChanged(); break;
            case nameof(ReserveScreenEdge): _appServices.AppearanceService.SetReserveScreenEdge(ReserveScreenEdge); App.ApplyEdgeReservation(); break;
            case nameof(HideInFullscreen): _appServices.AppearanceService.SetHideInFullscreen(HideInFullscreen); break;
            case nameof(AttentionBounce): _appServices.AppearanceService.SetAttentionBounce(AttentionBounce); break;
            case nameof(MirrorMonitors): _appServices.PositioningService.SetMirrorOnAllMonitors(MirrorMonitors); App.SyncMirrorDocks(); break;
            case nameof(AutoHide): _appServices.AppearanceService.SetAutoHide(AutoHide); _dockRefreshAction(); App.ApplyEdgeReservation(); break;
            case nameof(FolderStacks): _appServices.AppearanceService.SetFolderStacks(FolderStacks); break;
            case nameof(EdgeSnapping): _appServices.AppearanceService.SetEdgeSnapping(EdgeSnapping); break;
            case nameof(EdgeSnapMargin): _appServices.AppearanceService.SetEdgeSnapMargin(EdgeSnapMargin); OnPropertyChanged(nameof(EdgeSnapMarginLabel)); break;
            case nameof(MagnifyIcons): _appServices.AppearanceService.SetMagnifyIcons(MagnifyIcons); _dockRefreshAction(); break;
            case nameof(MagnifyScale): _appServices.AppearanceService.SetMagnifyScalePercentage(MagnifyScale); _dockRefreshAction(); break;
            case nameof(PreviewDelay): _appServices.AppearanceService.SetPreviewDelayMs(PreviewDelay); _dockRefreshAction(); break;
            case nameof(CheckUpdatesOnStartup): _appServices.AppearanceService.SetCheckUpdatesOnStartup(CheckUpdatesOnStartup); break;
            case nameof(FollowSystemTheme): OnFollowSystemThemeChanged(); break;
            case nameof(IsStaticMode): OnPositioningModeChanged(); break;
            case nameof(VerticalAnchor): OnVerticalAnchorChanged(); break;
            case nameof(HorizontalAnchor): OnHorizontalAnchorChanged(); break;
            case nameof(TopSpacing): OnTopSpacingChanged(); break;
            case nameof(LeftSpacing): OnLeftSpacingChanged(); break;
            case nameof(RightSpacing): OnRightSpacingChanged(); break;
            case nameof(BottomSpacing): OnBottomSpacingChanged(); break;
        }
    }

    private string T(string key) => _appServices.LocalizationService.Text(key);

    private static Color ParseRgbColor(string rgb)
    {
        var parts = rgb.Trim().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        byte r = parts.Length > 0 && byte.TryParse(parts[0], out var rv) ? rv : (byte)0;
        byte g = parts.Length > 1 && byte.TryParse(parts[1], out var gv) ? gv : (byte)0;
        byte b = parts.Length > 2 && byte.TryParse(parts[2], out var bv) ? bv : (byte)0;
        return Color.FromRgb(r, g, b);
    }

    private static string ColorToRgb(Color c) => $"{c.R}, {c.G}, {c.B}, ";
    // --- commands below ---

    public void OnIconSizeChanged() { _appServices.AppearanceService.SetIconsSize(IconSize); _dockRefreshAction(); }
    public void OnIconSpacingChanged() { _appServices.AppearanceService.SetSpacingBetweenIcons(IconSpacing); _dockRefreshAction(); }
    public void OnTransparencyChanged() { _appServices.AppearanceService.SetDockTransparencyPercentage(Transparency); _dockRefreshAction(); }
    public void OnBorderRoundingChanged() { _appServices.AppearanceService.SetDockBorderRounding(BorderRounding); _dockRefreshAction(); }
    public void OnDockColorChanged() { _appServices.AppearanceService.SetDockColorRGB(ColorToRgb(DockColor)); _dockRefreshAction(); }
    public void OnTintIconsChanged() { _appServices.AppearanceService.SetTintIcons(TintIcons); _dockRefreshAction(); }
    public void OnTintColorChanged() { _appServices.AppearanceService.SetTintColorRGB(ColorToRgb(TintColor)); _dockRefreshAction(); }
    public void OnLanguageChanged(SupportedLanguage lang) { _appServices.LocalizationService.SetLanguage(lang); _dockRefreshAction(); }

    public void OnAutoStartChanged()
    {
        if (IsAutoStartEnabled)
            Infrastructure.Windows.Adapters.AutoStartHelper.EnableAutoStart();
        else
            Infrastructure.Windows.Adapters.AutoStartHelper.DisableAutoStart();
    }

    public void OnShowUnpinnedRunningAppsChanged()
    {
        _appServices.AppearanceService.SetShowUnpinnedRunningApps(ShowUnpinnedRunningApps);
        _dockRefreshAction();
    }

    public void OnHideTaskbarChanged()
    {
        _appServices.AppearanceService.SetHideTaskbar(HideTaskbar);
        App.ApplyTaskbarVisibility();
    }

    public void OnAlwaysOnTopChanged()
    {
        _appServices.AppearanceService.SetAlwaysOnTop(AlwaysOnTop);
        _dockRefreshAction();
    }

    /// <summary>
    /// Turning "follow system theme" on immediately adopts the current Windows
    /// theme colour, so the effect is visible without waiting for a switch.
    /// </summary>
    public void OnFollowSystemThemeChanged()
    {
        _appServices.AppearanceService.SetFollowSystemTheme(FollowSystemTheme);
        if (!FollowSystemTheme) return;
        App.ApplySystemTheme(Infrastructure.Windows.Native.SystemThemeWatcher.IsLightTheme());
        // Reflect the colour the dock just adopted in the picker.
        _isInitialized = false;
        DockColor = ParseRgbColor(_appServices.AppearanceService.GetDockColorRGB());
        _isInitialized = true;
    }

    // --- Config export / import ---

    public async Task ExportConfigAsync(Window window)
    {
        var file = await window.StorageProvider.SaveFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = T("settings.general.config.export"),
                SuggestedFileName = Infrastructure.Windows.Persistence.ConfigTransfer.SuggestedFileName,
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                }
            });
        if (file == null) return;
        bool ok = Infrastructure.Windows.Persistence.ConfigTransfer.Export(
            _appServices.DockService, file.Path.LocalPath);
        ConfigStatusText = T(ok ? "settings.general.config.exported" : "settings.general.config.failed");
    }

    public async Task ImportConfigAsync(Window window)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(
            new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = T("settings.general.config.import"),
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
                }
            });
        if (files.Count == 0) return;

        bool ok = Infrastructure.Windows.Persistence.ConfigTransfer.Import(
            _appServices.DockService, files[0].Path.LocalPath);
        ConfigStatusText = T(ok ? "settings.general.config.imported" : "settings.general.config.invalid");
        if (!ok) return;

        // Everything may have changed — re-read the whole window from the model
        // without the change handlers writing each value back one by one.
        _isInitialized = false;
        Initialize();
        RefreshAllProperties();
        App.SyncMirrorDocks();
        App.ApplyTaskbarVisibility();
        _dockRefreshAction();
    }

    public void OnVerticalDockChanged()
    {
        _appServices.AppearanceService.SetVerticalDock(IsVerticalDock);
        _dockRefreshAction();
    }
    public void OnPositioningModeChanged()
    {
        var mode = IsStaticMode ? DockPositioningMode.STATIC : DockPositioningMode.DYNAMIC;
        _positioningModeChangeAction(mode);
        _dockRefreshAction();
    }
    public void OnVerticalAnchorChanged() { _appServices.PositioningService.SetVerticalAnchor(VerticalAnchor); _dockRefreshAction(); }
    public void OnHorizontalAnchorChanged() { _appServices.PositioningService.SetHorizontalAnchor(HorizontalAnchor); _dockRefreshAction(); }
    public void OnTopSpacingChanged() { _appServices.PositioningService.SetTopSpacing(TopSpacing); _dockRefreshAction(); }
    public void OnLeftSpacingChanged() { _appServices.PositioningService.SetLeftSpacing(LeftSpacing); _dockRefreshAction(); }
    public void OnRightSpacingChanged() { _appServices.PositioningService.SetRightSpacing(RightSpacing); _dockRefreshAction(); }
    public void OnBottomSpacingChanged() { _appServices.PositioningService.SetBottomSpacing(BottomSpacing); _dockRefreshAction(); }

    public async Task AddProgramAsync(Window window)
    {
        var files = await window.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = T("dialog.fileChooser.executableTitle"),
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType(T("dialog.fileChooser.executableFilter")) { Patterns = new[] { "*.exe", "*.lnk" } },
                new Avalonia.Platform.Storage.FilePickerFileType("Executable (*.exe)") { Patterns = new[] { "*.exe" } },
                new Avalonia.Platform.Storage.FilePickerFileType("Shortcut (*.lnk)") { Patterns = new[] { "*.lnk" } },
            }
        });
        if (files.Count == 0) return;
        bool added = false;
        foreach (var file in files)
        {
            var item = BuildProgramItem(file.Path.LocalPath);
            if (item == null) continue;
            _appServices.IconGateway.CacheProgramIcon(item.ExecutablePath);
            _appServices.DockService.AddItem(item);
            added = true;
        }
        if (!added) return;
        RefreshItemLabels();
        _dockRefreshAction();
    }

    /// <summary>
    /// Turns a picked file into a program item. A .lnk shortcut contributes
    /// its target, arguments and display name; a bare .exe goes through the
    /// Squirrel-aware resolver as before.
    /// </summary>
    private static DockProgramItemModel? BuildProgramItem(string path)
        => Infrastructure.Windows.Native.ProgramItemFactory.Create(path);

    /// <summary>
    /// #14 Import the taskbar's pinned shortcuts (see
    /// <see cref="Infrastructure.Windows.Native.TaskbarPinImporter"/>).
    /// Shortcuts that resolve to an exe already on the dock are skipped.
    /// Returns the number of items added.
    /// </summary>
    public int ImportFromTaskbar()
    {
        var existing = new HashSet<string>(
            _appServices.DockService.GetItems().OfType<DockProgramItemModel>().Select(p => p.ExecutablePath),
            StringComparer.OrdinalIgnoreCase);

        int added = 0;
        foreach (var item in Infrastructure.Windows.Native.TaskbarPinImporter.Enumerate())
        {
            if (!existing.Add(item.ExecutablePath)) continue;
            _appServices.IconGateway.CacheProgramIcon(item.ExecutablePath);
            _appServices.DockService.AddItem(item);
            added++;
        }
        if (added > 0)
        {
            RefreshItemLabels();
            _dockRefreshAction();
        }
        return added;
    }

    public async Task AddFolderAsync(Window window)
    {
        var folders = await window.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions
        {
            Title = T("dialog.directoryChooser.title")
        });
        if (folders.Count == 0) return;
        var path = folders[0].Path.LocalPath;
        // GetFileName returns "" for paths ending in a separator (e.g. root
        // drives); fall back to the raw path so the list never shows a blank row.
        var label = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrWhiteSpace(label))
            label = path;
        _appServices.IconGateway.CacheFolderIcon(path);
        _appServices.DockService.AddItem(new DockFolderItemModel(label, path));
        RefreshItemLabels();
        _dockRefreshAction();
    }

    public void RemoveSelected()
    {
        if (SelectedItemIndex < 0) return;
        _appServices.DockService.RemoveItem(SelectedItemIndex);
        SelectedItemIndex = -1;
        RefreshItemLabels();
        _dockRefreshAction();
    }

    public void MoveItemUp()
    {
        if (SelectedItemIndex <= 0) return;
        _appServices.DockService.SwapItems(SelectedItemIndex, SelectedItemIndex - 1);
        int newIndex = SelectedItemIndex - 1;
        RefreshItemLabels();
        // Apply the selection after the rebuild: setting it before
        // RefreshItemLabels would be wiped by the collection Clear.
        SelectedItemIndex = newIndex;
        _dockRefreshAction();
    }

    public void MoveItemDown()
    {
        if (SelectedItemIndex < 0 || SelectedItemIndex >= ItemEntries.Count - 1) return;
        _appServices.DockService.SwapItems(SelectedItemIndex, SelectedItemIndex + 1);
        int newIndex = SelectedItemIndex + 1;
        RefreshItemLabels();
        SelectedItemIndex = newIndex;
        _dockRefreshAction();
    }

    /// <summary>
    /// Moves the item at <paramref name="fromIndex"/> into the gap at
    /// <paramref name="toIndex"/> (drag-reorder). The target is clamped to the
    /// list bounds and the selection follows the moved item.
    /// </summary>
    public void MoveItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= ItemEntries.Count) return;
        toIndex = Math.Clamp(toIndex, 0, ItemEntries.Count);
        if (fromIndex == toIndex) return;
        _appServices.DockService.MoveItem(fromIndex, toIndex);
        // DockModel.MoveItem leaves the item at index toIndex - 1 when moving
        // down (the gap shifts), so mirror that here for the selection.
        int newIndex = toIndex > fromIndex ? toIndex - 1 : toIndex;
        RefreshItemLabels();
        SelectedItemIndex = newIndex;
        _dockRefreshAction();
    }

    public void RefreshItemLabels()
    {
        ItemEntries.Clear();
        foreach (var item in _appServices.DockService.GetItems())
        {
            string label = _appServices.LocalizationService.DockItemLabel(item);
            ItemEntries.Add(new DockItemListEntry(label, ResolveItemIcon(item)));
        }
        UpdateButtonStates();
    }

    private Bitmap? ResolveItemIcon(DockItem item)
    {
        // A user-chosen icon wins; a missing file falls through to the default.
        var custom = IconLoader.LoadCustomIcon(item.CustomIcon);
        if (custom != null) return custom;

        if (item is DockSettingsItemModel)
        {
            var icon = IconLoader.LoadFromAsset(IconLoader.MapResourcePath(item.Path));
            return icon ?? IconLoader.LoadFromAsset("Assets/icons/folder.png");
        }

        if (item is DockWindowsModuleItemModel moduleItem)
        {
            return IconLoader.LoadWindowsModuleIcon(moduleItem.Module)
                ?? IconLoader.LoadFromAsset("Assets/icons/folder.png");
        }

        if (item is DockProgramItemModel programItem)
        {
            return IconLoader.LoadFromFile(_appServices.IconGateway.ResolveProgramIcon(programItem.ExecutablePath));
        }

        if (item is DockFolderItemModel folderItem)
        {
            return IconLoader.LoadFromFile(_appServices.IconGateway.ResolveFolderIcon(folderItem.FolderPath))
                ?? IconLoader.LoadFromAsset("Assets/icons/folder.png");
        }

        return null;
    }

    private void UpdateButtonStates()
    {
        var items = _appServices.DockService.GetItems();
        CanRemove = SelectedItemIndex >= 0 && SelectedItemIndex < items.Count
            && items[SelectedItemIndex] is not DockSettingsItemModel;
        CanMoveUp = SelectedItemIndex > 0;
        CanMoveDown = SelectedItemIndex >= 0 && SelectedItemIndex < ItemEntries.Count - 1;
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    private void RefreshAllProperties()
    {
        // Force all computed properties to re-notify (localized texts).
        foreach (var prop in GetType().GetProperties()
            .Where(p => p.PropertyType == typeof(string) && p.GetMethod != null && p.GetIndexParameters().Length == 0))
            OnPropertyChanged(prop.Name);
        OnPropertyChanged(nameof(Languages));
        OnPropertyChanged(nameof(VerticalAnchorOptions));
        OnPropertyChanged(nameof(HorizontalAnchorOptions));
        OnPropertyChanged(nameof(SelectedVerticalOption));
        OnPropertyChanged(nameof(SelectedHorizontalOption));
    }

    public void Shutdown()
    {
        if (_localizationRegistered)
        {
            _appServices.LocalizationService.RemoveListener(_applyLocalizedTexts);
            _localizationRegistered = false;
        }
    }
}
