namespace MobreadModernDock.Core.Domain;

/// <summary>
/// One reading of the system battery. <see cref="TimeRemaining"/> is null
/// when Windows cannot estimate it (charging, or no battery).
/// </summary>
public sealed record BatteryStatus(
    bool HasBattery,
    int Percent,
    bool IsCharging,
    bool IsPluggedIn,
    TimeSpan? TimeRemaining);

/// <summary>Port for reading battery state.</summary>
public interface IBatteryGateway
{
    BatteryStatus Read();
}
