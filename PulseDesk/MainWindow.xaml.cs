using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PulseDesk.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;

namespace PulseDesk
{
    public sealed partial class MainWindow : Window
    {
        // Single timer ticks at the live cadence (RunCat365 uses 1000 ms). Cheap metrics
        // (CPU/GPU/Memory/Temperature) sample every tick; drives are gated by a counter
        // so they refresh every Nth tick instead of on every poll.
        private const int FetchIntervalMs = 1000;
        private const int DriveTicksPerFetch = 50;
        private const int TopProcessCount = 3;

        private readonly CpuService _cpu = new();
        private readonly MemoryService _memory = new();
        private readonly GpuService _gpu = new();
        private readonly TemperatureService _temperature = new();
        private readonly NetworkService _network = new();
        private readonly DriveService _drives = new();
        private readonly GpuTopProcessesService _topGpuProcesses = new();
        private readonly TopProcessesService _topProcesses = new();
        private readonly DispatcherTimer _fetchTimer;
        private readonly ObservableCollection<DriveViewModel> _driveItems = new();
        private readonly ObservableCollection<ProcessRowViewModel> _topCpuItems = new();
        private readonly ObservableCollection<ProcessRowViewModel> _topMemoryItems = new();
        private readonly ObservableCollection<ProcessRowViewModel> _topGpuItems = new();

        private int _driveTickCounter = DriveTicksPerFetch; // sample drives on the first tick
        private bool _isFetching;
        private bool _disposed;

        public MainWindow()
        {
            InitializeComponent();

            DrivesList.ItemsSource = _driveItems;
            CpuTopProcessesList.ItemsSource = _topCpuItems;
            MemTopProcessesList.ItemsSource = _topMemoryItems;
            GpuTopProcessesList.ItemsSource = _topGpuItems;

            _fetchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FetchIntervalMs) };
            _fetchTimer.Tick += FetchTick;

            Closed += OnClosed;

