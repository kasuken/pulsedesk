using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;

namespace PulseDesk.Services;

internal readonly record struct NetworkSample(string InterfaceName, double SentBytesPerSecond, double ReceivedBytesPerSecond);

internal sealed class NetworkService
{
    private NetworkInterface? _interface;
    private long _lastSent;
    private long _lastReceived;
    private DateTime _lastUpdateUtc;

    public bool IsAvailable => _interface is not null;

    public NetworkService()
    {
        _interface = PickActiveInterface();
        if (_interface is null) return;

        try
        {
            var stats = _interface.GetIPStatistics();
            _lastSent = stats.BytesSent;
            _lastReceived = stats.BytesReceived;
            _lastUpdateUtc = DateTime.UtcNow;
        }
        catch (NetworkInformationException ex)
        {
            Debug.WriteLine($"NetworkService init failed: {ex.Message}");
            _interface = null;
        }
    }

    public NetworkSample? Read()
    {
        if (_interface is null) return null;
        try
        {
            var stats = _interface.GetIPStatistics();
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastUpdateUtc).TotalSeconds;

            double sentPerSec = 0;
            double receivedPerSec = 0;
            if (elapsed > 0)
            {
                sentPerSec = Math.Max(0, (stats.BytesSent - _lastSent) / elapsed);
                receivedPerSec = Math.Max(0, (stats.BytesReceived - _lastReceived) / elapsed);
            }

            _lastSent = stats.BytesSent;
            _lastReceived = stats.BytesReceived;
            _lastUpdateUtc = now;

            return new NetworkSample(_interface.Name, sentPerSec, receivedPerSec);
        }
        catch (NetworkInformationException ex)
        {
            Debug.WriteLine($"NetworkService.Read failed: {ex.Message}");
            // Active interface may have been disabled; try to pick a new one on the next tick.
            _interface = PickActiveInterface();
            return null;
        }
    }

    private static NetworkInterface? PickActiveInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(IsUsable);
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    private static bool IsUsable(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up) return false;
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) return false;

        var description = nic.Description.ToLowerInvariant();
        if (description.Contains("vpn") || description.Contains("tap") ||
            description.Contains("virtual") || description.Contains("tun"))
        {
            return false;
        }
        return true;
    }
}
