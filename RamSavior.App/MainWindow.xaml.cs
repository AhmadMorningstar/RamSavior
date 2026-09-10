using System.Windows;
using System.Windows.Threading;
using RamSavior.App.Settings;
using RamSavior.Core.Engine;
using RamSavior.Core.Monitoring;
using Wpf.Ui.Controls;

namespace RamSavior.App;

public partial class MainWindow : FluentWindow
{
    private readonly DispatcherTimer _pollTimer;
    private readonly AppSettings _settings;

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _pollTimer.Tick += (_, _) => RefreshStatus();
        _pollTimer.Start();

        RefreshStatus();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void RefreshStatus()
    {
        var reading = MemoryStatus.Read();

        AvailableText.Text = $"{reading.AvailablePhysicalGB:F2} GB available";
        TotalText.Text = $"of {reading.TotalPhysicalGB:F2} GB total";
        LoadBar.Value = reading.MemoryLoadPercent;
        LoadPercentText.Text = $"{reading.MemoryLoadPercent}% memory load";
    }

    private void CleanButton_Click(object sender, RoutedEventArgs e)
    {
        CleanButton.IsEnabled = false;
        ResultText.Text = "Cleaning...";

        var mode = FullModeRadio.IsChecked == true ? CleanMode.Full : CleanMode.Smart;

        // CleanupEngine.Run is synchronous and fast (kernel call + short settle-poll),
        // but we still hop off the UI thread so the window never appears to hang.
        Task.Run(() => CleanupEngine.Run(mode)).ContinueWith(t =>
        {
            var result = t.Result;

            Dispatcher.Invoke(() =>
            {
                CleanButton.IsEnabled = true;
                RefreshStatus();

                ResultText.Text = result.Success
                    ? $"Freed {result.FreedGB:F2} GB in {result.Duration.TotalMilliseconds:F0}ms " +
                      $"({result.BeforeAvailableGB:F2} GB \u2192 {result.AfterAvailableGB:F2} GB)"
                    : $"Cleanup failed: {result.Error}";
            });
        });
    }
}
