namespace MobreadModernDock.Core.Domain;

/// <summary>One sample of system load. Percentages are 0..100; rates are bytes/second.</summary>
public sealed record SystemStats(
    double CpuPercent,
    double RamPercent,
    double RamUsedGb,
    double RamTotalGb,
    double? GpuPercent,
    double NetDownBytesPerSec,
    double NetUpBytesPerSec);

/// <summary>Port for reading system utilisation.</summary>
public interface ISystemStatsGateway
{
    /// <summary>
    /// Takes a sample. The first call after construction may return zeros for
    /// rate-based counters (CPU, network) since they need two samples.
    /// </summary>
    SystemStats Sample();
}
