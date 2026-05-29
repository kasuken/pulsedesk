using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PulseDesk.Services;

internal readonly record struct TemperatureSample(float AverageCelsius, float MaximumCelsius);

internal sealed class TemperatureService : IDisposable
{
    private const string CategoryName = "Thermal Zone Information";
    private const string CounterName = "Temperature";
    private const float KelvinOffset = 273.15f;
    private const float MinValidCelsius = -50.0f;
    private const float MaxValidCelsius = 150.0f;

    private readonly List<PerformanceCounter> _counters = new();

    public bool IsAvailable { get; }

    public TemperatureService()
    {
        try
        {
            var category = new PerformanceCounterCategory(CategoryName);
            var instances = category.GetInstanceNames();
            if (instances.Length == 0)
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

    public TemperatureSample? Read()
    {
        if (!IsAvailable || _counters.Count == 0)
        {
            return null;
        }

        try
        {
            var values = new List<float>(_counters.Count);
            foreach (var counter in _counters)
            {
                var kelvin = counter.NextValue();
                if (kelvin <= 0) continue;
                var celsius = kelvin - KelvinOffset;
                if (celsius is < MinValidCelsius or > MaxValidCelsius) continue;
                values.Add(celsius);
            }

            if (values.Count == 0) return null;

            return new TemperatureSample(values.Average(), values.Max());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"TemperatureService.Read failed: {ex.Message}");
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
