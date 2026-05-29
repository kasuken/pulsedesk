using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace PulseDesk.Services;

internal readonly record struct DriveSample(string Name, string? Label, string DriveFormat, long TotalBytes, long UsedBytes, long FreeBytes)
{
    public double UsedPercent => TotalBytes > 0 ? (double)UsedBytes / TotalBytes * 100.0 : 0;
}

internal sealed class DriveService
{
    public IReadOnlyList<DriveSample> Read()
    {
        var result = new List<DriveSample>();
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"DriveService.Read GetDrives failed: {ex.Message}");
            return result;
        }

        foreach (var drive in drives)
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
            {
                continue;
            }

            try
            {
                var total = drive.TotalSize;
                var free = drive.AvailableFreeSpace;
                result.Add(new DriveSample(
                    drive.Name,
                    string.IsNullOrWhiteSpace(drive.VolumeLabel) ? null : drive.VolumeLabel,
                    drive.DriveFormat,
                    total,
                    total - free,
                    free));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"DriveService.Read failed for {drive.Name}: {ex.Message}");
            }
        }

        return result;
    }
}
