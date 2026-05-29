using System;
using System.Globalization;

namespace PulseDesk.Services;

internal static class ByteFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(long bytes)
    {
        if (bytes < 0) return "—";
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        var digits = value >= 100 || unit == 0 ? 0 : value >= 10 ? 1 : 2;
        return string.Format(CultureInfo.CurrentCulture, $"{{0:F{digits}}} {{1}}", value, Units[unit]);
    }
}
