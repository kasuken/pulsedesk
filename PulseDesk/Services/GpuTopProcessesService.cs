using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PulseDesk.Services;

internal readonly record struct ProcessGpuSample(int Pid, string Name, double GpuPercent);

internal sealed class GpuTopProcessesService : IDisposable
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName = "Utilization Percentage";
    private const string EngineFilter = "engtype_3D";

    private readonly List<(PerformanceCounter Counter, int Pid)> _counters = new();
    private readonly Dictionary<int, string> _processNames = new();

    public bool IsAvailable { get; }

    public GpuTopProcessesService()
    {
        try
        {
            var category = new PerformanceCounterCategory(CategoryName);
            var instances = category.GetInstanceNames()
                .Where(name => name.Contains(EngineFilter, StringComparison.Ordinal))
                .ToList();

            foreach (var instance in instances)
            {
                if (!TryExtractPid(instance, out var pid) || pid <= 0)
                {
                    continue;
                }

                var counter = new PerformanceCounter(CategoryName, CounterName, instance);
                _counters.Add((counter, pid));
                _ = counter.NextValue();
            }

            IsAvailable = _counters.Count > 0;
        }
        catch
        {
            Dispose();
            IsAvailable = false;
        }
    }

    public IReadOnlyList<ProcessGpuSample> Read(int top)
    {
        if (!IsAvailable || _counters.Count == 0)
        {
            return [];
        }

        try
        {
            var byPid = new Dictionary<int, double>();
            foreach (var (counter, pid) in _counters)
            {
                var value = Math.Max(0, counter.NextValue());
                if (!byPid.TryAdd(pid, value))
                {
                    byPid[pid] += value;
                }
            }

            return byPid
                .Where(pair => pair.Value > 0)
                .OrderByDescending(pair => pair.Value)
                .Take(top)
                .Select(pair => new ProcessGpuSample(
                    pair.Key,
                    ResolveProcessName(pair.Key),
                    Math.Min(100, pair.Value)))
                .ToList();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"GpuTopProcessesService.Read failed: {ex.Message}");
            return [];
        }
    }

    private string ResolveProcessName(int pid)
    {
        if (_processNames.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            var name = process.ProcessName;
            _processNames[pid] = name;
            return name;
        }
        catch
        {
            return $"pid {pid}";
        }
    }

    private static bool TryExtractPid(string instanceName, out int pid)
    {
        const string token = "pid_";
        var start = instanceName.IndexOf(token, StringComparison.Ordinal);
        if (start < 0)
        {
            pid = 0;
            return false;
        }

        start += token.Length;
        var end = start;
        while (end < instanceName.Length && char.IsDigit(instanceName[end]))
        {
            end++;
        }

        if (end == start)
        {
            pid = 0;
            return false;
        }

        return int.TryParse(instanceName[start..end], out pid);
    }

    public void Dispose()
    {
        foreach (var (counter, _) in _counters)
        {
            counter.Close();
        }
        _counters.Clear();
    }
}
