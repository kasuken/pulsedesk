using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PulseDesk.Services;

internal readonly record struct CpuSample(float TotalPercent, float UserPercent, float KernelPercent, float IdlePercent);

internal sealed class CpuService : IDisposable
{
    private const int SmoothingWindow = 5;

    private readonly PerformanceCounter? _total;
    private readonly PerformanceCounter? _user;
    private readonly PerformanceCounter? _kernel;
    private readonly PerformanceCounter? _idle;
    private readonly List<CpuSample> _samples = new(SmoothingWindow);

    public bool IsAvailable { get; }

    public CpuService()
    {
        try
        {
            _total = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _user = new PerformanceCounter("Processor", "% User Time", "_Total");
            _kernel = new PerformanceCounter("Processor", "% Privileged Time", "_Total");
            _idle = new PerformanceCounter("Processor", "% Idle Time", "_Total");
            _ = _total.NextValue();
            _ = _user.NextValue();
            _ = _kernel.NextValue();
            _ = _idle.NextValue();
            IsAvailable = true;
        }
        catch
        {
            Dispose();
            IsAvailable = false;
        }
    }

    public CpuSample? Read()
    {
        if (!IsAvailable || _total is null || _user is null || _kernel is null || _idle is null)
        {
            return null;
        }

        try
        {
            var sample = new CpuSample(
                Math.Min(100, _total.NextValue()),
                Math.Min(100, _user.NextValue()),
                Math.Min(100, _kernel.NextValue()),
                Math.Min(100, _idle.NextValue()));

            _samples.Add(sample);
            if (_samples.Count > SmoothingWindow)
            {
                _samples.RemoveAt(0);
            }

            return new CpuSample(
                _samples.Average(s => s.TotalPercent),
                _samples.Average(s => s.UserPercent),
                _samples.Average(s => s.KernelPercent),
                _samples.Average(s => s.IdlePercent));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            Debug.WriteLine($"CpuService.Read failed: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        _total?.Close();
        _user?.Close();
        _kernel?.Close();
        _idle?.Close();
    }
}
