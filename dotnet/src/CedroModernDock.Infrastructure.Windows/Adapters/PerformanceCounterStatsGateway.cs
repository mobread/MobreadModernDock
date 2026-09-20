namespace CedroModernDock.Infrastructure.Windows.Adapters;

using System.Diagnostics;
using System.Runtime.InteropServices;
using CedroModernDock.Core.Domain;

/// <summary>
/// System utilisation via Windows performance counters. Counters are created
/// lazily and individually guarded: a missing category (no GPU counters on a
/// VM, say) disables that metric rather than the whole sampler.
/// </summary>
public sealed class PerformanceCounterStatsGateway : ISystemStatsGateway, IDisposable
{
    private readonly object _sync = new();
    private PerformanceCounter? _cpu;
    private PerformanceCounter[]? _netRecv;
    private PerformanceCounter[]? _netSent;
    private PerformanceCounterCategory? _gpuCategory;
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private DateTime _gpuLastEnumerate = DateTime.MinValue;
    private bool _gpuUnavailable;
    private bool _initialized;

    public SystemStats Sample()
    {
        lock (_sync)
        {
            if (!_initialized) Initialize();

            double cpu = SafeNext(_cpu);
            var (used, total) = ReadMemory();
            double ramPct = total > 0 ? used / total * 100.0 : 0;
            double? gpu = ReadGpu();
            double down = SumNext(_netRecv), up = SumNext(_netSent);

            return new SystemStats(
                Math.Clamp(cpu, 0, 100),
                Math.Clamp(ramPct, 0, 100),
                used / 1024.0 / 1024.0 / 1024.0,
                total / 1024.0 / 1024.0 / 1024.0,
                gpu is null ? null : Math.Clamp(gpu.Value, 0, 100),
                Math.Max(0, down), Math.Max(0, up));
        }
    }

    private void Initialize()
    {
        _initialized = true;
        // "% Processor Utility" matches Task Manager on modern CPUs (accounts
        // for frequency scaling); fall back to "% Processor Time".
        _cpu = TryCreate("Processor Information", "% Processor Utility", "_Total")
            ?? TryCreate("Processor", "% Processor Time", "_Total");
        try
        {
            var cat = new PerformanceCounterCategory("Network Interface");
            var names = cat.GetInstanceNames()
                .Where(n => !n.Contains("Loopback", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("isatap", StringComparison.OrdinalIgnoreCase)
                         && !n.Contains("Teredo", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            _netRecv = names.Select(n => TryCreate("Network Interface", "Bytes Received/sec", n)).Where(c => c != null).ToArray()!;
            _netSent = names.Select(n => TryCreate("Network Interface", "Bytes Sent/sec", n)).Where(c => c != null).ToArray()!;
        }
        catch { _netRecv = _netSent = Array.Empty<PerformanceCounter>(); }

        // Prime rate counters so the first real sample is meaningful.
        SafeNext(_cpu); SumNext(_netRecv); SumNext(_netSent);
    }

    private double? ReadGpu()
    {
        if (_gpuUnavailable) return null;
        try
        {
            _gpuCategory ??= new PerformanceCounterCategory("GPU Engine");
            // Sum 3D-engine utilisation across all processes, like Task Manager's
            // headline GPU number. "Utilization Percentage" is a rate counter:
            // the first NextValue() on a fresh counter is always 0, so counters
            // must persist across samples. Instances come and go per process,
            // so re-enumerate periodically to pick up new ones and drop dead ones.
            var now = DateTime.UtcNow;
            if (now - _gpuLastEnumerate > TimeSpan.FromSeconds(5))
            {
                _gpuLastEnumerate = now;
                var live = new HashSet<string>(
                    _gpuCategory.GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)));
                foreach (var name in live)
                {
                    if (_gpuCounters.ContainsKey(name)) continue;
                    var c = TryCreate("GPU Engine", "Utilization Percentage", name);
                    if (c != null) _gpuCounters[name] = c;
                }
                foreach (var dead in _gpuCounters.Keys.Where(k => !live.Contains(k)).ToArray())
                {
                    _gpuCounters[dead].Dispose();
                    _gpuCounters.Remove(dead);
                }
            }

            double sum = 0;
            foreach (var (name, c) in _gpuCounters)
            {
                try { sum += c.NextValue(); }
                catch { _gpuCounters.Remove(name); c.Dispose(); break; } // process exited; re-enumerate next pass
            }
            return sum;
        }
        catch
        {
            _gpuUnavailable = true;
            return null;
        }
    }

    private static PerformanceCounter? TryCreate(string category, string counter, string instance)
    {
        try
        {
            var c = new PerformanceCounter(category, counter, instance, readOnly: true);
            c.NextValue();
            return c;
        }
        catch { return null; }
    }

    private static double SafeNext(PerformanceCounter? c)
    {
        try { return c?.NextValue() ?? 0; } catch { return 0; }
    }

    private static double SumNext(PerformanceCounter[]? cs)
    {
        if (cs == null) return 0;
        double sum = 0;
        foreach (var c in cs) sum += SafeNext(c);
        return sum;
    }

    private static (double UsedBytes, double TotalBytes) ReadMemory()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status)) return (0, 0);
        return (status.ullTotalPhys - status.ullAvailPhys, status.ullTotalPhys);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public void Dispose()
    {
        _cpu?.Dispose();
        foreach (var c in _netRecv ?? Array.Empty<PerformanceCounter>()) c.Dispose();
        foreach (var c in _netSent ?? Array.Empty<PerformanceCounter>()) c.Dispose();
        foreach (var c in _gpuCounters.Values) c.Dispose();
        _gpuCounters.Clear();
    }
}