            // Counters need a tick to produce real values, so the very first CPU/GPU readings
            // may show 0%; the next tick fills them in.
            _ = FetchAsync(forceDrives: true);
            _fetchTimer.Start();
        }

        private void OnClosed(object sender, WindowEventArgs args)
        {
            if (_disposed) return;
            _disposed = true;
            _fetchTimer.Stop();
            _fetchTimer.Tick -= FetchTick;
            _cpu.Dispose();
            _gpu.Dispose();
            _temperature.Dispose();
            _topGpuProcesses.Dispose();
        }

        private async void FetchTick(object? sender, object e)
        {
            await FetchAsync(forceDrives: false);
        }

        private async Task FetchAsync(bool forceDrives)
        {
            if (_isFetching) return;
            _isFetching = true;
            try
            {
                _driveTickCounter += 1;
                var sampleDrives = forceDrives || _driveTickCounter >= DriveTicksPerFetch;
                if (sampleDrives) _driveTickCounter = 0;

                // Move all sampling off the UI thread; PerformanceCounter.NextValue() and
                // DriveInfo can briefly block, especially on the first poll or on slow disks.
                var snapshot = await Task.Run(() => new MetricsSnapshot(
                    _cpu.Read(),
                    _memory.Read(),
                    _gpu.IsAvailable ? _gpu.Read() : null,
                    _topGpuProcesses.IsAvailable ? _topGpuProcesses.Read(TopProcessCount) : [],
                    _temperature.IsAvailable ? _temperature.Read() : null,
                    _network.IsAvailable ? _network.Read() : null,
                    sampleDrives ? _drives.Read() : null,
                    _topProcesses.Read(TopProcessCount)));

                ApplyCpu(snapshot.Cpu);
                ApplyTopCpuProcesses(snapshot.TopProcesses.ByCpu);
                ApplyMemory(snapshot.Memory);
                ApplyTopMemoryProcesses(snapshot.TopProcesses.ByMemory);
                ApplyGpu(snapshot.Gpu);
                ApplyTopGpuProcesses(snapshot.TopGpuProcesses);
                ApplyTemperature(snapshot.Temperature);
                ApplyNetwork(snapshot.Network);
                if (snapshot.Drives is not null)
                {
                    ApplyDrives(snapshot.Drives);
                }
                ApplySummary(snapshot);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FetchAsync failed: {ex.Message}");
            }
            finally
            {
                _isFetching = false;
            }
        }

        private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Collapse the metric grid from 4 -> 2 -> 1 column as the window narrows.
            var width = e.NewSize.Width;
            int columns = width switch
            {
                < 560 => 1,
                < 900 => 2,
                < 1200 => 3,
                _ => 5
            };
            ApplyMetricColumns(columns);
        }

        private void ApplyMetricColumns(int columns)
        {
            MetricGrid.ColumnDefinitions.Clear();
            MetricGrid.RowDefinitions.Clear();
            for (var i = 0; i < columns; i++)
            {
                MetricGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var cards = new[] { CpuCard, MemCard, GpuCard, TempCard, NetCard };
            var rowsNeeded = (int)Math.Ceiling(cards.Length / (double)columns);
            for (var r = 0; r < rowsNeeded; r++)
            {
                MetricGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (var i = 0; i < cards.Length; i++)
            {
                Grid.SetColumn(cards[i], i % columns);
                Grid.SetRow(cards[i], i / columns);
            }
        }

        private void ApplyCpu(CpuSample? sample)
        {
            if (sample is null)
            {
                ShowUnavailable(CpuValueText, CpuProgress, CpuDetailText, "Performance counter unavailable.");
                return;
            }

            var s = sample.Value;
            CpuValueText.Text = s.TotalPercent.ToString("F0", CultureInfo.CurrentCulture) + "%";
            CpuProgress.Value = s.TotalPercent;
            CpuDetailText.Text = $"User {s.UserPercent:F0}% · Kernel {s.KernelPercent:F0}% · Idle {s.IdlePercent:F0}%";
        }

        private void ApplyTopCpuProcesses(IReadOnlyList<ProcessCpuSample> samples)
        {
            _topCpuItems.Clear();
            foreach (var sample in samples)
            {
                _topCpuItems.Add(new ProcessRowViewModel(sample));
            }
            CpuTopProcessesEmptyText.Visibility = _topCpuItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyTopMemoryProcesses(IReadOnlyList<ProcessMemorySample> samples)
        {
            _topMemoryItems.Clear();
            foreach (var sample in samples)
            {
                _topMemoryItems.Add(new ProcessRowViewModel(sample));
            }
            MemTopProcessesEmptyText.Visibility = _topMemoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyMemory(MemorySample? sample)
        {
            if (sample is null)
            {
                ShowUnavailable(MemValueText, MemProgress, MemDetailText, "Memory status unavailable.");
                return;
            }

            var s = sample.Value;
            MemValueText.Text = s.LoadPercent.ToString(CultureInfo.CurrentCulture) + "%";
            MemProgress.Value = s.LoadPercent;
            MemDetailText.Text = $"{ByteFormatter.Format(s.UsedBytes)} used of {ByteFormatter.Format(s.TotalBytes)} ({ByteFormatter.Format(s.AvailableBytes)} free)";
        }

        private void ApplyGpu(GpuSample? sample)
        {
            if (!_gpu.IsAvailable)
            {
                ShowUnavailable(GpuValueText, GpuProgress, GpuDetailText, "No GPU counters detected.");
                GpuTopProcessesEmptyText.Text = "Unavailable · No GPU counters detected.";
                ApplyTopGpuProcesses([]);
                return;
            }

            if (sample is null)
            {
                ShowUnavailable(GpuValueText, GpuProgress, GpuDetailText, "GPU counters unavailable.");
                GpuTopProcessesEmptyText.Text = "Unavailable · GPU counters unavailable.";
                ApplyTopGpuProcesses([]);
                return;
            }

            var s = sample.Value;
            GpuValueText.Text = s.MaximumPercent.ToString("F0", CultureInfo.CurrentCulture) + "%";
            GpuProgress.Value = s.MaximumPercent;
            GpuDetailText.Text = $"Avg {s.AveragePercent:F0}% across 3D engines";
            GpuTopProcessesEmptyText.Text = "Measuring…";
        }

        private void ApplyTopGpuProcesses(IReadOnlyList<ProcessGpuSample> samples)
        {
            _topGpuItems.Clear();
            foreach (var sample in samples)
            {
                _topGpuItems.Add(new ProcessRowViewModel(sample));
            }
            GpuTopProcessesEmptyText.Visibility = _topGpuItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplyTemperature(TemperatureSample? sample)
        {
            if (!_temperature.IsAvailable)
            {
                ShowUnavailable(TempValueText, TempProgress, TempDetailText, "No thermal sensors exposed.");
                return;
            }

            if (sample is null)
            {
                ShowUnavailable(TempValueText, TempProgress, TempDetailText, "Thermal data unavailable.");
                return;
            }

            var s = sample.Value;
            TempValueText.Text = s.MaximumCelsius.ToString("F0", CultureInfo.CurrentCulture) + "°C";
            // Map 30°C -> 0%, 95°C -> 100% for the bar visualization.
            var normalized = Math.Clamp((s.MaximumCelsius - 30.0) / (95.0 - 30.0) * 100.0, 0, 100);
            TempProgress.Value = normalized;
            TempDetailText.Text = $"Avg {s.AverageCelsius:F0}°C · peak {s.MaximumCelsius:F0}°C";
        }

        private void ApplyNetwork(NetworkSample? sample)
        {
            if (!_network.IsAvailable)
            {
                NetValueText.Text = "—";
                NetDownText.Text = "—";
                NetUpText.Text = "—";
                NetDetailText.Text = "Unavailable · No active network adapter.";
                return;
            }

            if (sample is null)
            {
                NetValueText.Text = "—";
                NetDownText.Text = "—";
                NetUpText.Text = "—";
                NetDetailText.Text = "Unavailable · Adapter statistics unavailable.";
                return;
            }

            var s = sample.Value;
            var total = s.SentBytesPerSecond + s.ReceivedBytesPerSecond;
            NetValueText.Text = FormatRate(total);
            NetDownText.Text = FormatRate(s.ReceivedBytesPerSecond);
            NetUpText.Text = FormatRate(s.SentBytesPerSecond);
            NetDetailText.Text = s.InterfaceName;
        }

        private static string FormatRate(double bytesPerSecond)
        {
            return ByteFormatter.Format((long)bytesPerSecond) + "/s";
        }

        private void ApplyDrives(IReadOnlyList<DriveSample> samples)
        {
            _driveItems.Clear();
            foreach (var sample in samples)
            {
                _driveItems.Add(new DriveViewModel(sample));
            }
            DrivesEmptyText.Visibility = _driveItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ApplySummary(MetricsSnapshot snapshot)
        {
            var parts = new List<string>();
            if (snapshot.Cpu is { } cpu)
            {
                parts.Add($"CPU {cpu.TotalPercent:F0}%");
            }
            if (snapshot.Memory is { } mem)
            {
                parts.Add($"RAM {mem.LoadPercent}%");
            }
            if (snapshot.Network is { } net)
            {
                parts.Add($"NET ↓{FormatRate(net.ReceivedBytesPerSecond)} ↑{FormatRate(net.SentBytesPerSecond)}");
            }
            if (_driveItems.Count > 0)
            {
                parts.Add($"{_driveItems.Count} drive{(_driveItems.Count == 1 ? "" : "s")}");
            }

            SummaryText.Text = parts.Count == 0
                ? "Collecting metrics…"
                : string.Join("  ·  ", parts);
        }

        private static void ShowUnavailable(TextBlock value, ProgressBar bar, TextBlock detail, string reason)
        {
            value.Text = "—";
            bar.Value = 0;
            detail.Text = $"Unavailable · {reason}";
        }

        private readonly record struct MetricsSnapshot(
            CpuSample? Cpu,
            MemorySample? Memory,
            GpuSample? Gpu,
            IReadOnlyList<ProcessGpuSample> TopGpuProcesses,
            TemperatureSample? Temperature,
            NetworkSample? Network,
            IReadOnlyList<DriveSample>? Drives,
            TopProcessesSnapshot TopProcesses);
    }
}

