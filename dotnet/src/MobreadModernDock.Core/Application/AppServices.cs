namespace MobreadModernDock.Core.Application;

using MobreadModernDock.Core.Domain;

/// <summary>
/// Composition root record bundling all application services and adapters.
/// App.axaml.cs constructs this with concrete infrastructure adapters and
/// injects it into the UI.
/// </summary>
public sealed record AppServices(
    DockService DockService,
    DockAppearanceService AppearanceService,
    DockPositioningService PositioningService,
    DockItemActionService ItemActionService,
    WindowPreviewService WindowPreviewService,
    IIconGateway IconGateway,
    LocalizationService LocalizationService,
    WidgetService WidgetService,
    ITrayIconGateway TrayIconGateway,
    ISystemStatsGateway SystemStatsGateway,
    IMediaSessionGateway MediaSessionGateway,
    IWeatherGateway WeatherGateway,
    IJumpListGateway JumpListGateway,
    IBatteryGateway BatteryGateway,
    IVirtualDesktopGateway VirtualDesktopGateway
);
