using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PulseDesk.Services;

internal readonly record struct BatterySample(
    int ChargePercent,
    bool IsCharging,
    bool IsOnAcPower,
    TimeSpan? RemainingTime,
    bool IsBatterySaver);

internal sealed class BatteryService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    public bool IsAvailable { get; }

    public BatteryService()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                IsAvailable = false;
                return;
            }

            // BatteryFlag 128 means "No system battery".
            IsAvailable = (status.BatteryFlag & 128) == 0 && status.BatteryLifePercent <= 100;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public BatterySample? Read()
    {
        if (!IsAvailable) return null;

        try
        {
            if (!GetSystemPowerStatus(out var status))
                return null;

            if (status.BatteryLifePercent > 100)
                return null;

            var isCharging = (status.BatteryFlag & 8) != 0;
            var isOnAc = status.ACLineStatus == 1;
            var isBatterySaver = status.SystemStatusFlag == 1;

            TimeSpan? remaining = status.BatteryLifeTime >= 0 && !isCharging
                ? TimeSpan.FromSeconds(status.BatteryLifeTime)
                : null;

            return new BatterySample(
                status.BatteryLifePercent,
                isCharging,
                isOnAc,
                remaining,
                isBatterySaver);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"BatteryService.Read failed: {ex.Message}");
            return null;
        }
    }
}
