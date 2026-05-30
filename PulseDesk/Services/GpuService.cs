using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PulseDesk.Services;

internal readonly record struct GpuSample(float AveragePercent, float MaximumPercent);

internal readonly record struct ProcessGpuSample(int Pid, string Name, double GpuPercent);

internal sealed class GpuService : IDisposable
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName = "Utilization Percentage";
    private const string EngineFilter = "engtype_3D";
    private const int SmoothingWindow = 5;

    // Each 3D-engine instance is polled exactly once per Read(); the values feed both the
    // aggregate avg/max gauge and the per-process breakdown, so the GPU driver is only
    // queried a single time per tick instead of once per consumer.
    private readonly List<(PerformanceCounter Counter, int Pid)> _counters = new();
    private readonly List<GpuSample> _samples = new(SmoothingWindow);
    private readonly Dictionary<int, double> _lastByPid = new();
    private readonly Dictionary<int, string> _processNames = new();

    public bool IsAvailable { get; }

    public GpuService()
    {
        try
        {
            var category = new PerformanceCounterCategory(CategoryName);
            string[] instances;
            try
            {
                instances = category.GetInstanceNames();
            }
            catch
            {
                IsAvailable = false;
                return;
            }

            foreach (var instance in instances)
            {
                if (!instance.Contains(EngineFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                TryExtractPid(instance, out var pid);
                var counter = new PerformanceCounter(CategoryName, CounterName, instance);
                _counters.Add((counter, pid));
                _ = counter.NextValue();
            }

            IsAvailable = _counters.Count > 0;
            if (!IsAvailable)
            {
                DisposeCounters();
            }
        }
        catch
        {
            DisposeCounters();
            IsAvailable = false;
        }
    }

    public GpuSample? Read()
    {
        if (!IsAvailable || _counters.Count == 0)
        {
            return null;
        }

        try
        {
            _lastByPid.Clear();
            double sum = 0;
            float max = 0;
            foreach (var (counter, pid) in _counters)
            {
                var value = Math.Max(0, counter.NextValue());
                sum += value;
                if (value > max) max = value;

                if (pid > 0 && value > 0)
                {
                    if (!_lastByPid.TryAdd(pid, value))
                    {
                        _lastByPid[pid] += value;
                    }
                }
            }

            var sample = new GpuSample(
                Math.Min(100, (float)(sum / _counters.Count)),
                Math.Min(100, max));

            _samples.Add(sample);
            if (_samples.Count > SmoothingWindow)
            {
                _samples.RemoveAt(0);
            }

            float avgSum = 0;
            float maxSmoothed = 0;
            foreach (var s in _samples)
            {
                avgSum += s.AveragePercent;
                if (s.MaximumPercent > maxSmoothed) maxSmoothed = s.MaximumPercent;
            }

            return new GpuSample(avgSum / _samples.Count, maxSmoothed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"GpuService.Read failed: {ex.Message}");
            _samples.Clear();
            _lastByPid.Clear();
            return null;
        }
    }

    /// <summary>
    /// Returns the top GPU-consuming processes from the most recent <see cref="Read"/>.
    /// This does not query the driver again; it reuses the values already sampled this tick.
    /// </summary>
    public IReadOnlyList<ProcessGpuSample> GetTopProcesses(int top)
    {
        if (!IsAvailable || _lastByPid.Count == 0)
        {
            return [];
        }

        var rows = new List<ProcessGpuSample>(_lastByPid.Count);
        foreach (var (pid, value) in _lastByPid)
        {
            rows.Add(new ProcessGpuSample(pid, ResolveProcessName(pid), Math.Min(100, value)));
        }

        rows.Sort((a, b) => b.GpuPercent.CompareTo(a.GpuPercent));
        if (rows.Count > top)
        {
            rows.RemoveRange(top, rows.Count - top);
        }
        return rows;
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

    private void DisposeCounters()
    {
        foreach (var (counter, _) in _counters)
        {
            counter.Close();
        }
        _counters.Clear();
    }

    public void Dispose() => DisposeCounters();
}
