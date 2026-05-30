using Windows.Storage;

namespace PulseDesk.Services;

public sealed class SettingsService
{
    private readonly ApplicationDataContainer? _container;

    public SettingsService()
    {
        try
        {
            _container = ApplicationData.Current.LocalSettings;
        }
        catch
        {
            // Unpackaged scenario – settings will not persist across restarts.
        }
    }

    public bool CpuTrayIconEnabled
    {
        get => GetBool(nameof(CpuTrayIconEnabled), true);
        set => Set(nameof(CpuTrayIconEnabled), value);
    }

    public bool RamTrayIconEnabled
    {
        get => GetBool(nameof(RamTrayIconEnabled), true);
        set => Set(nameof(RamTrayIconEnabled), value);
    }

    public bool GpuTrayIconEnabled
    {
        get => GetBool(nameof(GpuTrayIconEnabled), true);
        set => Set(nameof(GpuTrayIconEnabled), value);
    }

    public bool MinimizeToTray
    {
        get => GetBool(nameof(MinimizeToTray), true);
        set => Set(nameof(MinimizeToTray), value);
    }

    public int PollingIntervalMs
    {
        get => GetInt(nameof(PollingIntervalMs), 1000);
        set => Set(nameof(PollingIntervalMs), value);
    }

    private bool GetBool(string key, bool defaultValue)
    {
        if (_container is null) return defaultValue;
        return _container.Values.TryGetValue(key, out var value) && value is bool b ? b : defaultValue;
    }

    private int GetInt(string key, int defaultValue)
    {
        if (_container is null) return defaultValue;
        return _container.Values.TryGetValue(key, out var value) && value is int i ? i : defaultValue;
    }

    private void Set(string key, object value)
    {
        if (_container is not null)
            _container.Values[key] = value;
    }
}
