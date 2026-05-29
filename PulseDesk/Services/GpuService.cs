using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PulseDesk.Services;

internal readonly record struct GpuSample(float AveragePercent, float MaximumPercent);

internal sealed class GpuService : IDisposable
{
    private const string CategoryName = "GPU Engine";
    private const string CounterName = "Utilization Percentage";
    private const string EngineFilter = "engtype_3D";
    private const int SmoothingWindow = 5;

    private readonly List<PerformanceCounter> _counters = new();
    private readonly List<GpuSample> _samples = new(SmoothingWindow);

    public bool IsAvailable { get; }

    public GpuService()
    {
        try
        {
            var category = new PerformanceCounterCategory(CategoryName);
            var instances = category.GetInstanceNames()
                .Where(n => n.Contains(EngineFilter, StringComparison.Ordinal))
                .ToList();

            if (instances.Count == 0)
            {
                IsAvailable = false;
                return;
            }

            foreach (var instance in instances)
            {
                var counter = new PerformanceCounter(CategoryName, CounterName, instance);
                _counters.Add(counter);
                _ = counter.NextValue();
            }
            IsAvailable = true;
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
            var values = _counters.Select(c => c.NextValue()).ToList();
            if (values.Count == 0) return null;

            var sample = new GpuSample(
                Math.Min(100, values.Average()),
                Math.Min(100, values.Max()));

            _samples.Add(sample);
            if (_samples.Count > SmoothingWindow)
            {
                _samples.RemoveAt(0);
            }

            return new GpuSample(
                _samples.Average(s => s.AveragePercent),
                _samples.Max(s => s.MaximumPercent));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"GpuService.Read failed: {ex.Message}");
            _samples.Clear();
            return null;
        }
    }

    private void DisposeCounters()
    {
        foreach (var counter in _counters)
        {
            counter.Close();
        }
        _counters.Clear();
    }

    public void Dispose() => DisposeCounters();
}
