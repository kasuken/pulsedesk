using System.Runtime.InteropServices;

namespace PulseDesk.Services;

internal readonly record struct MemorySample(uint LoadPercent, long TotalBytes, long AvailableBytes, long UsedBytes);

internal sealed partial class MemoryService
{
    public MemorySample? Read()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return null;
        }

        var total = (long)status.ullTotalPhys;
        var available = (long)status.ullAvailPhys;
        return new MemorySample(status.dwMemoryLoad, total, available, total - available);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);
}
