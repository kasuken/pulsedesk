using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PulseDesk.Services;

internal readonly record struct ProcessCpuSample(int Pid, string Name, double CpuPercent);

internal readonly record struct ProcessMemorySample(int Pid, string Name, long WorkingSetBytes);

internal readonly record struct TopProcessesSnapshot(
    IReadOnlyList<ProcessCpuSample> ByCpu,
    IReadOnlyList<ProcessMemorySample> ByMemory);

internal sealed class TopProcessesService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _previous = new();
    private readonly int _logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);

    public TopProcessesSnapshot Read(int top)
    {
        var now = DateTime.UtcNow;
        var cpuRows = new List<ProcessCpuSample>(64);
        var memRows = new List<ProcessMemorySample>(64);
        var seen = new HashSet<int>();

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var pid = process.Id;
                if (pid == 0) continue; // Idle process is not meaningful here.

                seen.Add(pid);

                long workingSet = 0;
                try { workingSet = process.WorkingSet64; }
                catch { workingSet = 0; }
                if (workingSet > 0)
                {
                    memRows.Add(new ProcessMemorySample(pid, process.ProcessName, workingSet));
                }

                var cpu = process.TotalProcessorTime;
                if (_previous.TryGetValue(pid, out var prev))
                {
                    var elapsedMs = (now - prev.At).TotalMilliseconds;
                    if (elapsedMs > 0)
                    {
                        var cpuMs = (cpu - prev.Cpu).TotalMilliseconds;
                        var percent = Math.Max(0, cpuMs / (elapsedMs * _logicalProcessorCount) * 100.0);
                        cpuRows.Add(new ProcessCpuSample(pid, process.ProcessName, percent));
                    }
                }
                _previous[pid] = (cpu, now);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Process exited or access denied (e.g. protected system process). Skip.
            }
            finally
            {
                process.Dispose();
            }
        }

        if (_previous.Count > seen.Count)
        {
            var stale = _previous.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var pid in stale) _previous.Remove(pid);
        }

        return new TopProcessesSnapshot(
            cpuRows.OrderByDescending(p => p.CpuPercent).Take(top).ToList(),
            memRows.OrderByDescending(p => p.WorkingSetBytes).Take(top).ToList());
    }
}
