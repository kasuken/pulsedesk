using System.Globalization;
using PulseDesk.Services;

namespace PulseDesk;

public sealed class ProcessRowViewModel
{
    internal ProcessRowViewModel(ProcessCpuSample sample)
    {
        Name = sample.Name;
        ValueLabel = sample.CpuPercent.ToString("F1", CultureInfo.CurrentCulture) + "%";
    }

    internal ProcessRowViewModel(ProcessMemorySample sample)
    {
        Name = sample.Name;
        ValueLabel = ByteFormatter.Format(sample.WorkingSetBytes);
    }

    internal ProcessRowViewModel(ProcessGpuSample sample)
    {
        Name = sample.Name;
        ValueLabel = sample.GpuPercent.ToString("F1", CultureInfo.CurrentCulture) + "%";
    }

    public string Name { get; }
    public string ValueLabel { get; }
}
