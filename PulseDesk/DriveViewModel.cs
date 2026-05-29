using System.Globalization;
using PulseDesk.Services;

namespace PulseDesk;

public sealed class DriveViewModel
{
    internal DriveViewModel(DriveSample sample)
    {
        Letter = ExtractLetter(sample.Name);
        var label = sample.Label is { Length: > 0 } ? $" ({sample.Label})" : string.Empty;
        Title = $"{sample.Name}{label} · {sample.DriveFormat}";
        UsedPercent = sample.UsedPercent;
        PercentLabel = sample.UsedPercent.ToString("F0", CultureInfo.CurrentCulture) + "%";
        Detail = $"{ByteFormatter.Format(sample.UsedBytes)} used · {ByteFormatter.Format(sample.FreeBytes)} free · {ByteFormatter.Format(sample.TotalBytes)} total";
    }

    public string Letter { get; }
    public string Title { get; }
    public double UsedPercent { get; }
    public string PercentLabel { get; }
    public string Detail { get; }

    private static string ExtractLetter(string driveName)
    {
        return driveName.Length > 0 ? driveName.Substring(0, 1).ToUpperInvariant() : "?";
    }
}
