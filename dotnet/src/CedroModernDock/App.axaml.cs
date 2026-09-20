using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;
using CedroModernDock.Core.Application;
using CedroModernDock.Core.Models;
using CedroModernDock.Infrastructure.Windows.Adapters;
using CedroModernDock.Infrastructure.Windows.Native;
using CedroModernDock.Infrastructure.Windows.Persistence;
using CedroModernDock.ViewModels;
using CedroModernDock.Views;
using CedroModernDock.Widgets;

namespace CedroModernDock;

public partial class App : Application
{
    private static AppServices? _appServices;
    private static MainWindow? _mainWindow;
    private static MainWindowViewModel? _mainViewModel;
    private static IClassicDesktopStyleApplicationLifetime? _desktop;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            DisableAvaloniaDataAnnotationValidation();

            // Keep the app alive via tray icon — the window is never closed, only hidden.
            _desktop = desktop;

            // Composition root — wire concrete Windows adapters into the application services.
            var appServices = CreateServices();
            _appServices = appServices;

            // Wire tray icon events (Avalonia TrayIcon events can't be set via XAML string attributes).
            WireTrayIcon(appServices.LocalizationService);

            var viewModel = new MainWindowViewModel(appServices);
            _mainViewModel = viewModel;

            var mainWindow = new MainWindow { DataContext = viewModel };
            mainWindow.SetAppServices(appServices);
            _mainWindow = mainWindow;
            desktop.MainWindow = mainWindow;

            // Widget: show at startup if enabled, and follow the toggle.
            appServices.WidgetService.AddListener(SyncWidgetWindows);
            mainWindow.Opened += (_, _) =>
            {
                SyncWidgetWindows();
                ApplyTaskbarVisibility();
                StartFullscreenWatcher();
            };
            desktop.ShutdownRequested += (_, _) => TaskbarVisibility.Restore();
            desktop.Exit += (_, _) => TaskbarVisibility.Restore();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MemoryStream? _trayIconStream;

    private void WireTrayIcon(LocalizationService loc)
    {
        // Resolve the embedded Avalonia asset via AssetLoader.Open() with an
        // avares:// URI (same pattern as IconLoader.LoadFromAsset).  WindowIcon(string)
        // treats the argument as a file path on disk, which fails at runtime.
        //
        // Note: WindowIcon(Stream) may hold the stream until the icon is used,
        // so the stream must stay alive for the lifetime of the tray icon.
        _trayIconStream = new MemoryStream();
        using (var source = AssetLoader.Open(
            new Uri("avares://CedroModernDock/Assets/icons/cedro/logo_32.png")))
        {
            source.CopyTo(_trayIconStream);
        }
        _trayIconStream.Position = 0;

        var trayIcon = new TrayIcon
        {
            Icon = new WindowIcon(_trayIconStream),
            ToolTipText = "Cedro Modern Dock",
            Menu = new NativeMenu()
        };

        var settingsItem = new NativeMenuItem { Header = loc.Text("tray.openSettings") };
        var exitItem = new NativeMenuItem { Header = loc.Text("tray.exit") };

        // In Avalonia 11.3, NativeMenuItem uses Command property for click handling.
        settingsItem.Command = new ViewModels.RelayCommand(_ => OpenSettingsFromTray());
        exitItem.Command = new ViewModels.RelayCommand(_ => OnTrayExit());

        trayIcon.Menu.Items.Add(settingsItem);
        trayIcon.Menu.Items.Add(new NativeMenuItemSeparator());
        trayIcon.Menu.Items.Add(exitItem);

        // Also handle a direct click on the tray icon.
        trayIcon.Clicked += (_, _) => OpenSettingsFromTray();

        // Register the tray icon with the application.
        var icons = new TrayIcons();
        icons.Add(trayIcon);
        TrayIcon.SetIcons(this, icons);
    }

    // --- Tray icon event handlers ---

    private static void OnTrayIconClick()
    {
        // Left-click on tray icon → open settings (same as Java SystemTrayManager).
        OpenSettingsFromTray();
    }

    private static void OnTrayOpenSettings()
    {
        OpenSettingsFromTray();
    }

    private static void OnTrayExit()
    {
        RequestShutdown();
    }

    private static bool _shuttingDown;

