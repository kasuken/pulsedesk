using System;
using System.Collections.Generic;

namespace PulseDesk.Services;

internal enum BottleneckKind
{
    None,
    Cpu,
    Memory,
    Gpu,
    Thermal,
    Power,
    Storage
}

internal enum BottleneckSeverity
{
    Ok = 0,
    Watch = 1,
    Warning = 2,
    Critical = 3
}

internal readonly record struct BottleneckFinding(
    BottleneckKind Kind,
    BottleneckSeverity Severity,
    string Headline,
    string Detail);

internal readonly record struct BottleneckReport(
    BottleneckSeverity Overall,
    IReadOnlyList<BottleneckFinding> Findings);

internal sealed class BottleneckService
{
    private readonly Dictionary<string, int> _streaks = new(StringComparer.Ordinal);

    public BottleneckReport Analyze(
        CpuSample? cpu,
        MemorySample? memory,
        GpuSample? gpu,
        TemperatureSample? temperature,
        BatterySample? battery,
        IReadOnlyList<DriveSample>? drives,
        IReadOnlyList<ProcessCpuSample>? topCpu,
        IReadOnlyList<ProcessMemorySample>? topMemory,
        IReadOnlyList<ProcessGpuSample>? topGpu)
    {
        var findings = new List<BottleneckFinding>();

        if (cpu is { } c)
        {
            if (IsSustained("cpu-total", c.TotalPercent >= 90, 3))
            {
                var severity = c.TotalPercent >= 97 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                var detail = $"CPU is pinned near {c.TotalPercent:F0}%.";
                if (topCpu is { Count: > 0 })
                {
                    var top = topCpu[0];
                    if (top.CpuPercent >= 15)
                    {
                        detail += $" Top process: {top.Name} ({top.CpuPercent:F0}%).";
                    }
                }

                findings.Add(new BottleneckFinding(
                    BottleneckKind.Cpu,
                    severity,
                    "CPU saturation",
                    detail));
            }

            if (IsSustained("cpu-kernel", c.TotalPercent >= 70 && c.KernelPercent >= 55, 3))
            {
                findings.Add(new BottleneckFinding(
                    BottleneckKind.Cpu,
                    BottleneckSeverity.Warning,
                    "High kernel time",
                    $"Kernel time is {c.KernelPercent:F0}% while total CPU is {c.TotalPercent:F0}%. Drivers, interrupts, or background I/O may be contributing."));
            }
        }

        if (memory is { } m)
        {
            if (IsSustained("memory-load", m.LoadPercent >= 88, 3))
            {
                var severity = m.LoadPercent >= 95 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
                var detail = $"RAM load is {m.LoadPercent}% with only {ByteFormatter.Format(m.AvailableBytes)} free.";
                if (topMemory is { Count: > 0 })
                {
                    var top = topMemory[0];
                    detail += $" Largest process: {top.Name} ({ByteFormatter.Format(top.WorkingSetBytes)}).";
                }

                findings.Add(new BottleneckFinding(
                    BottleneckKind.Memory,
                    severity,
                    "Memory pressure",
                    detail));
            }

            if (drives is { Count: > 0 } && IsSustained("memory-paging-risk", m.LoadPercent >= 90, 3))
            {
                DriveSample? tightest = null;
                double lowestFreePercent = 100;
                foreach (var drive in drives)
                {
                    if (drive.TotalBytes <= 0) continue;
                    var freePercent = drive.FreeBytes / (double)drive.TotalBytes * 100.0;
                    if (freePercent < lowestFreePercent)
                    {
                        lowestFreePercent = freePercent;
                        tightest = drive;
                    }
                }

                if (tightest is { } selectedDrive && lowestFreePercent < 10)
                {
                    findings.Add(new BottleneckFinding(
                        BottleneckKind.Storage,
                        BottleneckSeverity.Warning,
                        "Low free disk space",
                        $"{selectedDrive.Name.TrimEnd('\\')} has {lowestFreePercent:F1}% free. Low free space can worsen stutter under heavy RAM pressure."));
                }
            }
        }

        if (gpu is { } g && IsSustained("gpu-max", g.MaximumPercent >= 92, 3))
        {
            var severity = g.MaximumPercent >= 98 ? BottleneckSeverity.Critical : BottleneckSeverity.Warning;
            var detail = $"GPU utilization is near {g.MaximumPercent:F0}%";
            if (topGpu is { Count: > 0 })
            {
                var top = topGpu[0];
                if (top.GpuPercent >= 10)
                {
                    detail += $". Top process: {top.Name} ({top.GpuPercent:F0}%).";
                }
                else
                {
                    detail += ".";
                }
            }
            else
            {
                detail += ".";
            }

            findings.Add(new BottleneckFinding(
                BottleneckKind.Gpu,
                severity,
                "GPU saturation",
                detail));
        }

        if (temperature is { } t)
        {
            if (IsSustained("temp-critical", t.MaximumCelsius >= 90, 2))
            {
                findings.Add(new BottleneckFinding(
                    BottleneckKind.Thermal,
                    BottleneckSeverity.Critical,
                    "Thermal throttling risk",
                    $"Peak temperature reached {t.MaximumCelsius:F0}°C. Clock throttling may reduce responsiveness."));
            }
            else if (IsSustained("temp-high", t.MaximumCelsius >= 82, 2))
            {
                findings.Add(new BottleneckFinding(
                    BottleneckKind.Thermal,
                    BottleneckSeverity.Warning,
                    "High temperature",
                    $"Peak temperature is {t.MaximumCelsius:F0}°C. Sustained heat can reduce turbo headroom."));
            }
        }

        if (battery is { } b && IsSustained("battery-saver", !b.IsOnAcPower && b.IsBatterySaver, 2))
        {
            findings.Add(new BottleneckFinding(
                BottleneckKind.Power,
                BottleneckSeverity.Watch,
                "Battery saver is active",
                "Power-saving mode can limit CPU and GPU performance while unplugged."));
        }

        if (findings.Count == 0)
        {
            findings.Add(new BottleneckFinding(
                BottleneckKind.None,
                BottleneckSeverity.Ok,
                "No strong bottleneck detected",
                "PulseDesk is not seeing sustained CPU, RAM, GPU, thermal, or power pressure right now."));
        }

        findings.Sort(static (a, b) =>
        {
            var bySeverity = b.Severity.CompareTo(a.Severity);
            return bySeverity != 0 ? bySeverity : a.Kind.CompareTo(b.Kind);
        });

        var overall = BottleneckSeverity.Ok;
        foreach (var finding in findings)
        {
            if (finding.Severity > overall)
            {
                overall = finding.Severity;
            }
        }

        return new BottleneckReport(overall, findings);
    }

    private bool IsSustained(string key, bool active, int threshold)
    {
        if (active)
        {
            _streaks[key] = _streaks.TryGetValue(key, out var current) ? current + 1 : 1;
        }
        else
        {
            _streaks[key] = 0;
        }

        return _streaks[key] >= threshold;
    }
}
