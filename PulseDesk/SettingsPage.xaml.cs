using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PulseDesk.Services;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace PulseDesk;

public sealed partial class SettingsPage : UserControl
{
    private readonly SettingsService _settings;
    private readonly StartupService _startup;
    private bool _initializing = true;

    public event Action? SettingsChanged;

    public SettingsPage(SettingsService settings, StartupService startup)
    {
        _settings = settings;
        _startup = startup;
        InitializeComponent();
        LoadSettings();
        _initializing = false;
    }

    private void LoadSettings()
    {
        CpuTrayToggle.IsOn = _settings.CpuTrayIconEnabled;
        RamTrayToggle.IsOn = _settings.RamTrayIconEnabled;
        GpuTrayToggle.IsOn = _settings.GpuTrayIconEnabled;
        MinimizeToTrayToggle.IsOn = _settings.MinimizeToTray;

        SelectPollingInterval(_settings.PollingIntervalMs);
        _ = LoadStartupStateAsync();

        CpuTrayToggle.Toggled += OnSettingToggled;
        RamTrayToggle.Toggled += OnSettingToggled;
        GpuTrayToggle.Toggled += OnSettingToggled;
        MinimizeToTrayToggle.Toggled += OnSettingToggled;
        StartupToggle.Toggled += OnStartupToggled;
        PollingIntervalComboBox.SelectionChanged += OnPollingIntervalChanged;

        try
        {
            var v = Package.Current.Id.Version;
            VersionText.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            VersionText.Text = "Development build";
        }
    }

    private void SelectPollingInterval(int intervalMs)
    {
        for (var i = 0; i < PollingIntervalComboBox.Items.Count; i++)
        {
            if (PollingIntervalComboBox.Items[i] is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out var val) && val == intervalMs)
            {
                PollingIntervalComboBox.SelectedIndex = i;
                return;
            }
        }

        PollingIntervalComboBox.SelectedIndex = 2; // Default: 1.5 seconds
    }

    private void OnSettingToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        _settings.CpuTrayIconEnabled = CpuTrayToggle.IsOn;
        _settings.RamTrayIconEnabled = RamTrayToggle.IsOn;
        _settings.GpuTrayIconEnabled = GpuTrayToggle.IsOn;
        _settings.MinimizeToTray = MinimizeToTrayToggle.IsOn;

        SettingsChanged?.Invoke();
    }

    private void OnPollingIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing) return;

        if (PollingIntervalComboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag && int.TryParse(tag, out var ms))
        {
            _settings.PollingIntervalMs = ms;
            SettingsChanged?.Invoke();
        }
    }

    private async Task LoadStartupStateAsync()
    {
        var enabled = await _startup.IsEnabledAsync();
        StartupToggle.IsOn = enabled;

        if (await _startup.IsDisabledByUserAsync())
        {
            StartupToggle.IsEnabled = false;
        }
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;

        if (StartupToggle.IsOn)
        {
            var succeeded = await _startup.EnableAsync();
            if (!succeeded)
            {
                _initializing = true;
                StartupToggle.IsOn = false;
                _initializing = false;
            }
        }
        else
        {
            await _startup.DisableAsync();
        }
    }
}