    /// <summary>
    /// Single exit path: restores the taskbar, stops the dock VM, closes the
    /// widget windows and ends the Avalonia lifetime. Idempotent so the tray
    /// Exit and the dock window's Closed can both call it.
    /// </summary>
    public static void RequestShutdown()
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        try { TaskbarVisibility.Restore(); } catch { }
        _mainViewModel?.Shutdown();
        foreach (var window in _widgetWindows.Values.ToList())
        {
            try { window.Close(); } catch { }
        }
        _widgetWindows.Clear();
        _desktop?.Shutdown();
    }

    private static void OpenSettingsFromTray()
    {
        if (_appServices == null || _mainWindow == null || _mainViewModel == null) return;
        _mainWindow.Show();
        SettingsWindow.Open(
            _appServices,
            _mainWindow,
            _mainViewModel.UpdateDockUI,
            mode => HandlePositioningModeChange(mode)
        );
    }

    private static void HandlePositioningModeChange(DockPositioningMode mode)
    {
        if (_appServices == null || _mainWindow == null) return;
        var currentMode = _appServices.PositioningService.GetPositioningMode();
        if (currentMode == DockPositioningMode.STATIC && mode == DockPositioningMode.DYNAMIC)
        {
            var (x, y) = _mainWindow.CurrentScreenPosition;
            _appServices.DockService.SetDockPosition(x, y);
        }
        _appServices.PositioningService.SetPositioningMode(mode);
    }

    /// <summary>
    /// Composes all application services with their Windows-specific adapters.
    /// Direct port of App.java createServices().
    /// </summary>
    private static AppServices CreateServices()
    {
        var repository = new JsonDockRepository();
        var dockService = new DockService(repository);

        // First run (no config yet, so a default was created): register the app
        // for auto-start so "Start with Windows" is enabled by default. The
        // Settings checkbox reads the registry, so this makes it checked.
        if (repository.WasDefaultCreated)
            AutoStartHelper.EnableAutoStart();

        var screenBoundsProvider = new WindowsScreenBoundsProvider();

        return new AppServices(
            DockService: dockService,
            AppearanceService: new DockAppearanceService(dockService),
            PositioningService: new DockPositioningService(dockService, screenBoundsProvider),
            ItemActionService: new DockItemActionService(
                new WindowsProgramLauncher(),
                new WindowsFolderLauncher(),
                new WindowsModuleLauncher()
            ),
            WindowPreviewService: new WindowPreviewService(new Win32WindowQueryGateway()),
            IconGateway: new CachedWindowsIconGateway(),
            LocalizationService: new LocalizationService(dockService),
            WidgetService: new WidgetService(dockService),
            TrayIconGateway: new UiaTrayIconGateway(),
            SystemStatsGateway: new PerformanceCounterStatsGateway()
        );
    }

    // --- Floating widget lifecycle ---

    private static readonly WidgetRegistry _widgetRegistry = WidgetRegistry.CreateDefault();
    private static readonly Dictionary<string, WidgetWindow> _widgetWindows = new();

    public static WidgetRegistry WidgetRegistry => _widgetRegistry;

    /// <summary>
    /// Reconciles open widget windows with the persisted definitions: opens a
    /// window for each enabled definition that has none, closes windows whose
    /// definition was disabled or removed, and refreshes the rest. Called at
    /// startup and on every WidgetService change.
    /// </summary>
    private static void SyncWidgetWindows()
    {
        if (_appServices == null) return;
        var definitions = _appServices.WidgetService.GetWidgets();
        var wanted = new HashSet<string>();

        foreach (var def in definitions)
        {
            if (!def.Enabled) continue;
            var provider = _widgetRegistry.Get(def.Type);
            if (provider == null) continue;
            wanted.Add(def.Id);

            if (_widgetWindows.TryGetValue(def.Id, out var existing))
            {
                existing.Refresh();
                continue;
            }

            var window = new WidgetWindow();
            window.Initialize(_appServices, def, provider.CreateView(def, _appServices));
            string id = def.Id;
            window.Closed += (_, _) =>
            {
                if (_widgetWindows.TryGetValue(id, out var w) && w == window)
                    _widgetWindows.Remove(id);
            };
            _widgetWindows[def.Id] = window;
            window.Show();
            if (_hiddenForFullscreen)
                window.Opened += (_, _) => window.SetNativeVisible(false);
        }

        foreach (var id in _widgetWindows.Keys.ToList())
        {
            if (wanted.Contains(id)) continue;
            var window = _widgetWindows[id];
            _widgetWindows.Remove(id);
            window.Close();
        }
    }

    /// <summary>Dock appearance changed: widgets mirror dock color/rounding.</summary>
    public static void RefreshWidgetAppearance()
    {
        foreach (var window in _widgetWindows.Values)
            window.Refresh();
    }

    /// <summary>
    /// Hides or restores the Windows taskbar to match the setting. At startup
    /// this also repairs a taskbar left hidden by a previous unclean exit when
    /// the setting is off.
    /// </summary>
    public static void ApplyTaskbarVisibility()
    {
        if (_appServices == null) return;
        bool hide = _appServices.AppearanceService.GetHideTaskbar();
        if (hide)
            TaskbarVisibility.Hide();
        else
            TaskbarVisibility.RestoreIfLeftHidden();
    }

    // --- Fullscreen auto-hide ---

    private static System.Threading.Timer? _fullscreenPoll;
    private static bool _hiddenForFullscreen;

    /// <summary>
    /// Polls the shell for a fullscreen foreground app and hides/shows the
    /// dock and every widget window accordingly. Polling is cheap (two Win32
    /// calls) and avoids the fragility of WinEvent hooks; 500ms is fast enough
    /// that the dock is gone before a game finishes its first frame.
    /// </summary>
    private static void StartFullscreenWatcher()
    {
        _fullscreenPoll ??= new System.Threading.Timer(_ =>
        {
            if (_appServices == null || _shuttingDown) return;
            bool enabled = _appServices.AppearanceService.GetHideInFullscreen();
            bool shouldHide = enabled && FullscreenDetector.IsFullscreenAppActive();
            if (shouldHide == _hiddenForFullscreen) return;
            _hiddenForFullscreen = shouldHide;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => SetAllVisible(!shouldHide));
        }, null, 1000, 500);
    }

    private static void SetAllVisible(bool visible)
    {
        _mainWindow?.SetNativeVisible(visible);
        foreach (var w in _widgetWindows.Values)
            w.SetNativeVisible(visible);
    }

    /// <summary>Whether windows are currently hidden because a fullscreen app is active.</summary>
    public static bool IsHiddenForFullscreen => _hiddenForFullscreen;

    private void DisableAvaloniaDataAnnotationValidation()
    {
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}